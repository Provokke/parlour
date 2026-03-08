namespace Parlour;

public interface IValidator<in TRequest>
{
    IEnumerable<ValidationFailure> Validate(TRequest request);
}

public sealed class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IEnumerable<IValidator<TRequest>> validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        this.validators = validators;
    }

    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var failures = validators.SelectMany(v => v.Validate(request)).ToList();
        if (failures.Count > 0)
        {
            throw new ValidationException(typeof(TRequest), failures);
        }

        return next(cancellationToken);
    }
}
