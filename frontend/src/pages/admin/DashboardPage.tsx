import { useEffect, useMemo, useRef, useState, type CSSProperties, type ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis,
} from 'recharts';
import { dashboardApi } from '@/api/endpoints/dashboard';
import type { ChartSeries, DashboardDto } from '@/api/types';
import { useAuth } from '@/auth/AuthContext';
import {
  EmptyState, ErrorAlert, Loading, PageCard, PageCardHeader, Pill, type PillTone,
} from '@/components/ui';
import {
  IconArrowRight, IconBox, IconCalendar, IconCard, IconChart, IconCheckSquare, IconClock, IconCrown,
  IconMoney, IconPhone, IconPlus, IconRefresh, IconUser, IconUsers, IconWarning,
} from '@/components/icons';
import { date, money, relativeTime } from '@/lib/format';
import './admin.css';

/* The palette lives in tokens.css; these are only the fallbacks when the var is unreadable. */
const CHART_FALLBACK = [
  '#4e8cf5', '#22c55e', '#f97316', '#a855f7', '#0ea5e9', '#ec4899', '#f59e0b', '#14b8a6',
];

function readChartColors(): string[] {
  const style = getComputedStyle(document.documentElement);
  return CHART_FALLBACK.map((fallback, i) =>
    style.getPropertyValue(`--chart-${i + 1}`).trim() || fallback);
}

/* ---------------------------------------------------------------------- pieces */

function StatCard(
  { title, value, caption, icon, gradient, tint, action }:
  {
    title: string; value: ReactNode; caption?: ReactNode; icon: ReactNode;
    gradient?: string; tint?: string;
    /** Optional jump-off on the coloured cards, e.g. "Manage" → /admin/members. */
    action?: { label: string; to: string };
  },
) {
  const navigate = useNavigate();
  const cardStyle: CSSProperties | undefined = gradient ? { background: gradient } : undefined;
  const iconStyle: CSSProperties | undefined = gradient
    ? undefined
    : { background: tint ?? 'var(--primary-soft)', color: 'var(--primary-dark)' };

  return (
    <div className={`stat-card ${gradient ? 'grad' : ''} ${action ? 'has-action' : ''}`} style={cardStyle}>
      <div className="stat-icon" style={iconStyle}>{icon}</div>
      <div className="grow">
        <div className="stat-title">{title}</div>
        <div className="stat-value">{value}</div>
        {caption ? <div className="stat-caption">{caption}</div> : null}
        {action ? (
          <button type="button" className="stat-action" onClick={() => navigate(action.to)}>
            {action.label} <IconArrowRight size={13} />
          </button>
        ) : null}
      </div>
    </div>
  );
}

/**
 * A share-of-total breakdown as labelled bars. Replaces the donut and pie charts: with two or
 * three categories a pie is harder to read than the numbers themselves, and the percentage is
 * spelled out rather than left to be judged by eye.
 */
