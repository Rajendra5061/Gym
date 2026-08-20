/** Shapes mirroring the backend DTOs. Enums travel as integers. */

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
  hasPreviousPage: boolean;
  hasNextPage: boolean;
}

export interface CurrentUser {
  id: number;
  userName: string;
  fullName: string;
  email: string;
  phone?: string | null;
  profilePhotoPath?: string | null;
  memberId?: number | null;
  trainerId?: number | null;
  roles: string[];
  permissions: string[];
}

export interface LoginResponse {
  accessToken: string;
  refreshToken: string;
  accessTokenExpiresUtc: string;
  refreshTokenExpiresUtc: string;
  mustChangePassword: boolean;
  user: CurrentUser;
}

export interface Lookup { id: number; name: string; code?: string | null; extra?: string | null; isActive: boolean; }

export enum Gender { Unspecified = 0, Male = 1, Female = 2, Other = 3 }
export enum MemberStatus { Active = 1, Inactive = 2, Suspended = 3, Expired = 4 }
export enum UserStatus { Active = 1, Inactive = 2, Locked = 3 }
export enum TrainerStatus { Active = 1, Inactive = 2, OnLeave = 3, Resigned = 4 }
export enum PlanDurationType { Day = 1, Week = 2, Month = 3, Quarter = 4, HalfYear = 5, Year = 6, Custom = 7 }
export enum PlanStatus { Active = 1, Inactive = 2 }
export enum SubscriptionStatus { Pending = 1, Active = 2, Frozen = 3, Expired = 4, Cancelled = 5, Upgraded = 6, Downgraded = 7 }
export enum PaymentStatus { Pending = 1, Paid = 2, PartiallyPaid = 3, Failed = 4, Refunded = 5, PartiallyRefunded = 6, Cancelled = 7, AwaitingConfirmation = 8 }
export enum AttendanceStatus { CheckedIn = 1, CheckedOut = 2 }
export enum RefundStatus { Pending = 1, Approved = 2, Rejected = 3, Completed = 4 }
export enum NotificationSeverity { Info = 0, Success = 1, Warning = 2, Critical = 3 }
export enum ExerciseCategory { Strength = 1, Cardio = 2, Flexibility = 3, Balance = 4, Functional = 5, Other = 99 }
export enum DifficultyLevel { Beginner = 1, Intermediate = 2, Advanced = 3 }

export interface MemberListDto {
  id: number; memberCode: string; fullName: string; gender: Gender; genderText: string;
  phone: string; email?: string | null; joiningDate: string; status: MemberStatus; statusText: string;
  profilePhotoPath?: string | null; assignedTrainerName?: string | null;
  currentPlanName?: string | null; subscriptionEndDate?: string | null; daysRemaining?: number | null;
  subscriptionStatus?: SubscriptionStatus | null; outstandingAmount: number; isExpiringSoon: boolean;
}

export interface TrainerListDto {
  id: number; trainerCode: string; fullName: string; gender: Gender; phone: string;
  email?: string | null; specialization?: string | null; experienceYears?: number | null;
  joiningDate: string; status: TrainerStatus; statusText: string; photoPath?: string | null;
  assignedMemberCount: number;
}

export interface MembershipPlanDto {
  id: number; planCode: string; name: string; description?: string | null; features?: string | null;
  durationType: PlanDurationType; durationTypeText: string; durationValue: number; totalDays: number;
  price: number; registrationFee?: number | null; taxPercent: number; maxDiscountPercent: number;
  gracePeriodDays: number; maxFreezeDays: number; sessionLimit?: number | null; trainerIncluded: boolean;
  displayOrder: number; status: PlanStatus; statusText: string; activeSubscriptionCount: number;
}

export interface SubscriptionDto {
  id: number; subscriptionCode: string; memberId: number; memberCode: string; memberName: string;
  memberPhone?: string | null; membershipPlanId: number; planName: string;
  startDate: string; endDate: string; planAmount: number; registrationFee: number;
  discountAmount: number; taxPercent: number; taxAmount: number; finalAmount: number;
  paidAmount: number; outstandingAmount: number; paymentStatus: PaymentStatus; paymentStatusText: string;
  status: SubscriptionStatus; statusText: string; gracePeriodDays: number; isRenewal: boolean;
  daysRemaining: number; isExpiringSoon: boolean; createdAt: string; createdByName?: string | null;
}

