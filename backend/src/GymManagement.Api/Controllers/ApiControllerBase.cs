using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymManagement.Api.Controllers;

/// <summary>
/// Shared base for every controller: authenticated by default, JSON only, and helpers that wrap
/// results in the standard <see cref="ApiResponse"/> envelope.
/// </summary>
[ApiController]
[Authorize]
[Route("api/[controller]")]
[Produces("application/json")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>Wraps a payload in a successful envelope.</summary>
    protected ActionResult<ApiResponse<T>> Success<T>(T data, string? message = null) =>
        base.Ok(ApiResponse<T>.Ok(data, message));

    /// <summary>Returns a successful envelope with no payload.</summary>
    protected ActionResult<ApiResponse> SuccessMessage(string message) =>
        base.Ok(ApiResponse.Ok(message));

    /// <summary>Returns a generated file as a download.</summary>
    protected FileContentResult FileDownload(FileExportDto file) =>
        File(file.Content, file.ContentType, file.FileName);
}
