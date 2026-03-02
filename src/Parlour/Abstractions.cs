namespace Parlour;

public readonly struct Unit : IEquatable<Unit>
{
    public static readonly Unit Value = default;

    public bool Equals(Unit other) => true;

    public override bool Equals(object? obj) => obj is Unit;

    public override int GetHashCode() => 0;

    public override string ToString() => "()";

    public static bool operator ==(Unit left, Unit right) => true;

    public static bool operator !=(Unit left, Unit right) => false;
}

public interface IRequest<out TResponse>
{
}

public interface IRequest : IRequest<Unit>
{
}

public interface INotification
{
}

public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
}

public abstract class RequestHandler<TRequest> : IRequestHandler<TRequest, Unit>
    where TRequest : IRequest<Unit>
{
    public async Task<Unit> Handle(TRequest request, CancellationToken cancellationToken)
    {
        await HandleCore(request, cancellationToken).ConfigureAwait(false);
        return Unit.Value;
    }

    protected abstract Task HandleCore(TRequest request, CancellationToken cancellationToken);
}

public interface INotificationHandler<in TNotification>
    where TNotification : INotification
{
    Task Handle(TNotification notification, CancellationToken cancellationToken);
}

public delegate Task<TResponse> RequestHandlerDelegate<TResponse>(CancellationToken cancellationToken);

public interface IPipelineBehavior<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken);
}

public interface IRequestPreProcessor<in TRequest>
{
    Task Process(TRequest request, CancellationToken cancellationToken);
}

public interface IRequestPostProcessor<in TRequest, in TResponse>
{
    Task Process(TRequest request, TResponse response, CancellationToken cancellationToken);
}

public interface ISender
{
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);
}

public interface IPublisher
{
    Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification;

    Task Publish(object notification, CancellationToken cancellationToken = default);
}

public interface IMediator : ISender, IPublisher
{
}

public interface INotificationPublisher
{
    Task Publish<TNotification>(
        IReadOnlyList<INotificationHandler<TNotification>> handlers,
        TNotification notification,
        CancellationToken cancellationToken)
        where TNotification : INotification;
}
