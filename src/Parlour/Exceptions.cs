namespace Parlour;

public sealed class HandlerNotFoundException : InvalidOperationException
{
    public HandlerNotFoundException(Type requestType)
        : base($"no handler is registered for request '{requestType.Name}'")
    {
        RequestType = requestType;
    }

    public Type RequestType { get; }
}

public sealed record ValidationFailure(string Property, string Message);

public sealed class ValidationException : Exception
{
    public ValidationException(Type requestType, IReadOnlyList<ValidationFailure> failures)
        : base($"{requestType.Name} failed validation: " + string.Join("; ", failures.Select(f => f.Property.Length == 0 ? f.Message : $"{f.Property}: {f.Message}")))
    {
        RequestType = requestType;
        Failures = failures;
    }

    public Type RequestType { get; }

    public IReadOnlyList<ValidationFailure> Failures { get; }
}
