/**
 * Member communications — tracking and history for the direct messages members receive.
 *
 * Two tabs. "Tracking" is the single place where all outbound messaging across the app is
 * counted, laid out as a dashboard: a gradient KPI row, then panels whose contents are grids of
 * cards — per-stream cards with a channel-mix bar, channel status, the occasion grid and the
 * festival calendar. "History" is the filtered, server-paged log of every message, in the same
 * shape as the audit trail.
 *
 * Every count drills through to its own detail, and each one goes where its rows actually live.
 * That distinction is load-bearing: the usage counters come from MemberNotificationLogs, which is
 * exactly what the History tab pages through, but the tracking totals sum four different tables —
 * occasions, expiry reminders, pay links and in-app notifications. Sending the tracking totals to
 * History would show a number and then a list that disagrees with it, so those open a breakdown
 * instead, and each stream links to the screen that owns its rows.
 */

import { useCallback, useEffect, useMemo, useState, type ComponentType, type ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  Alert, EmptyState, ErrorAlert, Field, FilterField, FilterMenu, Loading, Modal,
  PageCard, PageCardHeader, Pager, Pill, type PillTone,
} from '@/components/ui';
import {
  IconArrowRight, IconBell, IconCalendar, IconChart, IconCheck, IconClock, IconCrown, IconFile,
  IconFlame, IconInfo, IconMail, IconMessage, IconMoney, IconPhone, IconRefresh, IconSun, IconUsers,
} from '@/components/icons';
import {
  COMMUNICATION_KINDS, communicationsApi,
  type CommunicationChannel, type CommunicationChannelsDto, type CommunicationKind,
  type CommunicationLogDto, type CommunicationUsageDto, type CommunicationsQuery,
  type FestivalDto, type MessageTrackingDto, type OccasionChannelsDto, type TestWhatsAppResultDto,
  type TrackingStreamDto,
} from '@/api/endpoints/communications';
import type { PagedResult } from '@/api/types';
import { useAuth } from '@/auth/AuthContext';
import { date, words } from '@/lib/format';
import './communications.css';

/* ------------------------------------------------------------- shared bits */

/** Icons are stored as components, not elements, so each surface renders them at its own size. */
type IconComp = ComponentType<{ size?: number }>;

const KIND_META: Record<CommunicationKind, { tone: PillTone; Icon: IconComp }> = {
  PaymentReceived: { tone: 'success', Icon: IconMoney },
  RenewalPayment:  { tone: 'info',    Icon: IconRefresh },
  DietPlan:        { tone: 'primary', Icon: IconFile },
  StreakMilestone: { tone: 'warning', Icon: IconFlame },
  Birthday:        { tone: 'primary', Icon: IconCrown },
  Festival:        { tone: 'info',    Icon: IconSun },
};

/** Occasion pill with its small icon. Shared with the member details timeline. */
export function KindPill({ kind }: { kind: CommunicationKind | string }) {
  const meta = KIND_META[kind as CommunicationKind];
  return (
    <Pill tone={meta?.tone ?? 'neutral'}>
      <span className="comm-kind">{meta ? <meta.Icon size={12} /> : null}{words(kind)}</span>
    </Pill>
  );
}

/** Email / WhatsApp badges — filled when the message actually went out on that channel. */
export function ChannelTicks({ emailSent, whatsAppSent }: { emailSent: boolean; whatsAppSent: boolean }) {
  return (
    <span className="comm-ticks">
      <span className={`comm-tick ${emailSent ? 'on' : ''}`} title={emailSent ? 'Sent by email' : 'Not sent by email'}>
        {emailSent ? <IconCheck size={12} /> : null}<IconMail size={13} /> Email
      </span>
      <span className={`comm-tick ${whatsAppSent ? 'on' : ''}`} title={whatsAppSent ? 'Sent on WhatsApp' : 'Not sent on WhatsApp'}>
        {whatsAppSent ? <IconCheck size={12} /> : null}<IconMessage size={13} /> WhatsApp
      </span>
    </span>
  );
}

/**
 * Channel and provider naming. The API returns raw keys — `email`, `whatsapp`, `Smtp`, `File` —
 * and `words()` cannot capitalise them, so a lowercase "whatsapp" was reaching the screen.
 */
const CHANNEL_LABEL: Record<string, string> = {
  email: 'Email', sms: 'SMS', whatsapp: 'WhatsApp', push: 'Push', inapp: 'In-app',
};

function channelLabel(raw: string): string {
  const key = (raw ?? '').toLowerCase();
  return CHANNEL_LABEL[key] ?? words(raw).replace(/^./, (c) => c.toUpperCase());
}

/**
 * What the provider actually means for delivery. "File" is the local drop folder: messages are
 * written to disk and never transmitted. Reporting that as "Live" tells an administrator their
 * members are being messaged when not one message has left the building.
 */
