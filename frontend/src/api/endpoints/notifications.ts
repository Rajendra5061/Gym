/** Notification centre: the alert feed, its counters and the housekeeping actions. */
import { api } from '@/api/client';
import type { NotificationDto, PagedResult } from '@/api/types';
import { NotificationSeverity } from '@/api/types';
import { words } from '@/lib/format';

export interface NotificationCountsDto {
  total: number;
  unread: number;
  critical: number;
  warning: number;
}

export interface NotificationQuery {
  search?: string;
  type?: number | '';
  severity?: NotificationSeverity | '';
  isRead?: boolean | '';
  onlyMine?: boolean;
  fromDate?: string;
  toDate?: string;
  pageNumber: number;
  pageSize: number;
}

export interface CreateNotificationDto {
  type: number;
  severity: NotificationSeverity;
  title: string;
  message: string;
  userId?: number | null;
  memberId?: number | null;
}

export const SEVERITY_OPTIONS: { value: NotificationSeverity; label: string }[] = [
  { value: NotificationSeverity.Info, label: 'Info' },
  { value: NotificationSeverity.Success, label: 'Success' },
  { value: NotificationSeverity.Warning, label: 'Warning' },
  { value: NotificationSeverity.Critical, label: 'Critical' },
];

export const NOTIFICATION_TYPES: { value: number; label: string }[] = [
  { value: 0, label: 'General' },
  { value: 1, label: 'Membership expiring soon' },
  { value: 2, label: 'Membership expired' },
  { value: 3, label: 'Payment pending' },
  { value: 4, label: 'Payment successful' },
  { value: 5, label: 'Subscription renewal' },
  { value: 6, label: 'New member registration' },
  { value: 7, label: 'Licence expiring soon' },
  { value: 8, label: 'Licence expired' },
];

export function severityLabel(
  value: NotificationSeverity | null | undefined,
  serverText?: string | null,
): string {
  return SEVERITY_OPTIONS.find((s) => s.value === value)?.label ?? (words(serverText) || 'Info');
}

/**
 * The wording for a notification type. Types are plain integers, so a value added on the server
 * after this build falls back to the `PaymentSuccessful`-style name it sent, split into words.
 */
export function notificationTypeLabel(value: number | null | undefined, serverText?: string | null): string {
  return NOTIFICATION_TYPES.find((t) => t.value === value)?.label ?? (words(serverText) || '');
}

/** CSS suffix driving the 4px coloured edge on a notification card. */
export function severityKey(value: NotificationSeverity | null | undefined): string {
  switch (value) {
    case NotificationSeverity.Critical: return 'critical';
    case NotificationSeverity.Warning: return 'warning';
    case NotificationSeverity.Success: return 'success';
    default: return 'info';
  }
}

export const listNotifications = (query: NotificationQuery, signal?: AbortSignal) =>
  api.get<PagedResult<NotificationDto>>('/api/notifications', { ...query }, signal);

export const getNotificationCounts = (signal?: AbortSignal) =>
  api.get<NotificationCountsDto>('/api/notifications/counts', undefined, signal);

export const createNotification = (dto: CreateNotificationDto) =>
  api.post<NotificationDto>('/api/notifications', dto);

export const markNotificationsRead = (ids: number[]) =>
  api.post<void>('/api/notifications/mark-read', ids);

export const markAllNotificationsRead = () =>
  api.post<void>('/api/notifications/mark-all-read');

export const deleteNotification = (id: number) =>
  api.del<void>(`/api/notifications/${id}`);

export const generateSystemAlerts = () =>
  api.post<number>('/api/notifications/generate-alerts');
