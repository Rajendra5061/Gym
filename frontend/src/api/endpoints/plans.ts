/**
 * Membership plan catalogue endpoints.
 *
 * Everything the plan screen needs: the paged catalogue, the sellable subset, the combo-box
 * lookup and the create/update/delete/restore mutations. Amounts are never derived here;
 * the server owns pricing.
 */

import { api } from '@/api/client';
import type { Lookup, MembershipPlanDto, PagedResult, PlanDurationType, PlanStatus } from '@/api/types';

/** Query string accepted by `GET /api/membership-plans`. Mirrors MembershipPlanQueryDto. */
export interface PlanQuery {
  pageNumber?: number;
  pageSize?: number;
  search?: string;
  sortBy?: string;
  sortDescending?: boolean;
  status?: PlanStatus | '';
  durationType?: PlanDurationType | '';
  includeDeleted?: boolean;
}

/**
 * Writable half of MembershipPlanDto. `planCode`, `totalDays` and the *Text projections are
 * server-derived, so the form never sends them.
 */
export interface PlanInput {
  id: number;
  name: string;
  description?: string | null;
  features?: string | null;
  durationType: PlanDurationType;
  durationValue: number;
  price: number;
  registrationFee?: number | null;
  taxPercent: number;
  maxDiscountPercent: number;
  gracePeriodDays: number;
  maxFreezeDays: number;
  sessionLimit?: number | null;
  trainerIncluded: boolean;
  displayOrder: number;
  status: PlanStatus;
}

export const plansApi = {
  paged: (query: PlanQuery, signal?: AbortSignal) =>
    api.get<PagedResult<MembershipPlanDto>>('/api/membership-plans', { ...query }, signal),

  active: (signal?: AbortSignal) =>
    api.get<MembershipPlanDto[]>('/api/membership-plans/active', undefined, signal),

  byId: (id: number, signal?: AbortSignal) =>
    api.get<MembershipPlanDto>(`/api/membership-plans/${id}`, undefined, signal),

  lookup: (signal?: AbortSignal) =>
    api.get<Lookup[]>('/api/membership-plans/lookup', undefined, signal),

  create: (body: PlanInput) =>
    api.post<MembershipPlanDto>('/api/membership-plans', body),

  update: (id: number, body: PlanInput) =>
    api.put<MembershipPlanDto>(`/api/membership-plans/${id}`, body),

  remove: (id: number) =>
    api.del<void>(`/api/membership-plans/${id}`),

  restore: (id: number) =>
    api.post<void>(`/api/membership-plans/${id}/restore`),
};
