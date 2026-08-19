/**
 * The diet plans a trainer has assigned to the signed-in member.
 *
 * `GET /api/diet-plans/members/{memberId}` is self-access guarded server-side, but the module
 * may not be reachable for every account, so the request runs through `optional()` and quietly
 * degrades to the empty state instead of an error the member cannot act on.
 */

import { useEffect, useMemo, useState } from 'react';
import { EmptyState, ErrorAlert, Loading, PageCard, PageCardHeader, Pill } from '@/components/ui';
import { IconBox, IconCalendar, IconUser } from '@/components/icons';
import { optional } from '@/api/endpoints/member';
import { DietPlanStatus, dietApi, dietStatusLabel, dietStatusTone, mealTypeLabel } from '@/api/endpoints/diet';
import type { DietPlanDto, DietPlanMealDto } from '@/api/endpoints/diet';
import { useAuth } from '@/auth/AuthContext';
import { date } from '@/lib/format';
import './member.css';

/** Meals in serving order, grouped under their meal-type heading. */
function mealGroups(plan: DietPlanDto): { type: number; text: string; meals: DietPlanMealDto[] }[] {
  const sorted = [...(plan.meals ?? [])]
    .sort((a, b) => a.mealType - b.mealType || a.displayOrder - b.displayOrder);
  const groups: { type: number; text: string; meals: DietPlanMealDto[] }[] = [];
  for (const meal of sorted) {
    const last = groups[groups.length - 1];
    if (last && last.type === meal.mealType) last.meals.push(meal);
    else groups.push({ type: meal.mealType, text: meal.mealTypeText, meals: [meal] });
  }
  return groups;
}

/** Whole-plan macro totals; a meal without a macro simply does not count towards it. */
function planTotals(plan: DietPlanDto) {
  const sum = (pick: (meal: DietPlanMealDto) => number | null | undefined) =>
    (plan.meals ?? []).reduce((acc, meal) => acc + (pick(meal) ?? 0), 0);
  return {
    calories: sum((m) => m.calories),
    protein: sum((m) => m.proteinGrams),
    carbs: sum((m) => m.carbsGrams),
    fat: sum((m) => m.fatGrams),
  };
}

function MacroChips({ meal }: { meal: DietPlanMealDto }) {
  const chips: string[] = [];
  if (meal.calories != null) chips.push(`${meal.calories} kcal`);
  if (meal.proteinGrams != null) chips.push(`P ${meal.proteinGrams}g`);
  if (meal.carbsGrams != null) chips.push(`C ${meal.carbsGrams}g`);
  if (meal.fatGrams != null) chips.push(`F ${meal.fatGrams}g`);
  if (chips.length === 0) return null;
  return (
    <div className="m-macro-row">
      {chips.map((chip) => <span className="m-macro-chip" key={chip}>{chip}</span>)}
    </div>
  );
}

function MealGroups({ plan }: { plan: DietPlanDto }) {
  const groups = mealGroups(plan);
  if (groups.length === 0) {
    return <EmptyState icon={<IconBox size={30} />} title="No meals in this plan yet" />;
  }
  return (
    <div className="m-meal-groups">
      {groups.map((group) => (
        <section className="m-meal-group" key={group.type}>
          <header className="m-meal-group-head">{mealTypeLabel(group.type, group.text)}</header>
          {group.meals.map((meal) => (
            <div className="m-meal-item" key={meal.id}>
              <div className="m-meal-name">{meal.title}</div>
              {meal.description ? <div className="m-meal-desc">{meal.description}</div> : null}
              <MacroChips meal={meal} />
            </div>
          ))}
        </section>
      ))}
    </div>
  );
}

/** The date-range / trainer / goal strip plus daily totals, shared by current and past plans. */
function PlanMeta({ plan }: { plan: DietPlanDto }) {
  const totals = planTotals(plan);
  const hasTotals = totals.calories > 0 || totals.protein > 0 || totals.carbs > 0 || totals.fat > 0;
  return (
    <>
      <div className="m-plan-card-meta">
        {date(plan.startDate)}{plan.endDate ? ` – ${date(plan.endDate)}` : ' onwards'}
        {plan.trainerName ? ` · by ${plan.trainerName}` : ''}
        {plan.goal ? ` · Goal: ${plan.goal}` : ''}
      </div>
      {hasTotals && (
        <div className="m-macro-row" style={{ marginTop: 0 }}>
          {totals.calories > 0 && <span className="m-macro-chip">{totals.calories} kcal / day</span>}
          {totals.protein > 0 && <span className="m-macro-chip">Protein {totals.protein}g</span>}
          {totals.carbs > 0 && <span className="m-macro-chip">Carbs {totals.carbs}g</span>}
          {totals.fat > 0 && <span className="m-macro-chip">Fat {totals.fat}g</span>}
        </div>
      )}
    </>
  );
}

