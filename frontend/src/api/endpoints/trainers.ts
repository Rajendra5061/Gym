/** Trainer endpoints, mirroring GymManagement.Application/DTOs/TrainerDtos.cs. */
import { api } from '@/api/client';
import {
  Gender,
  TrainerStatus,
  type Lookup,
  type PagedResult,
  type TrainerListDto,
} from '@/api/types';
import type { TemporaryPasswordDto } from '@/api/endpoints/members';
import { words } from '@/lib/format';

export interface TrainerDetailDto extends TrainerListDto {
  address?: string | null;
  certifications?: string | null;
  monthlySalary?: number | null;
  commissionPercent?: number | null;
  notes?: string | null;
  hasUserAccount: boolean;
  sessionsThisMonth: number;
  createdAt: string;
}

/** Body for POST /api/trainers and PUT /api/trainers/{id}; `status` only travels on update. */
export interface SaveTrainerDto {
  fullName: string; gender: Gender; phone: string; email?: string | null;
  address?: string | null; specialization?: string | null; certifications?: string | null;
  experienceYears?: number | null; joiningDate: string;
  monthlySalary?: number | null; commissionPercent?: number | null;
  photoPath?: string | null; notes?: string | null;
  createUserAccount: boolean;
  status?: TrainerStatus;
}

export interface CreateTrainerResult { trainer: TrainerDetailDto; account?: TemporaryPasswordDto | null; }

export interface TrainerWorkloadDto {
  trainerId: number; trainerName: string; specialization?: string | null;
  assignedMembers: number; activeWorkoutPlans: number; sessionsInPeriod: number;
  distinctMembersTrained: number; totalSessionMinutes: number;
}

export interface TrainerQuery {
  Search?: string;
  Status?: TrainerStatus;
  Specialization?: string;
  IncludeDeleted?: boolean;
  PageNumber: number;
  PageSize: number;
  SortBy?: string;
  SortDescending?: boolean;
}

export const trainersApi = {
  list: (query: TrainerQuery, signal?: AbortSignal) =>
    api.get<PagedResult<TrainerListDto>>('/api/trainers', { ...query }, signal),

  get: (id: number, signal?: AbortSignal) =>
    api.get<TrainerDetailDto>(`/api/trainers/${id}`, undefined, signal),

  create: (body: SaveTrainerDto) => api.post<CreateTrainerResult>('/api/trainers', body),

  /**
   * The id has to travel in the body as well as the route. UpdateTrainerDto is validated during
   * model binding, before the controller copies the route id onto it, so a body without `id`
   * fails with "Select the trainer you want to update."
   */
  update: (id: number, body: SaveTrainerDto) =>
    api.put<TrainerDetailDto>(`/api/trainers/${id}`, { ...body, id }),

  /** The API takes the enum as the raw JSON body, not an object. */
  setStatus: (id: number, status: TrainerStatus) =>
    api.post<void>(`/api/trainers/${id}/status`, status),

  remove: (id: number) => api.del<void>(`/api/trainers/${id}`),

  restore: (id: number) => api.post<void>(`/api/trainers/${id}/restore`),

  lookup: (onlyActive = true, signal?: AbortSignal) =>
    api.get<Lookup[]>('/api/trainers/lookup', { onlyActive }, signal),

  workload: (from?: string, to?: string, signal?: AbortSignal) =>
    api.get<TrainerWorkloadDto[]>('/api/trainers/workload', { from, to }, signal),
};

/** The enum's own member name, split into words so `OnLeave` reads as "On Leave". */
export const trainerStatusLabel = (value: TrainerStatus): string => words(TrainerStatus[value]) || '—';

export const TRAINER_STATUS_OPTIONS: TrainerStatus[] = [
  TrainerStatus.Active, TrainerStatus.Inactive, TrainerStatus.OnLeave, TrainerStatus.Resigned,
];
