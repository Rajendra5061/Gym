using System.Globalization;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Application.Interfaces;
using GymManagement.Domain.Entities;
using GymManagement.Domain.Enums;
using GymManagement.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GymManagement.Infrastructure.Services;

/// <summary>
/// Reads and writes the single gym profile row plus the key/value system settings table.
/// Every change is written to the audit trail as <see cref="AuditActions.ConfigurationChanged"/>.
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private readonly GymDbContext _db;
    private readonly IAuditService _audit;

    public SettingsService(GymDbContext db, IAuditService audit)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    public async Task<GymSettingsDto> GetGymSettingsAsync(CancellationToken ct = default)
    {
        var entity = await _db.GymSettings
            .AsNoTracking()
            .OrderBy(g => g.Id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (entity is not null) return Map(entity);

        // First run: materialise a default row so the rest of the application always has one.
        var created = new GymSetting();
        _db.GymSettings.Add(created);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return Map(created);
    }

    public async Task<GymBrandingDto> GetGymBrandingAsync(CancellationToken ct = default)
    {
        // Projected in the query rather than mapped from the full profile, so the columns holding
        // the UPI handle, tax number and lockout policy are never even read for this call.
        var branding = await _db.GymSettings
            .AsNoTracking()
            .OrderBy(g => g.Id)
            .Select(g => new GymBrandingDto
            {
                GymName = g.GymName,
                LogoPath = g.LogoPath,
                CurrencyCode = g.CurrencyCode,
                CurrencySymbol = g.CurrencySymbol,
                Phone = g.Phone,
                Email = g.Email
            })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (branding is not null) return branding;

        // First run: let the profile reader materialise the default row rather than duplicating it.
        return GymBrandingDto.From(await GetGymSettingsAsync(ct).ConfigureAwait(false));
    }

    public async Task<GymSettingsDto> UpdateGymSettingsAsync(GymSettingsDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (string.IsNullOrWhiteSpace(dto.GymName))
            throw new ValidationAppException(nameof(dto.GymName), "Gym name is required.");

        var entity = await _db.GymSettings
            .OrderBy(g => g.Id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            entity = new GymSetting();
            _db.GymSettings.Add(entity);
        }

        var before = Map(entity);

        entity.GymName = dto.GymName.Trim();
        entity.LegalName = Clean(dto.LegalName);
        entity.Address = Clean(dto.Address);
        entity.City = Clean(dto.City);
        entity.State = Clean(dto.State);
        entity.PostalCode = Clean(dto.PostalCode);
        entity.Country = Clean(dto.Country);
        entity.Phone = Clean(dto.Phone);
        entity.Email = Clean(dto.Email);
        entity.Website = Clean(dto.Website);
        entity.LogoPath = Clean(dto.LogoPath);
        entity.TaxNumber = Clean(dto.TaxNumber);
        entity.CurrencyCode = string.IsNullOrWhiteSpace(dto.CurrencyCode) ? entity.CurrencyCode : dto.CurrencyCode.Trim();
        entity.CurrencySymbol = string.IsNullOrWhiteSpace(dto.CurrencySymbol) ? entity.CurrencySymbol : dto.CurrencySymbol.Trim();
        entity.UpiId = Clean(dto.UpiId);
        entity.UpiPayeeName = Clean(dto.UpiPayeeName);
        entity.UpiQrImagePath = Clean(dto.UpiQrImagePath);
        entity.OpeningTime = dto.OpeningTime;
        entity.ClosingTime = dto.ClosingTime;
        entity.ReceiptPrefix = Clean(dto.ReceiptPrefix);
        entity.MemberCodePrefix = Clean(dto.MemberCodePrefix);
        entity.ReceiptFooterText = Clean(dto.ReceiptFooterText);
        entity.ExpiryReminderDays = dto.ExpiryReminderDays;
        entity.DefaultGracePeriodDays = dto.DefaultGracePeriodDays;
        entity.MaxFailedLoginAttempts = dto.MaxFailedLoginAttempts;
        entity.LockoutMinutes = dto.LockoutMinutes;
        entity.AllowExpiredMemberCheckIn = dto.AllowExpiredMemberCheckIn;

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        var after = Map(entity);

        await _audit.LogAsync(AuditActions.ConfigurationChanged, nameof(GymSetting), entity.Id,
            oldValues: before, newValues: after,
            description: "Gym settings updated.", ct).ConfigureAwait(false);

        return after;
    }

    public async Task<List<SystemSettingDto>> GetSystemSettingsAsync(string? category = null,
        CancellationToken ct = default)
    {
        var query = _db.SystemSettings.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(category))
        {
            var wanted = category.Trim();
            query = query.Where(s => s.Category == wanted);
        }

        return await query
            .OrderBy(s => s.Category)
            .ThenBy(s => s.Key)
            .Select(s => new SystemSettingDto
            {
                Id = s.Id,
                Key = s.Key,
                Value = s.Value,
                DataType = s.DataType,
                Category = s.Category,
                Description = s.Description,
                IsReadOnly = s.IsReadOnly
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<SystemSettingDto> UpsertSystemSettingAsync(SystemSettingDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (string.IsNullOrWhiteSpace(dto.Key))
            throw new ValidationAppException(nameof(dto.Key), "Setting key is required.");

        var key = dto.Key.Trim();

        var entity = await _db.SystemSettings
            .FirstOrDefaultAsync(s => s.Key == key, ct)
            .ConfigureAwait(false);

        SystemSettingDto? before = null;

        if (entity is null)
        {
            entity = new SystemSetting
            {
                Key = key,
                IsReadOnly = dto.IsReadOnly
            };
            _db.SystemSettings.Add(entity);
        }
        else
        {
            if (entity.IsReadOnly)
                throw new BusinessRuleAppException($"Setting '{key}' is read-only and cannot be changed.");

            before = MapSetting(entity);
            entity.IsReadOnly = dto.IsReadOnly;
        }

        entity.Value = dto.Value;
        entity.DataType = string.IsNullOrWhiteSpace(dto.DataType) ? "string" : dto.DataType.Trim();
        entity.Category = string.IsNullOrWhiteSpace(dto.Category) ? "General" : dto.Category.Trim();
        entity.Description = Clean(dto.Description);

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        var after = MapSetting(entity);

        await _audit.LogAsync(AuditActions.ConfigurationChanged, nameof(SystemSetting), entity.Id,
            oldValues: before, newValues: after,
            description: $"System setting '{key}' {(before is null ? "created" : "updated")}.", ct)
            .ConfigureAwait(false);

        return after;
    }

    public Task<string?> GetValueAsync(string key, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key)) return Task.FromResult<string?>(null);

        var wanted = key.Trim();
        return _db.SystemSettings
            .AsNoTracking()
            .Where(s => s.Key == wanted)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<T?> GetValueAsync<T>(string key, T? defaultValue = default, CancellationToken ct = default)
    {
        var raw = await GetValueAsync(key, ct).ConfigureAwait(false);
        return Convert<T>(raw, defaultValue);
    }

    /// <summary>Parses a stored string into <typeparamref name="T"/>, using invariant culture.</summary>
    private static T? Convert<T>(string? raw, T? defaultValue)
    {
        if (string.IsNullOrWhiteSpace(raw)) return defaultValue;

        var text = raw.Trim();
        var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);

        try
        {
            if (target == typeof(string))
                return (T)(object)text;

            if (target == typeof(bool))
            {
                if (bool.TryParse(text, out var flag)) return (T)(object)flag;

                return text.ToLowerInvariant() switch
                {
                    "1" or "yes" or "y" or "on" or "enabled" => (T)(object)true,
                    "0" or "no" or "n" or "off" or "disabled" => (T)(object)false,
                    _ => defaultValue
                };
            }

            if (target == typeof(int))
                return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)
                    ? (T)(object)i : defaultValue;

            if (target == typeof(long))
                return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)
                    ? (T)(object)l : defaultValue;

            // Money and rates are decimal; floating point is never used for currency.
            if (target == typeof(decimal))
                return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)
                    ? (T)(object)d : defaultValue;

            if (target == typeof(DateTime))
                return DateTime.TryParse(text, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var dt) ? (T)(object)dt : defaultValue;

            if (target == typeof(TimeSpan))
                return TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var ts)
                    ? (T)(object)ts : defaultValue;

            if (target == typeof(Guid))
                return Guid.TryParse(text, out var guid) ? (T)(object)guid : defaultValue;

            if (target.IsEnum)
                return Enum.TryParse(target, text, ignoreCase: true, out var enumValue) && enumValue is not null
                    ? (T)enumValue : defaultValue;

            return (T)System.Convert.ChangeType(text, target, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException or ArgumentException)
        {
            return defaultValue;
        }
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static SystemSettingDto MapSetting(SystemSetting s) => new()
    {
        Id = s.Id,
        Key = s.Key,
        Value = s.Value,
        DataType = s.DataType,
        Category = s.Category,
        Description = s.Description,
        IsReadOnly = s.IsReadOnly
    };

    private static GymSettingsDto Map(GymSetting g) => new()
    {
        Id = g.Id,
        GymName = g.GymName,
        LegalName = g.LegalName,
        Address = g.Address,
        City = g.City,
        State = g.State,
        PostalCode = g.PostalCode,
        Country = g.Country,
        Phone = g.Phone,
        Email = g.Email,
        Website = g.Website,
        LogoPath = g.LogoPath,
        TaxNumber = g.TaxNumber,
        CurrencyCode = g.CurrencyCode,
        CurrencySymbol = g.CurrencySymbol,
        UpiId = g.UpiId,
        UpiPayeeName = g.UpiPayeeName,
        UpiQrImagePath = g.UpiQrImagePath,
        OpeningTime = g.OpeningTime,
        ClosingTime = g.ClosingTime,
        ReceiptPrefix = g.ReceiptPrefix,
        MemberCodePrefix = g.MemberCodePrefix,
        ReceiptFooterText = g.ReceiptFooterText,
        ExpiryReminderDays = g.ExpiryReminderDays,
        DefaultGracePeriodDays = g.DefaultGracePeriodDays,
        MaxFailedLoginAttempts = g.MaxFailedLoginAttempts,
        LockoutMinutes = g.LockoutMinutes,
        AllowExpiredMemberCheckIn = g.AllowExpiredMemberCheckIn
    };
}
