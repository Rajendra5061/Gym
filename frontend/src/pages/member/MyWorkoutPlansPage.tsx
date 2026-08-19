/**
 * The workout plans assigned to the signed-in member, one card each.
 *
 * A member without the workouts permission still sees the active plan the dashboard returns,
 * so the screen degrades to "your current plan" rather than an error.
 */

import { useEffect, useState } from 'react';
import { EmptyState, ErrorAlert, Loading, Modal, PageCard, PageCardHeader, Pill } from '@/components/ui';
import { IconCalendar, IconDumbbell, IconUser } from '@/components/icons';
import { memberApi, optional } from '@/api/endpoints/member';
import type { MemberWorkoutPlanDto } from '@/api/endpoints/member';
import { useAuth } from '@/auth/AuthContext';
import { date, label } from '@/lib/format';
import './member.css';

const DAYS = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];

/** The body text on the card: the trainer's note, or a summary of the prescribed exercises. */
function planDetail(plan: MemberWorkoutPlanDto): string {
  if (plan.notes && plan.notes.trim()) return plan.notes.trim();
  if (plan.exercises.length === 0) return 'No exercises have been added to this plan yet.';
  return plan.exercises
    .slice(0, 6)
    .map((e) => `• ${e.exerciseName} — ${e.sets} × ${e.repetitions}${e.targetWeightKg ? ` @ ${e.targetWeightKg} kg` : ''}`)
    .join('\n');
}

export default function MyWorkoutPlansPage() {
  const { user } = useAuth();
  const memberId = user?.memberId ?? null;

  const [plans, setPlans] = useState<MemberWorkoutPlanDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<unknown>(null);
  const [open, setOpen] = useState<MemberWorkoutPlanDto | null>(null);

  useEffect(() => {
    if (memberId === null) { setLoading(false); return; }
    const ctrl = new AbortController();
    setLoading(true);
    setError(null);

    optional(() => memberApi.workoutPlans(memberId, ctrl.signal))
      .then(async (result) => {
        if (ctrl.signal.aborted) return;
        if (result) { setPlans(result); return; }
        const dashboard = await memberApi.dashboard(memberId, ctrl.signal);
        if (ctrl.signal.aborted) return;
        setPlans(dashboard.activeWorkoutPlan ? [dashboard.activeWorkoutPlan] : []);
      })
      .catch((e) => { if (!ctrl.signal.aborted) setError(e); })
      .finally(() => { if (!ctrl.signal.aborted) setLoading(false); });

    return () => ctrl.abort();
  }, [memberId]);

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
          icon={<IconDumbbell size={20} />}
          title="My Workout Plans"
          subtitle="The routines your trainer has put together for you."
          actions={
            <span className="btn btn-dark" style={{ cursor: 'default' }}>
              <IconDumbbell size={15} /> {plans.length} Plan{plans.length === 1 ? '' : 's'}
            </span>
          }
        />
        <div className="page-card-body">
          {error ? <ErrorAlert error={error} /> : loading ? <Loading message="Loading your plans…" /> : plans.length === 0 ? (
            <EmptyState
              icon={<IconDumbbell size={34} />}
              title="No workout plans yet"
              message="Once a trainer assigns you a routine it shows up here with every exercise."
            />
          ) : (
            <div className="m-plan-cards">
              {plans.map((plan) => (
                <article className="m-plan-card" key={plan.id}>
                  <header className="m-plan-card-head">
                    <span className="m-plan-card-name">{plan.workoutPlanName}</span>
                    <span className="m-plan-card-date">{date(plan.startDate)}</span>
                  </header>
                  <div className="m-plan-card-body">
                    <div className="m-plan-card-meta">
                      Assigned {date(plan.startDate)}
                      {plan.trainerName ? ` by ${plan.trainerName}` : ''}
                      {plan.endDate ? ` · runs to ${date(plan.endDate)}` : ''}
                    </div>
                    <div className="row" style={{ gap: 6 }}>
                      {plan.isActive ? <Pill tone="success">Active</Pill> : <Pill tone="neutral">Completed</Pill>}
                      <Pill tone="info">{plan.exercises.length} exercise{plan.exercises.length === 1 ? '' : 's'}</Pill>
                    </div>
                    <div className="m-plan-card-detail">{planDetail(plan)}</div>
                    <div className="m-plan-card-foot">
                      <button className="btn btn-outline btn-sm" onClick={() => setOpen(plan)}>View Full</button>
                    </div>
                  </div>
                </article>
              ))}
            </div>
          )}
        </div>
      </PageCard>

      {open && (
        <Modal
          title={open.workoutPlanName}
          icon={<IconDumbbell size={18} />}
          width={860}
          onClose={() => setOpen(null)}
          footer={<button className="btn btn-dark" onClick={() => setOpen(null)}>Close</button>}
        >
          <div className="stack">
            <div className="row" style={{ gap: 8, flexWrap: 'wrap' }}>
              <Pill tone="dark"><IconCalendar size={12} /> {date(open.startDate)}{open.endDate ? ` – ${date(open.endDate)}` : ''}</Pill>
              {open.trainerName ? <Pill tone="primary">{open.trainerName}</Pill> : null}
              {open.isActive ? <Pill tone="success">Active</Pill> : <Pill tone="neutral">Completed</Pill>}
            </div>

            {open.notes && <div style={{ whiteSpace: 'pre-wrap' }}>{open.notes}</div>}

            {open.exercises.length === 0 ? (
              <EmptyState icon={<IconDumbbell size={30} />} title="No exercises in this plan" />
            ) : (
              <div className="table-wrap">
                <table className="table">
                  <thead>
                    <tr>
                      <th className="idx">#</th>
                      <th>Exercise</th>
                      <th>Day</th>
                      <th className="center">Sets</th>
                      <th className="center">Reps</th>
                      <th className="center">Weight</th>
                      <th className="center">Rest</th>
                      <th>Notes</th>
                    </tr>
                  </thead>
                  <tbody>
                    {[...open.exercises]
                      .sort((a, b) => (a.dayOfWeek ?? 0) - (b.dayOfWeek ?? 0) || a.displayOrder - b.displayOrder)
                      .map((exercise, index) => (
                        <tr key={exercise.id}>
                          <td className="idx">{index + 1}</td>
                          <td>
                            <div className="cell-main">{exercise.exerciseName}</div>
                            <div className="cell-sub">{label(exercise.muscleGroupText)}</div>
                          </td>
                          <td>{exercise.dayOfWeek !== null && exercise.dayOfWeek !== undefined ? DAYS[exercise.dayOfWeek % 7] : '—'}</td>
                          <td className="center">{exercise.sets}</td>
                          <td className="center">{exercise.repetitions}</td>
                          <td className="center">{exercise.targetWeightKg ? `${exercise.targetWeightKg} kg` : '—'}</td>
                          <td className="center">{exercise.restSeconds ? `${exercise.restSeconds}s` : '—'}</td>
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
    </div>
  );
}
