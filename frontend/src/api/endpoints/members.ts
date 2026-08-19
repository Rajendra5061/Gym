/**
 * Member endpoints. Every shape here mirrors a DTO the API already returns — nothing is
 * invented client-side. Query keys are sent in the PascalCase the ASP.NET binder expects.
 */
import { api } from '@/api/client';
import {
  Gender,
  MemberStatus,
  type AttendanceDto,
  type Lookup,
  type MemberListDto,
  type PagedResult,
  type PaymentDto,
  type SubscriptionDto,
} from '@/api/types';
import { words } from '@/lib/format';

export interface MemberDetailDto {
  id: number; memberCode: string; fullName: string; gender: Gender;
  dateOfBirth?: string | null; age?: number | null;
  phone: string; email?: string | null;
  address?: string | null; city?: string | null; postalCode?: string | null;
  emergencyContactName?: string | null; emergencyContactPhone?: string | null;
  emergencyContactRelation?: string | null;
  joiningDate: string; profilePhotoPath?: string | null; bloodGroup?: string | null;
  heightCm?: number | null; weightKg?: number | null;
  status: MemberStatus; notes?: string | null;
  assignedTrainerId?: number | null; assignedTrainerName?: string | null;
  hasUserAccount: boolean; createdAt: string; updatedAt?: string | null;
  activeSubscription?: SubscriptionDto | null;
  totalPaid: number; totalOutstanding: number; totalVisits: number; lastVisitDate?: string | null;
}

export interface MemberMeasurementDto {
  id: number; memberId: number; measuredOn: string;
  weightKg?: number | null; heightCm?: number | null; bodyFatPercent?: number | null;
  muscleMassKg?: number | null; chestCm?: number | null; waistCm?: number | null;
  hipCm?: number | null; armCm?: number | null; thighCm?: number | null; bmi?: number | null;
  recordedByTrainerId?: number | null; recordedByTrainerName?: string | null; notes?: string | null;
}

export interface MemberDocumentDto {
  id: number; memberId: number; documentType: string; fileName: string; filePath: string;
  contentType?: string | null; fileSizeBytes: number; notes?: string | null; createdAt: string;
}

export interface WorkoutSessionDto {
  id: number; memberId: number; memberName: string; memberCode?: string | null;
  trainerId?: number | null; trainerName?: string | null;
  workoutPlanId?: number | null; workoutPlanName?: string | null;
  sessionDate: string; startTime?: string | null; endTime?: string | null;
  durationMinutes?: number | null; caloriesBurned?: number | null; notes?: string | null;
  exerciseCount: number; totalVolumeKg: number;
}

/** Everything the member details screen needs, in one round trip. */
export interface MemberHistoryDto {
  member: MemberDetailDto;
  subscriptions: SubscriptionDto[];
  payments: PaymentDto[];
  attendance: AttendanceDto[];
  workoutSessions: WorkoutSessionDto[];
  measurements: MemberMeasurementDto[];
  documents: MemberDocumentDto[];
}

export interface TemporaryPasswordDto { userId: number; userName: string; temporaryPassword: string; }

export interface CreateMemberResult { member: MemberDetailDto; account?: TemporaryPasswordDto | null; }

/** Body for POST /api/members and PUT /api/members/{id}; `status` only travels on update. */
export interface SaveMemberDto {
  fullName: string; gender: Gender; dateOfBirth?: string | null;
  phone: string; email?: string | null;
  address?: string | null; city?: string | null; postalCode?: string | null;
  emergencyContactName?: string | null; emergencyContactPhone?: string | null;
  emergencyContactRelation?: string | null;
  joiningDate: string; profilePhotoPath?: string | null; bloodGroup?: string | null;
  heightCm?: number | null; weightKg?: number | null; notes?: string | null;
  assignedTrainerId?: number | null; createUserAccount: boolean;
  status?: MemberStatus;
}

export interface MemberQuery {
  Search?: string;
  Status?: MemberStatus;
  Gender?: Gender;
  TrainerId?: number;
  MembershipPlanId?: number;
  JoinedFrom?: string;
  JoinedTo?: string;
  OnlyExpiringSoon?: boolean;
  OnlyWithOutstanding?: boolean;
  IncludeDeleted?: boolean;
  PageNumber: number;
  PageSize: number;
  SortBy?: string;
  SortDescending?: boolean;
}

export const membersApi = {
  list: (query: MemberQuery, signal?: AbortSignal) =>
    api.get<PagedResult<MemberListDto>>('/api/members', { ...query }, signal),

  get: (id: number, signal?: AbortSignal) =>
    api.get<MemberDetailDto>(`/api/members/${id}`, undefined, signal),

  history: (id: number, signal?: AbortSignal) =>
    api.get<MemberHistoryDto>(`/api/members/${id}/history`, undefined, signal),

  create: (body: SaveMemberDto) => api.post<CreateMemberResult>('/api/members', body),

  /**
   * The id has to travel in the body as well as the route. UpdateMemberDto is validated during
   * model binding, before the controller copies the route id onto it, so a body without `id`
   * fails with "Select the member you want to update."
   */
  update: (id: number, body: SaveMemberDto) =>
    api.put<MemberDetailDto>(`/api/members/${id}`, { ...body, id }),

  /** The API takes the enum as the raw JSON body, not an object. */
  setStatus: (id: number, status: MemberStatus) =>
    api.post<void>(`/api/members/${id}/status`, status),

  remove: (id: number) => api.del<void>(`/api/members/${id}`),

  restore: (id: number) => api.post<void>(`/api/members/${id}/restore`),

  lookup: (term?: string, take = 20, signal?: AbortSignal) =>
    api.get<Lookup[]>('/api/members/lookup', { term, take }, signal),

  assignTrainer: (id: number, trainerId?: number) =>
    api.post<void>(`/api/members/${id}/assign-trainer`, undefined, { trainerId }),
};

/** Plan lookup, used by the member filter strip. */
export const planLookup = (signal?: AbortSignal) =>
  api.get<Lookup[]>('/api/membership-plans/lookup', undefined, signal);

/* ------------------------------------------------------------------ enum labels */

export const genderLabel = (value: Gender): string => words(Gender[value]) || '—';
export const memberStatusLabel = (value: MemberStatus): string => words(MemberStatus[value]) || '—';

export const GENDER_OPTIONS: Gender[] = [Gender.Male, Gender.Female, Gender.Other, Gender.Unspecified];
export const MEMBER_STATUS_OPTIONS: MemberStatus[] = [
  MemberStatus.Active, MemberStatus.Inactive, MemberStatus.Suspended, MemberStatus.Expired,
];
