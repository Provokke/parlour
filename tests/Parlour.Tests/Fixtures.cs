using Parlour;

namespace Parlour.Tests;

public sealed class Log
{
    private readonly List<string> items = new();

    public IReadOnlyList<string> Items
    {
        get
        {
            lock (items)
            {
                return items.ToArray();
            }
        }
    }

    public void Add(string text)
    {
        lock (items)
        {
            items.Add(text);
        }
    }
}

public record Ping(string Message) : IRequest<string>;

public sealed class PingHandler : IRequestHandler<Ping, string>
{
    public Task<string> Handle(Ping request, CancellationToken cancellationToken)
    {
        return Task.FromResult("Pong: " + request.Message);
    }
}

public record Sum(int A, int B) : IRequest<int>;

public sealed class SumHandler : IRequestHandler<Sum, int>
{
    public Task<int> Handle(Sum request, CancellationToken cancellationToken)
    {
        return Task.FromResult(request.A + request.B);
    }
}

public record Touch(string Id) : IRequest;

public sealed class TouchHandler : RequestHandler<Touch>
{
    private readonly Log log;

    public TouchHandler(Log log)
    {
        this.log = log;
    }

    protected override async Task HandleCore(Touch request, CancellationToken cancellationToken)
    {
        await Task.Yield();
        log.Add("touched " + request.Id);
    }
}

public record Boom : IRequest<int>;

public sealed class BoomHandler : IRequestHandler<Boom, int>
{
    public Task<int> Handle(Boom request, CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("boom");
    }
}

public record Orphan : IRequest<int>;

public record WhoAmI : IRequest<string>;

public sealed class WhoAmIHandler : IRequestHandler<WhoAmI, string>
{
    private readonly IMediator mediator;
    private readonly Log log;

    public WhoAmIHandler(IMediator mediator, Log log)
    {
        this.mediator = mediator;
        this.log = log;
    }

    public async Task<string> Handle(WhoAmI request, CancellationToken cancellationToken)
    {
        log.Add("who");
        return await mediator.Send(new Ping("nested"), cancellationToken);
    }
}

public record Slow(TimeSpan Delay, TimeSpan Timeout) : IRequest<string>, ITimeLimited;

public sealed class SlowHandler : IRequestHandler<Slow, string>
{
    public async Task<string> Handle(Slow request, CancellationToken cancellationToken)
    {
        await Task.Delay(request.Delay, cancellationToken);
        return "done";
    }
}

public record Stubborn(TimeSpan Delay, TimeSpan Timeout) : IRequest<string>, ITimeLimited;

public sealed class StubbornHandler : IRequestHandler<Stubborn, string>
{
    public async Task<string> Handle(Stubborn request, CancellationToken cancellationToken)
    {
        await Task.Delay(request.Delay);
        return "late";
    }
}

public record UserCreated(string Name) : INotification;

public sealed class WelcomeEmail : INotificationHandler<UserCreated>
{
    private readonly Log log;

    public WelcomeEmail(Log log)
    {
        this.log = log;
    }

    public Task Handle(UserCreated notification, CancellationToken cancellationToken)
    {
        log.Add("email " + notification.Name);
        return Task.CompletedTask;
    }
}

public sealed class AuditTrail : INotificationHandler<UserCreated>
{
    private readonly Log log;

    public AuditTrail(Log log)
    {
        this.log = log;
    }

    public async Task Handle(UserCreated notification, CancellationToken cancellationToken)
    {
        await Task.Yield();
        log.Add("audit " + notification.Name);
    }
}

public sealed class FailingHandler : INotificationHandler<UserCreated>
{
    private readonly string name;

    public FailingHandler(string name)
    {
        this.name = name;
    }

    public Task Handle(UserCreated notification, CancellationToken cancellationToken)
    {
        throw new InvalidOperationException(name);
    }
}

public sealed class AsyncFailingHandler : INotificationHandler<UserCreated>
{
    private readonly string name;

    public AsyncFailingHandler(string name)
    {
        this.name = name;
    }

    public async Task Handle(UserCreated notification, CancellationToken cancellationToken)
    {
        await Task.Yield();
        throw new InvalidOperationException(name);
    }
}

public record NoListeners(int Id) : INotification;

public interface ICacheable
{
}

public record CachedLookup(string Key) : IRequest<string>, ICacheable;

