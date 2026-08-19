/** Dashboard endpoints. */
import { api } from '@/api/client';
import type { DashboardDto, DashboardStats } from '@/api/types';

export const dashboardApi = {
  /** Every card, chart and feed in one call. */
  get: (signal?: AbortSignal) => api.get<DashboardDto>('/api/dashboard', undefined, signal),

  /** Just the headline counters, for lightweight polling. */
  stats: (signal?: AbortSignal) => api.get<DashboardStats>('/api/dashboard/stats', undefined, signal),
};
