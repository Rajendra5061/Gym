using System.Globalization;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Application.Interfaces;
using GymManagement.Domain.Entities;
using GymManagement.Domain.Enums;
using GymManagement.Infrastructure.Data;
using GymManagement.Infrastructure.Licensing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GymManagement.Infrastructure.Services;

/// <summary>
/// Trial and commercial licensing for this installation. There is at most one <see cref="License"/>
/// row; it holds the signed key, the quota limits and the clock high-water mark.
/// <para>
/// IMPORTANT: this is an offline check and therefore only a deterrent. Everything it trusts (the
/// local clock, the local database, the shared secret in configuration) lives on the customer's
/// machine. A production deployment must validate the licence against a server-side licensing
/// service, using the server's clock and an asymmetric signature, and treat the local record as a
/// cache only.
/// </para>
/// </summary>
public sealed class LicenseService : ILicenseService
{
    /// <summary>Warn the operator this many days before the licence or trial ends.</summary>
    private const int ExpiringSoonDays = 7;

    /// <summary>Tolerance before a backwards clock is treated as tampering, not as clock drift.</summary>
    private const int ClockToleranceDays = 1;

    private const int DefaultTrialDays = 30;
    private const int DefaultTrialMaxMembers = 50;

    // Column widths from LicenseConfiguration.
    private const int LicenseKeyLength = 256;
    private const int CustomerNameLength = 200;
    private const int GymIdentifierLength = 120;

    public const string MembersResource = "members";
    public const string UsersResource = "users";

    private readonly GymDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditService _audit;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LicenseService> _logger;