function BarBreakdown(
  { title, rows, colors }: { title: string; rows: ChartSeries[]; colors: string[] },
) {
  const total = rows.reduce((sum, row) => sum + Number(row.value || 0), 0);

  return (
    <div>
      <div className="quick-title" style={{ fontSize: 'var(--fs-md)', marginBottom: 10 }}>{title}</div>
      {rows.length === 0 || total === 0 ? (
        <div className="dash-bars-empty">Nothing recorded yet.</div>
      ) : (
        <div className="dash-bars">
          {rows.map((row, i) => {
            const share = Math.round((Number(row.value) / total) * 100);
            return (
              <div className="dash-bar-row" key={row.label}>
                <div className="dash-bar-head">
                  {row.label}
                  <b>{row.value}</b>
                  <span className="pct">{share}%</span>
                </div>
                <div className="dash-bar">
                  <div
                    className="dash-bar-fill"
                    style={{ width: `${share}%`, background: colors[i % colors.length] }}
                  />
                </div>
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}

/** Two bar charts, deliberately: enough to see a trend without a wall of graphs to decode. */
function ChartCard(
  { title, icon, action, hasData, children }:
  { title: string; icon: ReactNode; action?: ReactNode; hasData: boolean; children: ReactNode },
) {
  return (
    <div className="chart-card">
      <div className="chart-head">
        <div className="chart-title">{icon}{title}</div>
        {action}
      </div>
      {hasData ? children : <div className="chart-empty">No data for this period yet.</div>}
    </div>
  );
}

const AXIS = { fontSize: 11, fill: '#8e97ab' };
const GRID = '#e5e8f0';

/* ------------------------------------------------------------------------ page */

export default function DashboardPage() {
  const navigate = useNavigate();
  const { currency } = useAuth();

  const [data, setData] = useState<DashboardDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<unknown>(null);
  const [reloadKey, setReloadKey] = useState(0);
  const [range, setRange] = useState<'day' | 'week' | 'month'>('day');

  const colors = useMemo(readChartColors, []);
  const alive = useRef(true);
  useEffect(() => {
    alive.current = true;
    return () => { alive.current = false; };
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    let current = true;
    setLoading(true);
    dashboardApi.get(controller.signal)
      .then((res) => { if (current) { setData(res); setError(null); } })
      .catch((err) => { if (current) { setError(err); setData(null); } })
      .finally(() => { if (current) setLoading(false); });
    return () => { current = false; controller.abort(); };
  }, [reloadKey]);

  if (loading) {
    return <div className="page"><PageCard><Loading message="Building the dashboard..." /></PageCard></div>;
  }

  if (!data) {
    return (
      <div className="page">
        <PageCard>
          <PageCardHeader icon={<IconChart size={20} />} title="Dashboard" />
          <div className="page-card-body"><ErrorAlert error={error} /></div>
        </PageCard>
      </div>
    );
  }

  const s = data.stats;
  const revenue = range === 'day' ? data.revenueDaily : range === 'week' ? data.revenueWeekly : data.revenueMonthly;

  // Only rows that actually need doing, so an empty list genuinely means "nothing outstanding".
  const attention = [
    s.pendingPaymentsCount > 0 && {
      icon: <IconMoney size={16} />, tint: 'var(--warning-soft)', ink: '#a86a06',
      text: `${s.pendingPaymentsCount} payment${s.pendingPaymentsCount === 1 ? '' : 's'} awaiting collection`,
      sub: `${money(s.pendingPaymentsAmount, currency)} outstanding`,
      cta: 'Collect', to: '/admin/payments',
    },
    s.expiringSoon > 0 && {
      icon: <IconClock size={16} />, tint: 'var(--warning-soft)', ink: '#a86a06',
      text: `${s.expiringSoon} membership${s.expiringSoon === 1 ? '' : 's'} expiring soon`,
      sub: 'Inside the renewal reminder window',
      cta: 'Renew', to: '/admin/subscriptions',
    },
    s.expiredMemberships > 0 && {
      icon: <IconWarning size={16} />, tint: 'var(--danger-soft)', ink: '#b02a37',
      text: `${s.expiredMemberships} membership${s.expiredMemberships === 1 ? '' : 's'} already expired`,
      sub: 'These members can no longer check in',
      cta: 'Review', to: '/admin/subscriptions',
    },
    s.unreadNotifications > 0 && {
      icon: <IconBox size={16} />, tint: 'var(--info-soft)', ink: '#0b6f96',
      text: `${s.unreadNotifications} unread notification${s.unreadNotifications === 1 ? '' : 's'}`,
      sub: 'Alerts raised by the system',
      cta: 'Open', to: '/admin/notifications',
    },
  ].filter(Boolean) as {
    icon: ReactNode; tint: string; ink: string;
    text: string; sub: string; cta: string; to: string;
  }[];

  return (
    <div className="page dash-stack">
      {error ? <ErrorAlert error={error} /> : null}

      {/* ------------------------------------------------------------- masthead */}
      <section className="dash-hero">
        <div className="grow">
          <div className="dash-hero-eyebrow">Admin Dashboard</div>
          <h1 className="dash-hero-title"><IconChart size={26} /> Overview &amp; Controls</h1>
          <p className="dash-hero-text">
            Manage members, memberships, registrations, payments, attendance and more.
          </p>
        </div>
        <div className="dash-hero-actions">
          <button className="btn btn-on-hero" onClick={() => navigate('/admin/members/new')}>
            <IconPlus size={15} /> Add Member
          </button>
          <button className="btn btn-hero-outline" onClick={() => navigate('/admin/payments/new')}>
            <IconCard size={15} /> Record Payment
          </button>
        </div>
      </section>

      {/* --------------------------------------------------------------- KPI row */}
      <div className="stat-grid">
        <StatCard
          title="Total Members"
          value={s.totalMembers}
          caption={`${s.memberGrowthPercent >= 0 ? '+' : ''}${s.memberGrowthPercent}% vs last month`}
          icon={<IconUsers size={22} />}
          gradient="var(--grad-blue)"
          action={{ label: 'Manage', to: '/admin/members' }}
        />
        <StatCard
          title="Active Members"
          value={s.activeMembers}
          caption={`${s.inactiveMembers} inactive`}
          icon={<IconCheckSquare size={22} />}
          gradient="var(--grad-green)"
          action={{ label: 'Plans', to: '/admin/plans' }}
        />
        <StatCard
          title="Expired Memberships"
          value={s.expiredMemberships}
          caption={`${s.expiringSoon} expiring soon`}
          icon={<IconWarning size={22} />}
          gradient="var(--grad-orange)"
          action={{ label: 'View', to: '/admin/subscriptions' }}
        />
        <StatCard
          title="Today's Revenue"
          value={money(s.todayRevenue, currency)}
          caption={`${money(s.monthRevenue, currency)} this month`}
          icon={<IconMoney size={22} />}
          gradient="var(--grad-cyan)"
          action={{ label: 'Payments', to: '/admin/payments' }}
        />

      </div>

      {/* ------------------------------------------------------------- mini metrics */}
      <div className="dash-metrics">
        {[
          { label: "Today's attendance", icon: <IconCalendar size={12} />, value: s.todayAttendance, sub: `${s.currentlyInGym} in the gym now` },
          { label: 'Month revenue', icon: <IconMoney size={12} />, value: money(s.monthRevenue, currency), sub: `${s.revenueGrowthPercent >= 0 ? '+' : ''}${s.revenueGrowthPercent}% vs last month` },
          { label: 'Month expenses', icon: <IconCard size={12} />, value: money(s.monthExpenses, currency), sub: `Net ${money(s.monthNetIncome, currency)}` },
          { label: 'Pending payments', icon: <IconWarning size={12} />, value: money(s.pendingPaymentsAmount, currency), sub: `${s.pendingPaymentsCount} to collect` },
          { label: 'Subscriptions', icon: <IconCrown size={12} />, value: s.activeSubscriptions, sub: `${s.frozenSubscriptions} frozen` },
          { label: 'Expiring soon', icon: <IconClock size={12} />, value: s.expiringSoon, sub: 'Within the reminder window' },
        ].map((metric) => (
          <div className="dash-metric" key={metric.label}>
            <div className="dash-metric-label">{metric.icon} {metric.label}</div>
            <div className="dash-metric-value">{metric.value}</div>
            <div className="dash-metric-sub">{metric.sub}</div>
          </div>
        ))}
      </div>

      {/* ---------------------------------------------------------- quick access */}
      <section>
        <div className="quick-head">
          <div className="quick-title"><IconBox size={18} /> Quick Access</div>
          <span className="quick-hint">Jump to frequently used modules.</span>
        </div>
        <div className="quick-grid">
          {[
            { to: '/admin/members', title: 'Manage Members', text: 'Add, edit, search', icon: <IconUsers size={19} />, tint: 'var(--info-soft)' },
            { to: '/admin/plans', title: 'Membership Plans', text: 'Fee, duration', icon: <IconCrown size={19} />, tint: 'var(--warning-soft)' },
            { to: '/admin/trainers', title: 'Trainers', text: 'Profiles, shifts', icon: <IconUser size={19} />, tint: 'var(--primary-soft)' },
            { to: '/admin/payments', title: 'Payments', text: 'Paid / Pending', icon: <IconCard size={19} />, tint: 'var(--success-soft)' },
            { to: '/admin/attendance', title: 'Attendance', text: 'Check-ins today', icon: <IconCalendar size={19} />, tint: 'var(--info-soft)' },
            { to: '/admin/reports', title: 'Reports', text: 'Export, analyse', icon: <IconChart size={19} />, tint: 'var(--primary-soft)' },
          ].map((tile) => (
            <button type="button" className="quick-tile" key={tile.to} onClick={() => navigate(tile.to)}>
              <span className="quick-tile-icon" style={{ background: tile.tint, color: 'var(--primary-dark)' }}>
                {tile.icon}
              </span>
              <span className="grow">
                <span className="quick-tile-title">{tile.title}</span>
                <span className="quick-tile-text">{tile.text}</span>
              </span>
              <IconArrowRight size={16} className="quick-tile-chevron" />
            </button>
          ))}
        </div>
      </section>


      {/* ------------------------------------------------------------ two charts */}
      <div className="dash-two">
        <ChartCard
          title="Revenue"
          icon={<IconMoney size={17} />}
          hasData={revenue.length > 0}
          action={(
            <div className="range-seg" role="group" aria-label="Revenue period">
              {(['day', 'week', 'month'] as const).map((key) => (
                <button
                  key={key}
                  className={range === key ? 'on' : ''}
                  onClick={() => setRange(key)}
                >
                  {key === 'day' ? 'Day' : key === 'week' ? 'Week' : 'Month'}
                </button>
              ))}
            </div>
          )}
        >
          <ResponsiveContainer width="100%" height={230}>
            <BarChart data={revenue} margin={{ top: 8, right: 12, left: 4, bottom: 0 }}>
              <CartesianGrid stroke={GRID} strokeDasharray="3 3" vertical={false} />
              <XAxis dataKey="label" tick={AXIS} axisLine={false} tickLine={false} />
              <YAxis tick={AXIS} axisLine={false} tickLine={false} width={62} />
              <Tooltip formatter={(value) => money(Number(value), currency)} />
              <Bar dataKey="value" fill={colors[0]} radius={[5, 5, 0, 0]} maxBarSize={34} />
            </BarChart>
          </ResponsiveContainer>
        </ChartCard>

        <ChartCard
          title="Attendance"
          icon={<IconCalendar size={17} />}
          hasData={data.attendanceTrend.length > 0}
        >
          <ResponsiveContainer width="100%" height={230}>
            <BarChart data={data.attendanceTrend} margin={{ top: 8, right: 12, left: 4, bottom: 0 }}>
              <CartesianGrid stroke={GRID} strokeDasharray="3 3" vertical={false} />
              <XAxis dataKey="label" tick={AXIS} axisLine={false} tickLine={false} />
              <YAxis tick={AXIS} axisLine={false} tickLine={false} width={34} allowDecimals={false} />
              <Tooltip />
              <Bar dataKey="value" fill={colors[4]} radius={[5, 5, 0, 0]} maxBarSize={34} />
            </BarChart>
          </ResponsiveContainer>
        </ChartCard>
      </div>

      {/* ------------------------------------------------- attention + breakdowns */}
      <div className="dash-two">
        <PageCard>
          <PageCardHeader icon={<IconWarning size={18} />} title="Needs your attention" />
          <div className="page-card-body">
            {attention.length === 0 ? (
              <div className="dash-attn-clear">
                <IconCheckSquare size={17} /> Nothing outstanding — the desk is clear.
              </div>
            ) : (
              <div className="dash-attn">
                {attention.map((item) => (
                  <div className="dash-attn-row" key={item.text}>
                    <span className="dash-attn-icon" style={{ background: item.tint, color: item.ink }}>
                      {item.icon}
                    </span>
                    <span>
                      <span className="dash-attn-text">{item.text}</span>
                      <span className="dash-attn-sub">{item.sub}</span>
                    </span>
                    <button className="btn btn-outline btn-sm" onClick={() => navigate(item.to)}>
                      {item.cta} <IconArrowRight size={13} />
                    </button>
                  </div>
                ))}
              </div>
            )}
          </div>
        </PageCard>

        <PageCard>
          <PageCardHeader
            icon={<IconCrown size={18} />}
            title="Where members are"
            subtitle="Plans in use, and how people pay."
          />
          <div className="page-card-body">
            <BarBreakdown title="Membership plans" rows={data.planDistribution} colors={colors} />
            <div style={{ height: 18 }} />
            <BarBreakdown title="Payment methods" rows={data.paymentMethodDistribution} colors={colors} />
          </div>
        </PageCard>
      </div>

      {/* -------------------------------------------------------------- expiring soon */}
      <PageCard>
        <PageCardHeader
          icon={<IconWarning size={18} />}
          title="Expiring Soon"
          subtitle="Memberships that need a renewal call."
          actions={
            <button className="btn btn-outline" onClick={() => navigate('/admin/members')}>
              View all members
            </button>
          }
        />
        {data.expiringSoonMembers.length === 0 ? (
          <EmptyState icon={<IconCheckSquare size={36} />} title="Nothing expiring soon" />
        ) : (
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th className="idx">#</th>
                  <th>Member</th>
                  <th>Phone</th>
                  <th>Plan</th>
                  <th>Ends</th>
                  <th>Days left</th>
                  <th className="num">Outstanding</th>
                </tr>
              </thead>
              <tbody>
                {data.expiringSoonMembers.map((member, i) => {
                  const days = member.daysRemaining;
                  const tone: PillTone = days === null || days === undefined
                    ? 'neutral' : days < 0 ? 'danger' : days <= 7 ? 'warning' : 'success';
                  return (
                    <tr
                      key={member.id}
                      className="row-link"
                      onDoubleClick={() => navigate(`/admin/members/${member.id}`)}
                    >
                      <td className="idx">{i + 1}</td>
                      <td>
                        <div className="cell-icon">
                          <IconUser size={14} />
                          <span className="cell-main">{member.fullName}</span>
                        </div>
                        <div className="cell-sub">{member.memberCode}</div>
                      </td>
                      <td><span className="cell-icon"><IconPhone size={13} />{member.phone}</span></td>
                      <td>
                        {member.currentPlanName
                          ? <Pill tone="primary">{member.currentPlanName}</Pill>
                          : <span className="muted">&mdash;</span>}
                      </td>
                      <td>
                        <span className="cell-icon">
                          <IconCalendar size={13} />{date(member.subscriptionEndDate)}
                        </span>
                      </td>
                      <td>
                        {days === null || days === undefined
                          ? <span className="muted">&mdash;</span>
                          : <Pill tone={tone}>{days < 0 ? `${Math.abs(days)} overdue` : `${days} days`}</Pill>}
                      </td>
                      <td className="num">{money(member.outstandingAmount, currency)}</td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </PageCard>

      <div className="row" style={{ justifyContent: 'center' }}>
        <span className="muted" style={{ fontSize: 'var(--fs-sm)' }}>
          Generated {relativeTime(data.generatedAtUtc)}
        </span>
        <button className="btn btn-outline btn-sm" onClick={() => setReloadKey((k) => k + 1)}>
          <IconRefresh size={14} /> Refresh
        </button>
      </div>
    </div>
  );
}
