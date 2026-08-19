/**
 * The reporting engine is shape-agnostic: the server describes the columns and returns rows as
 * plain dictionaries, so one dynamic table renders all sixteen reports.
 */
import { api, download, request } from '@/api/client';
import type { ChartSeries, Lookup } from '@/api/types';
import { date as fmtDate, dateTime as fmtDateTime, money } from '@/lib/format';

export enum ReportType {
  MemberList = 1,
  ActiveMembers = 2,
  ExpiredMembers = 3,
  NewRegistrations = 4,
  SubscriptionReport = 5,
  RenewalReport = 6,
  AttendanceReport = 7,
  DailyPaymentReport = 8,
  MonthlyPaymentReport = 9,
  RevenueReport = 10,
  OutstandingPaymentReport = 11,
  TrainerReport = 12,
  WorkoutActivityReport = 13,
  AuditReport = 14,
  ExpenseReport = 15,
  ProfitAndLossReport = 16,
}

/** Extra pickers a report understands, beyond the date range every report accepts. */
export type ReportFilterKey = 'member' | 'trainer' | 'plan' | 'method' | 'user' | 'status' | 'groupBy';

export interface ReportMeta {
  type: ReportType;
  name: string;
  group: string;
  hint: string;
  filters: ReportFilterKey[];
}

export const REPORT_CATALOGUE: ReportMeta[] = [
  { type: ReportType.MemberList, name: 'Member list', group: 'Members', hint: 'Every member on file with plan and outstanding balance.', filters: ['plan', 'status'] },
  { type: ReportType.ActiveMembers, name: 'Active members', group: 'Members', hint: 'Members holding a live subscription today.', filters: ['plan'] },
  { type: ReportType.ExpiredMembers, name: 'Expired members', group: 'Members', hint: 'Memberships that have lapsed and need a renewal call.', filters: ['plan'] },
  { type: ReportType.NewRegistrations, name: 'New registrations', group: 'Members', hint: 'Members who joined inside the selected range.', filters: ['plan'] },
  { type: ReportType.SubscriptionReport, name: 'Subscriptions', group: 'Subscriptions', hint: 'Subscriptions started in the range with amounts due.', filters: ['plan', 'status'] },
  { type: ReportType.RenewalReport, name: 'Renewals', group: 'Subscriptions', hint: 'Renewed subscriptions and the revenue they carried.', filters: ['plan'] },
  { type: ReportType.DailyPaymentReport, name: 'Daily payments', group: 'Payments', hint: 'Every receipt collected, day by day.', filters: ['method'] },
  { type: ReportType.MonthlyPaymentReport, name: 'Monthly payments', group: 'Payments', hint: 'Collections rolled up by month.', filters: ['method'] },
  { type: ReportType.RevenueReport, name: 'Revenue', group: 'Payments', hint: 'Revenue bucketed by day, week or month.', filters: ['method', 'groupBy'] },
  { type: ReportType.OutstandingPaymentReport, name: 'Outstanding balances', group: 'Payments', hint: 'Members carrying an unpaid balance.', filters: ['plan'] },
  { type: ReportType.ExpenseReport, name: 'Expenses', group: 'Payments', hint: 'Expenses recorded in the range, by category.', filters: [] },
  { type: ReportType.ProfitAndLossReport, name: 'Profit and loss', group: 'Payments', hint: 'Revenue set against expenses for the range.', filters: [] },
  { type: ReportType.AttendanceReport, name: 'Attendance', group: 'Operations', hint: 'Check-ins with duration for the range.', filters: ['member'] },
  { type: ReportType.TrainerReport, name: 'Trainers', group: 'Operations', hint: 'Trainer workload and assigned members.', filters: ['trainer'] },
  { type: ReportType.WorkoutActivityReport, name: 'Workout activity', group: 'Operations', hint: 'Logged workout sessions, volume and calories.', filters: ['member', 'trainer'] },
  { type: ReportType.AuditReport, name: 'Audit trail', group: 'System', hint: 'Who changed what, and when.', filters: ['user'] },
];

export const REPORT_GROUPS = ['Members', 'Subscriptions', 'Payments', 'Operations', 'System'];

export interface ReportRequestDto {
  reportType: ReportType;
  fromDate?: string | null;
  toDate?: string | null;
  memberId?: number | null;
  trainerId?: number | null;
  membershipPlanId?: number | null;
  paymentMethodId?: number | null;
  userId?: number | null;
  status?: number | null;
  groupBy: string;
  pageNumber: number;
  pageSize: number;
}

export interface ReportColumnDto {
  key: string;
  header: string;
  dataType: string;
  alignment?: string | null;
  width?: number | null;
  isTotalled: boolean;
}

export interface ReportResultDto {
  reportType: ReportType;
  title: string;
  subtitle?: string | null;
  fromDate?: string | null;
  toDate?: string | null;
  generatedAtUtc?: string | null;
  generatedByName?: string | null;
  currencySymbol: string;
  columns: ReportColumnDto[];
  rows: Record<string, unknown>[];
  totals: Record<string, number>;
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  chart: ChartSeries[];
}

export const runReport = (body: ReportRequestDto, signal?: AbortSignal) =>
  request<ReportResultDto>('/api/reports/run', { method: 'POST', body, signal });

