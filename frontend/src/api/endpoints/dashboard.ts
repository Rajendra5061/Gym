/** Dashboard endpoints. */
import { api } from '@/api/client';
import type { DashboardDto, DashboardStats } from '@/api/types';

/** A member whose subscription runs out within the reminder window (≤10 days). */
export interface TrainerExpiringMemberDto {
  memberId: number;
  memberName: string;
  endDate: string;
  daysLeft: number;
}

/** One row of the trainer's roster, with the state of their current membership. */
export interface TrainerRosterMemberDto {
  memberId: number;
  memberName: string;
  memberCode: string;
  planName?: string | null;
  endDate?: string | null;
  daysLeft?: number | null;
  lastCheckInDate?: string | null;
}

/** Everything the trainer's landing screen shows, mirroring TrainerDashboardDto on the API. */
export interface TrainerDashboardDto {
  trainerId: number;
  trainerName: string;
  assignedMemberCount: number;
  activeWorkoutPlanCount: number;
  activeDietPlanCount: number;
  todayCheckInCount: number;
  expiringSoon: TrainerExpiringMemberDto[];
  myMembers: TrainerRosterMemberDto[];
}

export const dashboardApi = {
  /** Every card, chart and feed in one call. */
  get: (signal?: AbortSignal) => api.get<DashboardDto>('/api/dashboard', undefined, signal),

  /** Just the headline counters, for lightweight polling. */
  stats: (signal?: AbortSignal) => api.get<DashboardStats>('/api/dashboard/stats', undefined, signal),

  /**
   * The trainer's own dashboard, scoped server-side to the trainerId claim on the token.
   * 404s when the signed-in account has no trainer record linked.
   */
  trainer: (signal?: AbortSignal) =>
    api.get<TrainerDashboardDto>('/api/dashboard/trainer', undefined, signal),
};
