using GymManagement.Application.Common;
using GymManagement.Application.DTOs;

namespace GymManagement.Application.Interfaces;

public interface IAuthService
{
    Task<LoginResponseDto> LoginAsync(LoginRequestDto request, string? ipAddress, CancellationToken ct = default);
    Task<LoginResponseDto> RefreshTokenAsync(RefreshTokenRequestDto request, string? ipAddress, CancellationToken ct = default);
    Task LogoutAsync(LogoutRequestDto request, CancellationToken ct = default);
    Task<ForgotPasswordResponseDto> ForgotPasswordAsync(ForgotPasswordRequestDto request, CancellationToken ct = default);
    Task ResetPasswordAsync(ResetPasswordRequestDto request, CancellationToken ct = default);
    Task ChangePasswordAsync(int userId, ChangePasswordRequestDto request, CancellationToken ct = default);
    Task<CurrentUserDto> GetCurrentUserAsync(int userId, CancellationToken ct = default);
}

/// <summary>Issues and validates JWT access tokens and opaque refresh tokens.</summary>
public interface IJwtTokenService
{
    (string Token, DateTime ExpiresUtc) CreateAccessToken(int userId, string userName, string fullName,
        int? memberId, int? trainerId, IEnumerable<string> roles, IEnumerable<string> permissions);

    (string Token, string TokenHash, DateTime ExpiresUtc) CreateRefreshToken();

    string HashToken(string token);
}

public interface IUserService
{
    Task<PagedResult<UserListDto>> GetPagedAsync(UserQueryDto query, CancellationToken ct = default);
    Task<UserDetailDto> GetByIdAsync(int id, CancellationToken ct = default);
    Task<TemporaryPasswordDto> CreateAsync(CreateUserDto dto, CancellationToken ct = default);
    Task<UserDetailDto> UpdateAsync(UpdateUserDto dto, CancellationToken ct = default);
    Task<TemporaryPasswordDto> ResetPasswordAsync(int userId, CancellationToken ct = default);
    Task SetStatusAsync(int userId, Domain.Enums.UserStatus status, CancellationToken ct = default);
    Task UnlockAsync(int userId, CancellationToken ct = default);
    Task SoftDeleteAsync(int userId, CancellationToken ct = default);
    Task<List<LookupDto>> GetLookupAsync(CancellationToken ct = default);
}

public interface IRoleService
{
    Task<List<RoleDto>> GetAllAsync(CancellationToken ct = default);
    Task<RoleDto> GetByIdAsync(int id, CancellationToken ct = default);
    Task<RoleDto> CreateAsync(SaveRoleDto dto, CancellationToken ct = default);
    Task<RoleDto> UpdateAsync(SaveRoleDto dto, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
    Task<List<PermissionGroupDto>> GetPermissionGroupsAsync(CancellationToken ct = default);
    Task SetRolePermissionsAsync(int roleId, IEnumerable<string> permissionCodes, CancellationToken ct = default);
}
