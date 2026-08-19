/**
 * Front-desk operations: equipment inventory, enquiries (leads) and member feedback.
 *
 * All three controllers are live in the API — `/api/equipment`, `/api/enquiries` and
 * `/api/feedback` each answer normally. `isModuleMissing` remains as the shared 404 check the
 * Equipment, Enquiries and Feedback screens use to fall back to an empty state rather than
 * surfacing a raw error.
 */
import { ApiError, api } from '@/api/client';
import type { Lookup, PagedResult } from '@/api/types';
import { words } from '@/lib/format';

/**
 * True when a call answered 404. Now that the controllers exist this means the record itself is
 * gone (deleted by someone else, or a stale id in the URL) rather than a missing module.
 */
export function isModuleMissing(error: unknown): boolean {
  return error instanceof ApiError && error.status === 404;
}

/* --------------------------------------------------------------------------- equipment */

export enum EquipmentCondition {
  New = 1,
  Good = 2,
  NeedsService = 3,
  UnderRepair = 4,
  Retired = 5,
}

export const EQUIPMENT_CONDITIONS: { value: EquipmentCondition; label: string }[] = [
  { value: EquipmentCondition.New, label: 'New' },
  { value: EquipmentCondition.Good, label: 'Good' },
  { value: EquipmentCondition.NeedsService, label: 'Needs service' },
  { value: EquipmentCondition.UnderRepair, label: 'Under repair' },
  { value: EquipmentCondition.Retired, label: 'Retired' },
];

/**
 * The wording for a condition. The curated label wins; a value this build has never heard of
 * falls back to the enum name the API sent, split into words rather than shown as `NeedsService`.
 */
export function conditionLabel(
  value: EquipmentCondition | null | undefined,
  serverText?: string | null,
): string {
  return EQUIPMENT_CONDITIONS.find((c) => c.value === value)?.label ?? (words(serverText) || '—');
}

export interface EquipmentDto {
  id: number;
  name: string;
  code?: string | null;
  category?: string | null;
  serialNumber?: string | null;
  manufacturer?: string | null;
  purchaseDate?: string | null;
  purchaseCost?: number | null;
  quantity: number;
  condition: EquipmentCondition;
  conditionText?: string | null;
  location?: string | null;
  warrantyExpiry?: string | null;
  lastServicedOn?: string | null;
  nextServiceDue?: string | null;
  notes?: string | null;
  isActive: boolean;
}

export interface EquipmentQuery {
  search?: string;
  category?: string;
  condition?: EquipmentCondition | '';
  includeDeleted?: boolean;
  pageNumber: number;
  pageSize: number;
}

export const listEquipment = (query: EquipmentQuery, signal?: AbortSignal) =>
  api.get<PagedResult<EquipmentDto>>('/api/equipment', { ...query }, signal);

/**
 * The list projection carries no purchase, warranty or serial fields, so the editor loads the
 * full record first — saving a list row straight back would blank everything it omits.
 */
export const getEquipment = (id: number, signal?: AbortSignal) =>
  api.get<EquipmentDto>(`/api/equipment/${id}`, undefined, signal);

/** Distinct categories already in use, for the filter drop-down. */
export const equipmentCategories = (signal?: AbortSignal) =>
  api.get<string[]>('/api/equipment/categories', undefined, signal);

export const saveEquipment = (dto: Partial<EquipmentDto>) =>
  dto.id
    ? api.put<EquipmentDto>(`/api/equipment/${dto.id}`, dto)
    : api.post<EquipmentDto>('/api/equipment', dto);

export const deleteEquipment = (id: number) => api.del<void>(`/api/equipment/${id}`);
export const restoreEquipment = (id: number) => api.post<void>(`/api/equipment/${id}/restore`);

/* --------------------------------------------------------------------------- enquiries */

export enum EnquirySource {
  WalkIn = 1,
  Phone = 2,
  Website = 3,
  Referral = 4,
  SocialMedia = 5,
  Other = 99,
}

export enum EnquiryStatus {
  New = 1,
  Contacted = 2,
  FollowUp = 3,
  Converted = 4,
  Lost = 5,
}

export const ENQUIRY_SOURCES: { value: EnquirySource; label: string }[] = [
  { value: EnquirySource.WalkIn, label: 'Walk-in' },
  { value: EnquirySource.Phone, label: 'Phone' },
  { value: EnquirySource.Website, label: 'Website' },
  { value: EnquirySource.Referral, label: 'Referral' },
  { value: EnquirySource.SocialMedia, label: 'Social media' },
  { value: EnquirySource.Other, label: 'Other' },
];

export const ENQUIRY_STATUSES: { value: EnquiryStatus; label: string }[] = [
  { value: EnquiryStatus.New, label: 'New' },
  { value: EnquiryStatus.Contacted, label: 'Contacted' },
  { value: EnquiryStatus.FollowUp, label: 'Follow up' },
  { value: EnquiryStatus.Converted, label: 'Converted' },
  { value: EnquiryStatus.Lost, label: 'Lost' },
];

