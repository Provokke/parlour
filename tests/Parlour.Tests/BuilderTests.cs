using Parlour;

namespace Parlour.Tests;

public class BuilderTests
{
    private sealed class MapProvider : IServiceProvider
    {
        private readonly Dictionary<Type, object> map = new();

        public MapProvider Add(Type type, object value)
        {
            map[type] = value;
            return this;
        }

        public object? GetService(Type serviceType)
        {
            if (map.TryGetValue(serviceType, out var value))
            {
                return value;
            }

            if (serviceType.IsGenericType && serviceType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                return Array.CreateInstance(serviceType.GetGenericArguments()[0], 0);
            }

            return null;
        }
    }

    [Fact]
    public async Task ScanningFindsHandlersInAnAssembly()
    {
        var log = new Log();
        var mediator = new MediatorBuilder()
            .AddSingleton(log)
            .AddHandlersFromAssembly(typeof(BuilderTests).Assembly, t => t == typeof(PingHandler) || t == typeof(WelcomeEmail) || t == typeof(AuditTrail))
            .Build();
        Assert.Equal("Pong: s", await mediator.Send(new Ping("s")));
        await mediator.Publish(new UserCreated("scan"));
        Assert.Equal(new[] { "audit scan", "email scan" }, log.Items.OrderBy(x => x));
    }

    [Fact]
    public async Task ScanningSkipsAbstractAndOpenGenericTypes()
    {
        var mediator = new MediatorBuilder()
            .AddHandlersFromAssembly(typeof(BuilderTests).Assembly, t => t.Namespace == "Parlour.Tests" && t.Name.Contains("Behavior"))
            .Build();
        await Assert.ThrowsAsync<HandlerNotFoundException>(() => mediator.Send(new Ping("x")));
    }

    [Fact]
    public async Task ScanningRegistersValidatorsAndProcessors()
    {
        var log = new Log();
        var mediator = new MediatorBuilder()
            .AddSingleton(log)
            .AddHandlersFromAssembly(typeof(BuilderTests).Assembly, t => t == typeof(PingHandler) || t == typeof(RecordingPre) || t == typeof(RecordingPost))
            .AddBehavior(typeof(RequestProcessorBehavior<,>))
            .Build();
        await mediator.Send(new Ping("scan"));
        Assert.Equal(new[] { "pre scan", "post Pong: scan" }, log.Items);
    }

    [Fact]
    public async Task InstanceRegistrationCoversEveryHandlerInterface()
    {
        var log = new Log();
        var both = new CombinedHandler(log);
        var mediator = new MediatorBuilder().AddHandler(both).Build();
        Assert.Equal("combined", await mediator.Send(new Ping("x")));
        await mediator.Publish(new UserCreated("c"));
        Assert.Equal(new[] { "combined saw c" }, log.Items);
    }

    [Fact]
    public void InstanceRegistrationNeedsAHandlerInterface()
    {
        Assert.Throws<ArgumentException>(() => new MediatorBuilder().AddHandler(new object()));
    }

    [Fact]
    public async Task BuilderRegistersTheMediatorForHandlers()
    {
        var builder = new MediatorBuilder();
        var mediator = builder.Build();
        Assert.Same(mediator, builder.Services.GetService(typeof(IMediator)));
        Assert.Same(mediator, builder.Services.GetService(typeof(ISender)));
        Assert.Same(mediator, builder.Services.GetService(typeof(IPublisher)));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ABringYourOwnProviderWorks()
    {
        var provider = new MapProvider().Add(typeof(IRequestHandler<Ping, string>), new PingHandler());
        var mediator = new Mediator(provider);
        Assert.Equal("Pong: own", await mediator.Send(new Ping("own")));
        await mediator.Publish(new UserCreated("nobody listens"));
    }

    [Fact]
    public async Task ABringYourOwnProviderCanSupplyBehaviours()
    {
        var provider = new MapProvider()
            .Add(typeof(IRequestHandler<Ping, string>), new PingHandler())
            .Add(typeof(IEnumerable<IPipelineBehavior<Ping, string>>), new IPipelineBehavior<Ping, string>[] { new ShoutBehavior() });
        var mediator = new Mediator(provider);
        Assert.Equal("PONG: Z", await mediator.Send(new Ping("Z")));
    }

    [Fact]
    public async Task ServicesCanBeSharedWithHandlers()
    {
        var mediator = new MediatorBuilder()
            .AddSingleton(new Log())
            .AddTransient<IRequestHandler<Touch, Unit>, TouchHandler>()
            .Build();
        await mediator.Send(new Touch("shared"));
    }

    [Fact]
    public async Task ASingleClassCanHandleSeveralRequests()
    {
        var multi = new MultiHandler();
        var mediator = new MediatorBuilder().AddHandler(multi).Build();
        Assert.Equal("Pong: multi", await mediator.Send(new Ping("multi")));
        Assert.Equal(11, await mediator.Send(new Sum(5, 6)));
    }

    private sealed class CombinedHandler : IRequestHandler<Ping, string>, INotificationHandler<UserCreated>
    {
        private readonly Log log;

        public CombinedHandler(Log log)
        {
            this.log = log;
        }

        public Task<string> Handle(Ping request, CancellationToken cancellationToken)
        {
            return Task.FromResult("combined");
        }

        public Task Handle(UserCreated notification, CancellationToken cancellationToken)
        {
            log.Add("combined saw " + notification.Name);
            return Task.CompletedTask;
        }
    }

    private sealed class MultiHandler : IRequestHandler<Ping, string>, IRequestHandler<Sum, int>
    {
        public Task<string> Handle(Ping request, CancellationToken cancellationToken)
        {
            return Task.FromResult("Pong: " + request.Message);
        }

        public Task<int> Handle(Sum request, CancellationToken cancellationToken)
        {
            return Task.FromResult(request.A + request.B);
        }
    }
}
