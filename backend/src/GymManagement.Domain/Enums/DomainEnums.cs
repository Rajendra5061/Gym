namespace GymManagement.Domain.Enums;

public enum Gender
{
    Unspecified = 0,
    Male = 1,
    Female = 2,
    Other = 3
}

public enum MemberStatus
{
    Active = 1,
    Inactive = 2,
    Suspended = 3,
    Expired = 4
}

public enum UserStatus
{
    Active = 1,
    Inactive = 2,
    Locked = 3
}

public enum TrainerStatus
{
    Active = 1,
    Inactive = 2,
    OnLeave = 3,
    Resigned = 4
}

/// <summary>Billing period of a membership plan.</summary>
public enum PlanDurationType
{
    Day = 1,
    Week = 2,
    Month = 3,
    Quarter = 4,
    HalfYear = 5,
    Year = 6,
    Custom = 7
}

public enum PlanStatus
{
    Active = 1,
    Inactive = 2
}

public enum SubscriptionStatus
{
    Pending = 1,
    Active = 2,
    Frozen = 3,
    Expired = 4,
    Cancelled = 5,
    Upgraded = 6,
    Downgraded = 7
}

public enum SubscriptionActionType
{
    Created = 1,
    Renewed = 2,
    Upgraded = 3,
    Downgraded = 4,
    Frozen = 5,
    Resumed = 6,
    Cancelled = 7,
    Expired = 8,
    PaymentReceived = 9,
    GracePeriodApplied = 10
}

public enum PaymentStatus
{
    Pending = 1,
    Paid = 2,
    PartiallyPaid = 3,
    Failed = 4,
    Refunded = 5,
    PartiallyRefunded = 6,
    Cancelled = 7,
    AwaitingConfirmation = 8
}

public enum AttendanceStatus
{
    CheckedIn = 1,
    CheckedOut = 2
}

public enum NotificationType
{
    General = 0,
    MembershipExpiringSoon = 1,
    MembershipExpired = 2,
    PaymentPending = 3,
    PaymentSuccessful = 4,
    SubscriptionRenewal = 5,
    NewMemberRegistration = 6,
    LicenseExpiringSoon = 7,
    LicenseExpired = 8
}

public enum NotificationSeverity
{
    Info = 0,
    Success = 1,
    Warning = 2,
    Critical = 3
}

public enum RefundStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Completed = 4
}

public enum LoginResult
{
    Success = 1,
    InvalidPassword = 2,
    UserNotFound = 3,
    AccountLocked = 4,
    AccountInactive = 5,
    LicenseInvalid = 6
}

public enum LicenseStatus
{
    NotActivated = 0,
    Trial = 1,
    Active = 2,
    Expired = 3,
    Revoked = 4
}

public enum ExerciseCategory
{
    Strength = 1,
    Cardio = 2,
    Flexibility = 3,
    Balance = 4,
    Functional = 5,
    Other = 99
}

public enum MuscleGroup
{
    FullBody = 0,
    Chest = 1,
    Back = 2,
    Shoulders = 3,
    Biceps = 4,
    Triceps = 5,
    Legs = 6,
    Glutes = 7,
    Core = 8,
    Calves = 9,
    Forearms = 10
}

public enum DifficultyLevel
{
    Beginner = 1,
    Intermediate = 2,
    Advanced = 3
}

/// <summary>Service state of a piece of gym equipment.</summary>
public enum EquipmentCondition
{
    New = 1,
    Good = 2,
    NeedsService = 3,
    UnderRepair = 4,
    Retired = 5
}

/// <summary>Where a sales enquiry came from.</summary>
public enum EnquirySource
{
    WalkIn = 1,
    Phone = 2,
    Website = 3,
    Referral = 4,
    SocialMedia = 5,
    Other = 99
}

/// <summary>Lifecycle of a sales enquiry.</summary>
public enum EnquiryStatus
{
    New = 1,
    Contacted = 2,
    FollowUp = 3,
    Converted = 4,
    Lost = 5
}

/// <summary>Lifecycle of a diet plan assigned to a member.</summary>
public enum DietPlanStatus
{
    Active = 1,
    Completed = 2,
    Cancelled = 3
}

/// <summary>The meal slot a diet plan line belongs to.</summary>
public enum DietMealType
{
    Breakfast = 1,
    MidMorning = 2,
    Lunch = 3,
    EveningSnack = 4,
    Dinner = 5,
    PostWorkout = 6
}

/// <summary>Review state of member feedback.</summary>
public enum FeedbackStatus
{
    New = 1,
    Reviewed = 2,
    Resolved = 3,
    Dismissed = 4
}

/// <summary>Well known audit actions. Stored as a string for readability in reports.</summary>
public static class AuditActions
{
    public const string Login = "Login";
    public const string LoginFailed = "LoginFailed";
    public const string Logout = "Logout";
    public const string Create = "Create";
    public const string Update = "Update";
    public const string Delete = "Delete";
    public const string SoftDelete = "SoftDelete";
    public const string Restore = "Restore";
    public const string Deactivate = "Deactivate";
    public const string Reactivate = "Reactivate";
    public const string PasswordChanged = "PasswordChanged";
    public const string PasswordReset = "PasswordReset";
    public const string SubscriptionCreated = "SubscriptionCreated";
    public const string SubscriptionRenewed = "SubscriptionRenewed";
    public const string SubscriptionCancelled = "SubscriptionCancelled";
    public const string SubscriptionFrozen = "SubscriptionFrozen";
    public const string SubscriptionResumed = "SubscriptionResumed";
    public const string SubscriptionChanged = "SubscriptionChanged";
    public const string PaymentCreated = "PaymentCreated";
    public const string PaymentConfirmed = "PaymentConfirmed";
    public const string PaymentRefunded = "PaymentRefunded";

    /// <summary>A payment settled by a verified gateway webhook rather than by a member of staff.</summary>
    public const string PaymentGatewaySettled = "PaymentGatewaySettled";

    /// <summary>A verified gateway event that was refused — amount mismatch, unsettleable payment.</summary>
    public const string PaymentGatewayRejected = "PaymentGatewayRejected";

    /// <summary>A verified gateway event reporting that the collection failed at the gateway.</summary>
    public const string PaymentGatewayFailed = "PaymentGatewayFailed";
    public const string RoleChanged = "RoleChanged";
    public const string ConfigurationChanged = "ConfigurationChanged";
    public const string LicenseActivated = "LicenseActivated";
    public const string BackupCreated = "BackupCreated";
    public const string BackupRestored = "BackupRestored";
}