export const sourceLabel = (value: EnquirySource | null | undefined, serverText?: string | null) =>
  ENQUIRY_SOURCES.find((s) => s.value === value)?.label ?? (words(serverText) || '—');

export const enquiryStatusLabel = (value: EnquiryStatus | null | undefined, serverText?: string | null) =>
  ENQUIRY_STATUSES.find((s) => s.value === value)?.label ?? (words(serverText) || '—');

export interface EnquiryDto {
  id: number;
  fullName: string;
  phone?: string | null;
  email?: string | null;
  source: EnquirySource;
  sourceText?: string | null;
  interestedPlanId?: number | null;
  interestedPlanName?: string | null;
  message?: string | null;
  status: EnquiryStatus;
  statusText?: string | null;
  followUpDate?: string | null;
  assignedToUserId?: number | null;
  assignedToName?: string | null;
  notes?: string | null;
  createdAt?: string | null;
  createdAtUtc?: string | null;
}

export interface EnquiryQuery {
  search?: string;
  status?: EnquiryStatus | '';
  source?: EnquirySource | '';
  fromDate?: string;
  toDate?: string;
  pageNumber: number;
  pageSize: number;
}

export const listEnquiries = (query: EnquiryQuery, signal?: AbortSignal) =>
  api.get<PagedResult<EnquiryDto>>('/api/enquiries', { ...query }, signal);

export const getEnquiry = (id: number, signal?: AbortSignal) =>
  api.get<EnquiryDto>(`/api/enquiries/${id}`, undefined, signal);

export const saveEnquiry = (dto: Partial<EnquiryDto>) =>
  dto.id
    ? api.put<EnquiryDto>(`/api/enquiries/${dto.id}`, dto)
    : api.post<EnquiryDto>('/api/enquiries', dto);

export const deleteEnquiry = (id: number) => api.del<void>(`/api/enquiries/${id}`);

export const convertEnquiry = (id: number, memberId: number) =>
  api.post<EnquiryDto>(`/api/enquiries/${id}/convert/${memberId}`);

/* ---------------------------------------------------------------------------- feedback */

export enum FeedbackStatus {
  New = 1,
  Reviewed = 2,
  Resolved = 3,
  Dismissed = 4,
}

export const FEEDBACK_STATUSES: { value: FeedbackStatus; label: string }[] = [
  { value: FeedbackStatus.New, label: 'New' },
  { value: FeedbackStatus.Reviewed, label: 'Reviewed' },
  { value: FeedbackStatus.Resolved, label: 'Resolved' },
  { value: FeedbackStatus.Dismissed, label: 'Dismissed' },
];

export const feedbackStatusLabel = (value: FeedbackStatus | null | undefined, serverText?: string | null) =>
  FEEDBACK_STATUSES.find((s) => s.value === value)?.label ?? (words(serverText) || '—');

export interface FeedbackDto {
  id: number;
  memberId?: number | null;
  memberName?: string | null;
  memberCode?: string | null;
  subject?: string | null;
  message?: string | null;
  rating?: number | null;
  status: FeedbackStatus;
  statusText?: string | null;
  hasResponse?: boolean | null;
  adminResponse?: string | null;
  respondedAt?: string | null;
  isPrivate?: boolean | null;
  createdAt?: string | null;
  createdAtUtc?: string | null;
}

export interface FeedbackQuery {
  search?: string;
  status?: FeedbackStatus | '';
  /** The API filters on a rating band, so an exact rating is sent as both bounds. */
  minRating?: number | '';
  maxRating?: number | '';
  fromDate?: string;
  toDate?: string;
  pageNumber: number;
  pageSize: number;
}

export const listFeedback = (query: FeedbackQuery, signal?: AbortSignal) =>
  api.get<PagedResult<FeedbackDto>>('/api/feedback', { ...query }, signal);

/** The list projection omits the message body, so the respond dialog loads the full record. */
export const getFeedback = (id: number, signal?: AbortSignal) =>
  api.get<FeedbackDto>(`/api/feedback/${id}`, undefined, signal);

export const respondToFeedback = (id: number, response: string) =>
  api.post<FeedbackDto>(`/api/feedback/${id}/respond`, { response });

export const deleteFeedback = (id: number) => api.del<void>(`/api/feedback/${id}`);

/* ----------------------------------------------------------------------------- lookups */

export const membershipPlanLookup = (signal?: AbortSignal) =>
  api.get<Lookup[]>('/api/membership-plans/lookup', undefined, signal);

export const userLookup = (signal?: AbortSignal) =>
  api.get<Lookup[]>('/api/users/lookup', undefined, signal);
