namespace GymManagement.Domain.Constants;

/// <summary>Built-in role names. Role/permission rows are seeded from these values.</summary>
public static class RoleNames
{
    public const string Admin = "Admin";
    public const string Staff = "Staff";
    public const string Trainer = "Trainer";
    public const string Member = "Member";

    public static readonly string[] All = { Admin, Staff, Trainer, Member };
}

/// <summary>
/// Permission codes used by <c>[HasPermission]</c> on API endpoints. Grouped by module so that
/// the roles and permissions screen can render them under headings.
/// </summary>
public static class Permissions
{
    public const string MembersView = "members.view";
    public const string MembersCreate = "members.create";
    public const string MembersEdit = "members.edit";
    public const string MembersDelete = "members.delete";
    public const string MembersRestore = "members.restore";

    public const string TrainersView = "trainers.view";
    public const string TrainersManage = "trainers.manage";

    public const string ExercisesView = "exercises.view";
    public const string ExercisesManage = "exercises.manage";

    public const string WorkoutsView = "workouts.view";
    public const string WorkoutsManage = "workouts.manage";

    public const string AttendanceView = "attendance.view";
    public const string AttendanceManage = "attendance.manage";

    public const string DietView = "diet.view";
    public const string DietManage = "diet.manage";

    /// <summary>Record / delete body measurements without needing full member-edit rights.</summary>
    public const string MeasurementsManage = "measurements.manage";

    public const string SalaryView = "salary.view";
    public const string SalaryManage = "salary.manage";

    public const string PlansView = "plans.view";
    public const string PlansManage = "plans.manage";

    public const string SubscriptionsView = "subscriptions.view";
    public const string SubscriptionsCreate = "subscriptions.create";
    public const string SubscriptionsManage = "subscriptions.manage";
    public const string SubscriptionsCancel = "subscriptions.cancel";

    public const string PaymentsView = "payments.view";
    public const string PaymentsCollect = "payments.collect";
    public const string PaymentsRefund = "payments.refund";

    public const string ExpensesView = "expenses.view";
    public const string ExpensesManage = "expenses.manage";

    public const string EquipmentView = "equipment.view";
    public const string EquipmentManage = "equipment.manage";

    public const string EnquiriesView = "enquiries.view";
    public const string EnquiriesManage = "enquiries.manage";

    public const string FeedbackView = "feedback.view";
    public const string FeedbackManage = "feedback.manage";
    /// <summary>Lets a member post their own feedback and read it back.</summary>
    public const string FeedbackSubmit = "feedback.submit";

    public const string ReportsBasic = "reports.basic";
    public const string ReportsFinancial = "reports.financial";
    public const string ReportsExport = "reports.export";

    public const string NotificationsView = "notifications.view";
    public const string NotificationsManage = "notifications.manage";

    public const string UsersView = "users.view";
    public const string UsersManage = "users.manage";
    public const string RolesManage = "roles.manage";

    public const string AuditView = "audit.view";
    public const string RecycleBinView = "recyclebin.view";
    public const string RecycleBinRestore = "recyclebin.restore";
    public const string RecycleBinPurge = "recyclebin.purge";

    public const string SettingsView = "settings.view";
    public const string SettingsManage = "settings.manage";
    public const string LicenseManage = "license.manage";
    public const string BackupManage = "backup.manage";

    public const string DashboardView = "dashboard.view";

