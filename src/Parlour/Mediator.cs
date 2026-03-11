using System.Collections.Concurrent;

namespace Parlour;

public sealed class Mediator : IMediator
{
    private static readonly ConcurrentDictionary<(Type Request, Type Response), object> RequestWrappers = new();
    private static readonly ConcurrentDictionary<Type, NotificationWrapper> NotificationWrappers = new();

    private readonly IServiceProvider provider;
    private readonly INotificationPublisher publisher;

    public Mediator(IServiceProvider provider, INotificationPublisher? publisher = null)
    {
        this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
        this.publisher = publisher ?? new SequentialPublisher();
    }

    public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var wrapper = (RequestWrapper<TResponse>)RequestWrappers.GetOrAdd(
            (request.GetType(), typeof(TResponse)),
            key => Activator.CreateInstance(typeof(RequestWrapper<,>).MakeGenericType(key.Request, key.Response))!);
        return await wrapper.Handle(request, provider, cancellationToken).ConfigureAwait(false);
    }

    public async Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        ArgumentNullException.ThrowIfNull(notification);
        cancellationToken.ThrowIfCancellationRequested();
        var handlers = Resolve<TNotification>(provider);
        await publisher.Publish(handlers, notification, cancellationToken).ConfigureAwait(false);
    }

    public async Task Publish(object notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        if (notification is not INotification)
        {
            throw new ArgumentException($"{notification.GetType().Name} does not implement INotification", nameof(notification));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var wrapper = NotificationWrappers.GetOrAdd(
            notification.GetType(),
            type => (NotificationWrapper)Activator.CreateInstance(typeof(NotificationWrapper<>).MakeGenericType(type))!);
        await wrapper.Publish(notification, provider, publisher, cancellationToken).ConfigureAwait(false);
    }

    internal static IReadOnlyList<INotificationHandler<TNotification>> Resolve<TNotification>(IServiceProvider provider)
        where TNotification : INotification
    {
        var found = provider.GetService(typeof(IEnumerable<INotificationHandler<TNotification>>));
        return found is IEnumerable<INotificationHandler<TNotification>> list
            ? list.ToList()
            : Array.Empty<INotificationHandler<TNotification>>();
    }

    private abstract class RequestWrapper<TResponse>
    {
        public abstract Task<TResponse> Handle(IRequest<TResponse> request, IServiceProvider provider, CancellationToken cancellationToken);
    }

    private sealed class RequestWrapper<TRequest, TResponse> : RequestWrapper<TResponse>
        where TRequest : IRequest<TResponse>
    {
        public override Task<TResponse> Handle(IRequest<TResponse> request, IServiceProvider provider, CancellationToken cancellationToken)
        {
            var handler = provider.GetService(typeof(IRequestHandler<TRequest, TResponse>)) as IRequestHandler<TRequest, TResponse>
                ?? throw new HandlerNotFoundException(typeof(TRequest));
            var behaviours = provider.GetService(typeof(IEnumerable<IPipelineBehavior<TRequest, TResponse>>))
                as IEnumerable<IPipelineBehavior<TRequest, TResponse>>;
            var typed = (TRequest)request;

            RequestHandlerDelegate<TResponse> next = ct => handler.Handle(typed, ct);
            if (behaviours != null)
            {
                foreach (var behaviour in behaviours.Reverse())
                {
                    var inner = next;
                    next = ct => behaviour.Handle(typed, inner, ct);
                }
            }

            return next(cancellationToken);
        }
    }

    private abstract class NotificationWrapper
    {
        public abstract Task Publish(object notification, IServiceProvider provider, INotificationPublisher publisher, CancellationToken cancellationToken);
    }

    private sealed class NotificationWrapper<TNotification> : NotificationWrapper
        where TNotification : INotification
    {
        public override Task Publish(object notification, IServiceProvider provider, INotificationPublisher publisher, CancellationToken cancellationToken)
        {
            return publisher.Publish(Resolve<TNotification>(provider), (TNotification)notification, cancellationToken);
        }
    }
}