export default function MyDietPage() {
  const { user } = useAuth();
  const memberId = user?.memberId ?? null;

  const [plans, setPlans] = useState<DietPlanDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<unknown>(null);
  const [expanded, setExpanded] = useState<Set<number>>(new Set());

  useEffect(() => {
    if (memberId === null) { setLoading(false); return; }
    const ctrl = new AbortController();
    setLoading(true);
    setError(null);

    optional(() => dietApi.memberPlans(memberId, ctrl.signal))
      .then((result) => { if (!ctrl.signal.aborted) setPlans(result ?? []); })
      .catch((e) => { if (!ctrl.signal.aborted) setError(e); })
      .finally(() => { if (!ctrl.signal.aborted) setLoading(false); });

    return () => ctrl.abort();
  }, [memberId]);

  // The most recent active plan is shown in full; everything else collapses into history.
  const activePlan = useMemo(() => {
    const actives = plans.filter((plan) => plan.status === DietPlanStatus.Active);
    if (actives.length === 0) return null;
    return [...actives].sort(
      (a, b) => new Date(b.startDate).getTime() - new Date(a.startDate).getTime(),
    )[0];
  }, [plans]);

  const pastPlans = useMemo(
    () => plans
      .filter((plan) => plan !== activePlan)
      .sort((a, b) => new Date(b.startDate).getTime() - new Date(a.startDate).getTime()),
    [plans, activePlan],
  );

  const toggle = (id: number) => setExpanded((prev) => {
    const next = new Set(prev);
    if (next.has(id)) next.delete(id); else next.add(id);
    return next;
  });

  if (memberId === null) {
    return (
      <div className="page">
        <PageCard>
          <EmptyState icon={<IconUser size={34} />} title="No member record linked" message="This login is not attached to a member profile." />
        </PageCard>
      </div>
    );
  }

  return (
    <div className="page">
      <PageCard>
        <PageCardHeader
          icon={<IconBox size={20} />}
          title="My Diet"
          subtitle="The nutrition plans your trainer has put together for you."
          actions={
            <span className="btn btn-dark" style={{ cursor: 'default' }}>
              <IconBox size={15} /> {plans.length} Plan{plans.length === 1 ? '' : 's'}
            </span>
          }
        />
        <div className="page-card-body">
          {error ? <ErrorAlert error={error} /> : loading ? <Loading message="Loading your diet plans…" /> : plans.length === 0 ? (
            <EmptyState
              icon={<IconBox size={34} />}
              title="No diet plans yet"
              message="Your trainer assigns these — once a nutrition plan is created for you, every meal shows up here."
            />
          ) : (
            <div className="m-diet-stack">
              {/* ------------------------------------------------- current plan */}
              {activePlan ? (
                <article className="m-plan-card">
                  <header className="m-plan-card-head">
                    <span className="m-plan-card-name">{activePlan.title}</span>
                    <span className="m-plan-card-date">{date(activePlan.startDate)}</span>
                  </header>
                  <div className="m-plan-card-body">
                    <div className="row" style={{ gap: 6, flexWrap: 'wrap' }}>
                      <Pill tone="success">Active</Pill>
                      <Pill tone="info">
                        {activePlan.meals.length} meal{activePlan.meals.length === 1 ? '' : 's'}
                      </Pill>
                    </div>
                    <PlanMeta plan={activePlan} />
                    {activePlan.notes ? <div className="m-meal-desc" style={{ marginTop: 0 }}>{activePlan.notes}</div> : null}
                    <MealGroups plan={activePlan} />
                  </div>
                </article>
              ) : (
                <EmptyState
                  icon={<IconBox size={30} />}
                  title="No active diet plan"
                  message="Your earlier plans are below; ask your trainer for a fresh one."
                />
              )}

              {/* -------------------------------------------------- past plans */}
              {pastPlans.length > 0 && (
                <div>
                  <div className="m-quick-info-heading">Past plans</div>
                  {pastPlans.map((plan) => (
                    <article className="m-diet-past" key={plan.id}>
                      <header className="m-diet-past-head">
                        <span className="cell-main">{plan.title}</span>
                        <span className="cell-icon muted">
                          <IconCalendar size={13} />
                          {date(plan.startDate)}{plan.endDate ? ` – ${date(plan.endDate)}` : ''}
                        </span>
                        <Pill tone={dietStatusTone(plan.status)}>{dietStatusLabel(plan.status, plan.statusText)}</Pill>
                        <Pill tone="neutral">{plan.meals.length} meal{plan.meals.length === 1 ? '' : 's'}</Pill>
                        <button className="btn btn-outline btn-sm" style={{ marginLeft: 'auto' }} onClick={() => toggle(plan.id)}>
                          {expanded.has(plan.id) ? 'Hide meals' : 'View meals'}
                        </button>
                      </header>
                      {expanded.has(plan.id) && (
                        <div className="m-diet-past-body">
                          <PlanMeta plan={plan} />
                          {plan.notes ? <div className="m-meal-desc">{plan.notes}</div> : null}
                          <div style={{ marginTop: 12 }}>
                            <MealGroups plan={plan} />
                          </div>
                        </div>
                      )}
                    </article>
                  ))}
                </div>
              )}
            </div>
          )}
        </div>
      </PageCard>
    </div>
  );
}
