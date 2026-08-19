using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace GymManagement.Api.Controllers;

/// <summary>
/// Authentication endpoints: sign in, token refresh, sign out and the self-service password flows.
/// The sign-in and forgot-password endpoints are additionally throttled by the "auth" rate-limit
/// policy so that credential stuffing is slowed down per source IP.
/// </summary>
[Route("api/auth")]
public sealed class AuthController : ApiControllerBase
{
    private readonly IAuthService _authService;
    private readonly ICurrentUserService _currentUser;

    public AuthController(IAuthService authService, ICurrentUserService currentUser)
    {
        _authService = authService;
        _currentUser = currentUser;
    }

    /// <summary>The signed-in user's id, taken from the token and never from the request body.</summary>
    private int CurrentUserId =>
        _currentUser.UserId ?? throw new UnauthorizedAppException();

    /// <summary>Signs a user in and returns an access token, a refresh token and the user profile.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(ApiResponse<LoginResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ApiResponse<LoginResponseDto>>> Login(
        [FromBody] LoginRequestDto dto, CancellationToken ct)
    {
        var result = await _authService.LoginAsync(
            dto, HttpContext.Connection.RemoteIpAddress?.ToString(), ct);

        return Success(result);
    }

    /// <summary>Exchanges a valid refresh token for a new access/refresh token pair.</summary>
    [HttpPost("refresh-token")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<LoginResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<LoginResponseDto>>> RefreshToken(
        [FromBody] RefreshTokenRequestDto dto, CancellationToken ct)
    {
        var result = await _authService.RefreshTokenAsync(
            dto, HttpContext.Connection.RemoteIpAddress?.ToString(), ct);

        return Success(result);
    }

    /// <summary>Revokes the supplied refresh token so the session can no longer be renewed.</summary>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse>> Logout(
        [FromBody] LogoutRequestDto dto, CancellationToken ct)
    {
        await _authService.LogoutAsync(dto, ct);
        return SuccessMessage("Signed out.");
    }

    /// <summary>Starts a password reset and returns the reset token for the account, when it exists.</summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(ApiResponse<ForgotPasswordResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ApiResponse<ForgotPasswordResponseDto>>> ForgotPassword(
        [FromBody] ForgotPasswordRequestDto dto, CancellationToken ct)
    {
        var result = await _authService.ForgotPasswordAsync(dto, ct);
        return Success(result);
    }

    /// <summary>Completes a password reset using the token issued by forgot-password.</summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> ResetPassword(
        [FromBody] ResetPasswordRequestDto dto, CancellationToken ct)
    {
        await _authService.ResetPasswordAsync(dto, ct);
        return SuccessMessage("Password has been reset. You can sign in with the new password.");
    }

    /// <summary>Changes the signed-in user's own password.</summary>
    [HttpPost("change-password")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse>> ChangePassword(
        [FromBody] ChangePasswordRequestDto dto, CancellationToken ct)
    {
        await _authService.ChangePasswordAsync(CurrentUserId, dto, ct);
        return SuccessMessage("Password changed.");
    }

    /// <summary>Returns the signed-in user's profile, roles and effective permissions.</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<CurrentUserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<CurrentUserDto>>> Me(CancellationToken ct)
    {
        var result = await _authService.GetCurrentUserAsync(CurrentUserId, ct);
        return Success(result);
    }
}
