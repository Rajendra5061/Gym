/**
 * Workout plan templates, their assignment to members, and the lookups the workout screens need.
 * Mirrors WorkoutsController on the API.
 */
import { api } from '@/api/client';
import type { Lookup, PagedResult } from '@/api/types';
import { DifficultyLevel } from '@/api/types';

export interface WorkoutPlanExerciseDto {
  id: number;
  exerciseId: number;
  exerciseName: string;
  muscleGroupText?: string | null;
  dayOfWeek?: number | null;
  displayOrder: number;
  sets: number;
  repetitions: number;
  targetWeightKg?: number | null;
  restSeconds?: number | null;
  durationMinutes?: number | null;
  notes?: string | null;
}

export interface WorkoutPlanDto {
  id: number;
  name: string;
  description?: string | null;
  goal?: string | null;
  difficulty: DifficultyLevel;
  difficultyText?: string | null;
  durationWeeks: number;
  sessionsPerWeek: number;
  isActive: boolean;
  exerciseCount: number;
  assignedMemberCount: number;
  exercises?: WorkoutPlanExerciseDto[] | null;
}

export interface MemberWorkoutPlanDto {
  id: number;
  memberId: number;
  memberName: string;
  workoutPlanId: number;
  workoutPlanName: string;
  trainerId?: number | null;
  trainerName?: string | null;
  startDate: string;
  endDate?: string | null;
  isActive: boolean;
  notes?: string | null;
}

export interface WorkoutPlanQuery {
  search?: string;
  difficulty?: DifficultyLevel | '';
  isActive?: boolean | '';
  includeDeleted?: boolean;
  sortBy?: string;
  sortDescending?: boolean;
  pageNumber: number;
  pageSize: number;
}

export interface AssignWorkoutPlanDto {
  memberId: number;
  workoutPlanId: number;
  trainerId?: number | null;
  startDate: string;
  endDate?: string | null;
  notes?: string | null;
}

export const DIFFICULTY_OPTIONS: { value: DifficultyLevel; label: string }[] = [
  { value: DifficultyLevel.Beginner, label: 'Beginner' },
  { value: DifficultyLevel.Intermediate, label: 'Intermediate' },
  { value: DifficultyLevel.Advanced, label: 'Advanced' },
];

export function difficultyLabel(value: DifficultyLevel | null | undefined): string {
  return DIFFICULTY_OPTIONS.find((d) => d.value === value)?.label ?? '—';
}

export const listWorkoutPlans = (query: WorkoutPlanQuery, signal?: AbortSignal) =>
  api.get<PagedResult<WorkoutPlanDto>>('/api/workouts/plans', { ...query }, signal);

export const getWorkoutPlan = (id: number, signal?: AbortSignal) =>
  api.get<WorkoutPlanDto>(`/api/workouts/plans/${id}`, undefined, signal);

export const createWorkoutPlan = (dto: Partial<WorkoutPlanDto>) =>
  api.post<WorkoutPlanDto>('/api/workouts/plans', dto);

export const updateWorkoutPlan = (id: number, dto: Partial<WorkoutPlanDto>) =>
  api.put<WorkoutPlanDto>(`/api/workouts/plans/${id}`, dto);

export const deleteWorkoutPlan = (id: number) =>
  api.del<void>(`/api/workouts/plans/${id}`);

export const restoreWorkoutPlan = (id: number) =>
  api.post<void>(`/api/workouts/plans/${id}/restore`);

export const assignWorkoutPlan = (dto: AssignWorkoutPlanDto) =>
  api.post<MemberWorkoutPlanDto>('/api/workouts/assign', dto);

export const getMemberWorkoutPlans = (memberId: number, signal?: AbortSignal) =>
  api.get<MemberWorkoutPlanDto[]>(`/api/workouts/members/${memberId}/plans`, undefined, signal);

/* ------------------------------------------------------------------ shared lookups */

export const memberLookup = (term: string, signal?: AbortSignal) =>
  api.get<Lookup[]>('/api/members/lookup', { term, take: 20 }, signal);

export const trainerLookup = (signal?: AbortSignal) =>
  api.get<Lookup[]>('/api/trainers/lookup', { onlyActive: true }, signal);

export const exerciseLookup = (signal?: AbortSignal) =>
  api.get<Lookup[]>('/api/exercises/lookup', undefined, signal);
