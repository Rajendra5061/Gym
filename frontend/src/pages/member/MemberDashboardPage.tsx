/**
 * The member's own dashboard.
 *
 * Everything is read from `GET /api/dashboard/member/{id}` using the member id carried on the
 * signed-in account — never a route parameter — so the screen cannot be pointed at anyone else.
 */

import { useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import {
  CartesianGrid, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis,
} from 'recharts';
import {
  EmptyState, ErrorAlert, Loading, PageCard, PageCardHeader, Pill, StatusPill,
} from '@/components/ui';
import {
  IconBell, IconCalendar, IconCard, IconChart, IconCheckSquare, IconCrown, IconDumbbell,
  IconFlame, IconMoney, IconUser, IconWarning,
} from '@/components/icons';
import { memberApi, optional } from '@/api/endpoints/member';
import type { MemberDashboardDto } from '@/api/endpoints/member';
import { severityKey } from '@/api/endpoints/notifications';
import { useAuth } from '@/auth/AuthContext';
import { date, initials, money, relativeTime, time } from '@/lib/format';
import { prefersReducedMotion } from '@/lib/motion';
import './member.css';

/** Midnight-normalised ISO key, so a Set of visit days can be probed per calendar date. */
function dayKey(d: Date): string {
  return `${d.getFullYear()}-${d.getMonth()}-${d.getDate()}`;
}

/**
 * Training consistency: the streak, the week's goal and a 12-week activity strip. All of it
 * reads from the day-level insights the dashboard endpoint computes — one visit per day
 * counts, however many times the member passed the front desk.
 */
const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

function ConsistencyCard({ insights }: { insights: MemberDashboardDto['attendanceInsights'] }) {
  const today = new Date();
  today.setHours(0, 0, 0, 0);

  const visited = useMemo(
    () => new Set(insights.activeDays.map((d) => dayKey(new Date(d)))),
    [insights.activeDays],
  );

  // The strip: 12 columns of Monday-first weeks, the rightmost being the CURRENT week —
  // anchored on this week's Monday and walked back 11 weeks, so today is always on screen.
  const stripStart = useMemo(() => {
    const start = new Date(today);
    start.setDate(start.getDate() - ((start.getDay() + 6) % 7) - 77);
    return start;
  }, [today]);

  const weeks = useMemo(() => Array.from({ length: 12 }, (_, w) =>
    Array.from({ length: 7 }, (_, r) => {
      const d = new Date(stripStart);
      d.setDate(d.getDate() + w * 7 + r);
      return d;
    })), [stripStart]);

  // This week's Monday-first day dots.
  const weekDays = useMemo(() => {
    const monday = new Date(today);
    monday.setDate(monday.getDate() - ((monday.getDay() + 6) % 7));
    return Array.from({ length: 7 }, (_, i) => {
      const d = new Date(monday);
      d.setDate(d.getDate() + i);
      return d;
    });
  }, [today]);

  const goalDone = Math.min(insights.visitsThisWeek, insights.weeklyTargetDays);
  // ~5 sessions a week over 30 days is 21 training days — the consistency yardstick.
  const monthTarget = Math.round((insights.weeklyTargetDays / 7) * 30);
  const consistency = Math.min(100, Math.round((insights.activeDaysLast30 / monthTarget) * 100));
  const streak = insights.currentStreakDays;
  const streakAlive = streak > 0;
  const visitedToday = visited.has(dayKey(today));
  const weekLeft = insights.weeklyTargetDays - goalDone;

  // The coach line: always encouraging, always about the very next step.
  const coach = !streakAlive
    ? 'No streak right now — and today is the perfect day to start one. One check-in lights the flame.'
    : visitedToday
      ? `${streak} day${streak === 1 ? '' : 's'} straight — brilliant work. Come back tomorrow and the flame keeps burning.`
      : `Your ${streak}-day streak is still alive. Check in today to keep it burning!`;

  // Distinct training days inside the strip window — the headline number above the grid.
  const countDays = (cols: Date[][]) =>
    cols.flat().filter((d) => d <= today && visited.has(dayKey(d))).length;
  const stripDays = useMemo(() => countDays(weeks), [weeks, visited, today]);
  /* A phone only draws the last six columns, so it needs its own total — quoting the 12-week
     figure over a 6-week grid would have the header contradicting the picture under it. */
  const stripDaysShort = useMemo(() => countDays(weeks.slice(6)), [weeks, visited, today]);

  // Streak badges: reached ones glow, the next one shows how close it is.
  const MILESTONES = [3, 7, 14, 30, 60];
  const nextMilestone = MILESTONES.find((m) => m > streak);

  return (
    <PageCard>
      <PageCardHeader
        icon={<IconFlame size={20} />}
        title="Consistency & Streaks"
        subtitle="Show up, keep the chain alive — one visit a day is all it takes."
      />
      <div className="page-card-body">
        {/* ------------------------------------------------------ the coach */}
        <div className={`m-coach ${streakAlive ? 'alive' : ''}`}>
          <span className="m-coach-icon"><IconFlame size={17} /></span>
          <span>{coach}</span>
        </div>

        <div className="m-streak-grid">
          {/* --------------------------------------------------- the streak */}
          <div className={`m-streak-hero ${streakAlive ? 'alive' : ''}`}>
            <div className="m-streak-flame"><IconFlame size={30} /></div>
            <div className="m-streak-count">{streak}</div>
            <div className="m-streak-label">day streak</div>
            <div className="m-streak-best">
              {streakAlive
                ? <>Best ever: <b>{insights.bestStreakDays}</b> day{insights.bestStreakDays === 1 ? '' : 's'}</>
                : 'Visit today to light it'}
            </div>
          </div>

          {/* ------------------------------------------------- weekly goal */}
          <div className="m-streak-panel">
            <div className="m-streak-panel-title">Your week</div>
            <div className="m-week-dots">
              {weekDays.map((d) => {
                const done = visited.has(dayKey(d));
                const isToday = dayKey(d) === dayKey(today);
                const future = d > today;
                return (
                  <span key={d.toISOString()} className={`m-week-dot ${done ? 'done' : ''} ${isToday ? 'today' : ''} ${future ? 'future' : ''}`}>
                    {'MTWTFSS'[(d.getDay() + 6) % 7]}
                  </span>
                );
              })}
            </div>
            <div className="m-streak-panel-value">
              {goalDone} <span>of {insights.weeklyTargetDays} sessions</span>
            </div>
            <div className="m-goal-track" role="img"
              aria-label={`${goalDone} of ${insights.weeklyTargetDays} sessions this week`}>
              <div className="m-goal-fill" style={{ width: `${(goalDone / insights.weeklyTargetDays) * 100}%` }} />
            </div>
            <div className={`m-streak-panel-sub ${weekLeft <= 0 ? 'm-goal-met' : ''}`}>
              {weekLeft <= 0
                ? 'Weekly goal smashed — strong week!'
                : `${weekLeft} more session${weekLeft === 1 ? '' : 's'} to hit your week.`}
            </div>
          </div>

          {/* ------------------------------------------------- performance */}
          <div className="m-streak-panel">
            <div className="m-streak-panel-title">Last 30 days</div>
            <div className="m-streak-panel-value">
              {consistency}% <span>consistency</span>
            </div>
            <div className="m-streak-panel-sub">
              {insights.activeDaysLast30} training day{insights.activeDaysLast30 === 1 ? '' : 's'} of a {monthTarget}-day goal
            </div>
            <div className="m-goal-track" role="img" aria-label={`${consistency}% consistency`}>
              <div className="m-goal-fill" style={{ width: `${consistency}%` }} />
            </div>
          </div>
        </div>

        {/* ------------------------------------------------ streak badges */}
        <div className="m-badges">
          {MILESTONES.map((m) => {
            const earned = insights.bestStreakDays >= m;
            const isNext = m === nextMilestone;
            return (
              <span key={m} className={`m-badge ${earned ? 'earned' : ''} ${isNext ? 'next' : ''}`}>
                <IconFlame size={13} /> {m}-day
                {isNext && <em>{m - streak} to go</em>}
              </span>
            );
          })}
        </div>

        {/* ------------------------------------------------ 12-week strip */}
        <div className="m-heat">
          <div className="m-heat-head">
            <span className="m-heat-title">
                <span className="m-heat-full">Last 12 weeks</span>
                <span className="m-heat-short">Last 6 weeks</span>
              </span>
            <span className="m-heat-count">
              <b><span className="m-heat-full">{stripDays}</span><span className="m-heat-short">{stripDaysShort}</span></b> gym days
            </span>
          </div>

          <div className="m-heat-frame">
            {/* Month labels ride above the column their month starts in, so twelve weeks of
                squares still say when. */}
            <div className="m-heat-months" aria-hidden="true">
              <span className="m-heat-daygutter" />
              {weeks.map((week, i) => {
                const prev = i === 0 ? null : weeks[i - 1][0];
                const showLabel = !prev || prev.getMonth() !== week[0].getMonth();
                return (
                  <span className="m-heat-month" key={week[0].toISOString()}>
                    {showLabel ? MONTHS[week[0].getMonth()] : ''}
                  </span>
                );
              })}
            </div>

            <div className="m-heat-grid" role="img" aria-label={`Training days over the last 12 weeks: ${stripDays} gym days`}>
              <div className="m-heat-days" aria-hidden="true">
                {['M', '', 'W', '', 'F', '', 'S'].map((l, i) => <span key={i}>{l}</span>)}
              </div>
              {weeks.map((week) => (
                <div key={week[0].toISOString()} className="m-heat-col">
                  {week.map((d) => {
                    const future = d > today;
                    const done = visited.has(dayKey(d));
                    const isToday = dayKey(d) === dayKey(today);
                    return (
                      <span
                        key={d.toISOString()}
                        title={`${date(d.toISOString())}${done ? ' — trained' : future ? '' : ' — rest day'}`}
                        className={`m-heat-cell ${done ? 'done' : ''} ${isToday ? 'today' : ''} ${future ? 'future' : ''}`}
                      />
                    );
                  })}
                </div>
              ))}
            </div>
          </div>

          <div className="m-heat-legend">
            <span><span className="m-heat-full">12 weeks ago</span><span className="m-heat-short">6 weeks ago</span></span>
            <span className="m-heat-key"><i className="m-heat-cell" /> rest <i className="m-heat-cell done" /> gym day</span>
            <span>today</span>
          </div>
        </div>
      </div>
    </PageCard>
  );
}

export default function MemberDashboardPage() {
  const { user, currency } = useAuth();
  const memberId = user?.memberId ?? null;

  const [data, setData] = useState<MemberDashboardDto | null>(null);
  const [planCount, setPlanCount] = useState<number | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<unknown>(null);

  useEffect(() => {
    if (memberId === null) { setLoading(false); return; }
    const ctrl = new AbortController();
    setLoading(true);
    setError(null);

    memberApi.dashboard(memberId, ctrl.signal)
      .then(async (result) => {
        if (ctrl.signal.aborted) return;
        setData(result);
        const plans = await optional(() => memberApi.workoutPlans(memberId, ctrl.signal));
        if (ctrl.signal.aborted) return;
        setPlanCount(plans ? plans.length : (result.activeWorkoutPlan ? 1 : 0));
      })
      .catch((e) => { if (!ctrl.signal.aborted) setError(e); })
      .finally(() => { if (!ctrl.signal.aborted) setLoading(false); });

    return () => ctrl.abort();
  }, [memberId]);

  const trend = useMemo(
    () => (data?.attendanceTrend ?? []).map((point) => ({
      label: point.label,
      visits: Number(point.value),
    })),
    [data],
  );

  if (memberId === null) {
    return (
      <div className="page">
        <PageCard>
          <EmptyState
            icon={<IconUser size={34} />}
            title="No member record linked"
            message="This login is not attached to a member profile, so there is nothing to show here yet."
          />
        </PageCard>
      </div>
    );
  }

  if (loading) return <div className="page"><PageCard><Loading message="Loading your dashboard…" /></PageCard></div>;
  if (error) return <div className="page"><PageCard><div style={{ padding: 20 }}><ErrorAlert error={error} /></div></PageCard></div>;
  if (!data) return null;

  const member = data.member;
  const subscription = data.activeSubscription;

  return (
    <div className="page">
      {/* ------------------------------------------------------------ banner */}
      <div className="member-hero">
        <div className="member-hero-avatar">{initials(member.fullName)}</div>
        <div className="grow">
          <div className="member-hero-title">Welcome back, {member.fullName.split(' ')[0]}!</div>
          <div className="member-hero-sub">
            Member {member.memberCode} · joined {date(member.joiningDate)}
          </div>
          <div className="member-hero-badges">
            <span className="member-hero-badge">
              <IconCrown size={13} /> {subscription?.planName ?? 'No active plan'}
            </span>
            <span className="member-hero-badge">
              <IconCalendar size={13} /> {data.daysRemaining} day{data.daysRemaining === 1 ? '' : 's'} remaining
              {subscription ? ` · ends ${date(subscription.endDate)}` : ''}
            </span>
            <span className="member-hero-badge">
              <IconMoney size={13} /> {money(data.outstandingAmount, currency)} outstanding
            </span>
          </div>
        </div>
      </div>

      {/* ------------------------------------------------------ expiry banner */}
      {data.daysRemaining <= 7 && (
        <Link
          to="/member/membership"
          className={`m-expiry-banner ${data.daysRemaining <= 0 ? 'm-expiry-danger' : 'm-expiry-warning'}`}
        >
          <IconWarning size={20} />
          <span className="grow">
            {data.daysRemaining <= 0
              ? (subscription
                ? `Your membership ended on ${date(subscription.endDate)}. Renew at the front desk to keep training.`
                : 'You have no active membership. Speak to the front desk to renew.')
              : `Your membership ends ${subscription ? `on ${date(subscription.endDate)}` : 'soon'} — ${data.daysRemaining} day${data.daysRemaining === 1 ? '' : 's'} left.`}
          </span>
          <span className="m-expiry-link">View membership →</span>
        </Link>
      )}

      {/* ------------------------------------------------------------- tiles */}
      <div className="member-tiles">
        <div className="m-tile m-tile-blue">
          <div className="m-tile-icon"><IconCheckSquare size={22} /></div>
          <div className="grow">
            <div className="m-tile-title">Total Attendance</div>
            <div className="m-tile-value">{data.totalVisits}</div>
            <div className="m-tile-caption">{data.visitsThisMonth} visit{data.visitsThisMonth === 1 ? '' : 's'} this month</div>
          </div>
        </div>
        <div className="m-tile m-tile-green">
          <div className="m-tile-icon"><IconMoney size={22} /></div>
          <div className="grow">
            <div className="m-tile-title">Total Payments</div>
            <div className="m-tile-value">{money(member.totalPaid, currency)}</div>
            <div className="m-tile-caption">{money(member.totalOutstanding, currency)} still due</div>
          </div>
        </div>
        <div className="m-tile m-tile-orange">
          <div className="m-tile-icon"><IconDumbbell size={22} /></div>
          <div className="grow">
            <div className="m-tile-title">Workout Plans</div>
            <div className="m-tile-value">{planCount ?? 0}</div>
            <div className="m-tile-caption">
              {data.activeWorkoutPlan ? data.activeWorkoutPlan.workoutPlanName : 'No active plan assigned'}
            </div>
          </div>
        </div>
      </div>

      {/* ------------------------------------------------ streaks & goals */}
      <ConsistencyCard insights={data.attendanceInsights} />

      {/* ----------------------------------------------------- quick actions */}
      <PageCard>
        <PageCardHeader
          icon={<IconChart size={20} />}
          title="Quick Actions"
          subtitle="Jump straight to the part of your membership you need."
        />
        <div className="page-card-body">
          <div className="m-quick-actions">
            <Link className="m-quick-card" to="/member/membership">
              <span className="m-quick-card-icon"><IconCrown size={20} /></span>
              <span>
                <span className="m-quick-card-title">My Membership</span>
                <span className="m-quick-card-sub">Plan, dates and balance</span>
              </span>
            </Link>
            <Link className="m-quick-card" to="/member/profile">
              <span className="m-quick-card-icon"><IconUser size={20} /></span>
              <span>
                <span className="m-quick-card-title">My Profile</span>
                <span className="m-quick-card-sub">Contact details and password</span>
              </span>
            </Link>
            <Link className="m-quick-card" to="/member/feedback">
              <span className="m-quick-card-icon"><IconCard size={20} /></span>
              <span>
                <span className="m-quick-card-title">Feedback</span>
                <span className="m-quick-card-sub">Tell the gym what you think</span>
              </span>
            </Link>
          </div>
        </div>
      </PageCard>

      {/* ---------------------------------------------- recent notifications */}
      <PageCard>
        <PageCardHeader
          icon={<IconBell size={20} />}
          title="Recent Notifications"
          subtitle="Reminders and alerts about your membership."
          actions={<Link className="btn btn-dark" to="/member/notifications">View all</Link>}
        />
        {data.notifications.length === 0 ? (
          <EmptyState icon={<IconBell size={30} />} title="No notifications yet" message="Alerts about your membership and payments appear here." />
        ) : (
          <div className="m-notice-list">
            {data.notifications.slice(0, 5).map((item) => (
              <Link
                className={`m-notice-card m-sev-${severityKey(item.severity)} ${item.isRead ? '' : 'm-unread'}`}
                key={item.id}
                to="/member/notifications"
              >
                <div className="grow">
                  <div className="m-notice-title">
                    {!item.isRead && <span className="m-notice-dot" aria-label="Unread" />}
                    {item.title}
                  </div>
                  <div className="m-notice-message m-clamp-2">{item.message}</div>
                </div>
                <span className="m-notice-time">{relativeTime(item.createdAtUtc)}</span>
              </Link>
            ))}
          </div>
        )}
      </PageCard>

      {/* ------------------------------------------------- recent attendance */}
      <PageCard>
        <PageCardHeader
          icon={<IconCalendar size={20} />}
          title="Recent Attendance"
          subtitle="Your latest visits to the gym."
          actions={<Link className="btn btn-dark" to="/member/attendance">View all</Link>}
        />
        {data.recentAttendance.length === 0 ? (
          <EmptyState icon={<IconCalendar size={30} />} title="No visits recorded yet" />
        ) : (
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th className="idx">#</th>
                  <th className="fit">Date</th>
                  <th className="fit">Time In</th>
                  <th className="fit">Time Out</th>
                  <th className="fit">Duration</th>
                </tr>
              </thead>
              <tbody>
                {data.recentAttendance.map((row, index) => (
                  <tr key={row.id}>
                    <td className="idx">{index + 1}</td>
                    <td className="fit"><span className="cell-icon"><IconCalendar size={14} />{date(row.attendanceDate)}</span></td>
                    <td><Pill tone="success">{time(row.checkInTime)}</Pill></td>
                    <td>{row.checkOutTime ? time(row.checkOutTime) : <span className="muted">—</span>}</td>
                    <td className="fit">{row.durationMinutes ? `${row.durationMinutes} min` : '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </PageCard>

      {/* --------------------------------------------------- recent payments */}
      <PageCard>
        <PageCardHeader
          icon={<IconCard size={20} />}
          title="Recent Payments"
          subtitle="The receipts raised against your membership."
          actions={<Link className="btn btn-dark" to="/member/payments">View all</Link>}
        />
        {data.recentPayments.length === 0 ? (
          <EmptyState icon={<IconCard size={30} />} title="No payments recorded yet" />
        ) : (
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th className="idx">#</th>
                  <th className="fit">Receipt</th>
                  <th className="wide">Plan</th>
                  <th className="fit">Amount</th>
                  <th className="fit">Payment Date</th>
                  <th className="fit">Status</th>
                </tr>
              </thead>
              <tbody>
                {data.recentPayments.map((row, index) => (
                  <tr key={row.id}>
                    <td className="idx">{index + 1}</td>
                    <td className="cell-main fit">{row.receiptNumber}</td>
                    <td>{row.planName ?? '—'}</td>
                    <td className="fit"><span className="cell-icon"><IconMoney size={14} />{money(row.finalAmount, currency)}</span></td>
                    <td className="fit"><span className="cell-icon"><IconCalendar size={14} />{date(row.paymentDate)}</span></td>
                    <td className="fit"><StatusPill status={row.statusText} /></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </PageCard>

      {/* ----------------------------------------------------- attendance trend */}
      <PageCard>
        <PageCardHeader
          icon={<IconChart size={20} />}
          title="Attendance Trend"
          subtitle="How often you have trained recently."
        />
        <div className="page-card-body">
          {trend.length === 0 ? (
            <EmptyState icon={<IconChart size={30} />} title="Not enough data yet" message="Your trend appears once a few visits have been recorded." />
          ) : (
            <div style={{ width: '100%', height: 280 }}>
              <ResponsiveContainer>
                <LineChart data={trend} margin={{ top: 8, right: 16, bottom: 8, left: 0 }}>
                  <CartesianGrid stroke="var(--divider)" strokeDasharray="3 3" vertical={false} />
                  <XAxis dataKey="label" tick={{ fontSize: 12, fill: 'var(--text-muted)' }} tickLine={false} axisLine={{ stroke: 'var(--divider)' }} />
                  <YAxis allowDecimals={false} tick={{ fontSize: 12, fill: 'var(--text-muted)' }} tickLine={false} axisLine={false} width={38} />
                  <Tooltip
                    contentStyle={{ borderRadius: 8, border: '1px solid var(--divider)', fontSize: 12 }}
                    formatter={(value: number) => [`${value} visit${value === 1 ? '' : 's'}`, 'Visits']}
                  />
                  <Line
                    isAnimationActive={!prefersReducedMotion}
                    type="monotone"
                    dataKey="visits"
                    stroke="var(--chart-1)"
                    strokeWidth={2.5}
                    dot={{ r: 3, fill: 'var(--chart-1)' }}
                    activeDot={{ r: 5 }}
                  />
                </LineChart>
              </ResponsiveContainer>
            </div>
          )}
        </div>
      </PageCard>
    </div>
  );
}
