import { useEffect, useMemo, useRef, useState, type CSSProperties, type ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  Area, AreaChart, CartesianGrid, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis,
} from 'recharts';
import { dashboardApi } from '@/api/endpoints/dashboard';
import type { ChartSeries, DashboardDto } from '@/api/types';
import { useAuth } from '@/auth/AuthContext';
import { ErrorAlert, Loading, PageCard, PageCardHeader } from '@/components/ui';
import {
  IconArrowRight, IconBox, IconCalendar, IconCard, IconChart, IconCheckSquare, IconClock, IconCrown,
  IconMoney, IconPlus, IconRefresh, IconUser, IconUsers, IconWarning,
} from '@/components/icons';
import { money, relativeTime } from '@/lib/format';
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
 * Part-to-whole as a single 100% stacked bar plus a labelled legend — the form the composition
 * actually calls for, and not a pie: these breakdowns run to two or three close categories,
 * which an eye reads badly as angles.
 *
 * The legend is load-bearing rather than decoration. Two slots of the palette (green and orange)
 * sit in the 6–8 ΔE band under deuteranopia, and several sit under 3:1 against the card, so the
 * validator only clears them alongside a second, non-colour channel: every segment is named and
 * numbered here, so nothing is carried by hue alone.
 */
function StackedShare(
  { title, rows, colors, format }:
  {
    title: string; rows: ChartSeries[]; colors: string[];
    /** Payment splits are money; plan counts are just counts. */
    format?: (value: number) => string;
  },
) {
  const total = rows.reduce((sum, row) => sum + Number(row.value || 0), 0);
  const show = (value: number) => (format ? format(value) : String(value));

  if (rows.length === 0 || total === 0) {
    return (
      <div className="share">
        <div className="share-title">{title}</div>
        <div className="dash-bars-empty">Nothing recorded yet.</div>
      </div>
    );
  }

  const parts = rows.map((row, i) => ({
    label: row.label,
    value: Number(row.value || 0),
    color: colors[i % colors.length],
    share: (Number(row.value || 0) / total) * 100,
  }));

  return (
    <div className="share">
      <div className="share-head">
        <span className="share-title">{title}</span>
        <span className="share-total">{show(total)} total</span>
      </div>
      {/* One bar, read left to right. The 2px gaps are the surface doing the separating, so no
          segment needs a stroke around it. */}
      <div
        className="share-bar"
        role="img"
        aria-label={parts.map((p) => `${p.label} ${show(p.value)}, ${Math.round(p.share)}%`).join('; ')}
      >
        {parts.map((part) => (
          <span key={part.label} className="share-seg" style={{ width: `${part.share}%`, background: part.color }} />
        ))}
      </div>
      <ul className="share-legend">
        {parts.map((part) => (
          <li key={part.label}>
            <span className="share-dot" style={{ background: part.color }} />
            <span className="share-label">{part.label}</span>
            <b className="share-value">{show(part.value)}</b>
            <span className="share-pct">{Math.round(part.share)}%</span>
          </li>
        ))}
      </ul>
    </div>
  );
}

/**
 * A single ratio against its total, which is a meter rather than a two-slice pie. The fill
 * carries severity — the further collection slips, the hotter it reads.
 */
function CollectionMeter(
  { collected, outstanding, currency }:
  { collected: number; outstanding: number; currency: string },
) {
  const total = collected + outstanding;
  if (total <= 0) return null;

  const pct = Math.round((collected / total) * 100);
  const tone = pct >= 80 ? 'good' : pct >= 50 ? 'warn' : 'bad';

  return (
    <div className={`meter meter-${tone}`}>
      <div className="meter-head">
        <span className="meter-label">Collected this month</span>
        <b className="meter-value">{money(collected, currency)}</b>
      </div>
      <div className="meter-track" role="img" aria-label={`${pct}% of this month's billing collected`}>
        <div className="meter-fill" style={{ width: `${pct}%` }} />
      </div>
      <div className="meter-foot">
        <span>{pct}% collected</span>
        <span>{money(outstanding, currency)} outstanding</span>
      </div>
    </div>
  );
}

/** Themed tooltip. Recharts' default is a hardcoded white card, which goes blind in dark mode. */
function ChartTooltip(
  { active, payload, label, format }:
  { active?: boolean; payload?: { value?: number | string }[]; label?: string; format: (value: number) => string },
) {
  if (!active || !payload?.length) return null;
  return (
    <div className="chart-tip">
      <div className="chart-tip-label">{label}</div>
      <div className="chart-tip-value">{format(Number(payload[0]?.value ?? 0))}</div>
    </div>
  );
}

