using GymManagement.Api.Authorization;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Application.Interfaces;
using GymManagement.Domain.Constants;
using Microsoft.AspNetCore.Mvc;

namespace GymManagement.Api.Controllers;

/// <summary>Roles and the permission codes granted to them.</summary>
[Route("api/roles")]
public sealed class RolesController : ApiControllerBase
{
    private readonly IRoleService _roleService;

    public RolesController(IRoleService roleService) => _roleService = roleService;

    /// <summary>Returns every role with its user count and granted permissions.</summary>
    [HttpGet]
    [HasPermission(Permissions.UsersView)]
    [ProducesResponseType(typeof(ApiResponse<List<RoleDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<RoleDto>>>> GetAll(CancellationToken ct)
    {
        var result = await _roleService.GetAllAsync(ct);
        return Success(result);
    }

    /// <summary>Returns a single role with its granted permissions.</summary>
    [HttpGet("{id:int}")]
    [HasPermission(Permissions.UsersView)]
    [ProducesResponseType(typeof(ApiResponse<RoleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<RoleDto>>> GetById(int id, CancellationToken ct)
    {
        var result = await _roleService.GetByIdAsync(id, ct);
        return Success(result);
    }

    /// <summary>Creates a role together with its initial permission grants.</summary>
    [HttpPost]
    [HasPermission(Permissions.RolesManage)]
    [ProducesResponseType(typeof(ApiResponse<RoleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<RoleDto>>> Create(
        [FromBody] SaveRoleDto dto, CancellationToken ct)
    {
        var result = await _roleService.CreateAsync(dto, ct);
        return Success(result, "Role created.");
    }

    /// <summary>Renames a role and replaces its permission grants.</summary>
    [HttpPut("{id:int}")]
    [HasPermission(Permissions.RolesManage)]
    [ProducesResponseType(typeof(ApiResponse<RoleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<RoleDto>>> Update(
        int id, [FromBody] SaveRoleDto dto, CancellationToken ct)
    {
        dto.Id = id;
        var result = await _roleService.UpdateAsync(dto, ct);
        return Success(result, "Role updated.");
    }

    /// <summary>Deletes a role. Built-in system roles cannot be removed.</summary>
    [HttpDelete("{id:int}")]
    [HasPermission(Permissions.RolesManage)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> Delete(int id, CancellationToken ct)
    {
        await _roleService.DeleteAsync(id, ct);
        return SuccessMessage("Role deleted.");
    }

    /// <summary>Returns every known permission grouped by module, for the permissions matrix.</summary>
    [HttpGet("permissions")]
    [HasPermission(Permissions.UsersView)]
    [ProducesResponseType(typeof(ApiResponse<List<PermissionGroupDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<PermissionGroupDto>>>> GetPermissionGroups(
        CancellationToken ct)
    {
        var result = await _roleService.GetPermissionGroupsAsync(ct);
        return Success(result);
    }

    /// <summary>Replaces the permission codes granted to a role.</summary>
    [HttpPut("{id:int}/permissions")]
    [HasPermission(Permissions.RolesManage)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> SetPermissions(
        int id, [FromBody] List<string> permissionCodes, CancellationToken ct)
    {
        await _roleService.SetRolePermissionsAsync(id, permissionCodes, ct);
        return SuccessMessage("Role permissions updated.");
    }
}
