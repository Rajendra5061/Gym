namespace GymManagement.Application.Common;

/// <summary>
/// Envelope returned by every API endpoint so the client can handle success and failure
/// uniformly. Serialised with camelCase by the API.
/// </summary>
public class ApiResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? ErrorCode { get; set; }
    public Dictionary<string, string[]>? ValidationErrors { get; set; }
    public string? TraceId { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    public static ApiResponse Ok(string? message = null) => new() { Success = true, Message = message };

    public static ApiResponse Fail(string message, string? errorCode = null,
        Dictionary<string, string[]>? validationErrors = null) =>
        new() { Success = false, Message = message, ErrorCode = errorCode, ValidationErrors = validationErrors };
}

/// <inheritdoc cref="ApiResponse"/>
public class ApiResponse<T> : ApiResponse
{
    public T? Data { get; set; }

    public static ApiResponse<T> Ok(T data, string? message = null) =>
        new() { Success = true, Data = data, Message = message };

    public static new ApiResponse<T> Fail(string message, string? errorCode = null,
        Dictionary<string, string[]>? validationErrors = null) =>
        new() { Success = false, Message = message, ErrorCode = errorCode, ValidationErrors = validationErrors };
}
