using Parlour;

namespace Parlour.Tests;

public class BuiltInBehaviourTests
{
    private static IMediator Create(Action<MediatorBuilder> configure)
    {
        var builder = new MediatorBuilder();
        configure(builder);
        return builder.Build();
    }

    private static IMediator Validating(params IValidator<Register>[] validators)
    {
        return Create(b =>
        {
            b.AddHandler<IRequestHandler<Register, string>, RegisterHandler>();
            b.AddBehavior(typeof(ValidationBehavior<,>));
            foreach (var v in validators)
            {
                b.Services.AddSingleton(typeof(IValidator<Register>), v);
            }
        });
    }

    [Fact]
    public async Task ValidRequestsPassThrough()
    {
        var mediator = Validating(new NameValidator(), new AgeValidator());
        Assert.Equal("registered Ann", await mediator.Send(new Register("Ann", 30)));
    }

    [Fact]
    public async Task InvalidRequestsAreRejectedBeforeTheHandler()
    {
        var mediator = Validating(new NameValidator(), new AgeValidator());
        var e = await Assert.ThrowsAsync<ValidationException>(() => mediator.Send(new Register("Ann", 12)));
        var failure = Assert.Single(e.Failures);
        Assert.Equal("Age", failure.Property);
        Assert.Equal(typeof(Register), e.RequestType);
    }

    [Fact]
    public async Task FailuresFromEveryValidatorAreCollected()
    {
        var mediator = Validating(new NameValidator(), new AgeValidator());
        var e = await Assert.ThrowsAsync<ValidationException>(() => mediator.Send(new Register(" ", 12)));
        Assert.Equal(new[] { "Name", "Age" }, e.Failures.Select(f => f.Property));
        Assert.Equal("Register failed validation: Name: must not be empty; Age: must be at least 18", e.Message);
    }

    [Fact]
    public async Task RequestsWithoutValidatorsAreUnaffected()
    {
        var mediator = Validating();
        Assert.Equal("registered ", await mediator.Send(new Register("", 1)));
    }

    [Fact]
    public async Task ValidationMessageOmitsEmptyPropertyNames()
    {
        var e = new ValidationException(typeof(Ping), new[] { new ValidationFailure("", "bad") });
        Assert.Equal("Ping failed validation: bad", e.Message);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task LoggingBehaviourReportsSuccess()
    {
        var lines = new List<string>();
        var mediator = Create(b =>
        {
            b.AddHandler<IRequestHandler<Ping, string>, PingHandler>();
            b.AddSingleton<IRequestLogger>(new DelegateRequestLogger(lines.Add));
            b.AddBehavior(typeof(LoggingBehavior<,>));
        });
        await mediator.Send(new Ping("x"));
        Assert.Equal(2, lines.Count);
        Assert.Equal("handling Ping", lines[0]);
        Assert.Matches(@"^handled Ping in \d+ ms$", lines[1]);
    }

    [Fact]
    public async Task LoggingBehaviourReportsFailuresAndRethrows()
    {
        var lines = new List<string>();
        var mediator = Create(b =>
        {
            b.AddHandler<IRequestHandler<Boom, int>, BoomHandler>();
            b.AddSingleton<IRequestLogger>(new DelegateRequestLogger(lines.Add));
            b.AddBehavior(typeof(LoggingBehavior<,>));
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Send(new Boom()));
        Assert.Matches(@"^failed Boom after \d+ ms: InvalidOperationException: boom$", lines[1]);
    }

    [Fact]
    public async Task LoggingBehaviourReportsCancellationSeparately()
    {
        var lines = new List<string>();
        var mediator = Create(b =>
        {
            b.AddHandler<IRequestHandler<Slow, string>, SlowHandler>();
            b.AddSingleton<IRequestLogger>(new DelegateRequestLogger(lines.Add));
            b.AddBehavior(typeof(LoggingBehavior<,>));
        });
        using var cts = new CancellationTokenSource(30);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => mediator.Send(new Slow(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)), cts.Token));
        Assert.StartsWith("cancelled Slow", lines[1]);
    }