export const exportReport = (format: 'excel' | 'pdf', body: ReportRequestDto) =>
  download(`/api/reports/export/${format}`, 'POST', body);

/* ------------------------------------------------------------------- cell rendering */

/**
 * Rows are untyped dictionaries. Keys come back exactly as the server wrote them, but a
 * case-insensitive fallback keeps a column from silently rendering blank if that ever changes.
 */
export function readCell(row: Record<string, unknown>, key: string): unknown {
  if (key in row) return row[key];
  const wanted = key.toLowerCase();
  for (const candidate of Object.keys(row)) {
    if (candidate.toLowerCase() === wanted) return row[candidate];
  }
  return undefined;
}

function toNumber(value: unknown): number | null {
  if (typeof value === 'number') return Number.isFinite(value) ? value : null;
  if (typeof value === 'string' && value.trim() !== '') {
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : null;
  }
  return null;
}

function toBool(value: unknown): boolean {
  if (typeof value === 'boolean') return value;
  if (typeof value === 'number') return value !== 0;
  if (typeof value === 'string') return ['true', 'yes', '1', 'y'].includes(value.toLowerCase());
  return false;
}

/** Formats one cell purely from the data type its column declares. */
export function formatCell(value: unknown, dataType: string | null | undefined, symbol: string): string {
  if (value === null || value === undefined || value === '') return '—';

  switch ((dataType ?? 'string').toLowerCase()) {
    case 'currency': {
      const n = toNumber(value);
      return n === null ? String(value) : money(n, symbol);
    }
    case 'int': {
      const n = toNumber(value);
      return n === null ? String(value) : n.toLocaleString('en-IN', { maximumFractionDigits: 0 });
    }
    case 'decimal': {
      const n = toNumber(value);
      return n === null
        ? String(value)
        : n.toLocaleString('en-IN', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    }
    case 'percent': {
      const n = toNumber(value);
      return n === null ? String(value) : `${n.toFixed(2)}%`;
    }
    case 'date':
      return fmtDate(String(value));
    case 'datetime':
      return fmtDateTime(String(value));
    case 'bool':
      return toBool(value) ? 'Yes' : 'No';
    default:
      return String(value);
  }
}

/** Numeric and money columns sit right, dates centre, everything else left. */
export function alignmentFor(column: ReportColumnDto): 'num' | 'center' | '' {
  const declared = (column.alignment ?? '').toLowerCase();
  if (declared === 'right') return 'num';
  if (declared === 'center') return 'center';
  if (declared === 'left') return '';

  const type = (column.dataType ?? 'string').toLowerCase();
  if (['currency', 'decimal', 'int', 'percent'].includes(type)) return 'num';
  if (['date', 'datetime'].includes(type)) return 'center';
  return '';
}

/* ------------------------------------------------------------------------ date ranges */

export type QuickRange = 'today' | 'week' | 'month' | 'lastMonth' | 'year';

export const QUICK_RANGES: { key: QuickRange; label: string }[] = [
  { key: 'today', label: 'Today' },
  { key: 'week', label: 'This week' },
  { key: 'month', label: 'This month' },
  { key: 'lastMonth', label: 'Last month' },
  { key: 'year', label: 'This year' },
];

function asIso(value: Date): string {
  const offset = value.getTimezoneOffset() * 60000;
  return new Date(value.getTime() - offset).toISOString().slice(0, 10);
}

/** Resolves a quick-range pill to the inclusive [from, to] pair the API expects. */
export function resolveQuickRange(range: QuickRange): { from: string; to: string } {
  const now = new Date();
  const y = now.getFullYear();
  const m = now.getMonth();

  switch (range) {
    case 'today':
      return { from: asIso(now), to: asIso(now) };
    case 'week': {
      const day = (now.getDay() + 6) % 7; // Monday-first
      const start = new Date(y, m, now.getDate() - day);
      return { from: asIso(start), to: asIso(now) };
    }
    case 'month':
      return { from: asIso(new Date(y, m, 1)), to: asIso(new Date(y, m + 1, 0)) };
    case 'lastMonth':
      return { from: asIso(new Date(y, m - 1, 1)), to: asIso(new Date(y, m, 0)) };
    case 'year':
      return { from: asIso(new Date(y, 0, 1)), to: asIso(new Date(y, 11, 31)) };
  }
}

/* ----------------------------------------------------------------------------- lookups */

export interface PaymentMethodLookup { id: number; code: string; name: string; isActive: boolean; }

export const reportPlanLookup = (signal?: AbortSignal) =>
  api.get<Lookup[]>('/api/membership-plans/lookup', undefined, signal);

export const reportTrainerLookup = (signal?: AbortSignal) =>
  api.get<Lookup[]>('/api/trainers/lookup', { onlyActive: true }, signal);

export const reportUserLookup = (signal?: AbortSignal) =>
  api.get<Lookup[]>('/api/users/lookup', undefined, signal);

export const reportMethodLookup = (signal?: AbortSignal) =>
  api.get<PaymentMethodLookup[]>('/api/payments/methods', { onlyActive: true }, signal);

export const reportMemberLookup = (term: string, signal?: AbortSignal) =>
  api.get<Lookup[]>('/api/members/lookup', { term, take: 20 }, signal);
