using System.Diagnostics;
using System.Text.Json;
using GymManagement.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace GymManagement.Api.Middleware;

/// <summary>
/// Terminal error boundary: converts every unhandled exception into an <see cref="ApiResponse"/>
/// envelope. Exception messages from unknown failures are never surfaced to the caller - only the
/// trace id is, so a support request can be correlated with the server log.
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    /// <summary>Non-standard status used by nginx/IIS for "client closed the request".</summary>
    private const int ClientClosedRequest = 499;

    private const string GenericMessage = "An unexpected error occurred.";

    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleAsync(context, ex);
        }
    }

    private async Task HandleAsync(HttpContext context, Exception exception)
    {
        var traceId = Activity.Current?.Id ?? context.TraceIdentifier;

        // The client hung up: nothing can be written and nothing is worth an error-level log entry.
        if (exception is OperationCanceledException or TaskCanceledException &&
            context.RequestAborted.IsCancellationRequested)
        {
            _logger.LogInformation("Request {Method} {Path} was cancelled by the client. TraceId: {TraceId}",
                context.Request.Method, context.Request.Path, traceId);

            if (!context.Response.HasStarted)
                context.Response.StatusCode = ClientClosedRequest;

            return;
        }

        var (statusCode, response) = Translate(exception, traceId);

        if (statusCode >= 500)
        {
            _logger.LogError(exception,
                "Unhandled exception on {Method} {Path}. TraceId: {TraceId}",
                context.Request.Method, context.Request.Path, traceId);
        }
        else
        {
            _logger.LogWarning(
                "Request {Method} {Path} failed with {StatusCode} ({ErrorCode}): {Reason}. TraceId: {TraceId}",
                context.Request.Method, context.Request.Path, statusCode, response.ErrorCode,
                exception.Message, traceId);
        }

        if (context.Response.HasStarted)
        {
            _logger.LogWarning("Response for {Path} already started; the error envelope could not be written.",
                context.Request.Path);
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";

        var payload = JsonSerializer.Serialize(response, SerializerOptions);
        await context.Response.WriteAsync(payload, context.RequestAborted);
    }

    private static (int StatusCode, ApiResponse Response) Translate(Exception exception, string traceId)
    {
        ApiResponse response;
        int statusCode;

        switch (exception)
        {
            case ValidationAppException validation:
                statusCode = validation.StatusCode;
                response = ApiResponse.Fail(validation.Message, validation.ErrorCode,
                    validation.Errors.ToDictionary(e => e.Key, e => e.Value));
                break;

            case AppException app:
                statusCode = app.StatusCode;
                response = ApiResponse.Fail(app.Message, app.ErrorCode);
                break;

            case FluentValidation.ValidationException fluent:
                statusCode = StatusCodes.Status400BadRequest;
                response = ApiResponse.Fail("One or more validation errors occurred.", "VALIDATION_ERROR",
                    fluent.Errors
                        .GroupBy(e => string.IsNullOrWhiteSpace(e.PropertyName) ? string.Empty : e.PropertyName)
                        .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray()));
                break;

            case DbUpdateConcurrencyException:
                statusCode = StatusCodes.Status409Conflict;
                response = ApiResponse.Fail(
                    "The record was modified by another user. Reload it and try again.", "CONCURRENCY_CONFLICT");
                break;

            case OperationCanceledException:
                statusCode = ClientClosedRequest;
                response = ApiResponse.Fail("The request was cancelled.", "REQUEST_CANCELLED");
                break;

            default:
                statusCode = StatusCodes.Status500InternalServerError;
                response = ApiResponse.Fail(GenericMessage, "INTERNAL_ERROR");
                break;
        }

        response.TraceId = traceId;
        return (statusCode, response);
    }

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
}

/// <summary>Registration helper so <c>Program</c> reads as a pipeline description.</summary>
public static class ExceptionHandlingMiddlewareExtensions
{
    public static IApplicationBuilder UseExceptionHandling(this IApplicationBuilder app) =>
        app.UseMiddleware<ExceptionHandlingMiddleware>();
}
