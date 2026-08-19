/**
 * Trainer salary payments. Mirrors SalariesController on the API: paged list, per-year summary,
 * record (the server also books a matching Expense under the "Salaries" category) and soft delete.
 */
import { api } from '@/api/client';
import type { PagedResult } from '@/api/types';

export interface SalaryPaymentDto {
  id: number;
  trainerId: number;
  trainerName: string;
  periodYear: number;
  periodMonth: number;
  baseAmount: number;
  bonus: number;
  deduction: number;
  netAmount: number;
  paymentDate: string;
  paymentMethodId?: number | null;
  paymentMethodName?: string | null;
  transactionReference?: string | null;
  notes?: string | null;
  createdAt: string;
}

/** Body for POST /api/salaries. NetAmount is computed server-side (base + bonus − deduction). */
export interface SaveSalaryPaymentDto {
  trainerId: number;
  periodYear: number;
  periodMonth: number;
  baseAmount: number;
  bonus: number;
  deduction: number;
  paymentDate: string;
  paymentMethodId?: number | null;
  transactionReference?: string | null;
  notes?: string | null;
}

/** Query string for GET /api/salaries. Mirrors SalaryQueryDto : PagedRequest on the API. */
export interface SalaryQueryDto {
  year?: number | '';
  month?: number | '';
  trainerId?: number | '';
  search?: string;
  sortBy?: string;
  sortDescending?: boolean;
  pageNumber: number;
  pageSize: number;
}

export interface SalarySummaryMonthDto { month: number; totalNet: number; payments: number; }

export interface SalarySummaryTrainerDto {
  trainerId: number;
  trainerName: string;
  monthlySalary?: number | null;
  paidCount: number;
  totalNet: number;
}

/** GET /api/salaries/summary?year= — the year at a glance for the tiles and the bar chart. */
export interface SalarySummaryDto {
  year: number;
  months: SalarySummaryMonthDto[];
  totalYear: number;
  perTrainer: SalarySummaryTrainerDto[];
}

export const salariesApi = {
  list: (query: SalaryQueryDto, signal?: AbortSignal) =>
    api.get<PagedResult<SalaryPaymentDto>>('/api/salaries', { ...query }, signal),

  summary: (year: number, signal?: AbortSignal) =>
    api.get<SalarySummaryDto>('/api/salaries/summary', { year }, signal),

  create: (dto: SaveSalaryPaymentDto) =>
    api.post<SalaryPaymentDto>('/api/salaries', dto),

  remove: (id: number) => api.del<void>(`/api/salaries/${id}`),
};
