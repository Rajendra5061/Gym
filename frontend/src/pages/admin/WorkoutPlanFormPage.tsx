import { useEffect, useMemo, useRef, useState } from 'react';
import { useLocation, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { useAuth } from '@/auth/AuthContext';
import {
  Alert, ErrorAlert, Field, FormSection, Loading, PageCard, PageCardHeader,
} from '@/components/ui';
import {
  IconArrowLeft, IconDumbbell, IconPlus, IconSearch, IconTrash, IconUser,
} from '@/components/icons';
import {
  DIFFICULTY_OPTIONS, assignWorkoutPlan, createWorkoutPlan, exerciseLookup, getWorkoutPlan,
  memberLookup, trainerLookup, updateWorkoutPlan,
} from '@/api/endpoints/workouts';
import { membersApi } from '@/api/endpoints/members';
import type { Lookup } from '@/api/types';
import { DifficultyLevel } from '@/api/types';
import { isoDate } from '@/lib/format';
import './ops.css';

interface FormState {
  memberId: string;
  trainerId: string;
  title: string;
  description: string;
  goal: string;
  difficulty: DifficultyLevel;
  durationWeeks: number;
  sessionsPerWeek: number;
  isActive: boolean;
}

const BLANK: FormState = {
  memberId: '',
  trainerId: '',
  title: '',
  description: '',
  goal: '',
  difficulty: DifficultyLevel.Beginner,
  durationWeeks: 4,
  sessionsPerWeek: 3,
  isActive: true,
};

/** One editable exercise line. Numbers live as strings so half-typed input never NaNs. */
interface ExerciseRow {
  key: number;
  id: number;            // existing server row id; 0 for a new line
  exerciseId: string;
  dayOfWeek: string;     // '' = any day, otherwise '1'..'7'
  sets: string;
  repetitions: string;
  targetWeightKg: string;
  restSeconds: string;
  notes: string;
}

const DAY_OPTIONS = [
  { value: '', label: 'Any day' },
  { value: '1', label: 'Day 1 (Mon)' },
  { value: '2', label: 'Day 2 (Tue)' },
  { value: '3', label: 'Day 3 (Wed)' },
  { value: '4', label: 'Day 4 (Thu)' },
  { value: '5', label: 'Day 5 (Fri)' },
  { value: '6', label: 'Day 6 (Sat)' },
  { value: '7', label: 'Day 7 (Sun)' },
];

export default function WorkoutPlanFormPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const { pathname } = useLocation();
  const [searchParams] = useSearchParams();
  const { can } = useAuth();
  const planId = id ? Number(id) : 0;
  const isEdit = planId > 0;
  const readOnly = !can('workouts.manage');

  // The same component serves /admin and /trainer, so every exit stays in the caller's area.
  const base = pathname.startsWith('/trainer') ? '/trainer' : '/admin';
  const listPath = `${base}/workout-plans`;

  const [form, setForm] = useState<FormState>(BLANK);
  const [rows, setRows] = useState<ExerciseRow[]>([]);
  const nextKey = useRef(1);
  const [assignedCount, setAssignedCount] = useState(0);
  const [loading, setLoading] = useState(isEdit);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [notice, setNotice] = useState<string | null>(null);

  const [memberTerm, setMemberTerm] = useState('');
  const [members, setMembers] = useState<Lookup[]>([]);
  const [pinnedMember, setPinnedMember] = useState<Lookup | null>(null);
  const [trainers, setTrainers] = useState<Lookup[]>([]);
  const [exercises, setExercises] = useState<Lookup[]>([]);

  const makeRow = (partial?: Partial<ExerciseRow>): ExerciseRow => ({
    key: nextKey.current++,
    id: 0,
    exerciseId: '',
    dayOfWeek: '',
    sets: '3',
    repetitions: '10',
    targetWeightKg: '',
    restSeconds: '',
    notes: '',
    ...partial,
  });

  /* Existing plan — including its exercise rows, so saving never wipes them ---------- */
  useEffect(() => {
    if (!isEdit) return;
    const controller = new AbortController();
    (async () => {
      setLoading(true);
      try {
        const plan = await getWorkoutPlan(planId, controller.signal);
        if (controller.signal.aborted) return;
        setForm({
          memberId: '',
          trainerId: '',
          title: plan.name,
          description: plan.description ?? '',
          goal: plan.goal ?? '',
          difficulty: plan.difficulty || DifficultyLevel.Beginner,
          durationWeeks: plan.durationWeeks || 4,
          sessionsPerWeek: plan.sessionsPerWeek || 3,
          isActive: plan.isActive,
        });
        setRows(
          [...(plan.exercises ?? [])]
            .sort((a, b) => a.displayOrder - b.displayOrder)
            .map((exercise) => makeRow({
              id: exercise.id,
              exerciseId: String(exercise.exerciseId),
              dayOfWeek: exercise.dayOfWeek ? String(exercise.dayOfWeek) : '',
              sets: String(exercise.sets),
              repetitions: String(exercise.repetitions),
              targetWeightKg: exercise.targetWeightKg != null ? String(exercise.targetWeightKg) : '',
              restSeconds: exercise.restSeconds != null ? String(exercise.restSeconds) : '',
              notes: exercise.notes ?? '',
            })),
        );
        setAssignedCount(plan.assignedMemberCount);
        setError(null);
      } catch (err) {
        if (!controller.signal.aborted) setError(err);
      } finally {
        if (!controller.signal.aborted) setLoading(false);
      }
    })();
    return () => controller.abort();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isEdit, planId]);

  /* ?memberId= prefill (from the trainer roster's "New workout plan" link) ----------- */
  useEffect(() => {
    if (isEdit) return;
    const preselect = Number(searchParams.get('memberId') ?? '');
    if (!preselect || Number.isNaN(preselect)) return;
    setForm((f) => (f.memberId ? f : { ...f, memberId: String(preselect) }));
    const controller = new AbortController();
    membersApi.get(preselect, controller.signal)
      .then((member) => {
        if (controller.signal.aborted) return;
        setPinnedMember({ id: member.id, name: member.fullName, code: member.memberCode, isActive: true });
      })
      .catch(() => { /* the id still travels; only the display name is missing */ });
    return () => controller.abort();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isEdit]);

  /* Member search — debounced so typing does not hammer the lookup ------------------ */
  useEffect(() => {
    const controller = new AbortController();
    const timer = window.setTimeout(() => {
      (async () => {
        try {
          const results = await memberLookup(memberTerm, controller.signal);
          if (!controller.signal.aborted) setMembers(results);
        } catch {
          if (!controller.signal.aborted) setMembers([]);
        }
      })();
    }, memberTerm ? 300 : 0);
    return () => { window.clearTimeout(timer); controller.abort(); };
  }, [memberTerm]);

  useEffect(() => {
    const controller = new AbortController();
    (async () => {
      try {
        const [trainerResults, exerciseResults] = await Promise.all([
          trainerLookup(controller.signal),
          exerciseLookup(controller.signal),
        ]);
        if (controller.signal.aborted) return;
        setTrainers(trainerResults);
        setExercises(exerciseResults);
      } catch {
        if (!controller.signal.aborted) { setTrainers([]); setExercises([]); }
      }
    })();
    return () => controller.abort();
  }, []);

  // The preselected member may not be inside the first page of lookup results.
  const memberOptions = useMemo(() => {
    if (pinnedMember && !members.some((m) => m.id === pinnedMember.id)) {
      return [pinnedMember, ...members];
    }
    return members;
  }, [members, pinnedMember]);

  /* Exercise row helpers ------------------------------------------------------------- */
  const addRow = () => setRows((current) => [...current, makeRow()]);
  const removeRow = (key: number) => setRows((current) => current.filter((row) => row.key !== key));
  const patchRow = (key: number, patch: Partial<ExerciseRow>) =>
    setRows((current) => current.map((row) => (row.key === key ? { ...row, ...patch } : row)));

  const validate = (): boolean => {
    const errors: Record<string, string> = {};
    if (!form.title.trim() || form.title.trim().length < 2) {
      errors.title = 'Enter a title of at least 2 characters.';
    }
    if (!isEdit && !form.memberId) {
      errors.memberId = 'Choose the member this plan is for.';
    }
    for (const row of rows) {
      if (!row.exerciseId) {
        errors.exercises = 'Every exercise line needs an exercise selected — remove empty lines.';
        break;
      }
      const sets = Number(row.sets);
      const reps = Number(row.repetitions);
      if (!Number.isInteger(sets) || sets < 1 || sets > 20) {
        errors.exercises = 'Sets must be between 1 and 20 on every line.';
        break;
      }
      if (!Number.isInteger(reps) || reps < 1 || reps > 200) {
        errors.exercises = 'Repetitions must be between 1 and 200 on every line.';
        break;
      }
      if (row.targetWeightKg.trim() !== '' && (Number(row.targetWeightKg) < 0 || Number(row.targetWeightKg) > 1000)) {
        errors.exercises = 'Target weight must be between 0 and 1000 kg.';
        break;
      }
      if (row.restSeconds.trim() !== '' && (Number(row.restSeconds) < 0 || Number(row.restSeconds) > 900)) {
        errors.exercises = 'Rest must be between 0 and 900 seconds.';
        break;
      }
    }
    setFieldErrors(errors);
    return Object.keys(errors).length === 0;
  };

  const save = async () => {
    if (!validate()) return;
    setSaving(true);
    setError(null);
    try {
      const payload = {
        id: isEdit ? planId : 0,
        name: form.title.trim(),
        description: form.description.trim() || null,
        goal: form.goal.trim() || null,
        difficulty: form.difficulty,
        durationWeeks: form.durationWeeks,
        sessionsPerWeek: form.sessionsPerWeek,
        isActive: form.isActive,
        // The server replaces the plan's children with exactly this list on every save,
        // so it must always be the real rows — an empty array here wipes the plan.
        exercises: rows.map((row, index) => ({
          id: row.id,
          exerciseId: Number(row.exerciseId),
          exerciseName: exercises.find((e) => e.id === Number(row.exerciseId))?.name ?? '',
          dayOfWeek: row.dayOfWeek ? Number(row.dayOfWeek) : null,
          displayOrder: index + 1,
          sets: Number(row.sets),
          repetitions: Number(row.repetitions),
          targetWeightKg: row.targetWeightKg.trim() === '' ? null : Number(row.targetWeightKg),
          restSeconds: row.restSeconds.trim() === '' ? null : Number(row.restSeconds),
          durationMinutes: null,
          notes: row.notes.trim() || null,
        })),
      };

      const saved = isEdit
        ? await updateWorkoutPlan(planId, payload)
        : await createWorkoutPlan(payload);

      // The plan itself is a template; picking a member assigns it straight away.
      if (form.memberId) {
        await assignWorkoutPlan({
          memberId: Number(form.memberId),
          workoutPlanId: saved?.id ?? planId,
          trainerId: form.trainerId ? Number(form.trainerId) : null,
          startDate: isoDate(new Date()),
        });
      }
      navigate(listPath);
    } catch (err) {
      setError(err);
      setNotice(null);
    } finally {
      setSaving(false);
    }
  };

  if (loading) {
    return (
      <div className="page">
        <PageCard><Loading message="Loading plan…" /></PageCard>
      </div>
    );
  }

  return (
    <div className="page">
      <PageCard>
        <PageCardHeader
          icon={<IconDumbbell size={20} />}
          title={isEdit ? 'Edit Workout Plan' : 'Add Workout Plan'}
          subtitle="Build the exercise programme and hand it to a member."
          actions={
            <button className="btn btn-outline" onClick={() => navigate(listPath)}>
              <IconArrowLeft size={15} /> Back to Plans
            </button>
          }
        />

        <div className="page-card-body">
          <div className="stack">
            {error ? <ErrorAlert error={error} /> : null}
            {notice ? <Alert tone="success">{notice}</Alert> : null}
            {isEdit && (
              <Alert tone="info">
                This plan is currently assigned to {assignedCount} member{assignedCount === 1 ? '' : 's'}.
                Choosing a member below assigns it to one more.
              </Alert>
            )}

            <FormSection
              title="Plan details"
              caption="The member, the title they will see, and any overall guidance."
              icon={<IconUser size={16} />}
            >
              <div className="form-grid">
                <Field
                  label="Member"
                  required={!isEdit}
                  help="Choose the member for whom this plan is created."
                  error={fieldErrors.memberId}
                >
                  <div className="stack" style={{ gap: 6 }}>
                    <div className="input-group">
                      <span className="input-icon"><IconSearch size={14} /></span>
                      <input
                        className="input"
                        placeholder="Search by name, code or phone"
                        value={memberTerm}
                        onChange={(e) => setMemberTerm(e.target.value)}
                        disabled={readOnly}
                      />
                    </div>
                    <select
                      className={`select ${fieldErrors.memberId ? 'input-invalid' : ''}`}
                      value={form.memberId}
                      onChange={(e) => setForm({ ...form, memberId: e.target.value })}
                      disabled={readOnly}
                    >
                      <option value="">Select a member…</option>
                      {memberOptions.map((m) => (
                        <option key={m.id} value={m.id}>
                          {m.name}{m.code ? ` (${m.code})` : ''}
                        </option>
                      ))}
                    </select>
                  </div>
                </Field>

                <Field label="Trainer" help="Optional. The trainer who will supervise this plan.">
                  <select
                    className="select"
                    value={form.trainerId}
                    onChange={(e) => setForm({ ...form, trainerId: e.target.value })}
                    disabled={readOnly}
                  >
                    <option value="">No trainer</option>
                    {trainers.map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}
                  </select>
                </Field>

                <Field label="Title" required error={fieldErrors.title}>
                  <input
                    className={`input ${fieldErrors.title ? 'input-invalid' : ''}`}
                    placeholder="e.g. 4-Week Strength Plan"
                    value={form.title}
                    onChange={(e) => setForm({ ...form, title: e.target.value })}
                    disabled={readOnly}
                  />
                </Field>

                <Field label="Goal" help="Optional. Fat loss, muscle gain, endurance…">
                  <input
                    className="input"
                    placeholder="e.g. Fat loss"
                    value={form.goal}
                    onChange={(e) => setForm({ ...form, goal: e.target.value })}
                    disabled={readOnly}
                  />
                </Field>
              </div>

              <div style={{ marginTop: 16 }}>
                <Field
                  label="Description"
                  help="Optional. Warm-up guidance, technique cues, or anything the exercise rows don't capture."
                >
                  <textarea
                    className="textarea"
                    style={{ minHeight: 120 }}
                    placeholder={'e.g. Warm up 10 minutes before every session. Focus on form over load in week 1.'}
                    value={form.description}
                    onChange={(e) => setForm({ ...form, description: e.target.value })}
                    disabled={readOnly}
                  />
                </Field>
              </div>
            </FormSection>

            <FormSection
              title="Exercises"
              caption="The day-wise exercise rows the member sees. Order here is the order they appear."
              icon={<IconDumbbell size={16} />}
            >
              <div className="stack" style={{ gap: 10 }}>
                {fieldErrors.exercises ? <Alert tone="warning">{fieldErrors.exercises}</Alert> : null}

                {rows.length === 0 && (
                  <Alert tone="info">
                    No exercise rows yet. Add lines below — the plan is saved with exactly the rows listed here.
                  </Alert>
                )}

                {rows.length > 0 && (
                  <div className="table-wrap">
                    <table className="table">
                      <thead>
                        <tr>
                          <th className="idx">#</th>
                          <th style={{ minWidth: 180 }}>Exercise</th>
                          <th>Day</th>
                          <th className="num">Sets</th>
                          <th className="num">Reps</th>
                          <th className="num">Weight (kg)</th>
                          <th className="num">Rest (s)</th>
                          <th style={{ minWidth: 140 }}>Notes</th>
                          {!readOnly && <th aria-label="Remove" />}
                        </tr>
                      </thead>
                      <tbody>
                        {rows.map((row, index) => (
                          <tr key={row.key}>
                            <td className="idx">{index + 1}</td>
                            <td>
                              <select
                                className="select"
                                value={row.exerciseId}
                                onChange={(e) => patchRow(row.key, { exerciseId: e.target.value })}
                                disabled={readOnly}
                              >
                                <option value="">Select exercise…</option>
                                {exercises.map((exercise) => (
                                  <option key={exercise.id} value={exercise.id}>{exercise.name}</option>
                                ))}
                              </select>
                            </td>
                            <td>
                              <select
                                className="select"
                                value={row.dayOfWeek}
                                onChange={(e) => patchRow(row.key, { dayOfWeek: e.target.value })}
                                disabled={readOnly}
                              >
                                {DAY_OPTIONS.map((day) => (
                                  <option key={day.value} value={day.value}>{day.label}</option>
                                ))}
                              </select>
                            </td>
                            <td className="num">
                              <input
                                className="input"
                                type="number"
                                min={1}
                                max={20}
                                style={{ width: 70 }}
                                value={row.sets}
                                onChange={(e) => patchRow(row.key, { sets: e.target.value })}
                                disabled={readOnly}
                              />
                            </td>
                            <td className="num">
                              <input
                                className="input"
                                type="number"
                                min={1}
                                max={200}
                                style={{ width: 70 }}
                                value={row.repetitions}
                                onChange={(e) => patchRow(row.key, { repetitions: e.target.value })}
                                disabled={readOnly}
                              />
                            </td>
                            <td className="num">
                              <input
                                className="input"
                                type="number"
                                min={0}
                                max={1000}
                                step="0.5"
                                style={{ width: 90 }}
                                placeholder="—"
                                value={row.targetWeightKg}
                                onChange={(e) => patchRow(row.key, { targetWeightKg: e.target.value })}
                                disabled={readOnly}
                              />
                            </td>
                            <td className="num">
                              <input
                                className="input"
                                type="number"
                                min={0}
                                max={900}
                                style={{ width: 80 }}
                                placeholder="—"
                                value={row.restSeconds}
                                onChange={(e) => patchRow(row.key, { restSeconds: e.target.value })}
                                disabled={readOnly}
                              />
                            </td>
                            <td>
                              <input
                                className="input"
                                placeholder="e.g. slow negatives"
                                value={row.notes}
                                onChange={(e) => patchRow(row.key, { notes: e.target.value })}
                                disabled={readOnly}
                              />
                            </td>
                            {!readOnly && (
                              <td>
                                <button
                                  className="btn btn-ghost btn-icon btn-sm"
                                  title="Remove this line"
                                  aria-label={`Remove exercise line ${index + 1}`}
                                  onClick={() => removeRow(row.key)}
                                >
                                  <IconTrash size={14} />
                                </button>
                              </td>
                            )}
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                )}

                {!readOnly && (
                  <div>
                    <button className="btn btn-outline btn-sm" onClick={addRow}>
                      <IconPlus size={14} /> Add Exercise
                    </button>
                  </div>
                )}
              </div>
            </FormSection>

            <FormSection
              title="Schedule"
              caption="How long the plan runs and how often the member trains."
              optional
            >
              <div className="form-grid">
                <Field label="Difficulty">
                  <select
                    className="select"
                    value={form.difficulty}
                    onChange={(e) => setForm({ ...form, difficulty: Number(e.target.value) as DifficultyLevel })}
                    disabled={readOnly}
                  >
                    {DIFFICULTY_OPTIONS.map((d) => <option key={d.value} value={d.value}>{d.label}</option>)}
                  </select>
                </Field>
                <Field label="Duration (weeks)" help="Between 1 and 104 weeks.">
                  <input
                    className="input"
                    type="number"
                    min={1}
                    max={104}
                    value={form.durationWeeks}
                    onChange={(e) => setForm({ ...form, durationWeeks: Number(e.target.value) })}
                    disabled={readOnly}
                  />
                </Field>
                <Field label="Sessions per week" help="Between 1 and 14 sessions.">
                  <input
                    className="input"
                    type="number"
                    min={1}
                    max={14}
                    value={form.sessionsPerWeek}
                    onChange={(e) => setForm({ ...form, sessionsPerWeek: Number(e.target.value) })}
                    disabled={readOnly}
                  />
                </Field>
                <Field label="Status">
                  <select
                    className="select"
                    value={String(form.isActive)}
                    onChange={(e) => setForm({ ...form, isActive: e.target.value === 'true' })}
                    disabled={readOnly}
                  >
                    <option value="true">Active</option>
                    <option value="false">Inactive</option>
                  </select>
                </Field>
              </div>
            </FormSection>

            <div className="form-footer">
              <button className="btn btn-dark" onClick={() => void save()} disabled={saving || readOnly}>
                {saving ? 'Saving…' : 'Save Plan'}
              </button>
              <button
                className="btn btn-outline"
                onClick={() => navigate(listPath)}
                disabled={saving}
              >
                Cancel
              </button>
            </div>
            <div className="form-note">Fields marked with * are mandatory.</div>
          </div>
        </div>
      </PageCard>
    </div>
  );
}
