/**
 * The signed-in member's own data.
 *
 * Every helper here takes the member id explicitly and each screen passes
 * `useAuth().user?.memberId` — never a route parameter — so a member can only ever ask the
 * API for their own record.
 *
 * Only `GET /api/dashboard/member/{id}` grants a member self-access; the richer per-module
 * endpoints are gated on staff permissions. `optional()` therefore lets a screen ask for the
 * fuller history and quietly fall back to the dashboard payload when the account is not
 * allowed to see it, instead of showing the member an error they cannot act on.
 */

import { ApiError, api, download } from '@/api/client';
import type {
  AttendanceDto, ChartSeries, Gender, MemberStatus, NotificationDto, PagedResult,
  PaymentDto, SubscriptionDto,
} from '@/api/types';

/* ---------------------------------------------------------------- member */

export interface MemberDetailDto {
  id: number;
  memberCode: string;
  fullName: string;
  gender: Gender;
  dateOfBirth?: string | null;
  age?: number | null;
  phone: string;
  email?: string | null;
  address?: string | null;
  city?: string | null;
  postalCode?: string | null;
  emergencyContactName?: string | null;
  emergencyContactPhone?: string | null;
  emergencyContactRelation?: string | null;
  joiningDate: string;
  profilePhotoPath?: string | null;
  bloodGroup?: string | null;
  heightCm?: number | null;
  weightKg?: number | null;
  status: MemberStatus;
  notes?: string | null;
  assignedTrainerId?: number | null;
  assignedTrainerName?: string | null;
  hasUserAccount: boolean;
  createdAt: string;
  updatedAt?: string | null;
  activeSubscription?: SubscriptionDto | null;
  totalPaid: number;
  totalOutstanding: number;
  totalVisits: number;
  lastVisitDate?: string | null;
}

export interface WorkoutPlanExerciseDto {
  id: number;
  exerciseId: number;
  exerciseName: string;
  muscleGroupText?: string | null;
  dayOfWeek?: number | null;
  displayOrder: number;
  sets: number;
  repetitions: number;
  targetWeightKg?: number | null;
  restSeconds?: number | null;
  durationMinutes?: number | null;
  notes?: string | null;
}

export interface MemberWorkoutPlanDto {
  id: number;
  memberId: number;
  memberName: string;
  workoutPlanId: number;
  workoutPlanName: string;
  trainerId?: number | null;
  trainerName?: string | null;
  startDate: string;
  endDate?: string | null;
  isActive: boolean;
  notes?: string | null;
  exercises: WorkoutPlanExerciseDto[];
}

export interface MemberDashboardDto {
  member: MemberDetailDto;
  activeSubscription?: SubscriptionDto | null;
  daysRemaining: number;
  outstandingAmount: number;
  visitsThisMonth: number;
  totalVisits: number;
  lastVisit?: string | null;
  recentAttendance: AttendanceDto[];
  recentPayments: PaymentDto[];
  activeWorkoutPlan?: MemberWorkoutPlanDto | null;
  notifications: NotificationDto[];
  attendanceTrend: ChartSeries[];
}

export interface MemberHistoryDto {
  member: MemberDetailDto;
  subscriptions: SubscriptionDto[];
  payments: PaymentDto[];
  attendance: AttendanceDto[];
}

/* -------------------------------------------------------------- feedback */

export interface FeedbackDto {
  id: number;
  memberId: number;
  memberName: string;
  memberCode?: string | null;
  subject?: string | null;
  rating?: number | null;
  status: number;
  statusText: string;
  isPrivate: boolean;
  hasResponse: boolean;
  createdAt: string;
  message: string;
  adminResponse?: string | null;
  respondedByUserName?: string | null;
  respondedAt?: string | null;
}

export interface CreateFeedbackInput {
  subject?: string | null;
  message: string;
  rating?: number | null;
}

/* --------------------------------------------------------------- helpers */

/**
 * Runs a request that the account may not be permitted to make. A 403 (no permission) or 404
 * (module not deployed yet) resolves to null so the caller can fall back; anything else — a
 * genuine server or network failure — still throws.
 */
export async function optional<T>(work: () => Promise<T>): Promise<T | null> {
  try {
    return await work();
  } catch (error) {
    if (error instanceof ApiError && (error.isForbidden || error.status === 404)) return null;
    throw error;
  }
}

/** True when a failure only means "this account may not see that", not a real fault. */
export function isUnavailable(error: unknown): boolean {
  return error instanceof ApiError && (error.isForbidden || error.status === 404);
}

/* ------------------------------------------------------------------ api */

export const memberApi = {
  /** The one endpoint a Member-role account may call for itself. */
  dashboard: (memberId: number, signal?: AbortSignal) =>
    api.get<MemberDashboardDto>(`/api/dashboard/member/${memberId}`, undefined, signal),

  profile: (memberId: number, signal?: AbortSignal) =>
    api.get<MemberDetailDto>(`/api/members/${memberId}`, undefined, signal),

  history: (memberId: number, signal?: AbortSignal) =>
    api.get<MemberHistoryDto>(`/api/members/${memberId}/history`, undefined, signal),

  subscriptions: (memberId: number, signal?: AbortSignal) =>
    api.get<SubscriptionDto[]>(`/api/subscriptions/member/${memberId}`, undefined, signal),

  payments: (memberId: number, signal?: AbortSignal) =>
    api.get<PaymentDto[]>(`/api/payments/member/${memberId}`, undefined, signal),

  receiptPdf: (paymentId: number) => download(`/api/payments/${paymentId}/receipt/pdf`),

  workoutPlans: (memberId: number, signal?: AbortSignal) =>
    api.get<MemberWorkoutPlanDto[]>(`/api/workouts/members/${memberId}/plans`, undefined, signal),

  changePassword: (currentPassword: string, newPassword: string, confirmPassword: string) =>
    api.post<void>('/api/auth/change-password', { currentPassword, newPassword, confirmPassword }),

  /** The feedback module answers either a bare list or a paged envelope. */
  myFeedback: (signal?: AbortSignal) =>
    api.get<FeedbackDto[] | PagedResult<FeedbackDto>>('/api/feedback/mine', undefined, signal),

  submitFeedback: (body: CreateFeedbackInput) => api.post<FeedbackDto>('/api/feedback', body),
};

/** Normalises the two shapes `GET /api/feedback/mine` might answer with. */
export function feedbackItems(payload: FeedbackDto[] | PagedResult<FeedbackDto> | null): FeedbackDto[] {
  if (!payload) return [];
  return Array.isArray(payload) ? payload : (payload.items ?? []);
}
