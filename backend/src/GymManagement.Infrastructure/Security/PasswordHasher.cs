using System.Security.Cryptography;
using GymManagement.Application.Common;

namespace GymManagement.Infrastructure.Security;

/// <summary>
/// BCrypt based implementation of <see cref="IPasswordHasher"/>. Plain passwords are never
/// persisted or logged; only the salted BCrypt hash is stored.
/// </summary>
public sealed class PasswordHasher : IPasswordHasher
{
    /// <summary>Cost factor. 12 is a good balance between security and desktop login latency.</summary>
    private const int WorkFactor = 12;

    private const int MinimumTemporaryPasswordLength = 8;

    private const string Uppercase = "ABCDEFGHJKLMNPQRSTUVWXYZ";   // no I / O
    private const string Lowercase = "abcdefghijkmnopqrstuvwxyz";   // no l
    private const string Digits = "23456789";                       // no 0 / 1
    private const string Symbols = "!@#$%*?-+";

    public string Hash(string password)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Password cannot be empty.", nameof(password));

        return BCrypt.Net.BCrypt.HashPassword(password, workFactor: WorkFactor);
    }

    public bool Verify(string password, string hash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrWhiteSpace(hash))
            return false;

        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            // A malformed / legacy hash must read as "wrong password", never as an unhandled error.
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Builds a cryptographically random temporary password that always contains at least one
    /// upper case letter, one lower case letter, one digit and one symbol.
    /// </summary>
    public string GenerateTemporaryPassword(int length = 12)
    {
        var target = Math.Max(length, MinimumTemporaryPasswordLength);

        var all = string.Concat(Uppercase, Lowercase, Digits, Symbols);
        var chars = new char[target];

        // Guarantee one character from every required category.
        chars[0] = PickRandom(Uppercase);
        chars[1] = PickRandom(Lowercase);
        chars[2] = PickRandom(Digits);
        chars[3] = PickRandom(Symbols);

        for (var i = 4; i < target; i++)
            chars[i] = PickRandom(all);

        Shuffle(chars);
        return new string(chars);
    }

    private static char PickRandom(string alphabet) =>
        alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];

    /// <summary>Fisher-Yates shuffle driven by a cryptographic RNG.</summary>
    private static void Shuffle(char[] buffer)
    {
        for (var i = buffer.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (buffer[i], buffer[j]) = (buffer[j], buffer[i]);
        }
    }
}
