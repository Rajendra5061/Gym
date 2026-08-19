namespace GymManagement.Application.Common;

/// <summary>Outcome of an operation that can fail for a business reason rather than an exception.</summary>
public class Result
{
    public bool Succeeded { get; protected init; }
    public string? Error { get; protected init; }
    public string? ErrorCode { get; protected init; }
    public IReadOnlyDictionary<string, string[]> ValidationErrors { get; protected init; }
        = new Dictionary<string, string[]>();

    public static Result Success() => new() { Succeeded = true };

    public static Result Failure(string error, string? errorCode = null) =>
        new() { Succeeded = false, Error = error, ErrorCode = errorCode };

    public static Result Invalid(IReadOnlyDictionary<string, string[]> errors) => new()
    {
        Succeeded = false,
        Error = "One or more validation errors occurred.",
        ErrorCode = "VALIDATION_ERROR",
        ValidationErrors = errors
    };
}

/// <inheritdoc cref="Result"/>
public class Result<T> : Result
{
    public T? Data { get; private init; }

    public static Result<T> Success(T data) => new() { Succeeded = true, Data = data };

    public static new Result<T> Failure(string error, string? errorCode = null) =>
        new() { Succeeded = false, Error = error, ErrorCode = errorCode };

    public static new Result<T> Invalid(IReadOnlyDictionary<string, string[]> errors) => new()
    {
        Succeeded = false,
        Error = "One or more validation errors occurred.",
        ErrorCode = "VALIDATION_ERROR",
        ValidationErrors = errors
    };
}
