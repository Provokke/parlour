using System.Diagnostics;

namespace Parlour;

public interface IRequestLogger
{
    void Log(string message);
}

public sealed class DelegateRequestLogger : IRequestLogger
{
    private readonly Action<string> sink;

    public DelegateRequestLogger(Action<string> sink)
    {
        this.sink = sink;
    }

    public void Log(string message)
    {
        sink(message);
    }
}

public sealed class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IRequestLogger logger;

    public LoggingBehavior(IRequestLogger logger)
    {
        this.logger = logger;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var name = typeof(TRequest).Name;
        logger.Log($"handling {name}");
        var clock = Stopwatch.StartNew();
        try
        {
            var response = await next(cancellationToken).ConfigureAwait(false);
            logger.Log($"handled {name} in {clock.ElapsedMilliseconds} ms");
            return response;
        }
        catch (OperationCanceledException)
        {
            logger.Log($"cancelled {name} after {clock.ElapsedMilliseconds} ms");
            throw;
        }
        catch (Exception e)
        {
            logger.Log($"failed {name} after {clock.ElapsedMilliseconds} ms: {e.GetType().Name}: {e.Message}");
            throw;
        }
    }
}