/** Two trend charts, deliberately: enough to see a shape without a wall of graphs to decode. */
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

/* Only the type size lives here. The grid stroke and tick fill are painted from tokens in
   admin.css — as literals they stayed light-mode grey and vanished against a dark card. */
const AXIS = { fontSize: 11 };
/* Room for the plot plus the x-axis band beneath it, so the labels are never the thing that
   gets cropped when the card is short. */
const CHART_HEIGHT = 236;

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
          {/* Money over time is a continuous quantity, so it reads as an area: the wash carries
              the volume and the 2px line carries the shape. */}
          <ResponsiveContainer width="100%" height={CHART_HEIGHT}>
            <AreaChart data={revenue} margin={{ top: 10, right: 12, left: 4, bottom: 0 }}>
              <defs>
                <linearGradient id="dash-revenue-fill" x1="0" y1="0" x2="0" y2="1">
                  {/* A wash, not a slab — 18% at the crest, fading out entirely at the baseline. */}
                  <stop offset="0%" stopColor={colors[0]} stopOpacity={0.18} />
                  <stop offset="100%" stopColor={colors[0]} stopOpacity={0} />
                </linearGradient>
              </defs>
              <CartesianGrid className="dash-grid" vertical={false} />
              <XAxis dataKey="label" tick={AXIS} axisLine={false} tickLine={false} minTickGap={18} />
              <YAxis tick={AXIS} axisLine={false} tickLine={false} width={62} />
              <Tooltip
                cursor={{ stroke: colors[0], strokeWidth: 1 }}
                content={<ChartTooltip format={(value) => money(value, currency)} />}
              />
              <Area
                type="monotone"
                dataKey="value"
                stroke={colors[0]}
                strokeWidth={2}
                strokeLinecap="round"
                strokeLinejoin="round"
                fill="url(#dash-revenue-fill)"
                /* Hidden until hovered, then an 8px dot ringed in the card colour so it stays
                   legible where it sits on the line. */
                dot={false}
                activeDot={{ r: 4, strokeWidth: 2, stroke: 'var(--card)' }}
              />
            </AreaChart>
          </ResponsiveContainer>
        </ChartCard>

        <ChartCard
          title="Attendance"
          icon={<IconCalendar size={17} />}
          hasData={data.attendanceTrend.length > 0}
        >
          {/* A line rather than a second area, so the two cards do not read as the same chart
              twice. Slot 4 (violet) also clears 3:1 against the card, which slot 5 does not. */}
          <ResponsiveContainer width="100%" height={CHART_HEIGHT}>
            <LineChart data={data.attendanceTrend} margin={{ top: 10, right: 12, left: 4, bottom: 0 }}>
              <CartesianGrid className="dash-grid" vertical={false} />
              <XAxis dataKey="label" tick={AXIS} axisLine={false} tickLine={false} minTickGap={18} />
              <YAxis tick={AXIS} axisLine={false} tickLine={false} width={34} allowDecimals={false} />
              <Tooltip
                cursor={{ stroke: colors[3], strokeWidth: 1 }}
                content={<ChartTooltip format={(value) => `${value} check-in${value === 1 ? '' : 's'}`} />}
              />
              <Line
                type="monotone"
                dataKey="value"
                stroke={colors[3]}
                strokeWidth={2}
                strokeLinecap="round"
                strokeLinejoin="round"
                dot={false}
                activeDot={{ r: 4, strokeWidth: 2, stroke: 'var(--card)' }}
              />
            </LineChart>
          </ResponsiveContainer>
        </ChartCard>
      </div>

      {/* ------------------------------------------------- attention + breakdowns */}
      <div className="dash-two">
        <PageCard>
          <PageCardHeader icon={<IconWarning size={18} />} title="Needs your attention" />
          <div className="page-card-body">
            <CollectionMeter
              collected={s.monthRevenue}
              outstanding={s.pendingPaymentsAmount}
              currency={currency}
            />
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
            <StackedShare title="Membership plans" rows={data.planDistribution} colors={colors} />
            <StackedShare
              title="Payment methods"
              rows={data.paymentMethodDistribution}
              colors={colors}
              format={(value) => money(value, currency)}
            />
          </div>
        </PageCard>
      </div>

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
