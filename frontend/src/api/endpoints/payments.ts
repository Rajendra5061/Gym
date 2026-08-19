/**
 * Payment collection, receipts, UPI intents, refunds and outstanding balances.
 *
 * Nothing here ever carries card numbers, CVVs or UPI PINs — the only instrument details the
 * API accepts are a transaction reference and a payer VPA.
 */

import { api, download } from '@/api/client';
import type { PagedResult, PaymentDto, PaymentStatus, RefundStatus } from '@/api/types';

export interface PaymentMethodDto {
  id: number;
  code: string;
  name: string;
  requiresReference: boolean;
  supportsQrCode: boolean;
  isActive: boolean;
  displayOrder: number;
}

export interface CreatePaymentInput {
  memberId: number;
  subscriptionId?: number | null;
  /** Recorded when the desk picked a plan but no subscription was created. */
  membershipPlanId?: number | null;
  amount: number;
  discountAmount: number;
  taxAmount: number;
  paymentMethodId: number;
  transactionReference?: string | null;
  payerVpa?: string | null;
  paymentDate?: string | null;
  notes?: string | null;
  markConfirmed: boolean;
}

export interface ConfirmPaymentInput {
  paymentId: number;
  transactionReference?: string | null;
  remarks?: string | null;
}

export interface CreateRefundInput {
  paymentId: number;
  amount: number;
  reason: string;
  refundMethod?: string | null;
  transactionReference?: string | null;
  remarks?: string | null;
  approveImmediately: boolean;
}

export interface ApproveRefundInput {
  refundId: number;
  approve: boolean;
  remarks?: string | null;
}

export interface PaymentRefundDto {
  id: number;
  refundNumber: string;
  paymentId: number;
  receiptNumber: string;
  memberId: number;
  memberName: string;
  amount: number;
  reason: string;
  status: RefundStatus;
  statusText: string;
  requestedAt: string;
  requestedByName?: string | null;
  approvedAt?: string | null;
  approvedByName?: string | null;
  refundMethod?: string | null;
  transactionReference?: string | null;
  remarks?: string | null;
}

export interface OutstandingBalanceDto {
  memberId: number;
  memberCode: string;
  memberName: string;
  phone?: string | null;
  subscriptionId: number;
  subscriptionCode: string;
  planName: string;
  startDate: string;
  endDate: string;
  finalAmount: number;
  paidAmount: number;
  outstandingAmount: number;
  daysOverdue: number;
}

/**
 * Everything needed to render the UPI collection panel. A static QR carries no gateway
 * callback, so `requiresManualVerification` is almost always true and the operator has to
 * key the reference back in before the payment counts as settled.
 */
export interface UpiPaymentIntentDto {
  memberId: number;
  memberName: string;
  subscriptionId?: number | null;
  amount: number;
  currencySymbol: string;
  upiId?: string | null;
  payeeName?: string | null;
  upiDeepLink?: string | null;
  qrImagePath?: string | null;
  paymentReference: string;
  requiresManualVerification: boolean;
  instructions: string;
}

export interface UpiIntentInput {
  memberId: number;
  subscriptionId?: number | null;
  amount: number;
  notes?: string | null;
}

/** Query string accepted by `GET /api/payments`. Mirrors PaymentQueryDto. */
export interface PaymentQuery {
  pageNumber?: number;
  pageSize?: number;
  search?: string;
  sortBy?: string;
  sortDescending?: boolean;
  memberId?: number | '';
  subscriptionId?: number | '';
  paymentMethodId?: number | '';
  status?: PaymentStatus | '';
  fromDate?: string;
  toDate?: string;
  minAmount?: number | '';
  maxAmount?: number | '';
  onlyToday?: boolean;
}

/** `GET /api/payments/refunds` and `/outstanding` accept the plain paged request only. */
export interface PagedQuery {
  pageNumber?: number;
  pageSize?: number;
  search?: string;
  sortBy?: string;
  sortDescending?: boolean;
}

export const paymentsApi = {
  paged: (query: PaymentQuery, signal?: AbortSignal) =>
    api.get<PagedResult<PaymentDto>>('/api/payments', { ...query }, signal),

  byId: (id: number, signal?: AbortSignal) =>
    api.get<PaymentDto>(`/api/payments/${id}`, undefined, signal),

  create: (body: CreatePaymentInput) =>
    api.post<PaymentDto>('/api/payments', body),

  confirm: (body: ConfirmPaymentInput) =>
    api.post<PaymentDto>('/api/payments/confirm', body),

  /** Streams the receipt PDF; the caller hands the blob to `saveBlob`. */
  receiptPdf: (id: number) =>
    download(`/api/payments/${id}/receipt/pdf`),

  upiIntent: (body: UpiIntentInput) =>
    api.post<UpiPaymentIntentDto>('/api/payments/upi-intent', body),

  createRefund: (body: CreateRefundInput) =>
    api.post<PaymentRefundDto>('/api/payments/refunds', body),

  approveRefund: (body: ApproveRefundInput) =>
    api.post<PaymentRefundDto>('/api/payments/refunds/approve', body),

  refunds: (query: PagedQuery, signal?: AbortSignal) =>
    api.get<PagedResult<PaymentRefundDto>>('/api/payments/refunds', { ...query }, signal),

  outstanding: (query: PagedQuery, signal?: AbortSignal) =>
    api.get<PagedResult<OutstandingBalanceDto>>('/api/payments/outstanding', { ...query }, signal),

  methods: (signal?: AbortSignal, onlyActive = true) =>
    api.get<PaymentMethodDto[]>('/api/payments/methods', { onlyActive }, signal),
};

/**
 * Create or update a payment method. Requires `settings.manage` server-side — the Payments screen
 * hides the controls without it rather than letting the call fail at the API.
 */
export const savePaymentMethod = (dto: PaymentMethodDto) =>
  api.post<PaymentMethodDto>('/api/payments/methods', dto);
