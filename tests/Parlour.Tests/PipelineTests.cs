using Parlour;

namespace Parlour.Tests;

public class PipelineTests
{
    private static (IMediator Mediator, Log Log) Create(Action<MediatorBuilder>? configure = null)
    {
        var log = new Log();
        var builder = new MediatorBuilder()
            .AddSingleton(log)
            .AddHandler<IRequestHandler<Ping, string>, PingHandler>()
            .AddHandler<IRequestHandler<Sum, int>, SumHandler>()
            .AddHandler<IRequestHandler<Boom, int>, BoomHandler>()
            .AddHandler<IRequestHandler<CachedLookup, string>, CachedLookupHandler>();
        configure?.Invoke(builder);
        return (builder.Build(), log);
    }

    [Fact]
    public async Task BehavioursWrapTheHandlerOutermostFirst()
    {
        var (mediator, log) = Create(b => b
            .AddBehavior(typeof(OuterBehavior<,>))
            .AddBehavior(typeof(MiddleBehavior<,>))
            .AddBehavior(typeof(InnerBehavior<,>)));
        await mediator.Send(new Ping("x"));
        Assert.Equal(new[] { "outer>", "middle>", "inner>", "<inner", "<middle", "<outer" }, log.Items);
    }

    [Fact]
    public async Task RegistrationOrderDecidesNesting()
    {
        var (mediator, log) = Create(b => b
            .AddBehavior(typeof(InnerBehavior<,>))
            .AddBehavior(typeof(OuterBehavior<,>)));
        await mediator.Send(new Sum(1, 2));
        Assert.Equal(new[] { "inner>", "outer>", "<outer", "<inner" }, log.Items);
    }

    [Fact]
    public async Task OpenBehavioursApplyToEveryRequestType()
    {
        var (mediator, log) = Create(b => b.AddBehavior(typeof(OuterBehavior<,>)));
        await mediator.Send(new Ping("a"));
        await mediator.Send(new Sum(1, 1));
        Assert.Equal(4, log.Items.Count);
    }

    [Fact]
    public async Task ClosedBehavioursOnlyApplyToTheirRequest()
    {
        var (mediator, _) = Create(b => b.AddBehavior<ShoutBehavior>());
        Assert.Equal("PONG: HI", await mediator.Send(new Ping("hi")));
        Assert.Equal(5, await mediator.Send(new Sum(2, 3)));
    }

    [Fact]
    public async Task BehavioursCanShortCircuit()
    {
        var (mediator, _) = Create(b => b.AddBehavior<ShortCircuitBehavior>());
        Assert.Equal("short-circuited", await mediator.Send(new Ping("skip")));
        Assert.Equal("Pong: go", await mediator.Send(new Ping("go")));
    }

    [Fact]
    public async Task BehavioursCanChangeTheResponse()
    {
        var (mediator, _) = Create(b => b.AddBehavior<ShoutBehavior>().AddBehavior<ShortCircuitBehavior>());
        Assert.Equal("PONG: Q", await mediator.Send(new Ping("Q")));
        Assert.Equal("SHORT-CIRCUITED", await mediator.Send(new Ping("skip")));
    }

    [Fact]
    public async Task BehavioursCanSubstituteTheCancellationToken()
    {
        var (mediator, _) = Create(b => b
            .AddHandler<IRequestHandler<Ping, string>, TokenAwareHandler>()
            .AddBehavior<TokenCheckBehavior>());
        Assert.Equal("cancelled-token", await mediator.Send(new Ping("x")));
    }

    [Fact]
    public async Task ConstrainedOpenBehavioursSkipRequestsThatDoNotQualify()
    {
        var (mediator, log) = Create(b => b.AddBehavior(typeof(CachingBehavior<,>)));
        Assert.Equal("Pong: x", await mediator.Send(new Ping("x")));
        var key = Guid.NewGuid().ToString("N");
        await mediator.Send(new CachedLookup(key));
        await mediator.Send(new CachedLookup(key));
        Assert.Equal(new[] { "lookup " + key }, log.Items);
    }

    [Fact]
    public async Task BehavioursSeeHandlerExceptions()
    {
        var (mediator, log) = Create(b => b.AddBehavior(typeof(OuterBehavior<,>)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Send(new Boom()));
        Assert.Equal(new[] { "outer>", "<outer" }, log.Items);
    }

    [Fact]
    public async Task EachSendBuildsItsOwnBehaviourInstances()
    {
        var instances = new List<object>();
        var (mediator, _) = Create(b => b.Services.AddTransient(
            typeof(IPipelineBehavior<Ping, string>),
            _ =>
            {
                var created = new ShoutBehavior();
                lock (instances)
                {
                    instances.Add(created);
                }

                return created;
            }));
        await mediator.Send(new Ping("a"));
        await mediator.Send(new Ping("b"));
        Assert.Equal(2, instances.Distinct().Count());
    }

    [Fact]
    public async Task AddBehaviorRejectsTypesThatAreNotBehaviours()
    {
        var builder = new MediatorBuilder();
        Assert.Throws<ArgumentException>(() => builder.AddBehavior(typeof(string)));
        await Task.CompletedTask;
    }
}
