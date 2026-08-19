namespace GymManagement.Application.Common;

/// <summary>Details of the caller, resolved from the JWT on the API side.</summary>
public interface ICurrentUserService
{
    int? UserId { get; }
    string? UserName { get; }
    string? FullName { get; }
    int? MemberId { get; }
    int? TrainerId { get; }
    IReadOnlyList<string> Roles { get; }
    IReadOnlyList<string> Permissions { get; }
    string? IpAddress { get; }
    string? DeviceInfo { get; }
    bool IsAuthenticated { get; }
    bool IsInRole(string role);
    bool HasPermission(string permissionCode);
}

/// <summary>Clock abstraction so that expiry and licensing logic stays unit testable.</summary>
public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
    DateTime Now { get; }
    DateTime Today { get; }
}

/// <summary>Password hashing and verification. Implemented with BCrypt.</summary>
public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
    /// <summary>Creates a random, readable temporary password for new accounts and resets.</summary>
    string GenerateTemporaryPassword(int length = 12);
}

/// <summary>Sequential business code generator (member codes, receipt numbers, …).</summary>
public interface ICodeGeneratorService
{
    Task<string> NextMemberCodeAsync(CancellationToken ct = default);
    Task<string> NextTrainerCodeAsync(CancellationToken ct = default);
    Task<string> NextPlanCodeAsync(CancellationToken ct = default);
    Task<string> NextSubscriptionCodeAsync(CancellationToken ct = default);
    Task<string> NextReceiptNumberAsync(CancellationToken ct = default);
    Task<string> NextRefundNumberAsync(CancellationToken ct = default);
    Task<string> NextExpenseNumberAsync(CancellationToken ct = default);
}

/// <summary>Writes an entry to the audit trail. Never throws into the calling operation.</summary>
public interface IAuditService
{
    Task LogAsync(string action, string entityName, int? entityId = null,
        object? oldValues = null, object? newValues = null, string? description = null,
        CancellationToken ct = default);

    Task LogForUserAsync(int? userId, string? userName, string action, string entityName,
        int? entityId = null, object? oldValues = null, object? newValues = null,
        string? description = null, string? ipAddress = null, string? deviceInfo = null,
        CancellationToken ct = default);
}
