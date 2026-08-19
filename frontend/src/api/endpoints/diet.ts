/**
 * Diet plans: a member's meal programme, written by a trainer or the office.
 * Mirrors DietPlansController on the API. Enums travel as integers.
 */
import { api } from '@/api/client';
import type { PagedResult } from '@/api/types';
import type { PillTone } from '@/components/ui';

export enum DietPlanStatus { Active = 1, Completed = 2, Cancelled = 3 }

export enum DietMealType {
  Breakfast = 1,
  MidMorning = 2,
  Lunch = 3,
  EveningSnack = 4,
  Dinner = 5,
  PostWorkout = 6,
}

export interface DietPlanMealDto {
  id: number;
  mealType: DietMealType;
  mealTypeText: string;
  title: string;
  description?: string | null;
  calories?: number | null;
  proteinGrams?: number | null;
  carbsGrams?: number | null;
  fatGrams?: number | null;
  displayOrder: number;
}

export interface DietPlanDto {
  id: number;
  memberId: number;
  memberName: string;
  trainerId?: number | null;
  trainerName?: string | null;
  title: string;
  goal?: string | null;
  notes?: string | null;
  startDate: string;
  endDate?: string | null;
  status: DietPlanStatus;
  statusText: string;
  meals: DietPlanMealDto[];
  createdAt: string;
}

/** One meal row inside a save payload; the server wholesale-replaces meals on update. */
export interface SaveDietPlanMealDto {
  id?: number;
  mealType: DietMealType;
  title: string;
  description?: string | null;
  calories?: number | null;
  proteinGrams?: number | null;
  carbsGrams?: number | null;
  fatGrams?: number | null;
  displayOrder: number;
}

/** Body for POST /api/diet-plans and PUT /api/diet-plans/{id}. */
export interface SaveDietPlanDto {
  memberId: number;
  trainerId?: number | null;
  title: string;
  goal?: string | null;
  notes?: string | null;
  startDate: string;
  endDate?: string | null;
  status?: DietPlanStatus;
  meals: SaveDietPlanMealDto[];
}

/** Query keys travel in the PascalCase the ASP.NET binder expects, like MemberQuery. */
export interface DietPlanQuery {
  MemberId?: number;
  TrainerId?: number;
  Status?: DietPlanStatus | '';
  Search?: string;
  SortBy?: string;
  SortDescending?: boolean;
  PageNumber: number;
  PageSize: number;
}

/* ------------------------------------------------------------------ enum labels */

export const MEAL_TYPE_OPTIONS: { value: DietMealType; label: string }[] = [
  { value: DietMealType.Breakfast,    label: 'Breakfast' },
  { value: DietMealType.MidMorning,   label: 'Mid-Morning' },
  { value: DietMealType.Lunch,        label: 'Lunch' },
  { value: DietMealType.EveningSnack, label: 'Evening Snack' },
  { value: DietMealType.Dinner,       label: 'Dinner' },
  { value: DietMealType.PostWorkout,  label: 'Post-Workout' },
];

export const DIET_STATUS_OPTIONS: { value: DietPlanStatus; label: string }[] = [
  { value: DietPlanStatus.Active,    label: 'Active' },
  { value: DietPlanStatus.Completed, label: 'Completed' },
  { value: DietPlanStatus.Cancelled, label: 'Cancelled' },
];

export function mealTypeLabel(value: DietMealType | null | undefined, serverText?: string | null): string {
  return MEAL_TYPE_OPTIONS.find((m) => m.value === value)?.label ?? (serverText || '—');
}

export function dietStatusLabel(value: DietPlanStatus | null | undefined, serverText?: string | null): string {
  return DIET_STATUS_OPTIONS.find((s) => s.value === value)?.label ?? (serverText || '—');
}

/** Pill tone for a plan status, matching the pill idiom the other list screens use. */
export function dietStatusTone(value: DietPlanStatus | null | undefined): PillTone {
  switch (value) {
    case DietPlanStatus.Active: return 'success';
    case DietPlanStatus.Completed: return 'info';
    case DietPlanStatus.Cancelled: return 'danger';
    default: return 'neutral';
  }
}

/* ------------------------------------------------------------------ api surface */

export const dietApi = {
  list: (query: DietPlanQuery, signal?: AbortSignal) =>
    api.get<PagedResult<DietPlanDto>>('/api/diet-plans', { ...query }, signal),

  get: (id: number, signal?: AbortSignal) =>
    api.get<DietPlanDto>(`/api/diet-plans/${id}`, undefined, signal),

  create: (dto: SaveDietPlanDto) => api.post<DietPlanDto>('/api/diet-plans', dto),

  /** The id travels in the body as well as the route, like the member/trainer updates. */
  update: (id: number, dto: SaveDietPlanDto) =>
    api.put<DietPlanDto>(`/api/diet-plans/${id}`, { ...dto, id }),

  remove: (id: number) => api.del<void>(`/api/diet-plans/${id}`),

  /** A member's own plans; the server lets members read theirs and staff read anyone's. */
  memberPlans: (memberId: number, signal?: AbortSignal) =>
    api.get<DietPlanDto[]>(`/api/diet-plans/members/${memberId}`, undefined, signal),
};
