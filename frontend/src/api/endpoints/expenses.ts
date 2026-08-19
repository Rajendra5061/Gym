/** Expense ledger and its categories. Mirrors ExpensesController on the API. */
import { api } from '@/api/client';
import type { PagedResult } from '@/api/types';

export interface ExpenseCategoryDto {
  id: number;
  name: string;
  description?: string | null;
  isActive: boolean;
  totalAmount: number;
  expenseCount: number;
}

export interface ExpenseDto {
  id: number;
  expenseNumber: string;
  expenseCategoryId: number;
  categoryName: string;
  title: string;
  description?: string | null;
  amount: number;
  expenseDate: string;
  paymentMethodId?: number | null;
  paymentMethodName?: string | null;
  vendorName?: string | null;
  referenceNumber?: string | null;
  attachmentPath?: string | null;
  recordedByName?: string | null;
  createdAt?: string | null;
}

export interface SaveExpenseDto {
  id: number;
  expenseCategoryId: number;
  title: string;
  description?: string | null;
  amount: number;
  expenseDate: string;
  paymentMethodId?: number | null;
  vendorName?: string | null;
  referenceNumber?: string | null;
}

export interface ExpenseQuery {
  search?: string;
  expenseCategoryId?: number | '';
  fromDate?: string;
  toDate?: string;
  minAmount?: number | '';
  maxAmount?: number | '';
  includeDeleted?: boolean;
  pageNumber: number;
  pageSize: number;
}

export interface PaymentMethodDto {
  id: number;
  code: string;
  name: string;
  requiresReference: boolean;
  supportsQrCode: boolean;
  isActive: boolean;
  displayOrder: number;
}

export const listExpenses = (query: ExpenseQuery, signal?: AbortSignal) =>
  api.get<PagedResult<ExpenseDto>>('/api/expenses', { ...query }, signal);

export const getExpense = (id: number, signal?: AbortSignal) =>
  api.get<ExpenseDto>(`/api/expenses/${id}`, undefined, signal);

export const saveExpense = (dto: SaveExpenseDto) =>
  dto.id > 0
    ? api.put<ExpenseDto>(`/api/expenses/${dto.id}`, dto)
    : api.post<ExpenseDto>('/api/expenses', dto);

export const deleteExpense = (id: number) => api.del<void>(`/api/expenses/${id}`);

export const restoreExpense = (id: number) => api.post<void>(`/api/expenses/${id}/restore`);

export const listExpenseCategories = (signal?: AbortSignal) =>
  api.get<ExpenseCategoryDto[]>('/api/expenses/categories', undefined, signal);

export const saveExpenseCategory = (dto: Partial<ExpenseCategoryDto>) =>
  api.post<ExpenseCategoryDto>('/api/expenses/categories', dto);

export const listPaymentMethods = (signal?: AbortSignal) =>
  api.get<PaymentMethodDto[]>('/api/payments/methods', { onlyActive: true }, signal);