function providerLabel(raw: string): string {
  switch ((raw ?? '').toLowerCase()) {
    case 'none': return 'Not configured';
    case 'file': return 'File drop — nothing is sent';
    case 'smtp': return 'SMTP';
    case 'http': return 'HTTP gateway';
    default: return raw || 'Unknown';
  }
}

/** `true` when the provider only writes to disk, so the card can say so rather than claim "Live". */
function isDryRun(provider: string): boolean {
  return (provider ?? '').toLowerCase() === 'file';
}

/* -------------------------------------------------------------- tracking UI */

/** A gradient KPI tile, matching the dashboard's so both screens open the same way. */
function Kpi({ icon, gradient, label, value, sub, onClick, hint }: {
  icon: ReactNode; gradient: string; label: string; value: number; sub?: string;
  onClick?: () => void; hint?: string;
}) {
  const inner = (
    <>
      <span className="comm-kpi-icon">{icon}</span>
      <span className="comm-kpi-text grow">
        <span className="comm-kpi-label">{label}</span>
        <span className="comm-kpi-value">{value.toLocaleString()}</span>
        {sub ? <span className="comm-kpi-sub">{sub}</span> : null}
      </span>
      {onClick ? <span className="comm-kpi-go" aria-hidden="true"><IconArrowRight size={16} /></span> : null}
    </>
  );

  if (!onClick) {
    return <div className="comm-kpi" style={{ background: gradient }}>{inner}</div>;
  }

  return (
    <button
      type="button"
      className="comm-kpi is-clickable"
      style={{ background: gradient }}
      onClick={onClick}
      title={hint}
    >
      {inner}
    </button>
  );
}

/** A titled card. Every block of the tracking tab sits in one, rather than floating on the page. */
function Panel({ icon, title, caption, children }: {
  icon: ReactNode; title: string; caption?: string; children: ReactNode;
}) {
  return (
    <section className="comm-panel">
      <div className="comm-panel-head">
        <span className="comm-panel-icon">{icon}</span>
        <div className="grow">
          <div className="comm-panel-title">{title}</div>
          {caption ? <div className="comm-panel-caption">{caption}</div> : null}
        </div>
      </div>
      <div className="comm-panel-body">{children}</div>
    </section>
  );
}

/** The channel-mix segments in fixed order, each with its legend label and colour class. */
const MIX_CHANNELS = [
  { key: 'email',    label: 'Email' },
  { key: 'sms',      label: 'SMS' },
  { key: 'whatsApp', label: 'WhatsApp' },
  { key: 'inApp',    label: 'In-app' },
] as const;

function StreamCard({ stream, onOpen, openLabel }: {
  stream: TrackingStreamDto; onOpen?: () => void; openLabel?: string;
}) {
  const parts = MIX_CHANNELS
    .map((c) => ({ ...c, count: stream[c.key] }))
    .filter((c) => c.count > 0);
  const total = parts.reduce((sum, c) => sum + c.count, 0);

  const body = (
    <>
      <span className="comm-stream-head">
        <span className="grow">
          <span className="comm-stream-name">{words(stream.stream)}</span>
          <span className="comm-stream-desc">{stream.description}</span>
        </span>
        <span className="comm-stream-nums">
          <span className="comm-stream-num">
            <span className="comm-stream-num-value">{stream.today}</span>
            <span className="comm-stream-num-label">Today</span>
          </span>
          <span className="comm-stream-num lead">
            <span className="comm-stream-num-value">{stream.thisMonth}</span>
            <span className="comm-stream-num-label">This month</span>
          </span>
        </span>
      </span>

      {stream.thisMonth <= 0 || total === 0 ? (
        <span className="comm-stream-empty">Nothing sent yet this month.</span>
      ) : (
        <>
          <span className="comm-mix-bar" role="img" aria-label="Channel mix this month">
            {parts.map((c) => (
              <span
                key={c.key}
                className={`comm-mix-seg comm-mix-${c.key.toLowerCase()}`}
                style={{ width: `${(c.count / total) * 100}%` }}
              />
            ))}
          </span>
          <span className="comm-legend">
            {parts.map((c) => (
              <span key={c.key} className="comm-legend-chip">
                <span className={`comm-legend-dot comm-mix-${c.key.toLowerCase()}`} />
                {c.label} · {c.count}
              </span>
            ))}
          </span>
        </>
      )}

      {onOpen ? (
        <span className="comm-stream-go">{openLabel ?? 'View detail'} <IconArrowRight size={14} /></span>
      ) : null}
    </>
  );

  if (!onOpen) return <div className="comm-stream">{body}</div>;

  return (
    <button type="button" className="comm-stream is-clickable" onClick={onOpen}>
      {body}
    </button>
  );
}

