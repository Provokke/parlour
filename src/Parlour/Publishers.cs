using System.Runtime.ExceptionServices;

namespace Parlour;

internal static class PublishErrors
{
    public static void ThrowIfAny(List<Exception> errors)
    {
        if (errors.Count == 1)
        {
            ExceptionDispatchInfo.Capture(errors[0]).Throw();
        }

        if (errors.Count > 1)
        {
            throw new AggregateException(errors);
        }
    }
}

public sealed class SequentialPublisher : INotificationPublisher
{
    public async Task Publish<TNotification>(
        IReadOnlyList<INotificationHandler<TNotification>> handlers,
        TNotification notification,
        CancellationToken cancellationToken)
        where TNotification : INotification
    {
        var errors = new List<Exception>();
        foreach (var handler in handlers)
        {
            try
            {
                await handler.Handle(notification, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                errors.Add(e);
            }
        }

        PublishErrors.ThrowIfAny(errors);
    }
}

public sealed class StopOnFirstFailurePublisher : INotificationPublisher
{
    public async Task Publish<TNotification>(
        IReadOnlyList<INotificationHandler<TNotification>> handlers,
        TNotification notification,
        CancellationToken cancellationToken)
        where TNotification : INotification
    {
        foreach (var handler in handlers)
        {
            await handler.Handle(notification, cancellationToken).ConfigureAwait(false);
        }
    }
}

public sealed class ParallelPublisher : INotificationPublisher
{
    public async Task Publish<TNotification>(
        IReadOnlyList<INotificationHandler<TNotification>> handlers,
        TNotification notification,
        CancellationToken cancellationToken)
        where TNotification : INotification
    {
        var tasks = new List<Task>(handlers.Count);
        foreach (var handler in handlers)
        {
            try
            {
                tasks.Add(handler.Handle(notification, cancellationToken));
            }
            catch (Exception e)
            {
                tasks.Add(Task.FromException(e));
            }
        }

        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch
        {
            var errors = tasks
                .Where(t => t.IsFaulted || t.IsCanceled)
                .Select(t => t.Exception?.InnerException ?? new TaskCanceledException(t))
                .ToList();
            PublishErrors.ThrowIfAny(errors);
            throw;
        }
    }
}
