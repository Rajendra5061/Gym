import { useEffect, useMemo, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { useAuth } from '@/auth/AuthContext';
import {
  Alert, ErrorAlert, Field, FormSection, Loading, PageCard, PageCardHeader,
} from '@/components/ui';
import { IconArrowLeft, IconDumbbell, IconSearch, IconUser } from '@/components/icons';
import {
  DIFFICULTY_OPTIONS, assignWorkoutPlan, createWorkoutPlan, getWorkoutPlan, memberLookup,
  trainerLookup, updateWorkoutPlan,
} from '@/api/endpoints/workouts';
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

export default function WorkoutPlanFormPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const { can } = useAuth();
  const planId = id ? Number(id) : 0;
  const isEdit = planId > 0;
  const readOnly = !can('workouts.manage');

  const [form, setForm] = useState<FormState>(BLANK);
  const [assignedCount, setAssignedCount] = useState(0);
  const [loading, setLoading] = useState(isEdit);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [notice, setNotice] = useState<string | null>(null);

  const [memberTerm, setMemberTerm] = useState('');
  const [members, setMembers] = useState<Lookup[]>([]);
  const [trainers, setTrainers] = useState<Lookup[]>([]);

  /* Existing plan ------------------------------------------------------------------ */
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
        setAssignedCount(plan.assignedMemberCount);
        setError(null);
      } catch (err) {
        if (!controller.signal.aborted) setError(err);
      } finally {
        if (!controller.signal.aborted) setLoading(false);
      }
    })();
    return () => controller.abort();
  }, [isEdit, planId]);

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
        const results = await trainerLookup(controller.signal);
        if (!controller.signal.aborted) setTrainers(results);
      } catch {
        if (!controller.signal.aborted) setTrainers([]);
      }
    })();
    return () => controller.abort();
  }, []);

  const memberOptions = useMemo(() => members, [members]);

  const validate = (): boolean => {
    const errors: Record<string, string> = {};
    if (!form.title.trim() || form.title.trim().length < 2) {
      errors.title = 'Enter a title of at least 2 characters.';
    }
    if (!isEdit && !form.memberId) {
      errors.memberId = 'Choose the member this plan is for.';
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
        exercises: [],
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
      navigate('/admin/workout-plans');
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
          title={isEdit ? 'Edit Workout / Diet Plan' : 'Add Workout / Diet Plan'}
          subtitle="Create a day-wise workout and diet plan and hand it to a member."
          actions={
            <button className="btn btn-outline" onClick={() => navigate('/admin/workout-plans')}>
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
              caption="The member, the title they will see, and the day-wise plan itself."
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
                    placeholder="e.g. 4-Week Strength + Diet Plan"
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
                  label="Description / Plan Details"
                  help="Use Day-wise points for clarity (Workout + Diet)."
                >
                  <textarea
                    className="textarea"
                    style={{ minHeight: 220 }}
                    placeholder={'Day 1 - Chest & Triceps\n  Workout: Bench press 4x10, Incline dumbbell 3x12\n  Diet: 4 egg whites + oats (breakfast), grilled chicken + salad (lunch)\n\nDay 2 - Back & Biceps\n  ...'}
                    value={form.description}
                    onChange={(e) => setForm({ ...form, description: e.target.value })}
                    disabled={readOnly}
                  />
                </Field>
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
                onClick={() => navigate('/admin/workout-plans')}
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
