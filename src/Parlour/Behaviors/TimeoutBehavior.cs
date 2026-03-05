namespace Parlour;

public interface ITimeLimited
{
    TimeSpan Timeout { get; }
}

public sealed class TimeoutBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is not ITimeLimited limited)
        {
            return await next(cancellationToken).ConfigureAwait(false);
        }

        using var work = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var clock = new CancellationTokenSource();
        var running = next(work.Token);
        var timer = Task.Delay(limited.Timeout, clock.Token);
        var first = await Task.WhenAny(running, timer).ConfigureAwait(false);
        if (first == running)
        {
            clock.Cancel();
            return await running.ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        work.Cancel();
        _ = running.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
        throw new TimeoutException($"{typeof(TRequest).Name} did not finish within {limited.Timeout.TotalMilliseconds:0} ms");
    }
}
