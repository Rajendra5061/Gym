using System.Text.Json;
using System.Text.Json.Serialization;
using GymManagement.Application.Common;
using GymManagement.Domain.Entities;
using GymManagement.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace GymManagement.Infrastructure.Services;

/// <summary>
/// Writes the audit trail. Auditing is a support concern: a failure here is logged and swallowed
/// so it can never break or roll back the business operation that triggered it.
/// </summary>
public sealed class AuditService : IAuditService
{
    /// <summary>Matches the OldValues / NewValues column length.</summary>
    private const int ValueColumnLength = 4000;

    private const int ActionLength = 64;
    private const int EntityNameLength = 64;
    private const int UserNameLength = 160;
    private const int DescriptionLength = 1000;
    private const int IpAddressLength = 64;
    private const int DeviceInfoLength = 256;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        NumberHandling = JsonNumberHandling.Strict
    };

    private readonly GymDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<AuditService> _logger;

    public AuditService(GymDbContext db, ICurrentUserService currentUser,
        IDateTimeProvider clock, ILogger<AuditService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task LogAsync(string action, string entityName, int? entityId = null,
        object? oldValues = null, object? newValues = null, string? description = null,
        CancellationToken ct = default) =>
        LogForUserAsync(_currentUser.UserId, _currentUser.UserName, action, entityName, entityId,
            oldValues, newValues, description, _currentUser.IpAddress, _currentUser.DeviceInfo, ct);

    public async Task LogForUserAsync(int? userId, string? userName, string action, string entityName,
        int? entityId = null, object? oldValues = null, object? newValues = null,
        string? description = null, string? ipAddress = null, string? deviceInfo = null,
        CancellationToken ct = default)
    {
        try
        {
            var entry = new AuditLog
            {
                UserId = userId,
                UserName = Truncate(userName, UserNameLength),
                Action = Truncate(action, ActionLength) ?? string.Empty,
                EntityName = Truncate(entityName, EntityNameLength) ?? string.Empty,
                EntityId = entityId,
                OldValues = Serialize(oldValues),
                NewValues = Serialize(newValues),
                Description = Truncate(description, DescriptionLength),
                IpAddress = Truncate(ipAddress, IpAddressLength),
                DeviceInfo = Truncate(deviceInfo, DeviceInfoLength),
                ChangedAtUtc = _clock.UtcNow
            };

            _db.AuditLogs.Add(entry);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Never let an audit failure surface to the caller.
            _logger.LogError(ex,
                "Failed to write audit log entry. Action={Action} Entity={EntityName} EntityId={EntityId} UserId={UserId}",
                action, entityName, entityId, userId);
        }
    }

    /// <summary>
    /// Serialises a snapshot to JSON, truncating to the column width. Strings are stored verbatim
    /// so callers may pass a pre-formatted summary.
    /// </summary>
    private string? Serialize(object? value)
    {
        if (value is null) return null;

        try
        {
            if (value is string text)
                return Truncate(text, ValueColumnLength);

            return Truncate(JsonSerializer.Serialize(value, SerializerOptions), ValueColumnLength);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to serialise audit values of type {Type}.", value.GetType().Name);
            return null;
        }
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
