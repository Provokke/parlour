using Parlour;

namespace Parlour.Tests;

public class SendTests
{
    private static (IMediator Mediator, Log Log) Create(Action<MediatorBuilder>? configure = null)
    {
        var log = new Log();
        var builder = new MediatorBuilder()
            .AddSingleton(log)
            .AddHandler<IRequestHandler<Ping, string>, PingHandler>()
            .AddHandler<IRequestHandler<Sum, int>, SumHandler>()
            .AddHandler<IRequestHandler<Touch, Unit>, TouchHandler>()
            .AddHandler<IRequestHandler<Boom, int>, BoomHandler>()
            .AddHandler<IRequestHandler<WhoAmI, string>, WhoAmIHandler>();
        configure?.Invoke(builder);
        return (builder.Build(), log);
    }

    [Fact]
    public async Task SendReturnsTheHandlersResponse()
    {
        var (mediator, _) = Create();
        Assert.Equal("Pong: hi", await mediator.Send(new Ping("hi")));
    }

    [Fact]
    public async Task DifferentRequestsGoToDifferentHandlers()
    {
        var (mediator, _) = Create();
        Assert.Equal(7, await mediator.Send(new Sum(3, 4)));
        Assert.Equal("Pong: x", await mediator.Send(new Ping("x")));
    }

    [Fact]
    public async Task VoidRequestsReturnUnit()
    {
        var (mediator, log) = Create();
        var result = await mediator.Send(new Touch("a"));
        Assert.Equal(Unit.Value, result);
        Assert.Equal(new[] { "touched a" }, log.Items);
    }

    [Fact]
    public async Task MissingHandlerThrowsHandlerNotFound()
    {
        var (mediator, _) = Create();
        var e = await Assert.ThrowsAsync<HandlerNotFoundException>(() => mediator.Send(new Orphan()));
        Assert.Equal(typeof(Orphan), e.RequestType);
        Assert.Contains("Orphan", e.Message);
    }

    [Fact]
    public async Task HandlerExceptionsSurfaceThroughTheTask()
    {
        var (mediator, _) = Create();
        var task = mediator.Send(new Boom());
        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => task);
        Assert.Equal("boom", e.Message);
    }

    [Fact]
    public void SynchronousHandlerExceptionsDoNotEscapeSend()
    {
        var (mediator, _) = Create();
        var task = mediator.Send(new Boom());
        Assert.True(task.IsFaulted);
    }

    [Fact]
    public async Task NullRequestIsRejected()
    {
        var (mediator, _) = Create();
        await Assert.ThrowsAsync<ArgumentNullException>(() => mediator.Send<string>(null!));
    }

    [Fact]
    public async Task AnAlreadyCancelledTokenStopsTheSend()
    {
        var (mediator, _) = Create();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mediator.Send(new Ping("x"), cts.Token));
    }

    [Fact]
    public async Task TheTokenReachesTheHandler()
    {
        var (mediator, _) = Create(b => b.AddHandler<IRequestHandler<Slow, string>, SlowHandler>());
        using var cts = new CancellationTokenSource(50);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => mediator.Send(new Slow(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)), cts.Token));
    }

    [Fact]
    public async Task HandlersCanSendFurtherRequests()
    {
        var (mediator, log) = Create();
        Assert.Equal("Pong: nested", await mediator.Send(new WhoAmI()));
        Assert.Equal(new[] { "who" }, log.Items);
    }

    [Fact]
    public async Task HandlerDependenciesAreInjected()
    {
        var (mediator, log) = Create();
        await mediator.Send(new Touch("dep"));
        Assert.Contains("touched dep", log.Items);
    }

    [Fact]
    public async Task LastRegistrationForARequestWins()
    {
        var (mediator, _) = Create(b => b.AddHandler(new FixedPingHandler("second")));
        Assert.Equal("second", await mediator.Send(new Ping("x")));
    }

    [Fact]
    public async Task ResponseTypeFollowsTheRuntimeRequestType()
    {
        var (mediator, _) = Create();
        IRequest<string> request = new Ping("runtime");
        Assert.Equal("Pong: runtime", await mediator.Send(request));
    }

    [Fact]
    public async Task ManyConcurrentSendsAllGetTheirOwnAnswer()
    {
        var (mediator, _) = Create();
        var tasks = Enumerable.Range(0, 2000)
            .Select(i => Task.Run(async () => (i, await mediator.Send(new Sum(i, 1)))))
            .ToArray();
        var results = await Task.WhenAll(tasks);
        Assert.All(results, r => Assert.Equal(r.i + 1, r.Item2));
    }

    [Fact]
    public async Task SeparateMediatorsDoNotShareHandlers()
    {
        var (first, _) = Create(b => b.AddHandler(new FixedPingHandler("one")));
        var (second, _) = Create(b => b.AddHandler(new FixedPingHandler("two")));
        Assert.Equal("one", await first.Send(new Ping("x")));
        Assert.Equal("two", await second.Send(new Ping("x")));
    }

    [Fact]
    public void UnitBehavesLikeAValue()
    {
        Assert.True(Unit.Value == default);
        Assert.False(Unit.Value != default);
        Assert.Equal(Unit.Value.GetHashCode(), new Unit().GetHashCode());
        Assert.Equal("()", Unit.Value.ToString());
        Assert.True(Unit.Value.Equals((object)new Unit()));
    }

    [Fact]
    public void MediatorNeedsAProvider()
    {
        Assert.Throws<ArgumentNullException>(() => new Mediator(null!));
    }

    private sealed class FixedPingHandler : IRequestHandler<Ping, string>
    {
        private readonly string answer;

        public FixedPingHandler(string answer)
        {
            this.answer = answer;
        }

        public Task<string> Handle(Ping request, CancellationToken cancellationToken)
        {
            return Task.FromResult(answer);
        }
    }
}
