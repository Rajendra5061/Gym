using GymManagement.Api.Authorization;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Application.Interfaces;
using GymManagement.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymManagement.Api.Controllers;

/// <summary>Gym profile, system settings, mail diagnostics, licensing, backups and shared lookup data.</summary>
public sealed class SettingsController : ApiControllerBase
{
    private readonly ISettingsService _settings;
    private readonly ILicenseService _license;
    private readonly IBackupService _backups;
    private readonly ILookupService _lookups;
    private readonly IEmailDiagnosticsService _email;

    public SettingsController(ISettingsService settings, ILicenseService license,
        IBackupService backups, ILookupService lookups, IEmailDiagnosticsService email)
    {
        _settings = settings;
        _license = license;
        _backups = backups;
        _lookups = lookups;
        _email = email;
    }

    /// <summary>Returns the gym profile used on receipts, reports and the UPI screen.</summary>
    [HttpGet("gym")]
    [HasPermission(Permissions.SettingsView)]
    [ProducesResponseType(typeof(ApiResponse<GymSettingsDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<GymSettingsDto>>> GetGymSettings(CancellationToken ct) =>
        Success(await _settings.GetGymSettingsAsync(ct));

    /// <summary>
    /// Branding for the application shell: gym name, logo, currency and the front-desk contact
    /// details already published on the anonymous landing page.
    /// </summary>
    /// <remarks>
    /// Anonymous — the landing page and the login screens print this data before anyone signs
    /// in, and every field is public by nature (the name on the door, the front-desk contact,
    /// the currency on the price list). The full profile on <c>GET /api/settings/gym</c> stays
    /// behind <c>settings.view</c> because it also carries the UPI payment handle, the tax
    /// number, receipt/member numbering and the account-lockout thresholds.
    /// </remarks>
    [HttpGet("branding")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<GymBrandingDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<GymBrandingDto>>> GetGymBranding(CancellationToken ct) =>
        Success(await _settings.GetGymBrandingAsync(ct));

    /// <summary>Updates the gym profile.</summary>
    [HttpPut("gym")]
    [HasPermission(Permissions.SettingsManage)]
    [ProducesResponseType(typeof(ApiResponse<GymSettingsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<GymSettingsDto>>> UpdateGymSettings(
        [FromBody] GymSettingsDto dto, CancellationToken ct) =>
        Success(await _settings.UpdateGymSettingsAsync(dto, ct), "Gym settings updated.");

    /// <summary>Returns system settings, optionally restricted to one category.</summary>
    [HttpGet("system")]
    [HasPermission(Permissions.SettingsView)]
    [ProducesResponseType(typeof(ApiResponse<List<SystemSettingDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<SystemSettingDto>>>> GetSystemSettings(
        [FromQuery] string? category, CancellationToken ct) =>
        Success(await _settings.GetSystemSettingsAsync(category, ct));

    /// <summary>Creates or updates a single system setting.</summary>
    [HttpPut("system")]
    [HasPermission(Permissions.SettingsManage)]
    [ProducesResponseType(typeof(ApiResponse<SystemSettingDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<SystemSettingDto>>> UpsertSystemSetting(
        [FromBody] SystemSettingDto dto, CancellationToken ct) =>
        Success(await _settings.UpsertSystemSettingAsync(dto, ct), "Setting saved.");

    /// <summary>The mail provider actually in force on this server, and the settings it resolved.</summary>
    /// <remarks>
    /// Read from the live sender, not from the configuration file: a section naming SMTP but no
    /// host produced the null sender at start-up, and this reports the sender that exists.
    ///
    /// Never carries the SMTP password. It is supplied through <c>Email__Smtp__Password</c>, is not
    /// stored in the database, and <c>smtpCredentialsConfigured</c> says only whether one resolved.
    /// </remarks>
    [HttpGet("email")]
    [HasPermission(Permissions.SettingsView)]
    [ProducesResponseType(typeof(ApiResponse<EmailStatusDto>), StatusCodes.Status200OK)]
    public ActionResult<ApiResponse<EmailStatusDto>> GetEmailStatus() =>
        Success(_email.GetStatus());

    /// <summary>Sends one test message through whichever provider is live.</summary>
    /// <remarks>
    /// Answers 200 whether or not the message went: a refused relay or a provider that is off is
    /// the diagnosis the caller asked for, and comes back as <c>sent: false</c> with the reason.
    /// Only a malformed address is a 400. The body is fixed — the caller chooses the recipient and
    /// nothing else — so this cannot be used to send somebody else's words from the gym's address.
    /// </remarks>
    [HttpPost("email/test")]
    [HasPermission(Permissions.SettingsManage)]
    [ProducesResponseType(typeof(ApiResponse<EmailTestResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<EmailTestResultDto>>> SendTestEmail(
        [FromBody] SendTestEmailDto dto, CancellationToken ct)
    {
        var result = await _email.SendTestAsync(dto, ct);

        return Success(result, result.Sent
            ? "Test message accepted by the mail provider."
            : "Nothing was sent — see the reason below.");
    }

    /// <summary>Current licence or trial status, including quota usage.</summary>
    [HttpGet("license")]
    [HasPermission(Permissions.LicenseManage)]
    [ProducesResponseType(typeof(ApiResponse<LicenseStatusDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<LicenseStatusDto>>> GetLicenseStatus(CancellationToken ct) =>
        Success(await _license.GetStatusAsync(ct));

    /// <summary>Starts the evaluation trial for this installation.</summary>
    [HttpPost("license/trial")]
    [HasPermission(Permissions.LicenseManage)]
    [ProducesResponseType(typeof(ApiResponse<LicenseStatusDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<LicenseStatusDto>>> StartTrial(
        [FromBody] StartTrialDto dto, CancellationToken ct) =>
        Success(await _license.StartTrialAsync(dto, ct), "Trial started.");

    /// <summary>Activates a purchased licence key.</summary>
    [HttpPost("license/activate")]
    [HasPermission(Permissions.LicenseManage)]
    [ProducesResponseType(typeof(ApiResponse<LicenseStatusDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<LicenseStatusDto>>> ActivateLicense(
        [FromBody] ActivateLicenseDto dto, CancellationToken ct) =>
        Success(await _license.ActivateAsync(dto, ct), "Licence activated.");

    /// <summary>Backup history for this installation.</summary>
    [HttpGet("backups")]
    [HasPermission(Permissions.BackupManage)]
    [ProducesResponseType(typeof(ApiResponse<List<BackupRecordDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<BackupRecordDto>>>> GetBackups(CancellationToken ct) =>
        Success(await _backups.GetHistoryAsync(ct));

    /// <summary>Takes a database backup.</summary>
    [HttpPost("backups")]
    [HasPermission(Permissions.BackupManage)]
    [ProducesResponseType(typeof(ApiResponse<BackupRecordDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<BackupRecordDto>>> CreateBackup(
        [FromBody] CreateBackupDto dto, CancellationToken ct) =>
        Success(await _backups.CreateBackupAsync(dto, ct), "Backup created.");

    /// <summary>Restores a previous backup over the current database.</summary>
    [HttpPost("backups/restore")]
    [HasPermission(Permissions.BackupManage)]
    [ProducesResponseType(typeof(ApiResponse<BackupRecordDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<BackupRecordDto>>> RestoreBackup(
        [FromBody] RestoreBackupDto dto, CancellationToken ct) =>
        Success(await _backups.RestoreBackupAsync(dto, ct), "Backup restored.");

    /// <summary>Removes a backup record, optionally deleting the file from disk.</summary>
    [HttpDelete("backups/{id:int}")]
    [HasPermission(Permissions.BackupManage)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> DeleteBackup(
        int id, [FromQuery] bool deleteFile = false, CancellationToken ct = default)
    {
        await _backups.DeleteBackupRecordAsync(id, deleteFile, ct);
        return SuccessMessage("Backup record deleted.");
    }

    /// <summary>Every combo box list the client needs, in one call.</summary>
    [HttpGet("lookups")]
    [ProducesResponseType(typeof(ApiResponse<Dictionary<string, List<LookupDto>>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<Dictionary<string, List<LookupDto>>>>> GetLookups(
        CancellationToken ct) =>
        Success(await _lookups.GetAllAsync(ct));

    /// <summary>Members of a named domain enum, as id/name pairs.</summary>
    [HttpGet("enums/{enumName}")]
    [ProducesResponseType(typeof(ApiResponse<List<LookupDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<List<LookupDto>>>> GetEnum(
        string enumName, CancellationToken ct) =>
        Success(await _lookups.GetEnumAsync(enumName, ct));
}
