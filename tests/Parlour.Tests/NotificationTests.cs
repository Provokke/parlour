using Parlour;

namespace Parlour.Tests;

public class NotificationTests
{
    private static (IMediator Mediator, Log Log) Create(INotificationPublisher? publisher, Action<MediatorBuilder>? configure = null)
    {
        var log = new Log();
        var builder = new MediatorBuilder()
            .AddSingleton(log)
            .AddHandler<INotificationHandler<UserCreated>, WelcomeEmail>()
            .AddHandler<INotificationHandler<UserCreated>, AuditTrail>();
        if (publisher != null)
        {
            builder.UseNotificationPublisher(publisher);
        }

        configure?.Invoke(builder);
        return (builder.Build(), log);
    }

    [Fact]
    public async Task EveryHandlerReceivesTheNotification()
    {
        var (mediator, log) = Create(null);
        await mediator.Publish(new UserCreated("ann"));
        Assert.Equal(new[] { "email ann", "audit ann" }, log.Items);
    }

    [Fact]
    public async Task PublishingWithNoHandlersIsFine()
    {
        var (mediator, log) = Create(null);
        await mediator.Publish(new NoListeners(1));
        Assert.Empty(log.Items);
    }

    [Fact]
    public async Task SequentialPublisherRunsHandlersInRegistrationOrder()
    {
        var (mediator, log) = Create(new SequentialPublisher());
        await mediator.Publish(new UserCreated("a"));
        await mediator.Publish(new UserCreated("b"));
        Assert.Equal(new[] { "email a", "audit a", "email b", "audit b" }, log.Items);
    }

    [Fact]
    public async Task SequentialPublisherKeepsGoingAfterAFailure()
    {
        var (mediator, log) = Create(new SequentialPublisher(), b => b.AddHandler(new FailingHandler("first")));
        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Publish(new UserCreated("x")));
        Assert.Equal("first", e.Message);
        Assert.Equal(new[] { "email x", "audit x" }, log.Items);
    }

    [Fact]
    public async Task SequentialPublisherAggregatesSeveralFailures()
    {
        var (mediator, _) = Create(new SequentialPublisher(), b => b
            .Services.AddSingleton(typeof(INotificationHandler<UserCreated>), new FailingHandler("one"))
            .AddSingleton(typeof(INotificationHandler<UserCreated>), new AsyncFailingHandler("two")));
        var e = await Assert.ThrowsAsync<AggregateException>(() => mediator.Publish(new UserCreated("x")));
        Assert.Equal(new[] { "one", "two" }, e.InnerExceptions.Select(x => x.Message));
    }

    [Fact]
    public async Task StopOnFirstFailureSkipsTheRest()
    {
        var log = new Log();
        var mediator = new MediatorBuilder()
            .AddSingleton(log)
            .AddHandler(new FailingHandler("stop"))
            .AddHandler<INotificationHandler<UserCreated>, WelcomeEmail>()
            .UseNotificationPublisher(new StopOnFirstFailurePublisher())
            .Build();
        await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Publish(new UserCreated("x")));
        Assert.Empty(log.Items);
    }

    [Fact]
    public async Task StopOnFirstFailureRunsEverythingWhenNothingFails()
    {
        var (mediator, log) = Create(new StopOnFirstFailurePublisher());
        await mediator.Publish(new UserCreated("z"));
        Assert.Equal(2, log.Items.Count);
    }

    [Fact]
    public async Task ParallelPublisherRunsHandlersAtTheSameTime()
    {
        var aReady = new TaskCompletionSource();
        var bReady = new TaskCompletionSource();
        var mediator = new MediatorBuilder()
            .AddHandler(new Rendezvous(aReady, bReady))
            .AddHandler(new Rendezvous(bReady, aReady))
            .UseNotificationPublisher(new ParallelPublisher())
            .Build();
        var publish = mediator.Publish(new UserCreated("x"));
        Assert.Same(publish, await Task.WhenAny(publish, Task.Delay(5000)));
        await publish;
    }

    [Fact]
    public async Task SequentialPublisherWouldDeadlockTheSameRendezvous()
    {
        var aReady = new TaskCompletionSource();
        var bReady = new TaskCompletionSource();
        var mediator = new MediatorBuilder()
            .AddHandler(new Rendezvous(aReady, bReady))
            .AddHandler(new Rendezvous(bReady, aReady))
            .Build();
        var publish = mediator.Publish(new UserCreated("x"));
        var winner = await Task.WhenAny(publish, Task.Delay(200));
        Assert.NotSame(publish, winner);
        aReady.TrySetResult();
        bReady.TrySetResult();
        await publish;
    }

    [Fact]
    public async Task ParallelPublisherCollectsEveryFailure()
    {
        var (mediator, log) = Create(new ParallelPublisher(), b => b
            .Services.AddSingleton(typeof(INotificationHandler<UserCreated>), new FailingHandler("sync"))
            .AddSingleton(typeof(INotificationHandler<UserCreated>), new AsyncFailingHandler("async")));
        var e = await Assert.ThrowsAsync<AggregateException>(() => mediator.Publish(new UserCreated("p")));
        Assert.Equal(new[] { "async", "sync" }, e.InnerExceptions.Select(x => x.Message).OrderBy(x => x));
        Assert.Equal(2, log.Items.Count);
    }

    [Fact]
    public async Task ParallelPublisherRethrowsASingleFailureAsIs()
    {
        var (mediator, _) = Create(new ParallelPublisher(), b => b.AddHandler(new AsyncFailingHandler("only")));
        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Publish(new UserCreated("p")));
        Assert.Equal("only", e.Message);
    }

    [Fact]
    public async Task NonGenericPublishUsesTheRuntimeType()
    {
        var (mediator, log) = Create(null);
        object notification = new UserCreated("dyn");
        await mediator.Publish(notification);
        Assert.Equal(new[] { "email dyn", "audit dyn" }, log.Items);
    }

    [Fact]
    public async Task NonGenericPublishRejectsOtherObjects()
    {
        var (mediator, _) = Create(null);
        await Assert.ThrowsAsync<ArgumentException>(() => mediator.Publish((object)"nope"));
        await Assert.ThrowsAsync<ArgumentNullException>(() => mediator.Publish((object)null!));
    }

    [Fact]
    public async Task PublishHonoursACancelledToken()
    {
        var (mediator, log) = Create(null);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mediator.Publish(new UserCreated("c"), cts.Token));
        Assert.Empty(log.Items);
    }

    [Fact]
    public async Task NullNotificationIsRejected()
    {
        var (mediator, _) = Create(null);
        await Assert.ThrowsAsync<ArgumentNullException>(() => mediator.Publish<UserCreated>(null!));
    }

    [Fact]
    public void PublisherCannotBeNull()
    {
        Assert.Throws<ArgumentNullException>(() => new MediatorBuilder().UseNotificationPublisher(null!));
    }

    [Fact]
    public async Task HandlersOfOtherNotificationsAreNotCalled()
    {
        var (mediator, log) = Create(null);
        await mediator.Publish(new NoListeners(5));
        Assert.Empty(log.Items);
    }

    private sealed class Rendezvous : INotificationHandler<UserCreated>
    {
        private readonly TaskCompletionSource mine;
        private readonly TaskCompletionSource theirs;

        public Rendezvous(TaskCompletionSource mine, TaskCompletionSource theirs)
        {
            this.mine = mine;
            this.theirs = theirs;
        }

        public async Task Handle(UserCreated notification, CancellationToken cancellationToken)
        {
            mine.TrySetResult();
            await theirs.Task;
        }
    }
}