    /// <summary>Every permission code with the module it belongs to and a display label.</summary>
    public static readonly (string Code, string Module, string Description)[] All =
    {
        (DashboardView, "Dashboard", "View dashboard"),

        (MembersView, "Members", "View members"),
        (MembersCreate, "Members", "Register members"),
        (MembersEdit, "Members", "Edit members"),
        (MembersDelete, "Members", "Delete / deactivate members"),
        (MembersRestore, "Members", "Restore members"),

        (TrainersView, "Trainers", "View trainers"),
        (TrainersManage, "Trainers", "Add / edit trainers and assignments"),

        (ExercisesView, "Exercises", "View exercise library"),
        (ExercisesManage, "Exercises", "Manage exercise library"),

        (WorkoutsView, "Workouts", "View workout plans and sessions"),
        (WorkoutsManage, "Workouts", "Manage workout plans and sessions"),

        (AttendanceView, "Attendance", "View attendance"),
        (AttendanceManage, "Attendance", "Record check-in / check-out"),

        (DietView, "Diet Plans", "View diet plans"),
        (DietManage, "Diet Plans", "Create / edit diet plans"),

        (MeasurementsManage, "Members", "Record body measurements"),

        (SalaryView, "Salaries", "View trainer salary payments"),
        (SalaryManage, "Salaries", "Record trainer salary payments"),

        (PlansView, "Membership Plans", "View membership plans"),
        (PlansManage, "Membership Plans", "Create / edit membership plans"),

        (SubscriptionsView, "Subscriptions", "View subscriptions"),
        (SubscriptionsCreate, "Subscriptions", "Create subscriptions and renewals"),
        (SubscriptionsManage, "Subscriptions", "Upgrade / downgrade / freeze / resume"),
        (SubscriptionsCancel, "Subscriptions", "Cancel subscriptions"),

        (PaymentsView, "Payments", "View payments"),
        (PaymentsCollect, "Payments", "Collect payments and issue receipts"),
        (PaymentsRefund, "Payments", "Approve refunds"),

        (ExpensesView, "Expenses", "View expenses"),
        (ExpensesManage, "Expenses", "Record expenses"),

        (EquipmentView, "Equipment", "View equipment inventory"),
        (EquipmentManage, "Equipment", "Add / edit equipment and service records"),

        (EnquiriesView, "Enquiries", "View enquiries and leads"),
        (EnquiriesManage, "Enquiries", "Create / update / convert enquiries"),

        (FeedbackView, "Feedback", "View member feedback"),
        (FeedbackManage, "Feedback", "Respond to and close feedback"),
        (FeedbackSubmit, "Feedback", "Submit own feedback"),

        (ReportsBasic, "Reports", "Operational reports"),
        (ReportsFinancial, "Reports", "Financial reports"),
        (ReportsExport, "Reports", "Export to Excel / PDF"),

        (NotificationsView, "Notifications", "View notifications"),
        (NotificationsManage, "Notifications", "Send / dismiss notifications"),

        (UsersView, "Users", "View user accounts"),
        (UsersManage, "Users", "Create / edit user accounts"),
        (RolesManage, "Users", "Manage roles and permissions"),

        (AuditView, "Audit", "View audit logs"),
        (RecycleBinView, "Recycle Bin", "View deleted records"),
        (RecycleBinRestore, "Recycle Bin", "Restore deleted records"),
        (RecycleBinPurge, "Recycle Bin", "Permanently delete records"),

        (SettingsView, "Settings", "View settings"),
        (SettingsManage, "Settings", "Change settings"),
        (LicenseManage, "Settings", "Manage trial / license"),
        (BackupManage, "Settings", "Backup and restore")
    };

    /// <summary>Default permission grants per built-in role.</summary>
    public static string[] ForRole(string roleName) => roleName switch
    {
        RoleNames.Admin => All.Select(p => p.Code).ToArray(),

        RoleNames.Staff => new[]
        {
            DashboardView,
            MembersView, MembersCreate, MembersEdit,
            TrainersView,
            ExercisesView,
            WorkoutsView,
            AttendanceView, AttendanceManage,
            MeasurementsManage,
            PlansView,
            SubscriptionsView, SubscriptionsCreate,
            PaymentsView, PaymentsCollect,
            ExpensesView,
            EquipmentView,
            EnquiriesView, EnquiriesManage,
            FeedbackView,
            ReportsBasic, ReportsExport,
            NotificationsView
        },

        RoleNames.Trainer => new[]
        {
            DashboardView,
            MembersView,
            ExercisesView, ExercisesManage,
            WorkoutsView, WorkoutsManage,
            DietView, DietManage,
            MeasurementsManage,
            AttendanceView, AttendanceManage,
            SubscriptionsView,
            ReportsBasic,
            NotificationsView
        },

        RoleNames.Member => new[]
        {
            NotificationsView,
            FeedbackSubmit
        },

        _ => Array.Empty<string>()
    };
}
