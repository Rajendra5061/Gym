using System.Globalization;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Application.Interfaces;
using GymManagement.Domain.Constants;
using GymManagement.Domain.Entities;
using GymManagement.Domain.Enums;
using GymManagement.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GymManagement.Infrastructure.Services;

/// <summary>
/// Operator-driven SQL Server backup and restore.
/// <para>
/// IMPORTANT: <c>BACKUP DATABASE</c> and <c>RESTORE DATABASE</c> are executed by the SQL Server
/// service, not by this process, so the target folder must exist on the database host and be
/// writable by the SQL Server service account. This screen is a convenience for a single-site
/// installation; production must additionally run scheduled, off-host, server-side backups with a
/// verified restore drill, as the requirements state.
/// </para>
/// </summary>
public sealed class BackupService : IBackupService
{
    /// <summary>Backups of a real database easily outlive the default 30 second command timeout.</summary>
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(60);

    private const string FolderSettingKey = "Backup.Folder";
    private const string FolderConfigurationKey = "Backup:Folder";

    private readonly GymDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditService _audit;
    private readonly ISettingsService _settings;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BackupService> _logger;

    public BackupService(
        GymDbContext db,
        IDateTimeProvider clock,
        ICurrentUserService currentUser,
        IAuditService audit,
        ISettingsService settings,
        IConfiguration configuration,
        ILogger<BackupService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ----------------------------------------------------------------- read

    public async Task<List<BackupRecordDto>> GetHistoryAsync(CancellationToken ct = default)
    {
        var rows = await _db.BackupRecords
            .AsNoTracking()
            .OrderByDescending(b => b.CreatedAtUtc)
            .ThenByDescending(b => b.Id)
            .Select(b => new BackupRecordDto
            {
                Id = b.Id,
                FileName = b.FileName,
                FilePath = b.FilePath,
                FileSizeBytes = b.FileSizeBytes,
                CreatedAtUtc = b.CreatedAtUtc,
                CreatedByName = _db.Users.IgnoreQueryFilters()
                    .Where(u => u.Id == b.CreatedByUserId)
                    .Select(u => u.FullName)
                    .FirstOrDefault(),
                BackupType = b.BackupType,
                IsSuccess = b.IsSuccess,
                ErrorMessage = b.ErrorMessage,
                RestoredAtUtc = b.RestoredAtUtc,
                Notes = b.Notes
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // The operator needs to know when a recorded backup can no longer be restored.
        foreach (var row in rows)
        {
            if (row.IsSuccess && !FileExists(row.FilePath))
                row.Notes = Join(row.Notes, "File is missing from disk.");
        }

        return rows;
    }

    // --------------------------------------------------------------- backup

    public async Task<BackupRecordDto> CreateBackupAsync(CreateBackupDto dto, CancellationToken ct = default)
    {
        dto ??= new CreateBackupDto();
        RequireBackupPermission();

        var now = _clock.UtcNow;
        var databaseName = ResolveDatabaseName();
        var folder = await ResolveFolderAsync(dto.TargetFolder, ct).ConfigureAwait(false);

        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new BusinessRuleAppException($"The backup folder '{folder}' could not be created: {ex.Message}");
        }

        var fileName = $"{databaseName}_{now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)}.bak";
        var fullPath = Path.Combine(folder, fileName);

        var record = new BackupRecord
        {
            FileName = fileName,
            FilePath = fullPath,
            CreatedAtUtc = now,
            CreatedByUserId = _currentUser.UserId,
            BackupType = "Full",
            IsSuccess = false,
            Notes = Clean(dto.Notes)
        };

        var previousTimeout = _db.Database.GetCommandTimeout();

        try
        {
            _db.Database.SetCommandTimeout(CommandTimeout);

            // The database name cannot be a parameter, so it is bracket-quoted; the file path, which
            // is the only operator-supplied value, is always passed as a parameter.
            await RunBackupAsync(databaseName, fullPath, ct).ConfigureAwait(false);

            record.IsSuccess = true;
            record.FileSizeBytes = FileSize(fullPath);

            if (record.FileSizeBytes == 0)
            {
                // SQL Server wrote the file on its own host; this API may not be able to see it.
                record.Notes = Join(record.Notes,
                    "File size could not be read from this host (the file belongs to the SQL Server service).");
            }
        }
        catch (Exception ex)
        {
            record.IsSuccess = false;
            record.ErrorMessage = Truncate(ex.Message, 2000);

            _logger.LogError(ex, "Backup of database {Database} to {Path} failed.", databaseName, fullPath);

            await PersistAsync(record, ct).ConfigureAwait(false);

            await _audit.LogAsync(AuditActions.BackupCreated, nameof(BackupRecord), record.Id,
                description: $"Backup of '{databaseName}' failed: {record.ErrorMessage}", ct: ct)
                .ConfigureAwait(false);

            throw new BusinessRuleAppException($"The backup failed: {ex.Message}");
        }
        finally
        {
            _db.Database.SetCommandTimeout(previousTimeout);
        }

        await PersistAsync(record, ct).ConfigureAwait(false);

        await _audit.LogAsync(AuditActions.BackupCreated, nameof(BackupRecord), record.Id,
            newValues: new { record.FileName, record.FilePath, record.FileSizeBytes },
            description: $"Backup of '{databaseName}' created as '{fileName}'.", ct: ct)
            .ConfigureAwait(false);

        return Map(record, _currentUser.FullName ?? _currentUser.UserName);
    }

    /// <summary>
    /// Runs the backup. Compression is requested first because it is much faster and smaller, but
    /// SQL Server Express rejects it, so a compression failure is retried without that option.
    /// </summary>
    private async Task RunBackupAsync(string databaseName, string fullPath, CancellationToken ct)
    {
        try
        {
            await ExecuteBackupAsync(databaseName, fullPath, compress: true, ct).ConfigureAwait(false);
        }
        catch (SqlException ex) when (ex.Message.Contains("compression", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(ex, "This SQL Server edition does not support backup compression; retrying without it.");
            await ExecuteBackupAsync(databaseName, fullPath, compress: false, ct).ConfigureAwait(false);
        }
    }

    private Task ExecuteBackupAsync(string databaseName, string fullPath, bool compress, CancellationToken ct)
    {
        var options = compress ? "WITH INIT, COMPRESSION, STATS = 10" : "WITH INIT, STATS = 10";
        var sql = $"BACKUP DATABASE [{Quote(databaseName)}] TO DISK = @path {options}";

        return _db.Database.ExecuteSqlRawAsync(sql, new object[] { new SqlParameter("@path", fullPath) }, ct);
    }

    // -------------------------------------------------------------- restore

    public async Task<BackupRecordDto> RestoreBackupAsync(RestoreBackupDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        RequireBackupPermission();

        var databaseName = ResolveDatabaseName();

        // Typing the database name is the last line of defence against restoring over the wrong one.
        if (!string.Equals(dto.ConfirmationText, databaseName, StringComparison.Ordinal))
        {
            throw new ValidationAppException(nameof(dto.ConfirmationText),
                $"Type the database name '{databaseName}' exactly to confirm the restore.");
        }

        var record = await _db.BackupRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == dto.BackupId, ct)
            .ConfigureAwait(false);

        if (record is null)
            throw new NotFoundAppException(nameof(BackupRecord), dto.BackupId);

        if (!record.IsSuccess)
            throw new BusinessRuleAppException("This backup did not complete successfully and cannot be restored.");

        ValidateFolder(Path.GetDirectoryName(record.FilePath) ?? string.Empty);

        var now = _clock.UtcNow;
        var userId = _currentUser.UserId;

        await RunRestoreAsync(databaseName, record.FilePath, ct).ConfigureAwait(false);

        // The restored database is the backup's own copy of this table, so the row being stamped may
        // be an older version of itself - or may not exist at all. The stamp is best effort.
        try
        {
            await _db.BackupRecords
                .Where(b => b.Id == record.Id)
                .ExecuteUpdateAsync(setters => setters
                        .SetProperty(b => b.RestoredAtUtc, now)
                        .SetProperty(b => b.RestoredByUserId, userId), ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not stamp backup record {Id} after the restore.", record.Id);
        }

        await _audit.LogAsync(AuditActions.BackupRestored, nameof(BackupRecord), record.Id,
            description: $"Database '{databaseName}' restored from '{record.FileName}'.", ct: ct)
            .ConfigureAwait(false);

        record.RestoredAtUtc = now;
        record.RestoredByUserId = userId;

        // NOTE: the API should be restarted after a restore. Connection pools, the EF change tracker
        // and every cached lookup still describe the database as it was before the restore.
        _logger.LogWarning("Database {Database} was restored from {File}; restart the API to drop stale state.",
            databaseName, record.FileName);

        return Map(record, _currentUser.FullName ?? _currentUser.UserName);
    }

    /// <summary>
    /// Restores over the live database. This cannot run on a connection to that database, so it uses
    /// a dedicated connection to <c>master</c> built from the application's own connection string.
    /// </summary>
    private async Task RunRestoreAsync(string databaseName, string filePath, CancellationToken ct)
    {
        var builder = new SqlConnectionStringBuilder(_db.Database.GetConnectionString())
        {
            InitialCatalog = "master"
        };

        // Release this context's pooled connection so it cannot hold the database in MULTI_USER.
        await _db.Database.CloseConnectionAsync().ConfigureAwait(false);

        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);

        var quoted = Quote(databaseName);
        var singleUserApplied = false;

        try
        {
            await ExecuteAsync(connection,
                $"ALTER DATABASE [{quoted}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE", null, ct)
                .ConfigureAwait(false);

            singleUserApplied = true;

            await ExecuteAsync(connection,
                $"RESTORE DATABASE [{quoted}] FROM DISK = @path WITH REPLACE",
                new SqlParameter("@path", filePath), ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Restore of database {Database} from {Path} failed.", databaseName, filePath);
            throw new BusinessRuleAppException($"The restore failed: {ex.Message}");
        }
        finally
        {
            if (singleUserApplied)
            {
                try
                {
                    await ExecuteAsync(connection, $"ALTER DATABASE [{quoted}] SET MULTI_USER", null,
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // Leaving the database in SINGLE_USER would lock everyone out, so this is loud.
                    _logger.LogCritical(ex,
                        "Database {Database} could not be returned to MULTI_USER. Run 'ALTER DATABASE [{Database}] SET MULTI_USER' manually.",
                        databaseName, databaseName);
                }
            }
        }
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql, SqlParameter? parameter,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = (int)CommandTimeout.TotalSeconds;

        if (parameter is not null) command.Parameters.Add(parameter);

        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    // --------------------------------------------------------------- delete

    public async Task DeleteBackupRecordAsync(int id, bool deleteFile, CancellationToken ct = default)
    {
        RequireBackupPermission();

        var record = await _db.BackupRecords
            .FirstOrDefaultAsync(b => b.Id == id, ct)
            .ConfigureAwait(false);

        if (record is null)
            throw new NotFoundAppException(nameof(BackupRecord), id);

        var fileRemoved = false;

        if (deleteFile)
        {
            try
            {
                if (File.Exists(record.FilePath))
                {
                    File.Delete(record.FilePath);
                    fileRemoved = true;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                // A missing or locked file must not stop the history row from being removed.
                _logger.LogWarning(ex, "Could not delete backup file {Path}.", record.FilePath);
            }
        }

        _db.BackupRecords.Remove(record);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(AuditActions.Delete, nameof(BackupRecord), id,
            description: $"Backup history entry '{record.FileName}' removed" +
                         (fileRemoved ? " together with its file." : "."), ct: ct)
            .ConfigureAwait(false);
    }

    // -------------------------------------------------------------- helpers

    private void RequireBackupPermission()
    {
        if (!_currentUser.IsAuthenticated || !_currentUser.HasPermission(Permissions.BackupManage))
            throw new ForbiddenAppException("Only an operator with the backup permission may run backups or restores.");
    }

    /// <summary>The catalogue this context is connected to; it is never operator input.</summary>
    private string ResolveDatabaseName()
    {
        var name = _db.Database.GetDbConnection().Database;

        if (string.IsNullOrWhiteSpace(name))
            throw new BusinessRuleAppException("The database name could not be resolved from the connection string.");

        return name;
    }

    private async Task<string> ResolveFolderAsync(string? requested, CancellationToken ct)
    {
        var folder = Clean(requested)
                     ?? Clean(await _settings.GetValueAsync<string>(FolderSettingKey, null, ct).ConfigureAwait(false))
                     ?? Clean(_configuration[FolderConfigurationKey])
                     ?? Path.Combine(AppContext.BaseDirectory, "Backups");

        ValidateFolder(folder);
        return folder;
    }

    /// <summary>
    /// The folder ends up inside a T-SQL literal only as a parameter value, but it is still validated:
    /// a quote or a relative path is always a mistake and is rejected before anything runs.
    /// </summary>
    private static void ValidateFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            throw new ValidationAppException("TargetFolder", "A backup folder is required.");

        if (folder.Contains('\'', StringComparison.Ordinal))
            throw new ValidationAppException("TargetFolder", "The backup folder must not contain a quote character.");

        if (folder.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            throw new ValidationAppException("TargetFolder", "The backup folder contains invalid characters.");

        if (!Path.IsPathRooted(folder))
            throw new ValidationAppException("TargetFolder", "The backup folder must be an absolute path on the database host.");
    }

    private async Task PersistAsync(BackupRecord record, CancellationToken ct)
    {
        // Column widths from BackupRecordConfiguration.
        record.Notes = Truncate(record.Notes, 512);
        record.ErrorMessage = Truncate(record.ErrorMessage, 2000);

        if (_db.Entry(record).State == EntityState.Detached)
            _db.BackupRecords.Add(record);

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Doubles a closing bracket so a database name can be safely bracket-quoted.</summary>
    private static string Quote(string identifier) => identifier.Replace("]", "]]", StringComparison.Ordinal);

    private static bool FileExists(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        try
        {
            return File.Exists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    private static long FileSize(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length : 0L;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return 0L;
        }
    }

    private static BackupRecordDto Map(BackupRecord record, string? createdByName) => new()
    {
        Id = record.Id,
        FileName = record.FileName,
        FilePath = record.FilePath,
        FileSizeBytes = record.FileSizeBytes,
        CreatedAtUtc = record.CreatedAtUtc,
        CreatedByName = createdByName,
        BackupType = record.BackupType,
        IsSuccess = record.IsSuccess,
        ErrorMessage = record.ErrorMessage,
        RestoredAtUtc = record.RestoredAtUtc,
        Notes = record.Notes
    };

    private static string Join(string? existing, string addition) =>
        string.IsNullOrWhiteSpace(existing) ? addition : $"{existing} {addition}";

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) || value.Length <= maxLength ? value : value[..maxLength];
}