/**
 * One occasion as a card. This was a three-column table of ticks, which is the least scannable
 * way to answer the only question anyone brings to it — is this switched on, and where does it
 * go? — and it could not reflow to a phone.
 */
function OccasionCard({ occasion, onOpen }: { occasion: OccasionChannelsDto; onOpen: () => void }) {
  const meta = KIND_META[occasion.kind];
  const tone = meta?.tone ?? 'neutral';
  const silent = !occasion.email && !occasion.whatsApp;

  return (
    <button
      type="button"
      className={`comm-occ is-clickable ${silent ? 'off' : ''}`}
      onClick={onOpen}
      title={`Show every ${words(occasion.kind).toLowerCase()} message sent this month`}
    >
      <span className="comm-occ-head">
        <span className={`comm-occ-icon tone-${tone}`}>{meta ? <meta.Icon size={17} /> : null}</span>
        <span className="comm-occ-name grow">{words(occasion.kind)}</span>
        <span className="comm-occ-go" aria-hidden="true"><IconArrowRight size={14} /></span>
      </span>
      <span className="comm-occ-chans">
        <span className={`comm-occ-chan ${occasion.email ? 'on' : ''}`}>
          {occasion.email ? <IconCheck size={12} /> : null}<IconMail size={13} /> Email
        </span>
        <span className={`comm-occ-chan ${occasion.whatsApp ? 'on' : ''}`}>
          {occasion.whatsApp ? <IconCheck size={12} /> : null}<IconMessage size={13} /> WhatsApp
        </span>
      </span>
    </button>
  );
}

/** Midnight today, the reference point for every "is this festival still coming?" comparison. */
function startOfToday(): Date {
  const d = new Date();
  d.setHours(0, 0, 0, 0);
  return d;
}

/** "Today" / "In 12 days" / "3 days ago" — the fact an operator actually wants off this list. */
function relativeDay(value: string, today: Date): string | null {
  const d = new Date(value);
  if (Number.isNaN(d.getTime())) return null;
  d.setHours(0, 0, 0, 0);

  const days = Math.round((d.getTime() - today.getTime()) / 86_400_000);
  if (days === 0) return 'Today';
  if (days === 1) return 'Tomorrow';
  if (days === -1) return 'Yesterday';
  return days > 0 ? `In ${days} days` : `${Math.abs(days)} days ago`;
}

function FestivalCard({ festival, today, isNext, onOpen }: {
  festival: FestivalDto; today: Date; isNext: boolean; onOpen: () => void;
}) {
  const parsed = new Date(festival.date);
  const valid = !Number.isNaN(parsed.getTime());
  // Normalised to midnight so a festival dated this morning still counts as today, not as past.
  const midnight = valid ? new Date(parsed).setHours(0, 0, 0, 0) : 0;
  const past = valid && midnight < today.getTime();
  const when = relativeDay(festival.date, today);

  return (
    <button
      type="button"
      className={`comm-fest is-clickable ${isNext ? 'next' : ''} ${past ? 'past' : ''}`}
      onClick={onOpen}
      title="Show the festival wishes sent this month"
    >
      <span className="comm-fest-badge">
        <span className="comm-fest-day">{valid ? parsed.getDate() : '—'}</span>
        <span className="comm-fest-mon">
          {valid ? parsed.toLocaleDateString('en-GB', { month: 'short' }) : ''}
        </span>
      </span>
      <span className="comm-fest-text grow">
        <span className="comm-fest-name">
          {festival.name}
          {isNext ? <span className="comm-fest-next-tag">Next up</span> : null}
        </span>
        {festival.greeting ? <span className="comm-fest-greeting">“{festival.greeting}”</span> : null}
        <span className="comm-fest-when">{date(festival.date)}{when ? ` · ${when}` : ''}</span>
      </span>
    </button>
  );
}

/** Two initials for the history table's disc, so the eye can track a member down the column. */
function initials(name: string): string {
  const parts = (name ?? '').trim().split(/\s+/).filter(Boolean);
  if (parts.length === 0) return '?';
  const first = parts[0][0];
  const last = parts.length > 1 ? parts[parts.length - 1][0] : '';
  return (first + last).toUpperCase();
}

/**
 * Where a tracking stream's rows actually live. Occasions are the only stream the History tab
 * pages through; the rest are counted from their own tables, so each points at the screen that
 * owns them and a stream with no listing screen simply is not clickable.
 */
const STREAM_TARGET: Record<string, { label: string; route?: string; history?: boolean; note: string }> = {
  Occasions: {
    label: 'Open in History', history: true,
    note: 'Receipts, diet plans, streaks and wishes — every row is in the History tab.',
  },
  InApp: {
    label: 'Open notifications', route: '/admin/notifications',
    note: 'Bell notifications, counted from the notifications table.',
  },
  PayLinks: {
    label: 'Open payments', route: '/admin/payments',
    note: 'Tap-to-pay UPI links, counted from payment requests.',
  },
  RenewalReminders: {
    label: '', note: 'Daily expiry reminders, counted from the reminder log. No listing screen yet.',
  },
};