public sealed class CachedLookupHandler : IRequestHandler<CachedLookup, string>
{
    private readonly Log log;

    public CachedLookupHandler(Log log)
    {
        this.log = log;
    }

    public Task<string> Handle(CachedLookup request, CancellationToken cancellationToken)
    {
        log.Add("lookup " + request.Key);
        return Task.FromResult("value-of-" + request.Key);
    }
}

public sealed class CachingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>, ICacheable
{
    private static readonly Dictionary<string, object?> Cache = new();

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var key = request.ToString()!;
        lock (Cache)
        {
            if (Cache.TryGetValue(key, out var hit))
            {
                return (TResponse)hit!;
            }
        }

        var response = await next(cancellationToken);
        lock (Cache)
        {
            Cache[key] = response;
        }

        return response;
    }
}

public abstract class LabelledBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly Log log;
    private readonly string label;

    protected LabelledBehavior(Log log, string label)
    {
        this.log = log;
        this.label = label;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        log.Add(label + ">");
        try
        {
            return await next(cancellationToken);
        }
        finally
        {
            log.Add("<" + label);
        }
    }
}

public sealed class OuterBehavior<TRequest, TResponse> : LabelledBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public OuterBehavior(Log log)
        : base(log, "outer")
    {
    }
}

public sealed class MiddleBehavior<TRequest, TResponse> : LabelledBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public MiddleBehavior(Log log)
        : base(log, "middle")
    {
    }
}

public sealed class InnerBehavior<TRequest, TResponse> : LabelledBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public InnerBehavior(Log log)
        : base(log, "inner")
    {
    }
}

public sealed class ShortCircuitBehavior : IPipelineBehavior<Ping, string>
{
    public Task<string> Handle(Ping request, RequestHandlerDelegate<string> next, CancellationToken cancellationToken)
    {
        return request.Message == "skip" ? Task.FromResult("short-circuited") : next(cancellationToken);
    }
}

public sealed class ShoutBehavior : IPipelineBehavior<Ping, string>
{
    public async Task<string> Handle(Ping request, RequestHandlerDelegate<string> next, CancellationToken cancellationToken)
    {
        return (await next(cancellationToken)).ToUpperInvariant();
    }
}

public sealed class TokenCheckBehavior : IPipelineBehavior<Ping, string>
{
    public Task<string> Handle(Ping request, RequestHandlerDelegate<string> next, CancellationToken cancellationToken)
    {
        using var other = new CancellationTokenSource();
        other.Cancel();
        return next(other.Token);
    }
}

public sealed class TokenAwareHandler : IRequestHandler<Ping, string>
{
    public Task<string> Handle(Ping request, CancellationToken cancellationToken)
    {
        return Task.FromResult(cancellationToken.IsCancellationRequested ? "cancelled-token" : "live-token");
    }
}

public record Register(string Name, int Age) : IRequest<string>;

public sealed class RegisterHandler : IRequestHandler<Register, string>
{
    public Task<string> Handle(Register request, CancellationToken cancellationToken)
    {
        return Task.FromResult("registered " + request.Name);
    }
}

public sealed class NameValidator : IValidator<Register>
{
    public IEnumerable<ValidationFailure> Validate(Register request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            yield return new ValidationFailure("Name", "must not be empty");
        }
    }
}

public sealed class AgeValidator : IValidator<Register>
{
    public IEnumerable<ValidationFailure> Validate(Register request)
    {
        if (request.Age < 18)
        {
            yield return new ValidationFailure("Age", "must be at least 18");
        }

        if (request.Age > 150)
        {
            yield return new ValidationFailure("Age", "is not realistic");
        }
    }
}

public sealed class RecordingPre : IRequestPreProcessor<Ping>
{
    private readonly Log log;

    public RecordingPre(Log log)
    {
        this.log = log;
    }

    public Task Process(Ping request, CancellationToken cancellationToken)
    {
        log.Add("pre " + request.Message);
        return Task.CompletedTask;
    }
}

public sealed class RecordingPost : IRequestPostProcessor<Ping, string>
{
    private readonly Log log;

    public RecordingPost(Log log)
    {
        this.log = log;
    }

    public Task Process(Ping request, string response, CancellationToken cancellationToken)
    {
        log.Add("post " + response);
        return Task.CompletedTask;
    }
}
