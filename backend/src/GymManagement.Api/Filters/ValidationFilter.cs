using System.Diagnostics;
using FluentValidation;
using FluentValidation.Results;
using GymManagement.Application.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace GymManagement.Api.Filters;

/// <summary>
/// Runs the FluentValidation validator registered for every action argument before the action body
/// executes, and merges anything model binding already rejected. Controllers therefore never have to
/// check their inputs, and the client always receives the same <see cref="ApiResponse"/> envelope.
/// </summary>
public sealed class ValidationFilter : IAsyncActionFilter
{
    private readonly IServiceProvider _services;
    private readonly ILogger<ValidationFilter> _logger;

    public ValidationFilter(IServiceProvider services, ILogger<ValidationFilter> logger)
    {
        _services = services;
        _logger = logger;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var failures = new List<ValidationFailure>();

        // Model binding / type conversion problems first, so a bad payload never reaches a validator.
        foreach (var entry in context.ModelState)
        {
            foreach (var error in entry.Value.Errors)
            {
                failures.Add(new ValidationFailure(entry.Key,
                    string.IsNullOrWhiteSpace(error.ErrorMessage)
                        ? "The supplied value is not valid."
                        : error.ErrorMessage));
            }
        }

        StampRouteIdOntoBody(context);

        foreach (var argument in context.ActionArguments)
        {
            if (argument.Value is null) continue;

            var argumentType = argument.Value.GetType();
            var validatorType = typeof(IValidator<>).MakeGenericType(argumentType);

            if (_services.GetService(validatorType) is not IValidator validator) continue;

            var validationContext = new ValidationContext<object>(argument.Value);
            var result = await validator.ValidateAsync(validationContext, context.HttpContext.RequestAborted);

            if (!result.IsValid) failures.AddRange(result.Errors);
        }

        if (failures.Count > 0)
        {
            var errors = failures
                .GroupBy(f => string.IsNullOrWhiteSpace(f.PropertyName) ? string.Empty : f.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(f => f.ErrorMessage).Distinct().ToArray());

            _logger.LogWarning("Validation failed for {Action}: {FieldCount} field(s) rejected.",
                context.ActionDescriptor.DisplayName, errors.Count);

            var response = ApiResponse.Fail("One or more validation errors occurred.", "VALIDATION_ERROR", errors);
            response.TraceId = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;

            context.Result = new BadRequestObjectResult(response);
            return;
        }

        await next();
    }

    /// <summary>
    /// Copies the route's <c>{id}</c> onto a body argument's <c>Id</c> before validation runs.
    ///
    /// Every update action assigns <c>dto.Id = id</c>, but an action filter runs before the action
    /// body, so validators that require <c>Id &gt; 0</c> were rejecting perfectly well-formed
    /// requests: <c>PUT /api/trainers/7</c> with no <c>id</c> in the payload failed with "Select the
    /// trainer you want to update." That made editing members, trainers and users impossible for any
    /// caller that trusted the URL to identify the record — which is what a REST client should do.
    ///
    /// Only a zero <c>Id</c> is filled in, so a body that states its own id is left alone for the
    /// validators to judge. The route stays authoritative either way, because the action still
    /// overwrites the value immediately afterwards.
    /// </summary>
    private static void StampRouteIdOntoBody(ActionExecutingContext context)
    {
        if (!context.RouteData.Values.TryGetValue("id", out var raw)) return;
        if (!int.TryParse(raw?.ToString(), out var routeId) || routeId <= 0) return;

        foreach (var argument in context.ActionArguments)
        {
            if (argument.Value is not { } model) continue;

            var type = model.GetType();
            if (type.IsPrimitive || type == typeof(string) || type.IsValueType) continue;

            var idProperty = type.GetProperty("Id");
            if (idProperty is null || idProperty.PropertyType != typeof(int) || !idProperty.CanWrite) continue;

            if (idProperty.GetValue(model) is 0) idProperty.SetValue(model, routeId);
        }
    }
}
