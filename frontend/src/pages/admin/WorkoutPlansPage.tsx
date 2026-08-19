import { useCallback, useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '@/auth/AuthContext';
import {
  ConfirmModal, EmptyState, ErrorAlert, FilterField, FilterMenu, FilterStrip, Loading, Modal,
  PageCard, PageCardHeader, Pager, Pill, StatusPill,
} from '@/components/ui';
import {
  IconCalendar, IconCheckSquare, IconDashboard, IconDumbbell, IconPlus,
  IconUsers,
} from '@/components/icons';
import {
  DIFFICULTY_OPTIONS, deleteWorkoutPlan, difficultyLabel, getWorkoutPlan, listWorkoutPlans,
  type WorkoutPlanDto,
} from '@/api/endpoints/workouts';
import type { PagedResult } from '@/api/types';
import { DifficultyLevel } from '@/api/types';
import { label } from '@/lib/format';
import './ops.css';

interface Filters {
  search: string;
  difficulty: DifficultyLevel | '';
  isActive: boolean | '';
}

const EMPTY_FILTERS: Filters = { search: '', difficulty: '', isActive: '' };

/** Beginner/Intermediate/Advanced get their own tone so the gallery scans quickly. */
function difficultyTone(level: DifficultyLevel): 'success' | 'warning' | 'danger' {
  if (level === DifficultyLevel.Beginner) return 'success';
  if (level === DifficultyLevel.Intermediate) return 'warning';
  return 'danger';
}

export default function WorkoutPlansPage() {
  const navigate = useNavigate();
  const { can } = useAuth();
  const manage = can('workouts.manage');

  const [draft, setDraft] = useState<Filters>(EMPTY_FILTERS);
  const [applied, setApplied] = useState<Filters>(EMPTY_FILTERS);
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(12);
  const [view, setView] = useState<'cards' | 'table'>('cards');

  const [data, setData] = useState<PagedResult<WorkoutPlanDto> | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<unknown>(null);
  const [reloadKey, setReloadKey] = useState(0);

  const [preview, setPreview] = useState<WorkoutPlanDto | null>(null);
  const [previewLoading, setPreviewLoading] = useState(false);
  const [pendingDelete, setPendingDelete] = useState<WorkoutPlanDto | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    (async () => {
      setLoading(true);
      try {
        const result = await listWorkoutPlans(
          {
            search: applied.search || undefined,
            difficulty: applied.difficulty,
            isActive: applied.isActive,
            pageNumber,
            pageSize,
            sortBy: 'name',
          },
          controller.signal,
        );
        if (controller.signal.aborted) return;
        setData(result);
        setError(null);
      } catch (err) {
        if (controller.signal.aborted) return;
        setError(err);
        setData(null);
      } finally {
        if (!controller.signal.aborted) setLoading(false);
      }
    })();
    return () => controller.abort();
  }, [applied, pageNumber, pageSize, reloadKey]);

  const openPreview = useCallback(async (plan: WorkoutPlanDto) => {
    setPreview(plan);
    setPreviewLoading(true);
    try {
      const full = await getWorkoutPlan(plan.id);
      setPreview(full);
    } catch {
      /* the list row already holds enough to show something useful */
    } finally {
      setPreviewLoading(false);
    }
  }, []);

  const confirmDelete = async () => {
    if (!pendingDelete) return;
    setBusy(true);
    try {
      await deleteWorkoutPlan(pendingDelete.id);
      setPendingDelete(null);
      setReloadKey((k) => k + 1);
    } catch (err) {
      setError(err);
      setPendingDelete(null);
    } finally {
      setBusy(false);
    }
  };

  const applyFilters = () => { setApplied(draft); setPageNumber(1); };
  const resetFilters = () => { setDraft(EMPTY_FILTERS); setApplied(EMPTY_FILTERS); setPageNumber(1); };

  // Badge on the trigger. Counts only the folded-away filters — search has its own visible box,
  // so including it would show a badge for something the user can already see.
  const activeFilterCount = (['difficulty', 'isActive'] as const)
    .filter((key) => applied[key] !== EMPTY_FILTERS[key]).length;

  const items = data?.items ?? [];
  const firstIndex = (pageNumber - 1) * pageSize;

  return (
    <div className="page">
      <PageCard>
        <PageCardHeader
          icon={<IconDumbbell size={20} />}
          title="Workout Plans"
          subtitle="Workout and diet plan templates you can assign to members."
          actions={
            <>
              <div className="ops-seg">
                <button
                  type="button"
                  className={view === 'cards' ? 'active' : ''}
                  onClick={() => setView('cards')}
                >
                  <IconDashboard size={14} /> Cards
                </button>
                <button
                  type="button"
                  className={view === 'table' ? 'active' : ''}
                  onClick={() => setView('table')}
                >
                  <IconCheckSquare size={14} /> Table
                </button>
              </div>
              <FilterMenu
                activeCount={activeFilterCount}
                onApply={applyFilters}
                onReset={resetFilters}
              >
                <FilterField label="Difficulty">
                  <select
                    className="select"
                    value={draft.difficulty}
                    onChange={(e) => setDraft({
                      ...draft,
                      difficulty: e.target.value === '' ? '' : (Number(e.target.value) as DifficultyLevel),
                    })}
                  >
                    <option value="">All levels</option>
                    {DIFFICULTY_OPTIONS.map((d) => <option key={d.value} value={d.value}>{d.label}</option>)}
                  </select>
                </FilterField>

                <FilterField label="Status">
                  <select
                    className="select"
                    value={draft.isActive === '' ? '' : String(draft.isActive)}
                    onChange={(e) => setDraft({
                      ...draft,
                      isActive: e.target.value === '' ? '' : e.target.value === 'true',
                    })}
                  >
                    <option value="">All</option>
                    <option value="true">Active</option>
                    <option value="false">Inactive</option>
                  </select>
                </FilterField>
              </FilterMenu>

              {manage && (
                <button className="btn btn-dark" onClick={() => navigate('/admin/workout-plans/new')}>
                  <IconPlus size={15} /> Add Plan
                </button>
              )}
            </>
          }
        />

        {/* Search and Reset stay in the open, since they are the two controls reached most often. */}
        <FilterStrip>
          <FilterField label="Search">
            <input
              className="input"
              placeholder="Plan name or goal"
              value={draft.search}
              onChange={(e) => setDraft({ ...draft, search: e.target.value })}
              onKeyDown={(e) => { if (e.key === 'Enter') applyFilters(); }}
            />
          </FilterField>
        </FilterStrip>

        {loading && <Loading message="Loading workout plans…" />}

        {!loading && Boolean(error) && (
          <div className="page-card-body"><ErrorAlert error={error} /></div>
        )}

        {!loading && !error && items.length === 0 && (
          <EmptyState
            icon={<IconDumbbell size={34} />}
            title="No workout plans yet"
            message="Create a workout or diet plan template and assign it to your members."
            action={manage
              ? <button className="btn btn-dark" onClick={() => navigate('/admin/workout-plans/new')}><IconPlus size={15} /> Add Plan</button>
              : undefined}
          />
        )}

        {!loading && !error && items.length > 0 && view === 'cards' && (
          <div className="ops-plan-gallery">
            {items.map((plan) => (
              <article className="ops-plan-card" key={plan.id}>
                <div className="ops-plan-card-head">
                  <span className="ops-plan-card-name" title={plan.name}>{plan.name}</span>
                  <span className="ops-plan-card-datepill">
                    <IconCalendar size={12} />
                    {plan.durationWeeks} {plan.durationWeeks === 1 ? 'week' : 'weeks'}
                  </span>
                </div>
                <div className="ops-plan-card-body">
                  <div className="ops-plan-card-preview">
                    {plan.description?.trim() || plan.goal?.trim() || 'No plan details captured yet.'}
                  </div>
                  <div className="ops-plan-card-meta">
                    <Pill tone={difficultyTone(plan.difficulty)}>{difficultyLabel(plan.difficulty)}</Pill>
                    <Pill tone="neutral">{plan.sessionsPerWeek}× / week</Pill>
                    <Pill tone="info">{plan.exerciseCount} exercises</Pill>
                    <Pill tone="primary">{plan.assignedMemberCount} assigned</Pill>
                  </div>
                </div>
                <div className="ops-plan-card-foot">
                  <button className="btn btn-outline btn-sm grow" onClick={() => void openPreview(plan)}>
                    View Full
                  </button>
                  {manage && (
                    <>
                      <button
                        className="btn btn-edit"
                        onClick={() => navigate(`/admin/workout-plans/${plan.id}/edit`)}
                      >
                        Edit
                      </button>
                      <button className="btn btn-del" onClick={() => setPendingDelete(plan)}>Delete</button>
                    </>
                  )}
                </div>
              </article>
            ))}
          </div>
        )}

        {!loading && !error && items.length > 0 && view === 'table' && (
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th className="idx">#</th>
                  <th>Plan</th>
                  <th>Difficulty</th>
                  <th className="center">Duration</th>
                  <th className="num">Sessions / week</th>
                  <th className="num">Exercises</th>
                  <th className="num">Assigned</th>
                  <th>Status</th>
                  <th className="actions">Actions</th>
                </tr>
              </thead>
              <tbody>
                {items.map((plan, index) => (
                  <tr key={plan.id}>
                    <td className="idx">{firstIndex + index + 1}</td>
                    <td>
                      <div className="cell-main">{plan.name}</div>
                      <div className="cell-sub">{plan.goal || 'No goal set'}</div>
                    </td>
                    <td><Pill tone={difficultyTone(plan.difficulty)}>{difficultyLabel(plan.difficulty)}</Pill></td>
                    <td className="center">
                      <span className="cell-icon">
                        <IconCalendar size={13} />
                        {plan.durationWeeks} {plan.durationWeeks === 1 ? 'week' : 'weeks'}
                      </span>
                    </td>
                    <td className="num">{plan.sessionsPerWeek}</td>
                    <td className="num">{plan.exerciseCount}</td>
                    <td className="num">
                      <span className="cell-icon"><IconUsers size={13} />{plan.assignedMemberCount}</span>
                    </td>
                    <td><StatusPill status={plan.isActive ? 'Active' : 'Inactive'} /></td>
                    <td className="actions">
                      <button className="btn btn-edit" onClick={() => void openPreview(plan)}>View</button>
                      {manage && (
                        <>
                          <button
                            className="btn btn-edit"
                            onClick={() => navigate(`/admin/workout-plans/${plan.id}/edit`)}
                          >
                            Edit
                          </button>
                          <button className="btn btn-del" onClick={() => setPendingDelete(plan)}>Delete</button>
                        </>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {!loading && !error && data && data.totalCount > 0 && (
          <Pager
            pageNumber={pageNumber}
            pageSize={pageSize}
            totalCount={data.totalCount}
            onPage={setPageNumber}
            onPageSize={(size) => { setPageSize(size); setPageNumber(1); }}
          />
        )}
      </PageCard>

      {preview && (
        <Modal
          title={preview.name}
          icon={<IconDumbbell size={18} />}
          onClose={() => setPreview(null)}
          width={780}
          footer={<button className="btn btn-outline" onClick={() => setPreview(null)}>Close</button>}
        >
          <div className="stack">
            <div className="ops-chip-row">
              <Pill tone={difficultyTone(preview.difficulty)}>{difficultyLabel(preview.difficulty)}</Pill>
              <Pill tone="neutral">{preview.durationWeeks} weeks</Pill>
              <Pill tone="neutral">{preview.sessionsPerWeek}× / week</Pill>
              <Pill tone="primary">{preview.assignedMemberCount} members assigned</Pill>
              <StatusPill status={preview.isActive ? 'Active' : 'Inactive'} />
            </div>
            {preview.goal && <div><span className="field-label">Goal</span><div>{preview.goal}</div></div>}
            <div>
              <span className="field-label">Plan details</span>
              <div className="ops-plan-detail">
                {preview.description?.trim() || 'No plan details captured yet.'}
              </div>
            </div>
            {previewLoading && <Loading message="Loading full plan…" />}
            {!previewLoading && (preview.exercises?.length ?? 0) > 0 && (
              <div className="table-wrap">
                <table className="table">
                  <thead>
                    <tr>
                      <th className="idx">#</th>
                      <th>Exercise</th>
                      <th className="num">Sets</th>
                      <th className="num">Reps</th>
                      <th className="num">Rest (s)</th>
                      <th>Notes</th>
                    </tr>
                  </thead>
                  <tbody>
                    {(preview.exercises ?? []).map((exercise, index) => (
                      <tr key={exercise.id || index}>
                        <td className="idx">{index + 1}</td>
                        <td>
                          <div className="cell-main">{exercise.exerciseName}</div>
                          <div className="cell-sub">{label(exercise.muscleGroupText)}</div>
                        </td>
                        <td className="num">{exercise.sets}</td>
                        <td className="num">{exercise.repetitions}</td>
                        <td className="num">{exercise.restSeconds ?? '—'}</td>
                        <td className="muted">{exercise.notes || '—'}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        </Modal>
      )}

      {pendingDelete && (
        <ConfirmModal
          title="Delete workout plan"
          message={<>Delete <strong>{pendingDelete.name}</strong>? It moves to the recycle bin and can be restored later.</>}
          confirmLabel="Delete plan"
          busy={busy}
          onConfirm={() => void confirmDelete()}
          onClose={() => setPendingDelete(null)}
        />
      )}
    </div>
  );
}
