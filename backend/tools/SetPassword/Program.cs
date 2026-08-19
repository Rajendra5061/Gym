using GymManagement.Domain.Enums;
using GymManagement.Infrastructure.Data;
using GymManagement.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

// ---------------------------------------------------------------------------------------------
// Local administrative utility: sets a user's password directly against the database.
//
// This exists because the API's change-password endpoint deliberately enforces a password policy
// (minimum 8 characters, at least one letter and one digit), so it cannot be used to install a
// short operator-chosen password. The hash written here is still a proper BCrypt hash — the only
// thing bypassed is the strength policy.
//
// Usage:  dotnet run --project tools/SetPassword -- <userName> <newPassword> [connectionString]
// ---------------------------------------------------------------------------------------------

const string DefaultConnection =
    "Server=.\\SQLEXPRESS01;Database=GymDatabase;Integrated Security=True;" +
    "TrustServerCertificate=True;MultipleActiveResultSets=True";

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: SetPassword <userName> <newPassword> [connectionString]");
    return 1;
}

var userName = args[0];
var newPassword = args[1];
var connectionString = args.Length > 2 ? args[2] : DefaultConnection;

var options = new DbContextOptionsBuilder<GymDbContext>()
    .UseSqlServer(connectionString)
    .Options;

await using var db = new GymDbContext(options);

var user = await db.Users
    .IgnoreQueryFilters()
    .FirstOrDefaultAsync(u => u.UserName == userName);

if (user is null)
{
    Console.Error.WriteLine($"No user named '{userName}' was found in the database.");
    return 2;
}

var hasher = new PasswordHasher();
user.PasswordHash = hasher.Hash(newPassword);

// Make the account immediately usable: clear the forced change, unlock it and reactivate it.
user.MustChangePassword = false;
user.FailedLoginAttempts = 0;
user.LockoutEndUtc = null;
user.PasswordResetTokenHash = null;
user.PasswordResetTokenExpiresUtc = null;
user.Status = UserStatus.Active;
user.IsDeleted = false;

// Existing refresh tokens are invalidated, exactly as a normal password change would do.
var tokens = await db.RefreshTokens
    .Where(t => t.UserId == user.Id && t.RevokedAtUtc == null)
    .ToListAsync();

foreach (var token in tokens)
{
    token.RevokedAtUtc = DateTime.UtcNow;
    token.RevokedReason = "Password reset with the SetPassword tool";
}

await db.SaveChangesAsync();

Console.WriteLine($"Password updated for '{user.UserName}' ({user.FullName}).");
Console.WriteLine($"  Status                : {user.Status}");
Console.WriteLine($"  Must change password  : {user.MustChangePassword}");
Console.WriteLine($"  Refresh tokens revoked: {tokens.Count}");
Console.WriteLine();
Console.WriteLine("Verifying the stored hash…");
Console.WriteLine(hasher.Verify(newPassword, user.PasswordHash)
    ? "  OK — the new password verifies against the stored hash."
    : "  FAILED — the hash does not verify. Investigate before relying on this account.");

return 0;
