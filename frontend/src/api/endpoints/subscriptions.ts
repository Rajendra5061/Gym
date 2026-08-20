/**
 * Subscription sale, renewal, plan change, freeze/resume and cancellation endpoints.
 *
 * The one rule that shapes this module: **the client never prices a sale**. `quote` asks the
 * server for the breakdown and every screen renders what comes back verbatim.
 */

import { api } from '@/api/client';
import type {
  Lookup, PagedResult, PaymentStatus, SubscriptionDto, SubscriptionStatus,
} from '@/api/types';

/* --------------------------------------------------------------------- quote */

/** Server-side price breakdown returned by `POST /api/subscriptions/quote`. */
export interface SubscriptionQuoteDto {
  membershipPlanId: number;
  planName: string;
  startDate: string;
  endDate: string;
  totalDays: number;
  planAmount: number;
  registrationFee: number;
  proratedCredit: number;
  discountAmount: number;
  maxAllowedDiscount: number;
  taxPercent: number;
  taxAmount: number;
  finalAmount: number;
}

export interface QuoteRequest {
  membershipPlanId: number;
  startDate: string;
  discountAmount: number;
  chargeRegistrationFee: boolean;
  currentSubscriptionId?: number | null;
  prorateUnusedAmount?: boolean;
}

/* ------------------------------------------------------------------ requests */

/** Payment collected in the same transaction as the subscription. Reference and VPA only. */
export interface CollectPaymentInline {
  paymentMethodId: number;
  amount: number;
  transactionReference?: string | null;
  payerVpa?: string | null;
  paymentDate?: string | null;
  notes?: string | null;
  markConfirmed: boolean;
}

export interface CreateSubscriptionInput {
  memberId: number;
  membershipPlanId: number;
  startDate: string;
  discountAmount: number;
  chargeRegistrationFee: boolean;
  assignedTrainerId?: number | null;
  notes?: string | null;
  payment?: CollectPaymentInline | null;
}

export interface RenewSubscriptionInput {
  memberId: number;
  previousSubscriptionId?: number | null;
  membershipPlanId?: number | null;
  startDate?: string | null;
  discountAmount: number;
  assignedTrainerId?: number | null;
  notes?: string | null;
  payment?: CollectPaymentInline | null;
}

export interface ChangePlanInput {
  subscriptionId: number;
  newMembershipPlanId: number;
  effectiveDate: string;
  prorateUnusedAmount: boolean;
  discountAmount: number;
  reason?: string | null;
  payment?: CollectPaymentInline | null;
}

export interface FreezeInput {
  subscriptionId: number;
  freezeStartDate: string;
  freezeEndDate: string;
  reason?: string | null;
}

export interface ResumeInput {
  subscriptionId: number;
  resumeDate: string;
  remarks?: string | null;
}

export interface CancelInput {
  subscriptionId: number;
  reason: string;
  refundRemainingAmount: boolean;
}

/* ------------------------------------------------------------------ readback */

/**
 * The API returns `AssignedTrainerId` / `AssignedTrainerName` on every SubscriptionDto, but the
 * shared `api/types.ts` shape predates those columns. Widen it here rather than editing the
 * shared type, so a screen that needs the current trainer can read it:
 *   `(row as SubscriptionDto & SubscriptionTrainer).assignedTrainerId`
 */
export interface SubscriptionTrainer {
  assignedTrainerId?: number | null;
  assignedTrainerName?: string | null;
}

export interface SubscriptionHistoryDto {
  id: number;
  subscriptionId: number;
  actionType: number;
  actionText: string;
  actionDate: string;
  oldStatus?: SubscriptionStatus | null;
  newStatus?: SubscriptionStatus | null;
  oldEndDate?: string | null;
  newEndDate?: string | null;
  amount?: number | null;
  remarks?: string | null;
  performedByName?: string | null;
}

export interface ExpiryProcessingResultDto {
  expiredCount: number;
  notificationsCreated: number;
  resumedFromFreeze: number;
  processedAtUtc: string;
  messages: string[];
}

/** Query string accepted by `GET /api/subscriptions`. Mirrors SubscriptionQueryDto. */
export interface SubscriptionQuery {
  pageNumber?: number;
  pageSize?: number;
  search?: string;
  sortBy?: string;
  sortDescending?: boolean;
  memberId?: number | '';
  membershipPlanId?: number | '';
  status?: SubscriptionStatus | '';
  paymentStatus?: PaymentStatus | '';
  startFrom?: string;
  startTo?: string;
  endFrom?: string;
  endTo?: string;
  onlyRenewals?: boolean | '';
  onlyExpiringSoon?: boolean | '';
  expiringWithinDays?: number;
  onlyWithOutstanding?: boolean | '';
  excludeRenewed?: boolean | '';
}

export const subscriptionsApi = {
  quote: (body: QuoteRequest) =>
    api.post<SubscriptionQuoteDto>('/api/subscriptions/quote', body),

  paged: (query: SubscriptionQuery, signal?: AbortSignal) =>
    api.get<PagedResult<SubscriptionDto>>('/api/subscriptions', { ...query }, signal),

  byId: (id: number, signal?: AbortSignal) =>
    api.get<SubscriptionDto>(`/api/subscriptions/${id}`, undefined, signal),

  forMember: (memberId: number, signal?: AbortSignal) =>
    api.get<SubscriptionDto[]>(`/api/subscriptions/member/${memberId}`, undefined, signal),

  history: (id: number, signal?: AbortSignal) =>
    api.get<SubscriptionHistoryDto[]>(`/api/subscriptions/${id}/history`, undefined, signal),

  create: (body: CreateSubscriptionInput) =>
    api.post<SubscriptionDto>('/api/subscriptions', body),

  renew: (body: RenewSubscriptionInput) =>
    api.post<SubscriptionDto>('/api/subscriptions/renew', body),

  changePlan: (body: ChangePlanInput) =>
    api.post<SubscriptionDto>('/api/subscriptions/change-plan', body),

  freeze: (body: FreezeInput) =>
    api.post<SubscriptionDto>('/api/subscriptions/freeze', body),

  resume: (body: ResumeInput) =>
    api.post<SubscriptionDto>('/api/subscriptions/resume', body),

  cancel: (body: CancelInput) =>
    api.post<SubscriptionDto>('/api/subscriptions/cancel', body),

  remove: (id: number) =>
    api.del<void>(`/api/subscriptions/${id}`),

  restore: (id: number) =>
    api.post<void>(`/api/subscriptions/${id}/restore`),

  processExpiries: () =>
    api.post<ExpiryProcessingResultDto>('/api/subscriptions/process-expiries'),
};

/**
 * Member type-ahead used by every billing screen (new subscription, record payment, check-in).
 * It lives with the billing endpoints so the billing screens share one copy rather than each
 * re-declaring the call.
 */
export function lookupMembers(term: string, signal?: AbortSignal, take = 20) {
  return api.get<Lookup[]>('/api/members/lookup', { term, take }, signal);
}
