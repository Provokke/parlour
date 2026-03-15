using System.Reflection;

namespace Parlour;

public sealed class MediatorBuilder
{
    private static readonly Type[] HandlerShapes =
    {
        typeof(IRequestHandler<,>),
        typeof(INotificationHandler<>),
        typeof(IRequestPreProcessor<>),
        typeof(IRequestPostProcessor<,>),
        typeof(IValidator<>),
    };

    private INotificationPublisher publisher = new SequentialPublisher();

    public MediatorBuilder()
    {
        Services = new ServiceRegistry();
    }

    public ServiceRegistry Services { get; }

    public MediatorBuilder AddHandler<TService, TImplementation>()
        where TImplementation : TService
    {
        Services.AddTransient(typeof(TService), typeof(TImplementation));
        return this;
    }

    public MediatorBuilder AddHandler(object handler)
    {
        var shapes = handler.GetType().GetInterfaces().Where(IsHandlerShape).ToList();
        if (shapes.Count == 0)
        {
            throw new ArgumentException($"{handler.GetType().Name} does not implement any Parlour handler interface", nameof(handler));
        }

        foreach (var shape in shapes)
        {
            Services.AddSingleton(shape, handler);
        }

        return this;
    }

    public MediatorBuilder AddHandlersFromAssembly(Assembly assembly, Func<Type, bool>? filter = null)
    {
        foreach (var type in assembly.GetTypes())
        {
            if (!type.IsClass || type.IsAbstract || type.IsGenericTypeDefinition || (filter != null && !filter(type)))
            {
                continue;
            }

            foreach (var shape in type.GetInterfaces().Where(IsHandlerShape))
            {
                Services.AddTransient(shape, type);
            }
        }

        return this;
    }

    public MediatorBuilder AddBehavior(Type behavior)
    {
        var shape = behavior.GetInterfaces().FirstOrDefault(IsBehaviorShape)
            ?? throw new ArgumentException($"{behavior.Name} does not implement IPipelineBehavior<,>", nameof(behavior));
        var service = behavior.IsGenericTypeDefinition ? typeof(IPipelineBehavior<,>) : shape;
        Services.AddTransient(service, behavior);
        return this;
    }

    public MediatorBuilder AddBehavior<TBehavior>()
    {
        return AddBehavior(typeof(TBehavior));
    }

    public MediatorBuilder UseNotificationPublisher(INotificationPublisher notificationPublisher)
    {
        publisher = notificationPublisher ?? throw new ArgumentNullException(nameof(notificationPublisher));
        return this;
    }

    public MediatorBuilder AddTransient<TService, TImplementation>()
        where TImplementation : TService
    {
        Services.AddTransient<TService, TImplementation>();
        return this;
    }

    public MediatorBuilder AddSingleton<TService>(TService instance)
        where TService : class
    {
        Services.AddSingleton(instance);
        return this;
    }

    public IMediator Build()
    {
        var mediator = new Mediator(Services, publisher);
        Services.AddSingleton(typeof(IMediator), mediator);
        Services.AddSingleton(typeof(ISender), mediator);
        Services.AddSingleton(typeof(IPublisher), mediator);
        return mediator;
    }

    private static bool IsHandlerShape(Type type)
    {
        return type.IsGenericType && HandlerShapes.Contains(type.GetGenericTypeDefinition());
    }

    private static bool IsBehaviorShape(Type type)
    {
        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>);
    }
}