    public LicenseService(
        GymDbContext db,
        IDateTimeProvider clock,
        ICurrentUserService currentUser,
        IAuditService audit,
        IConfiguration configuration,
        ILogger<LicenseService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ----------------------------------------------------------------- read

    public async Task<LicenseStatusDto> GetStatusAsync(CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var license = await LoadAsync(ct).ConfigureAwait(false);

        var members = await _db.Members.CountAsync(ct).ConfigureAwait(false);
        var users = await _db.Users.CountAsync(ct).ConfigureAwait(false);

        if (license is null)
        {
            return new LicenseStatusDto
            {
                Status = LicenseStatus.NotActivated,
                IsValid = false,
                CurrentMembers = members,
                CurrentUsers = users,
                Message = "This installation is not licensed yet. Start the free trial or activate a licence key."
            };
        }

        var tampered = IsClockTampered(license, now);

        if (!tampered)
        {
            // Advance the high-water mark so the clock can never be rolled back past this point.
            if (license.HighWaterMarkUtc is null || now > license.HighWaterMarkUtc.Value)
                license.HighWaterMarkUtc = now;

            license.LastValidatedUtc = now;
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        var dto = Map(license, now, members, users);

        if (tampered)
        {
            dto.ClockTamperDetected = true;
            dto.IsValid = false;
            dto.IsExpiringSoon = false;
            dto.Message = "The system clock appears to have been moved backwards. " +
                          "Correct the date and time on this machine, then reopen the licence screen.";
        }

        return dto;
    }

    // ------------------------------------------------------------ activation

    public async Task<LicenseStatusDto> StartTrialAsync(StartTrialDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (string.IsNullOrWhiteSpace(dto.CustomerName))
            throw new ValidationAppException(nameof(dto.CustomerName), "Customer name is required.");

        var now = _clock.UtcNow;
        var license = await LoadAsync(ct).ConfigureAwait(false);

        // A trial may be started exactly once: an existing trial, active licence, expired licence or
        // revoked licence all block it, otherwise the trial could be restarted forever.
        if (license is not null && license.Status != LicenseStatus.NotActivated)
        {
            throw new ConflictAppException(license.IsTrial
                ? "The trial has already been started on this installation."
                : "This installation is already licensed.");
        }

        var trialDays = ReadInt("License:TrialDays", DefaultTrialDays);
        var maxMembers = ReadInt("License:TrialMaxMembers", DefaultTrialMaxMembers);

        if (license is null)
        {
            license = new License();
            _db.Licenses.Add(license);
        }

        license.LicenseKey = null;
        license.Signature = null;
        license.Status = LicenseStatus.Trial;
        license.IsTrial = true;
        license.TrialDays = trialDays;
        license.CustomerName = Truncate(dto.CustomerName.Trim(), CustomerNameLength);
        license.GymIdentifier = Truncate(string.IsNullOrWhiteSpace(dto.GymIdentifier)
            ? Guid.NewGuid().ToString("N")
            : dto.GymIdentifier.Trim(), GymIdentifierLength);
        license.MachineId = LicenseKeyProtector.ComputeMachineId();
        license.StartDateUtc = now;
        license.ExpiryDateUtc = now.AddDays(trialDays);
        license.MaxMembers = maxMembers;
        license.MaxUsers = null;
        license.EnabledFeatures = null;
        license.HighWaterMarkUtc = now;
        license.LastValidatedUtc = now;
        license.Notes = $"Trial started on {now.ToString("u", CultureInfo.InvariantCulture)} by {Operator()}.";

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(AuditActions.LicenseActivated, nameof(License), license.Id,
            newValues: new { license.Status, license.IsTrial, license.ExpiryDateUtc, license.MaxMembers },
            description: $"{trialDays} day trial started for '{license.CustomerName}'.", ct: ct)
            .ConfigureAwait(false);

        return await GetStatusAsync(ct).ConfigureAwait(false);
    }

    public async Task<LicenseStatusDto> ActivateAsync(ActivateLicenseDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (string.IsNullOrWhiteSpace(dto.LicenseKey))
            throw new ValidationAppException(nameof(dto.LicenseKey), "Licence key is required.");

        var key = dto.LicenseKey.Trim();

        // Longer than the column can hold, so it cannot be one of our keys.
        if (key.Length > LicenseKeyLength)
            throw new LicenseAppException("The licence key is not valid.");

        var secret = ReadSecret();

        if (secret is null)
            throw new LicenseAppException("The licence key is not valid.");

        if (!LicenseKeyProtector.TryParseKey(key, secret, out var payload) || payload is null)
        {
            _logger.LogWarning("Licence activation rejected: the key failed signature or format validation.");
            throw new LicenseAppException("The licence key is not valid.");
        }

        var now = _clock.UtcNow;

        if (payload.ExpiryUtc <= now)
        {
            throw new LicenseAppException(
                $"This licence key expired on {payload.ExpiryUtc.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)}.");
        }

        var license = await LoadAsync(ct).ConfigureAwait(false);

        if (license is not null
            && license.Status != LicenseStatus.NotActivated
            && !string.IsNullOrWhiteSpace(license.GymIdentifier)
            && !string.Equals(license.GymIdentifier, payload.GymId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictAppException(
                "This licence key was issued for a different gym installation and cannot be used here.");
        }

        var currentId = license?.Id ?? 0;

        var duplicate = await _db.Licenses
            .AsNoTracking()
            .AnyAsync(l => l.LicenseKey == key && l.Id != currentId, ct)
            .ConfigureAwait(false);

        if (duplicate)
            throw new ConflictAppException("This licence key has already been activated on this installation.");

        if (license is null)
        {
            license = new License();
            _db.Licenses.Add(license);
        }

        license.LicenseKey = key;
        license.Status = LicenseStatus.Active;
        license.IsTrial = false;
        license.CustomerName = string.IsNullOrWhiteSpace(dto.CustomerName)
            ? (string.IsNullOrWhiteSpace(payload.Customer) ? license.CustomerName : payload.Customer.Trim())
            : dto.CustomerName.Trim();
        license.CustomerName = Truncate(license.CustomerName, CustomerNameLength);
        license.GymIdentifier = Truncate(payload.GymId.Trim(), GymIdentifierLength);
        license.MachineId = LicenseKeyProtector.ComputeMachineId();
        license.StartDateUtc = now;
        license.ExpiryDateUtc = payload.ExpiryUtc;
        license.MaxMembers = payload.MaxMembers;
        license.MaxUsers = payload.MaxUsers;
        license.EnabledFeatures = payload.Features is { Length: > 0 }
            ? string.Join(',', payload.Features.Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f.Trim()))
            : null;

        // Keeps an HMAC of the stored key so a hand-edited Licenses row can be spotted.
        license.Signature = LicenseKeyProtector.Sign(key, secret);

        // A fresh licence resets the tamper baseline; the old high-water mark belonged to the trial.
        license.HighWaterMarkUtc = now;
        license.LastValidatedUtc = now;
        license.Notes = $"Activated on {now.ToString("u", CultureInfo.InvariantCulture)} by {Operator()}.";

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(AuditActions.LicenseActivated, nameof(License), license.Id,
            newValues: new { license.Status, license.ExpiryDateUtc, license.MaxMembers, license.MaxUsers },
            description: $"Licence activated for '{license.CustomerName}' until " +
                         $"{payload.ExpiryUtc.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)}.", ct: ct)
            .ConfigureAwait(false);

        return await GetStatusAsync(ct).ConfigureAwait(false);
    }

    // ------------------------------------------------------------- guardrails

    public async Task EnsureValidAsync(CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var license = await LoadAsync(ct).ConfigureAwait(false);

        if (license is null)
        {
            throw new LicenseAppException(
                "This installation is not licensed. Start the free trial or activate a licence key to continue.");
        }

        if (license.Status == LicenseStatus.NotActivated)
        {
            throw new LicenseAppException(
                "This installation is not licensed. Start the free trial or activate a licence key to continue.");
        }

        if (license.Status == LicenseStatus.Revoked)
            throw new LicenseAppException("This licence has been revoked. Contact your supplier.");

        if (IsClockTampered(license, now))
        {
            throw new LicenseAppException(
                "The system clock appears to have been moved backwards. Correct the date and time on this machine.");
        }

        if (license.Status == LicenseStatus.Expired)
        {
            throw new LicenseAppException(license.IsTrial
                ? "The trial period has ended. Activate a licence key to continue."
                : "The licence has expired. Renew it to continue.");
        }

        if (license.ExpiryDateUtc is null || license.ExpiryDateUtc.Value <= now)
        {
            license.Status = LicenseStatus.Expired;
            license.LastValidatedUtc = now;
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

            throw new LicenseAppException(license.IsTrial
                ? "The trial period has ended. Activate a licence key to continue."
                : "The licence has expired. Renew it to continue.");
        }
    }

    public async Task EnsureCapacityAsync(string resource, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(resource)) return;

        var license = await _db.Licenses
            .AsNoTracking()
            .OrderBy(l => l.Id)
            .Select(l => new { l.MaxMembers, l.MaxUsers })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (license is null) return;

        switch (resource.Trim().ToLowerInvariant())
        {
            case MembersResource:
                await EnforceAsync(license.MaxMembers, () => _db.Members.CountAsync(ct), "members")
                    .ConfigureAwait(false);
                return;

            case UsersResource:
                await EnforceAsync(license.MaxUsers, () => _db.Users.CountAsync(ct), "user accounts")
                    .ConfigureAwait(false);
                return;

            default:
                // Unknown resources are not licensed quantities: nothing to enforce.
                return;
        }
    }

    /// <summary>The count query only runs when the licence actually caps this resource.</summary>
    private static async Task EnforceAsync(int? limit, Func<Task<int>> countAsync, string label)
    {
        if (limit is null or <= 0) return;

        var current = await countAsync().ConfigureAwait(false);

        if (current >= limit.Value)
        {
            throw new LicenseAppException(
                $"This licence allows a maximum of {limit.Value} {label}; {current} are already in use. " +
                "Upgrade the licence to add more.");
        }
    }

    // -------------------------------------------------------------- helpers

    /// <summary>Who performed the licence change, for the Notes column.</summary>
    private string Operator() =>
        string.IsNullOrWhiteSpace(_currentUser.UserName) ? "the system" : _currentUser.UserName!;

    private Task<License?> LoadAsync(CancellationToken ct) =>
        _db.Licenses.OrderBy(l => l.Id).FirstOrDefaultAsync(ct);

    /// <summary>
    /// True when the local clock is more than a day behind the highest date this installation has
    /// already seen, which is the classic "roll the clock back to extend the trial" attempt.
    /// </summary>
    private static bool IsClockTampered(License license, DateTime now) =>
        license.HighWaterMarkUtc.HasValue &&
        now < license.HighWaterMarkUtc.Value.AddDays(-ClockToleranceDays);

    private LicenseStatusDto Map(License license, DateTime now, int members, int users)
    {
        var days = license.ExpiryDateUtc.HasValue
            ? (int)Math.Ceiling((license.ExpiryDateUtc.Value - now).TotalDays)
            : 0;

        if (days < 0) days = 0;

        var expired = license.Status is LicenseStatus.Expired
                      || license.ExpiryDateUtc is null
                      || license.ExpiryDateUtc.Value <= now;

        var isValid = (license.Status is LicenseStatus.Trial or LicenseStatus.Active) && !expired;

        var dto = new LicenseStatusDto
        {
            Status = expired && license.Status is LicenseStatus.Trial or LicenseStatus.Active
                ? LicenseStatus.Expired
                : license.Status,
            LicenseKey = license.LicenseKey,
            MaskedLicenseKey = Mask(license.LicenseKey),
            CustomerName = license.CustomerName,
            GymIdentifier = license.GymIdentifier,
            IsTrial = license.IsTrial,
            StartDateUtc = license.StartDateUtc,
            ExpiryDateUtc = license.ExpiryDateUtc,
            DaysRemaining = days,
            IsValid = isValid,
            IsExpiringSoon = isValid && days <= ExpiringSoonDays,
            MaxMembers = license.MaxMembers,
            CurrentMembers = members,
            MaxUsers = license.MaxUsers,
            CurrentUsers = users,
            EnabledFeatures = SplitFeatures(license.EnabledFeatures)
        };

        dto.Message = dto.Status switch
        {
            LicenseStatus.Revoked => "This licence has been revoked. Contact your supplier.",
            LicenseStatus.Expired when license.IsTrial => "The trial period has ended. Activate a licence key to continue.",
            LicenseStatus.Expired => "The licence has expired. Renew it to continue.",
            LicenseStatus.NotActivated => "This installation is not licensed yet. Start the free trial or activate a licence key.",
            _ when dto.IsExpiringSoon && license.IsTrial => $"The trial ends in {days} day(s).",
            _ when dto.IsExpiringSoon => $"The licence expires in {days} day(s).",
            _ => null
        };

        return dto;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private static List<string> SplitFeatures(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? new List<string>()
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    /// <summary>Shows just enough of the key for the operator to recognise it.</summary>
    private static string? Mask(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;

        var trimmed = key.Trim();
        return trimmed.Length <= 12 ? new string('*', trimmed.Length) : $"{trimmed[..8]}…{trimmed[^4..]}";
    }

    /// <summary>
    /// The signing secret is deployment configuration, never a compiled-in constant. Without it no
    /// key can be verified, so every key is treated as invalid while the trial flow keeps working.
    /// </summary>
    private string? ReadSecret()
    {
        var secret = _configuration["License:Secret"];

        if (!string.IsNullOrWhiteSpace(secret)) return secret;

        _logger.LogWarning(
            "License:Secret is not configured, so no licence key can be verified. Configure it before shipping.");
        return null;
    }

    private int ReadInt(string configurationKey, int fallback)
    {
        var raw = _configuration[configurationKey];

        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : fallback;
    }
}
