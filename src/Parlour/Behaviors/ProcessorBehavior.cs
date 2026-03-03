namespace Parlour;

public sealed class RequestProcessorBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IEnumerable<IRequestPreProcessor<TRequest>> preProcessors;
    private readonly IEnumerable<IRequestPostProcessor<TRequest, TResponse>> postProcessors;

    public RequestProcessorBehavior(
        IEnumerable<IRequestPreProcessor<TRequest>> preProcessors,
        IEnumerable<IRequestPostProcessor<TRequest, TResponse>> postProcessors)
    {
        this.preProcessors = preProcessors;
        this.postProcessors = postProcessors;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        foreach (var pre in preProcessors)
        {
            await pre.Process(request, cancellationToken).ConfigureAwait(false);
        }

        var response = await next(cancellationToken).ConfigureAwait(false);
        foreach (var post in postProcessors)
        {
            await post.Process(request, response, cancellationToken).ConfigureAwait(false);
        }

        return response;
    }
}