/* --------------------------------------------------------------------- page */

const EMPTY_TEST: { phone: string; busy: boolean; result: TestWhatsAppResultDto | null; error: unknown } =
  { phone: '', busy: false, result: null, error: null };

/** A local date as `yyyy-mm-dd`, the format the date inputs and the query string both want. */
function isoDay(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

export default function CommunicationsPage() {
  const { can } = useAuth();
  const navigate = useNavigate();
  const manage = can('notifications.manage');

  const [tab, setTab] = useState<'tracking' | 'history'>('tracking');
  const [notice, setNotice] = useState<string | null>(null);
  const [actionError, setActionError] = useState<unknown>(null);
  const [reloadKey, setReloadKey] = useState(0);
  const reload = useCallback(() => setReloadKey((k) => k + 1), []);

  /* ------------------------------------------------------------- tracking */
  const [usage, setUsage] = useState<CommunicationUsageDto | null>(null);
  const [channels, setChannels] = useState<CommunicationChannelsDto | null>(null);
  const [festivals, setFestivals] = useState<FestivalDto[]>([]);
  const [tracking, setTracking] = useState<MessageTrackingDto | null>(null);
  const [trackingLoading, setTrackingLoading] = useState(true);
  const [trackingError, setTrackingError] = useState<unknown>(null);

  useEffect(() => {
    const ctrl = new AbortController();
    setTrackingLoading(true);
    setTrackingError(null);
    Promise.all([
      communicationsApi.usage(ctrl.signal),
      communicationsApi.channels(ctrl.signal),
      communicationsApi.festivals(ctrl.signal),
      communicationsApi.tracking(ctrl.signal),
    ])
      .then(([usageDto, channelsDto, festivalList, trackingDto]) => {
        if (ctrl.signal.aborted) return;
        setUsage(usageDto);
        setChannels(channelsDto);
        setFestivals(festivalList);
        setTracking(trackingDto);
      })
      .catch((err) => { if (!ctrl.signal.aborted) setTrackingError(err); })
      .finally(() => { if (!ctrl.signal.aborted) setTrackingLoading(false); });
    return () => ctrl.abort();
  }, [reloadKey]);

  // Sorted here rather than trusted from the server, because "next up" is only meaningful on an
  // ordered list and a mis-ordered one would decorate the wrong card.
  const today = useMemo(startOfToday, [reloadKey]);
  const monthStartIso = useMemo(() => isoDay(new Date(today.getFullYear(), today.getMonth(), 1)), [today]);
  const todayIso = useMemo(() => isoDay(today), [today]);

  const sortedFestivals = useMemo(
    () => [...festivals].sort((a, b) => new Date(a.date).getTime() - new Date(b.date).getTime()),
    [festivals],
  );
  const nextFestivalKey = useMemo(() => {
    const upcoming = sortedFestivals.find((f) => {
      const d = new Date(f.date);
      return !Number.isNaN(d.getTime()) && d.setHours(0, 0, 0, 0) >= today.getTime();
    });
    return upcoming?.key ?? null;
  }, [sortedFestivals, today]);

  /* -------------------------------------------------------------- history */
  const [kind, setKind] = useState<CommunicationKind | ''>('');
  const [channel, setChannel] = useState<CommunicationChannel | ''>('');
  const [memberIdText, setMemberIdText] = useState('');
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');

  const [query, setQuery] = useState<CommunicationsQuery>({ pageNumber: 1, pageSize: 25 });
  const [page, setPage] = useState<PagedResult<CommunicationLogDto> | null>(null);
  const [historyLoading, setHistoryLoading] = useState(true);
  const [historyError, setHistoryError] = useState<unknown>(null);

  useEffect(() => {
    if (tab !== 'history') return;
    const ctrl = new AbortController();
    setHistoryLoading(true);
    setHistoryError(null);
    communicationsApi.paged(query, ctrl.signal)
      .then((result) => { if (!ctrl.signal.aborted) setPage(result); })
      .catch((err) => { if (!ctrl.signal.aborted) setHistoryError(err); })
      .finally(() => { if (!ctrl.signal.aborted) setHistoryLoading(false); });
    return () => ctrl.abort();
  }, [tab, query, reloadKey]);

  function applyFilters() {
    const parsedMemberId = Number(memberIdText.trim());
    setQuery((q) => ({
      ...q,
      pageNumber: 1,
      kind,
      channel,
      memberId: memberIdText.trim() && Number.isFinite(parsedMemberId) ? parsedMemberId : '',
      from: from || undefined,
      to: to || undefined,
    }));
  }

  function resetFilters() {
    setKind(''); setChannel(''); setMemberIdText(''); setFrom(''); setTo('');
    setQuery({ pageNumber: 1, pageSize: query.pageSize ?? 25 });
  }

  /**
   * Drill from a count into the rows behind it. The filter inputs are set as well as the query,
   * so the filter menu opens showing what the table is actually filtered by — a table narrowed by
   * controls that read "All occasions" is the kind of thing that gets called a bug.
   */
  const drillIntoHistory = useCallback((next: {
    kind?: CommunicationKind | ''; channel?: CommunicationChannel | '';
    from?: string; to?: string; label: string;
  }) => {
    const nextKind = next.kind ?? '';
    const nextChannel = next.channel ?? '';
    const nextFrom = next.from ?? '';
    const nextTo = next.to ?? '';

    setKind(nextKind);
    setChannel(nextChannel);
    setMemberIdText('');
    setFrom(nextFrom);
    setTo(nextTo);
    setQuery((q) => ({
      ...q,
      pageNumber: 1,
      kind: nextKind,
      channel: nextChannel,
      memberId: '',
      from: nextFrom || undefined,
      to: nextTo || undefined,
    }));
    setDrillLabel(next.label);
    setBreakdownOpen(false);
    setTab('history');
  }, []);

  /** What the History tab was narrowed to, so the user can see it and clear it in one click. */
  const [drillLabel, setDrillLabel] = useState<string | null>(null);

  function clearDrill() {
    setDrillLabel(null);
    resetFilters();
  }

  // Badge on the trigger, read from the applied query so it reflects what the table shows.
  const activeFilterCount = (['kind', 'channel', 'memberId', 'from', 'to'] as const)
    .filter((key) => {
      const value = query[key];
      return value !== undefined && value !== '';
    }).length;

  const rows = page?.items ?? [];
  const pageNumber = page?.pageNumber ?? query.pageNumber ?? 1;
  const pageSize = page?.pageSize ?? query.pageSize ?? 25;

  /* -------------------------------------------------------------- actions */
  const [wishesBusy, setWishesBusy] = useState(false);
  const [testOpen, setTestOpen] = useState(false);
  const [test, setTest] = useState(EMPTY_TEST);
  const [breakdownOpen, setBreakdownOpen] = useState<false | 'today' | 'month'>(false);

  async function sendWishes() {
    setWishesBusy(true);
    setNotice(null);
    setActionError(null);
    try {
      const count = await communicationsApi.sendWishes();
      setNotice(typeof count === 'number'
        ? `Today's wishes sent to ${count} member${count === 1 ? '' : 's'}.`
        : "Today's wishes have been sent.");
      reload();
    } catch (err) {
      setActionError(err);
    } finally {
      setWishesBusy(false);
    }
  }

  async function runWhatsAppTest() {
    setTest((t) => ({ ...t, busy: true, result: null, error: null }));
    try {
      const result = await communicationsApi.testWhatsApp(test.phone.trim());
      setTest((t) => ({ ...t, busy: false, result }));
    } catch (err) {
      setTest((t) => ({ ...t, busy: false, error: err }));
    }
  }

  return (
    <div className="page">
      <PageCard>
        <PageCardHeader
          icon={<IconMessage size={20} />}
          title="Member Communications"
          subtitle="The direct messages members receive — payment receipts, diet plans, streak milestones, birthday and festival wishes."
          actions={(
            <>
              {tab === 'history' && (
                <FilterMenu activeCount={activeFilterCount} onApply={applyFilters} onReset={resetFilters}>
                  <FilterField label="Occasion">
                    <select
                      className="select"
                      value={kind}
                      onChange={(e) => setKind(e.target.value as CommunicationKind | '')}
                    >
                      <option value="">All occasions</option>
                      {COMMUNICATION_KINDS.map((k) => <option key={k} value={k}>{words(k)}</option>)}
                    </select>
                  </FilterField>

                  <FilterField label="Channel">
                    <select
                      className="select"
                      value={channel}
                      onChange={(e) => setChannel(e.target.value as CommunicationChannel | '')}
                    >
                      <option value="">All channels</option>
                      <option value="email">Email</option>
                      <option value="whatsapp">WhatsApp</option>
                    </select>
                  </FilterField>

                  <FilterField label="Member id">
                    <input
                      className="input"
                      inputMode="numeric"
                      placeholder="All members"
                      value={memberIdText}
                      onChange={(e) => setMemberIdText(e.target.value)}
                    />
                  </FilterField>

                  <FilterField label="From">
                    <input className="input" type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
                  </FilterField>

                  <FilterField label="To">
                    <input className="input" type="date" value={to} onChange={(e) => setTo(e.target.value)} />
                  </FilterField>
                </FilterMenu>
              )}

              {manage && (
                <button className="btn btn-dark" onClick={sendWishes} disabled={wishesBusy}>
                  <IconBell size={15} /> {wishesBusy ? 'Sending…' : "Send today's wishes"}
                </button>
              )}
              {manage && (
                <button className="btn btn-outline" onClick={() => { setTest(EMPTY_TEST); setTestOpen(true); }}>
                  <IconPhone size={15} /> Test WhatsApp
                </button>
              )}
              <button className="btn btn-outline" onClick={reload}><IconRefresh size={15} /> Refresh</button>
            </>
          )}
        />

        <div className="comm-tabbar">
          <div className="comm-tabs" role="tablist">
            {([['tracking', 'Tracking'], ['history', 'History']] as const).map(([key, label]) => (
              <button
                key={key}
                role="tab"
                aria-selected={tab === key}
                className={`comm-tab ${tab === key ? 'on' : ''}`}
                onClick={() => setTab(key)}
              >
                {key === 'tracking' ? <IconChart size={15} /> : <IconClock size={15} />}
                {label}
              </button>
            ))}
          </div>
          <span className="comm-tabbar-note">
            {tab === 'tracking'
              ? 'Every message goes out automatically — click any figure for the detail behind it.'
              : page
                ? `${page.totalCount.toLocaleString()} message${page.totalCount === 1 ? '' : 's'} shown`
                : 'Every message ever sent to a member.'}
          </span>
        </div>

        {(notice !== null || actionError !== null) && (
          <div style={{ padding: 'var(--sp-5) var(--sp-5) 0' }} className="stack">
            {actionError ? <ErrorAlert error={actionError} /> : null}
            {notice ? <Alert tone="success">{notice}</Alert> : null}
          </div>
        )}

        {/* ------------------------------------------------------- tracking */}
        {tab === 'tracking' && (
          trackingLoading ? <Loading message="Loading message tracking…" /> : (
            <div className="page-card-body comm-body">
              {trackingError ? <ErrorAlert error={trackingError} /> : null}

              {(tracking || usage) && (
                <div className="comm-kpis">
                  {tracking && (
                    <Kpi
                      icon={<IconChart size={21} />}
                      gradient="var(--grad-hero)"
                      label="All messages"
                      value={tracking.monthTotal}
                      sub="this month, every stream"
                      onClick={() => setBreakdownOpen('month')}
                      hint="Break this total down by stream"
                    />
                  )}
                  {tracking && (
                    <Kpi
                      icon={<IconClock size={21} />}
                      gradient="var(--grad-blue)"
                      label="Sent today"
                      value={tracking.todayTotal}
                      sub={usage ? `${usage.todayEmails} email · ${usage.todayWhatsApp} WhatsApp` : undefined}
                      onClick={() => setBreakdownOpen('today')}
                      hint="Break today's total down by stream"
                    />
                  )}
                  {usage && (
                    <Kpi
                      icon={<IconMail size={21} />}
                      gradient="var(--grad-cyan)"
                      label="Emails"
                      value={usage.monthEmails}
                      sub={`this month · ${usage.todayEmails} today`}
                      onClick={() => drillIntoHistory({
                        channel: 'email', from: monthStartIso, to: todayIso,
                        label: 'Emails sent this month',
                      })}
                      hint="List every email sent this month"
                    />
                  )}
                  {usage && (
                    <Kpi
                      icon={<IconMessage size={21} />}
                      gradient="var(--grad-green)"
                      label="WhatsApp"
                      value={usage.monthWhatsApp}
                      sub={`this month · ${usage.todayWhatsApp} today`}
                      onClick={() => drillIntoHistory({
                        channel: 'whatsapp', from: monthStartIso, to: todayIso,
                        label: 'WhatsApp messages sent this month',
                      })}
                      hint="List every WhatsApp message sent this month"
                    />
                  )}
                  {usage && (
                    <Kpi
                      icon={<IconUsers size={21} />}
                      gradient="var(--grad-orange)"
                      label="Members reached"
                      value={usage.monthMembersReached}
                      sub="distinct, this month"
                      onClick={() => drillIntoHistory({
                        from: monthStartIso, to: todayIso,
                        label: 'Every message sent this month',
                      })}
                      hint="List every message sent this month"
                    />
                  )}
                </div>
              )}

              {tracking && (
                <Panel
                  icon={<IconChart size={17} />}
                  title="Message tracking"
                  caption="Every automated message across the app, counted by stream and channel. Click a stream to open its rows."
                >
                  <div className="comm-streams">
                    {tracking.streams.map((s) => {
                      const target = STREAM_TARGET[s.stream];
                      const open = target?.history
                        ? () => drillIntoHistory({
                          from: monthStartIso, to: todayIso, label: 'Occasion messages this month',
                        })
                        : target?.route
                          ? () => navigate(target.route!)
                          : undefined;
                      return (
                        <StreamCard key={s.stream} stream={s} onOpen={open} openLabel={target?.label} />
                      );
                    })}
                  </div>
                </Panel>
              )}

              {channels && (
                <div className="comm-split">
                  <Panel
                    icon={<IconPhone size={17} />}
                    title="Channels"
                    caption="Where messages go out from."
                  >
                    <div className="comm-chans">
                      {channels.channels.map((c) => {
                        const dryRun = c.enabled && isDryRun(c.provider);
                        const key = c.channel.toLowerCase();
                        return (
                          <div
                            key={c.channel}
                            className={`comm-chan ${c.enabled && !dryRun ? 'on' : ''} ${dryRun ? 'dry' : ''}`}
                          >
                            <span className="comm-chan-icon">
                              {key.includes('mail') ? <IconMail size={17} />
                                : key.includes('sms') ? <IconPhone size={17} />
                                  : <IconMessage size={17} />}
                            </span>
                            <span className="comm-chan-text grow">
                              <span className="comm-chan-name">{channelLabel(c.channel)}</span>
                              <span className="comm-chan-provider" title={c.provider}>
                                {providerLabel(c.provider)}
                              </span>
                            </span>
                            <span className="comm-chan-state">
                              {!c.enabled ? 'Off' : dryRun ? 'Test' : 'Live'}
                            </span>
                          </div>
                        );
                      })}
                    </div>
                  </Panel>

                  <Panel
                    icon={<IconBell size={17} />}
                    title="Occasions"
                    caption="What triggers a message, and which channels carry it. Click one for its messages."
                  >
                    <div className="comm-occ-grid">
                      {channels.occasions.map((o) => (
                        <OccasionCard
                          key={o.kind}
                          occasion={o}
                          onOpen={() => drillIntoHistory({
                            kind: o.kind, from: monthStartIso, to: todayIso,
                            label: `${words(o.kind)} messages this month`,
                          })}
                        />
                      ))}
                    </div>
                  </Panel>
                </div>
              )}

              <Panel
                icon={<IconSun size={17} />}
                title="Festival calendar"
                caption="Greetings go out on the day. Festivals and wording are configured in the server settings."
              >
                {sortedFestivals.length === 0 ? (
                  <div className="comm-stream-empty">No festivals configured.</div>
                ) : (
                  <div className="comm-fest-grid">
                    {sortedFestivals.map((f) => (
                      <FestivalCard
                        key={f.key}
                        festival={f}
                        today={today}
                        isNext={f.key === nextFestivalKey}
                        onOpen={() => drillIntoHistory({
                          kind: 'Festival', from: monthStartIso, to: todayIso,
                          label: 'Festival wishes this month',
                        })}
                      />
                    ))}
                  </div>
                )}
              </Panel>
            </div>
          )
        )}

        {/* -------------------------------------------------------- history */}
        {tab === 'history' && (
          <>
            {drillLabel && (
              <div className="comm-drill">
                <IconInfo size={15} />
                <span className="grow"><strong>{drillLabel}</strong> — filtered from the tracking view.</span>
                <button className="btn btn-ghost btn-sm" onClick={clearDrill}>Show everything</button>
              </div>
            )}

            {historyError ? <div style={{ padding: 20 }}><ErrorAlert error={historyError} /></div> : null}

            {historyLoading ? <Loading message="Loading messages…" /> : rows.length === 0 ? (
              <EmptyState
                icon={<IconMessage size={34} />}
                title="No messages"
                message="Nothing matches the current filters."
                action={activeFilterCount > 0
                  ? <button className="btn btn-outline" onClick={clearDrill}>Clear filters</button>
                  : undefined}
              />
            ) : (
              <div className="table-wrap">
                <table className="table">
                  <thead>
                    <tr>
                      <th className="idx">#</th>
                      <th>Member</th>
                      <th className="fit">Occasion</th>
                      <th className="wide">Detail</th>
                      <th className="fit">Channels</th>
                      <th className="fit">Date</th>
                    </tr>
                  </thead>
                  <tbody>
                    {rows.map((row, index) => (
                      <tr key={row.id}>
                        <td className="idx">{(pageNumber - 1) * pageSize + index + 1}</td>
                        <td>
                          <div className="comm-member">
                            <span className="comm-avatar" aria-hidden="true">{initials(row.memberName)}</span>
                            <div className="grow" style={{ minWidth: 0 }}>
                              <div className="cell-main">{row.memberName}</div>
                              <div className="cell-sub">{row.memberCode}</div>
                            </div>
                          </div>
                        </td>
                        <td className="fit"><KindPill kind={row.kind} /></td>
                        <td style={{ maxWidth: 340 }}>
                          {row.detail
                            ? <span className="comm-detail">{row.detail}</span>
                            : <span className="muted">—</span>}
                        </td>
                        <td className="fit"><ChannelTicks emailSent={row.emailSent} whatsAppSent={row.whatsAppSent} /></td>
                        <td className="fit"><span className="cell-icon"><IconCalendar size={13} />{date(row.sentOnDate)}</span></td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}

            {page && page.totalCount > 0 && (
              <Pager
                pageNumber={pageNumber}
                pageSize={pageSize}
                totalCount={page.totalCount}
                onPage={(p) => setQuery((q) => ({ ...q, pageNumber: p }))}
                onPageSize={(size) => setQuery((q) => ({ ...q, pageNumber: 1, pageSize: size }))}
              />
            )}
          </>
        )}
      </PageCard>

      {/* The tracking totals sum four separate tables, so they break down rather than drill in. */}
      {breakdownOpen && tracking && (
        <Modal
          title={breakdownOpen === 'today' ? "Today's messages, by stream" : "This month's messages, by stream"}
          icon={<IconChart size={18} />}
          onClose={() => setBreakdownOpen(false)}
          width={620}
          footer={<button className="btn btn-outline" onClick={() => setBreakdownOpen(false)}>Close</button>}
        >
          <div className="stack">
            <div className="table-wrap">
              <table className="table">
                <thead>
                  <tr>
                    <th>Stream</th>
                    <th className="center fit">{breakdownOpen === 'today' ? 'Today' : 'This month'}</th>
                    <th className="fit">Where it lives</th>
                  </tr>
                </thead>
                <tbody>
                  {tracking.streams.map((s) => {
                    const target = STREAM_TARGET[s.stream];
                    const count = breakdownOpen === 'today' ? s.today : s.thisMonth;
                    return (
                      <tr key={s.stream}>
                        <td>
                          <div className="cell-main">{words(s.stream)}</div>
                          <div className="cell-sub">{target?.note ?? s.description}</div>
                        </td>
                        <td className="center fit" style={{ fontWeight: 700, fontSize: 'var(--fs-lg)' }}>
                          {count}
                        </td>
                        <td className="fit">
                          {target?.history ? (
                            <button
                              className="btn btn-outline btn-sm"
                              onClick={() => drillIntoHistory({
                                from: monthStartIso, to: todayIso, label: 'Occasion messages this month',
                              })}
                            >
                              History <IconArrowRight size={13} />
                            </button>
                          ) : target?.route ? (
                            <button
                              className="btn btn-outline btn-sm"
                              onClick={() => { setBreakdownOpen(false); navigate(target.route!); }}
                            >
                              Open <IconArrowRight size={13} />
                            </button>
                          ) : <span className="muted">—</span>}
                        </td>
                      </tr>
                    );
                  })}
                  <tr>
                    <td><strong>Total</strong></td>
                    <td className="center fit" style={{ fontWeight: 800, fontSize: 'var(--fs-lg)' }}>
                      {breakdownOpen === 'today' ? tracking.todayTotal : tracking.monthTotal}
                    </td>
                    <td className="fit" />
                  </tr>
                </tbody>
              </table>
            </div>
            <Alert tone="info">
              <IconInfo size={16} />
              <span>
                Only the Occasions stream is listed message by message in History. The others are
                counted from their own tables and open on the screen that owns them.
              </span>
            </Alert>
          </div>
        </Modal>
      )}

      {testOpen && (
        <Modal
          title="Test WhatsApp delivery"
          icon={<IconPhone size={18} />}
          onClose={() => setTestOpen(false)}
          width={460}
          footer={
            <>
              <button className="btn btn-outline" onClick={() => setTestOpen(false)} disabled={test.busy}>
                Close
              </button>
              <button
                className="btn btn-dark"
                onClick={runWhatsAppTest}
                disabled={test.busy || !test.phone.trim()}
              >
                {test.busy ? 'Sending…' : 'Send test message'}
              </button>
            </>
          }
        >
          <div className="stack">
            <Field label="Phone number" required help="A short test message is sent through the configured WhatsApp provider.">
              <input
                className="input"
                value={test.phone}
                onChange={(e) => setTest((t) => ({ ...t, phone: e.target.value }))}
                placeholder="98765 43210"
                autoFocus
              />
            </Field>

            {test.error ? <ErrorAlert error={test.error} /> : null}
            {test.result && (
              <Alert tone={test.result.sent ? 'success' : 'warning'}>
                <IconInfo size={16} />
                <span>
                  {test.result.sent ? 'Sent' : 'Not sent'} via {test.result.provider}
                  {test.result.detail ? ` — ${test.result.detail}` : ''}
                </span>
              </Alert>
            )}
          </div>
        </Modal>
      )}
    </div>
  );
}