    [Fact]
    public async Task PreAndPostProcessorsWrapTheHandler()
    {
        var log = new Log();
        var mediator = Create(b =>
        {
            b.AddSingleton(log);
            b.AddHandler<IRequestHandler<Ping, string>, PingHandler>();
            b.AddHandler<IRequestPreProcessor<Ping>, RecordingPre>();
            b.AddHandler<IRequestPostProcessor<Ping, string>, RecordingPost>();
            b.AddBehavior(typeof(RequestProcessorBehavior<,>));
        });
        Assert.Equal("Pong: q", await mediator.Send(new Ping("q")));
        Assert.Equal(new[] { "pre q", "post Pong: q" }, log.Items);
    }

    [Fact]
    public async Task ProcessorsAreOptional()
    {
        var mediator = Create(b =>
        {
            b.AddHandler<IRequestHandler<Sum, int>, SumHandler>();
            b.AddBehavior(typeof(RequestProcessorBehavior<,>));
        });
        Assert.Equal(3, await mediator.Send(new Sum(1, 2)));
    }

    [Fact]
    public async Task PostProcessorsAreSkippedWhenTheHandlerFails()
    {
        var log = new Log();
        var mediator = Create(b =>
        {
            b.AddSingleton(log);
            b.AddHandler<IRequestHandler<Boom, int>, BoomHandler>();
            b.AddBehavior(typeof(RequestProcessorBehavior<,>));
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Send(new Boom()));
        Assert.Empty(log.Items);
    }

    [Fact]
    public async Task TimeoutBehaviourLetsFastRequestsFinish()
    {
        var mediator = Create(b =>
        {
            b.AddHandler<IRequestHandler<Slow, string>, SlowHandler>();
            b.AddBehavior(typeof(TimeoutBehavior<,>));
        });
        var result = await mediator.Send(new Slow(TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(10)));
        Assert.Equal("done", result);
    }

    [Fact]
    public async Task TimeoutBehaviourStopsSlowRequests()
    {
        var mediator = Create(b =>
        {
            b.AddHandler<IRequestHandler<Slow, string>, SlowHandler>();
            b.AddBehavior(typeof(TimeoutBehavior<,>));
        });
        var e = await Assert.ThrowsAsync<TimeoutException>(
            () => mediator.Send(new Slow(TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(50))));
        Assert.Contains("Slow", e.Message);
    }

    [Fact]
    public async Task TimeoutBehaviourAlsoStopsHandlersThatIgnoreTheToken()
    {
        var mediator = Create(b =>
        {
            b.AddHandler<IRequestHandler<Stubborn, string>, StubbornHandler>();
            b.AddBehavior(typeof(TimeoutBehavior<,>));
        });
        await Assert.ThrowsAsync<TimeoutException>(
            () => mediator.Send(new Stubborn(TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(40))));
    }

    [Fact]
    public async Task TimeoutBehaviourIgnoresRequestsWithoutALimit()
    {
        var mediator = Create(b =>
        {
            b.AddHandler<IRequestHandler<Sum, int>, SumHandler>();
            b.AddBehavior(typeof(TimeoutBehavior<,>));
        });
        Assert.Equal(9, await mediator.Send(new Sum(4, 5)));
    }

    [Fact]
    public async Task CallerCancellationBeatsTheTimeout()
    {
        var mediator = Create(b =>
        {
            b.AddHandler<IRequestHandler<Slow, string>, SlowHandler>();
            b.AddBehavior(typeof(TimeoutBehavior<,>));
        });
        using var cts = new CancellationTokenSource(30);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => mediator.Send(new Slow(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30)), cts.Token));
    }

    [Fact]
    public async Task BuiltInBehavioursStackInOrder()
    {
        var lines = new List<string>();
        var mediator = Create(b =>
        {
            b.AddHandler<IRequestHandler<Register, string>, RegisterHandler>();
            b.AddSingleton<IRequestLogger>(new DelegateRequestLogger(lines.Add));
            b.AddBehavior(typeof(LoggingBehavior<,>));
            b.AddBehavior(typeof(ValidationBehavior<,>));
            b.Services.AddSingleton(typeof(IValidator<Register>), new AgeValidator());
        });
        await Assert.ThrowsAsync<ValidationException>(() => mediator.Send(new Register("x", 1)));
        Assert.Equal("handling Register", lines[0]);
        Assert.StartsWith("failed Register", lines[1]);
    }
}
