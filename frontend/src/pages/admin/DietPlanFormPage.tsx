import { useEffect, useMemo, useState } from 'react';
import { useLocation, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { useAuth } from '@/auth/AuthContext';
import {
  Alert, ErrorAlert, Field, FormSection, Loading, PageCard, PageCardHeader,
} from '@/components/ui';
import { IconArrowLeft, IconFile, IconPlus, IconSearch, IconUser } from '@/components/icons';
import {
  DIET_STATUS_OPTIONS, DietMealType, DietPlanStatus, MEAL_TYPE_OPTIONS, dietApi,
  type SaveDietPlanDto,
} from '@/api/endpoints/diet';
import { membersApi } from '@/api/endpoints/members';
import { trainersApi } from '@/api/endpoints/trainers';
import type { Lookup } from '@/api/types';
import { isoDate } from '@/lib/format';
import './ops.css';

interface FormState {
  memberId: string;
  trainerId: string;
  title: string;
  goal: string;
  notes: string;
  startDate: string;
  endDate: string;
  status: DietPlanStatus;
}

/** Inputs keep raw strings so half-typed numbers never fight the keyboard; parsed on submit. */
interface MealRow {
  key: number;
  id: number;
  mealType: DietMealType;
  title: string;
  description: string;
  calories: string;
  proteinGrams: string;
  carbsGrams: string;
  fatGrams: string;
}

let nextKey = 1;

function blankMeal(mealType: DietMealType = DietMealType.Breakfast): MealRow {
  return {
    key: nextKey++,
    id: 0,
    mealType,
    title: '',
    description: '',
    calories: '',
    proteinGrams: '',
    carbsGrams: '',
    fatGrams: '',
  };
}

function blankForm(): FormState {
  return {
    memberId: '',
    trainerId: '',
    title: '',
    goal: '',
    notes: '',
    startDate: isoDate(new Date()),
    endDate: '',
    status: DietPlanStatus.Active,
  };
}

const toIntOrNull = (value: string): number | null =>
  value.trim() === '' ? null : Number(value);

/**
 * Serves both /admin/diet-plans/... and /trainer/diet-plans/... — the list path is derived from
 * the current location (strip the trailing /new or /:id/edit), never hardcoded to /admin.
 */
export default function DietPlanFormPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const location = useLocation();
  const [searchParams] = useSearchParams();
  const { can } = useAuth();

  const planId = id ? Number(id) : 0;
  const isEdit = planId > 0;
  const readOnly = !can('diet.manage');
  const listPath = location.pathname.replace(/\/+$/, '').replace(/\/(new|\d+\/edit)$/, '');

  const [form, setForm] = useState<FormState>(blankForm);
  const [meals, setMeals] = useState<MealRow[]>(() => [blankMeal()]);
  const [loading, setLoading] = useState(isEdit);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [attempted, setAttempted] = useState(false);

  const [memberTerm, setMemberTerm] = useState('');
  const [members, setMembers] = useState<Lookup[]>([]);
  const [selectedMemberName, setSelectedMemberName] = useState('');
  const [trainers, setTrainers] = useState<Lookup[]>([]);

  /* Existing plan ------------------------------------------------------------------- */
  useEffect(() => {
    if (!isEdit) return;
    const controller = new AbortController();
    (async () => {
      setLoading(true);
      try {
        const plan = await dietApi.get(planId, controller.signal);
        if (controller.signal.aborted) return;
        setForm({
          memberId: String(plan.memberId),
          trainerId: plan.trainerId ? String(plan.trainerId) : '',
          title: plan.title,
          goal: plan.goal ?? '',
          notes: plan.notes ?? '',
          startDate: isoDate(plan.startDate),
          endDate: plan.endDate ? isoDate(plan.endDate) : '',
          status: plan.status || DietPlanStatus.Active,
        });
        setSelectedMemberName(plan.memberName);
        const rows = [...(plan.meals ?? [])]
          .sort((a, b) => a.displayOrder - b.displayOrder)
          .map((meal) => ({
            key: nextKey++,
            id: meal.id,
            mealType: meal.mealType,
            title: meal.title,
            description: meal.description ?? '',
            calories: meal.calories != null ? String(meal.calories) : '',
            proteinGrams: meal.proteinGrams != null ? String(meal.proteinGrams) : '',
            carbsGrams: meal.carbsGrams != null ? String(meal.carbsGrams) : '',
            fatGrams: meal.fatGrams != null ? String(meal.fatGrams) : '',
          }));
        setMeals(rows.length > 0 ? rows : [blankMeal()]);
        setError(null);
      } catch (err) {
        if (!controller.signal.aborted) setError(err);
      } finally {
        if (!controller.signal.aborted) setLoading(false);
      }
    })();
    return () => controller.abort();
  }, [isEdit, planId]);

  /* ?memberId= prefill (new plans, e.g. from the trainer's member roster) ------------- */
  useEffect(() => {
    if (isEdit) return;
    const prefill = searchParams.get('memberId');
    if (!prefill || !Number(prefill)) return;
    setForm((f) => ({ ...f, memberId: prefill }));
    (async () => {
      try {
        const member = await membersApi.get(Number(prefill));
        setSelectedMemberName(member.fullName);
      } catch {
        /* the synthetic "Member #id" option stands in until a lookup names them */
      }
    })();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  /* Member search — debounced so typing does not hammer the lookup ------------------ */
  useEffect(() => {
    const controller = new AbortController();
    const timer = window.setTimeout(() => {
      (async () => {
        try {
          const results = await membersApi.lookup(memberTerm, 20, controller.signal);
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
        const results = await trainersApi.lookup(true, controller.signal);
        if (!controller.signal.aborted) setTrainers(results);
      } catch {
        if (!controller.signal.aborted) setTrainers([]);
      }
    })();
    return () => controller.abort();
  }, []);

  /** The chosen member always has an option, even when the search results do not include them. */
  const memberOptions = useMemo(() => {
    if (!form.memberId) return members;
    const chosen = Number(form.memberId);
    if (members.some((m) => m.id === chosen)) return members;
    const synthetic: Lookup = {
      id: chosen,
      name: selectedMemberName || `Member #${chosen}`,
      isActive: true,
    };
    return [synthetic, ...members];
  }, [members, form.memberId, selectedMemberName]);

  const pickMember = (value: string) => {
    const match = members.find((m) => m.id === Number(value));
    if (match) setSelectedMemberName(match.name);
    setForm({ ...form, memberId: value });
  };

  const updateMeal = (key: number, patch: Partial<MealRow>) =>
    setMeals((rows) => rows.map((row) => (row.key === key ? { ...row, ...patch } : row)));

  const removeMeal = (key: number) =>
    setMeals((rows) => rows.filter((row) => row.key !== key));

  const addMeal = () =>
    setMeals((rows) => {
      const last = rows[rows.length - 1];
      const next = last
        ? (Math.min(last.mealType + 1, DietMealType.PostWorkout) as DietMealType)
        : DietMealType.Breakfast;
      return [...rows, blankMeal(next)];
    });

  const validate = (): boolean => {
    const errors: Record<string, string> = {};
    if (!form.memberId) errors.memberId = 'Choose the member this plan is for.';
    const title = form.title.trim();
    if (title.length < 3 || title.length > 200) errors.title = 'Enter a title of 3 to 200 characters.';
    if (!form.startDate) errors.startDate = 'Pick the start date.';
    if (form.endDate && form.startDate && form.endDate < form.startDate) {
      errors.endDate = 'End date cannot be before the start date.';
    }
    if (meals.length === 0) {
      errors.meals = 'Add at least one meal.';
    } else if (meals.some((meal) => !meal.title.trim())) {
      errors.meals = 'Every meal needs a title.';
    }
    setFieldErrors(errors);
    setAttempted(true);
    return Object.keys(errors).length === 0;
  };

  const save = async () => {
    if (!validate()) return;
    setSaving(true);
    setError(null);
    try {
      const dto: SaveDietPlanDto = {
        memberId: Number(form.memberId),
        trainerId: form.trainerId ? Number(form.trainerId) : null,
        title: form.title.trim(),
        goal: form.goal.trim() || null,
        notes: form.notes.trim() || null,
        startDate: form.startDate,
        endDate: form.endDate || null,
        status: form.status,
        meals: meals.map((meal, index) => ({
          id: meal.id,
          mealType: meal.mealType,
          title: meal.title.trim(),
          description: meal.description.trim() || null,
          calories: toIntOrNull(meal.calories),
          proteinGrams: toIntOrNull(meal.proteinGrams),
          carbsGrams: toIntOrNull(meal.carbsGrams),
          fatGrams: toIntOrNull(meal.fatGrams),
          displayOrder: index + 1,
        })),
      };

      if (isEdit) await dietApi.update(planId, dto);
      else await dietApi.create(dto);

      navigate(listPath);
    } catch (err) {
      setError(err);
    } finally {
      setSaving(false);
    }
  };

  if (loading) {
    return (
      <div className="page">
        <PageCard><Loading message="Loading diet plan…" /></PageCard>
      </div>
    );
  }

  return (
    <div className="page">
      <PageCard>
        <PageCardHeader
          icon={<IconFile size={20} />}
          title={isEdit ? 'Edit Diet Plan' : 'New Diet Plan'}
          subtitle="A meal-by-meal nutrition plan for one member, with calories and macros."
          actions={
            <button className="btn btn-outline" onClick={() => navigate(listPath)}>
              <IconArrowLeft size={15} /> Back to Diet Plans
            </button>
          }
        />

        <div className="page-card-body">
          <div className="stack">
            {error ? <ErrorAlert error={error} /> : null}
            {readOnly && (
              <Alert tone="info">You can view this plan but you do not have permission to change it.</Alert>
            )}

            <FormSection
              title="Plan details"
              caption="Who the plan is for, what it aims at, and when it runs."
              icon={<IconUser size={16} />}
            >
              <div className="form-grid">
                <Field
                  label="Member"
                  required
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
                      onChange={(e) => pickMember(e.target.value)}
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

                <Field label="Trainer" help="Optional. The trainer who owns this plan.">
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

                <Field label="Title" required help="3 to 200 characters." error={fieldErrors.title}>
                  <input
                    className={`input ${fieldErrors.title ? 'input-invalid' : ''}`}
                    placeholder="e.g. 8-Week Fat Loss Nutrition Plan"
                    value={form.title}
                    onChange={(e) => setForm({ ...form, title: e.target.value })}
                    disabled={readOnly}
                  />
                </Field>

                <Field label="Goal" help="Optional. Fat loss, muscle gain, maintenance…">
                  <input
                    className="input"
                    placeholder="e.g. Fat loss"
                    value={form.goal}
                    onChange={(e) => setForm({ ...form, goal: e.target.value })}
                    disabled={readOnly}
                  />
                </Field>

                <Field label="Start date" required error={fieldErrors.startDate}>
                  <input
                    className={`input ${fieldErrors.startDate ? 'input-invalid' : ''}`}
                    type="date"
                    value={form.startDate}
                    onChange={(e) => setForm({ ...form, startDate: e.target.value })}
                    disabled={readOnly}
                  />
                </Field>

                <Field label="End date" help="Optional. Leave empty for an open-ended plan." error={fieldErrors.endDate}>
                  <input
                    className={`input ${fieldErrors.endDate ? 'input-invalid' : ''}`}
                    type="date"
                    value={form.endDate}
                    onChange={(e) => setForm({ ...form, endDate: e.target.value })}
                    disabled={readOnly}
                  />
                </Field>

                <Field label="Status">
                  <select
                    className="select"
                    value={form.status}
                    onChange={(e) => setForm({ ...form, status: Number(e.target.value) as DietPlanStatus })}
                    disabled={readOnly}
                  >
                    {DIET_STATUS_OPTIONS.map((s) => <option key={s.value} value={s.value}>{s.label}</option>)}
                  </select>
                </Field>
              </div>

              <div style={{ marginTop: 16 }}>
                <Field label="Notes" help="Optional. Hydration, supplements, cheat-meal rules…">
                  <textarea
                    className="textarea"
                    value={form.notes}
                    onChange={(e) => setForm({ ...form, notes: e.target.value })}
                    disabled={readOnly}
                  />
                </Field>
              </div>
            </FormSection>

            <FormSection
              title="Meals"
              caption="At least one meal. Rows are shown to the member in this order."
              icon={<IconFile size={16} />}
            >
              {fieldErrors.meals ? <Alert tone="error">{fieldErrors.meals}</Alert> : null}

              <div className="table-wrap" style={{ marginTop: fieldErrors.meals ? 12 : 0 }}>
                <table className="table">
                  <thead>
                    <tr>
                      <th className="idx">#</th>
                      <th style={{ minWidth: 140 }}>Meal</th>
                      <th style={{ minWidth: 180 }}>Title *</th>
                      <th style={{ minWidth: 200 }}>Description</th>
                      <th className="num">Calories</th>
                      <th className="num">Protein (g)</th>
                      <th className="num">Carbs (g)</th>
                      <th className="num">Fat (g)</th>
                      <th className="actions" />
                    </tr>
                  </thead>
                  <tbody>
                    {meals.map((meal, index) => (
                      <tr key={meal.key}>
                        <td className="idx">{index + 1}</td>
                        <td>
                          <select
                            className="select"
                            value={meal.mealType}
                            onChange={(e) => updateMeal(meal.key, { mealType: Number(e.target.value) as DietMealType })}
                            disabled={readOnly}
                          >
                            {MEAL_TYPE_OPTIONS.map((t) => (
                              <option key={t.value} value={t.value}>{t.label}</option>
                            ))}
                          </select>
                        </td>
                        <td>
                          <input
                            className={`input ${attempted && !meal.title.trim() ? 'input-invalid' : ''}`}
                            placeholder="e.g. Oats with whey"
                            value={meal.title}
                            onChange={(e) => updateMeal(meal.key, { title: e.target.value })}
                            disabled={readOnly}
                          />
                        </td>
                        <td>
                          <input
                            className="input"
                            placeholder="Portions, swaps, timing…"
                            value={meal.description}
                            onChange={(e) => updateMeal(meal.key, { description: e.target.value })}
                            disabled={readOnly}
                          />
                        </td>
                        <td className="num">
                          <input
                            className="input"
                            style={{ width: 90 }}
                            type="number"
                            min={0}
                            max={10000}
                            value={meal.calories}
                            onChange={(e) => updateMeal(meal.key, { calories: e.target.value })}
                            disabled={readOnly}
                          />
                        </td>
                        <td className="num">
                          <input
                            className="input"
                            style={{ width: 80 }}
                            type="number"
                            min={0}
                            max={10000}
                            value={meal.proteinGrams}
                            onChange={(e) => updateMeal(meal.key, { proteinGrams: e.target.value })}
                            disabled={readOnly}
                          />
                        </td>
                        <td className="num">
                          <input
                            className="input"
                            style={{ width: 80 }}
                            type="number"
                            min={0}
                            max={10000}
                            value={meal.carbsGrams}
                            onChange={(e) => updateMeal(meal.key, { carbsGrams: e.target.value })}
                            disabled={readOnly}
                          />
                        </td>
                        <td className="num">
                          <input
                            className="input"
                            style={{ width: 80 }}
                            type="number"
                            min={0}
                            max={10000}
                            value={meal.fatGrams}
                            onChange={(e) => updateMeal(meal.key, { fatGrams: e.target.value })}
                            disabled={readOnly}
                          />
                        </td>
                        <td className="actions">
                          {!readOnly && (
                            <button
                              className="btn btn-del"
                              onClick={() => removeMeal(meal.key)}
                              disabled={meals.length === 1}
                              title={meals.length === 1 ? 'A plan needs at least one meal' : 'Remove this meal'}
                            >
                              Remove
                            </button>
                          )}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>

              {!readOnly && (
                <div style={{ marginTop: 12 }}>
                  <button className="btn btn-outline" onClick={addMeal}>
                    <IconPlus size={15} /> Add meal
                  </button>
                </div>
              )}
            </FormSection>

            <div className="form-footer">
              <button className="btn btn-dark" onClick={() => void save()} disabled={saving || readOnly}>
                {saving ? 'Saving…' : 'Save Diet Plan'}
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