export interface PaymentDto {
  id: number; receiptNumber: string; memberId: number; memberCode: string; memberName: string;
  memberPhone?: string | null; subscriptionId?: number | null; subscriptionCode?: string | null;
  planName?: string | null; amount: number; discountAmount: number; taxAmount: number;
  finalAmount: number; refundedAmount: number; netAmount: number;
  paymentMethodId: number; paymentMethodName: string; transactionReference?: string | null;
  payerVpa?: string | null; paymentDate: string; status: PaymentStatus; statusText: string;
  collectedByName?: string | null; notes?: string | null; createdAt: string;
}

export interface AttendanceDto {
  id: number; memberId: number; memberCode: string; memberName: string;
  profilePhotoPath?: string | null; attendanceDate: string; checkInTime: string;
  checkOutTime?: string | null; status: AttendanceStatus; statusText: string;
  durationMinutes?: number | null; checkInMethod: string; planName?: string | null; notes?: string | null;
}

export interface DashboardStats {
  totalMembers: number; activeMembers: number; inactiveMembers: number; expiredMemberships: number;
  expiringSoon: number; expiringInEmailWindow: number; expiryEmailWindowDays: number;
  todayAttendance: number; currentlyInGym: number;
  todayRevenue: number; monthRevenue: number; yearRevenue: number; monthExpenses: number;
  monthNetIncome: number; pendingPaymentsAmount: number; pendingPaymentsCount: number;
  activeSubscriptions: number; frozenSubscriptions: number; newMembersThisMonth: number;
  newMembersToday: number; totalTrainers: number; activeTrainers: number; unreadNotifications: number;
  currencySymbol: string; revenueGrowthPercent: number; memberGrowthPercent: number;
}

export interface ChartSeries { label: string; value: number; secondaryValue?: number | null; date?: string | null; category?: string | null; }

export interface RecentActivity { whenUtc: string; action: string; entityName: string; entityId?: number | null; description?: string | null; userName?: string | null; icon?: string | null; }

export interface DashboardDto {
  stats: DashboardStats;
  revenueDaily: ChartSeries[]; revenueWeekly: ChartSeries[]; revenueMonthly: ChartSeries[];
  membershipGrowth: ChartSeries[]; attendanceTrend: ChartSeries[];
  planDistribution: ChartSeries[]; paymentMethodDistribution: ChartSeries[];
  recentTransactions: PaymentDto[]; recentActivities: RecentActivity[];
  expiringSoonMembers: MemberListDto[]; generatedAtUtc: string;
}

export interface NotificationDto {
  id: number; type: number; typeText: string; severity: NotificationSeverity; severityText: string;
  title: string; message: string; memberId?: number | null; memberName?: string | null;
  entityName?: string | null; entityId?: number | null; isRead: boolean;
  readAtUtc?: string | null; createdAtUtc: string;
}

/**
 * `GET /api/settings/branding` — the slice of the gym profile every signed-in user may read, so
 * the shell can render the gym's identity and format money. The rest of the profile (UPI handle,
 * tax number, receipt numbering, lockout thresholds) stays behind `settings.view`.
 */
export interface GymBranding {
  gymName: string; logoPath?: string | null;
  currencyCode: string; currencySymbol: string;
  phone?: string | null; email?: string | null;
  address?: string | null; city?: string | null;
}

/** `GET /api/settings/gym` — the whole profile, readable only with `settings.view`. */
export interface GymSettings extends GymBranding {
  id: number; state?: string | null;
  website?: string | null; taxNumber?: string | null;
  upiId?: string | null; upiPayeeName?: string | null;
  receiptPrefix?: string | null; memberCodePrefix?: string | null; receiptFooterText?: string | null;
  expiryReminderDays: number; defaultGracePeriodDays: number;
  maxFailedLoginAttempts: number; lockoutMinutes: number; allowExpiredMemberCheckIn: boolean;
}
