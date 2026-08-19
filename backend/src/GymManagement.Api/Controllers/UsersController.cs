using GymManagement.Api.Authorization;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Application.Interfaces;
using GymManagement.Domain.Constants;
using GymManagement.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace GymManagement.Api.Controllers;

/// <summary>Administration of login accounts: listing, creation, status changes and password resets.</summary>
[Route("api/users")]
public sealed class UsersController : ApiControllerBase
{
    private readonly IUserService _userService;

    public UsersController(IUserService userService) => _userService = userService;

    /// <summary>Returns a paged, filterable list of user accounts.</summary>
    [HttpGet]
    [HasPermission(Permissions.UsersView)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<UserListDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<PagedResult<UserListDto>>>> GetPaged(
        [FromQuery] UserQueryDto query, CancellationToken ct)
    {
        var result = await _userService.GetPagedAsync(query, ct);
        return Success(result);
    }

    /// <summary>Returns a single user account with its roles and lockout state.</summary>
    [HttpGet("{id:int}")]
    [HasPermission(Permissions.UsersView)]
    [ProducesResponseType(typeof(ApiResponse<UserDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<UserDetailDto>>> GetById(int id, CancellationToken ct)
    {
        var result = await _userService.GetByIdAsync(id, ct);
        return Success(result);
    }

    /// <summary>Creates a user account and returns the generated temporary password.</summary>
    [HttpPost]
    [HasPermission(Permissions.UsersManage)]
    [ProducesResponseType(typeof(ApiResponse<TemporaryPasswordDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<TemporaryPasswordDto>>> Create(
        [FromBody] CreateUserDto dto, CancellationToken ct)
    {
        var result = await _userService.CreateAsync(dto, ct);
        return Success(result, "User account created.");
    }

    /// <summary>Updates the profile, status and role assignments of a user account.</summary>
    [HttpPut("{id:int}")]
    [HasPermission(Permissions.UsersManage)]
    [ProducesResponseType(typeof(ApiResponse<UserDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<UserDetailDto>>> Update(
        int id, [FromBody] UpdateUserDto dto, CancellationToken ct)
    {
        dto.Id = id;
        var result = await _userService.UpdateAsync(dto, ct);
        return Success(result, "User account updated.");
    }

    /// <summary>Resets a user's password and returns the new temporary password.</summary>
    [HttpPost("{id:int}/reset-password")]
    [HasPermission(Permissions.UsersManage)]
    [ProducesResponseType(typeof(ApiResponse<TemporaryPasswordDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<TemporaryPasswordDto>>> ResetPassword(
        int id, CancellationToken ct)
    {
        var result = await _userService.ResetPasswordAsync(id, ct);
        return Success(result, "Password reset.");
    }

    /// <summary>Activates, deactivates or locks a user account.</summary>
    [HttpPost("{id:int}/status")]
    [HasPermission(Permissions.UsersManage)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> SetStatus(
        int id, [FromBody] UserStatus status, CancellationToken ct)
    {
        await _userService.SetStatusAsync(id, status, ct);
        return SuccessMessage($"User status set to {status}.");
    }

    /// <summary>Clears a lockout and resets the failed sign-in counter.</summary>
    [HttpPost("{id:int}/unlock")]
    [HasPermission(Permissions.UsersManage)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> Unlock(int id, CancellationToken ct)
    {
        await _userService.UnlockAsync(id, ct);
        return SuccessMessage("User account unlocked.");
    }

    /// <summary>Soft deletes a user account so it can be restored from the recycle bin.</summary>
    [HttpDelete("{id:int}")]
    [HasPermission(Permissions.UsersManage)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> Delete(int id, CancellationToken ct)
    {
        await _userService.SoftDeleteAsync(id, ct);
        return SuccessMessage("User account deleted.");
    }

    /// <summary>Returns id/name pairs of user accounts for combo boxes.</summary>
    [HttpGet("lookup")]
    [HasPermission(Permissions.UsersView)]
    [ProducesResponseType(typeof(ApiResponse<List<LookupDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<LookupDto>>>> GetLookup(CancellationToken ct)
    {
        var result = await _userService.GetLookupAsync(ct);
        return Success(result);
    }
}
