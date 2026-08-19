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
  IconMoney, IconUser, IconWarning,
} from '@/components/icons';
import { memberApi, optional } from '@/api/endpoints/member';
import type { MemberDashboardDto } from '@/api/endpoints/member';
import { severityKey } from '@/api/endpoints/notifications';
import { useAuth } from '@/auth/AuthContext';
import { date, initials, money, relativeTime, time } from '@/lib/format';
import './member.css';

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
