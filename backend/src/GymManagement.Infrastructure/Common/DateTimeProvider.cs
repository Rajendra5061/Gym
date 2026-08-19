using GymManagement.Application.Common;

namespace GymManagement.Infrastructure.Common;

/// <summary>
/// System clock implementation of <see cref="IDateTimeProvider"/>. Every service that needs the
/// current time takes this abstraction so expiry, lockout and licensing logic stays testable.
/// </summary>
public sealed class DateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;

    public DateTime Now => DateTime.Now;

    public DateTime Today => DateTime.Today;
}
