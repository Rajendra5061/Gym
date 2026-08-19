namespace GymManagement.Application.DTOs;

public class LoginRequestDto
{
    public string UserNameOrEmail { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? DeviceInfo { get; set; }
}

public class LoginResponseDto
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime AccessTokenExpiresUtc { get; set; }
    public DateTime RefreshTokenExpiresUtc { get; set; }
    public bool MustChangePassword { get; set; }
    public CurrentUserDto User { get; set; } = new();
}

public class CurrentUserDto
{
    public int Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? ProfilePhotoPath { get; set; }
    public int? MemberId { get; set; }
    public int? TrainerId { get; set; }
    public List<string> Roles { get; set; } = new();
    public List<string> Permissions { get; set; } = new();
}

public class RefreshTokenRequestDto
{
    public string RefreshToken { get; set; } = string.Empty;
}

public class ForgotPasswordRequestDto
{
    public string UserNameOrEmail { get; set; } = string.Empty;
}

/// <summary>
/// Result of the forgot-password request. The token is returned directly because this build has no
/// email provider configured; wire up an email/SMS sender before production use.
/// </summary>
public class ForgotPasswordResponseDto
{
    public string Message { get; set; } = string.Empty;
    public string? ResetToken { get; set; }
    public DateTime? ExpiresUtc { get; set; }
}

public class ResetPasswordRequestDto
{
    public string UserNameOrEmail { get; set; } = string.Empty;
    public string ResetToken { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class ChangePasswordRequestDto
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class LogoutRequestDto
{
    public string? RefreshToken { get; set; }
}
