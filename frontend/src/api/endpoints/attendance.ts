/**
 * Gym-floor check-in / check-out plus the registers and counters built on top of it.
 *
 * The server owns every business rule (duplicate check-in, expired membership, grace period);
 * this module just relays the request and lets the API's message reach the screen.
 */

import { api } from '@/api/client';
import type { AttendanceDto, AttendanceStatus, ChartSeries, PagedResult } from '@/api/types';

/** The five methods the API's CheckInRequestValidator accepts. */
export const CHECK_IN_METHODS = ['Manual', 'QrCode', 'Barcode', 'Rfid', 'Biometric'] as const;
export type CheckInMethod = (typeof CHECK_IN_METHODS)[number];

export interface CheckInInput {
  memberId?: number | null;
  memberCode?: string | null;
  checkInMethod: CheckInMethod;
  notes?: string | null;
  /** Admin override that lets a member in despite an expired subscription. */
  overrideExpiredMembership: boolean;
}

export interface CheckOutInput {
  attendanceId?: number | null;
  memberId?: number | null;
  notes?: string | null;
}

export interface HourlyAttendanceDto {
  hour: number;
  hourLabel: string;
  count: number;
}

export interface AttendanceSummaryDto {
  date: string;
  totalCheckIns: number;
  currentlyInGym: number;
  checkedOut: number;
  averageDurationMinutes: number;
  peakHour: number;
  hourlyBreakdown: HourlyAttendanceDto[];
}

/** Query string accepted by `GET /api/attendance`. Mirrors AttendanceQueryDto. */
export interface AttendanceQuery {
  pageNumber?: number;
  pageSize?: number;
  search?: string;
  sortBy?: string;
  sortDescending?: boolean;
  memberId?: number | '';
  fromDate?: string;
  toDate?: string;
  status?: AttendanceStatus | '';
  checkInMethod?: string;
  onlyToday?: boolean;
}

export const attendanceApi = {
  checkIn: (body: CheckInInput) =>
    api.post<AttendanceDto>('/api/attendance/check-in', body),

  checkOut: (body: CheckOutInput) =>
    api.post<AttendanceDto>('/api/attendance/check-out', body),

  paged: (query: AttendanceQuery, signal?: AbortSignal) =>
    api.get<PagedResult<AttendanceDto>>('/api/attendance', { ...query }, signal),

  summary: (date: string | undefined, signal?: AbortSignal) =>
    api.get<AttendanceSummaryDto>('/api/attendance/summary', { date }, signal),

  inGym: (signal?: AbortSignal) =>
    api.get<AttendanceDto[]>('/api/attendance/in-gym', undefined, signal),

  remove: (id: number) =>
    api.del<void>(`/api/attendance/${id}`),

  trend: (from: string, to: string, signal?: AbortSignal) =>
    api.get<ChartSeries[]>('/api/attendance/trend', { from, to }, signal),
};
