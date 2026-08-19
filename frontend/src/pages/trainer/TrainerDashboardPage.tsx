/**
 * The trainer's landing screen. Everything comes from `GET /api/dashboard/trainer`, which the
 * server scopes to the trainerId claim on the token — the page never chooses whose data to show.
 */
import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { ApiError } from '@/api/client';
import { dashboardApi, type TrainerDashboardDto } from '@/api/endpoints/dashboard';
import {
  EmptyState, ErrorAlert, Loading, PageCard, PageCardHeader, Pill, type PillTone,
} from '@/components/ui';
import {
  IconArrowRight, IconCalendar, IconCheckSquare, IconDumbbell, IconFile, IconRefresh,
  IconUser, IconUsers, IconWarning,
} from '@/components/icons';
import { date, initials } from '@/lib/format';
import './trainer.css';

/** Urgency tone for a days-left figure: overdue/immediate, this week, later. */
function daysLeftTone(daysLeft: number | null | undefined): PillTone {
  if (daysLeft === null || daysLeft === undefined) return 'neutral';
  if (daysLeft <= 3) return 'danger';
  if (daysLeft <= 7) return 'warning';
  return 'info';
}

function daysLeftText(daysLeft: number | null | undefined): string {
  if (daysLeft === null || daysLeft === undefined) return '—';
  if (daysLeft < 0) return 'expired';
  if (daysLeft === 0) return 'today';
  return `${daysLeft} day${daysLeft === 1 ? '' : 's'}`;
}

export default function TrainerDashboardPage() {
  const [data, setData] = useState<TrainerDashboardDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    const controller = new AbortController();
    setRefreshing(true);
    dashboardApi.trainer(controller.signal)
      .then((result) => { if (!controller.signal.aborted) { setData(result); setError(null); } })
      .catch((err) => { if (!controller.signal.aborted) setError(err); })
      .finally(() => {
        if (controller.signal.aborted) return;
        // `loading` blanks the screen, so only the first pass may set it: a refresh keeps the
        // numbers on screen and swaps them when the new ones land.
        setLoading(false);
        setRefreshing(false);
      });
    return () => controller.abort();
  }, [reloadKey]);

  if (loading) {
    return <div className="page"><PageCard><Loading message="Loading your dashboard…" /></PageCard></div>;
  }

  // A 404 means the signed-in account has no trainer record — a setup problem, not a crash.
  if (error instanceof ApiError && error.status === 404) {
    return (
      <div className="page">
        <PageCard>
          <EmptyState
            icon={<IconUser size={34} />}
            title="No trainer record linked"
            message="This login is not attached to a trainer profile, so there is nothing to show here yet. Ask the office to link your account to your trainer record."
          />
        </PageCard>
      </div>
    );
  }
  if (error) {
    return <div className="page"><PageCard><div style={{ padding: 20 }}><ErrorAlert error={error} /></div></PageCard></div>;
  }
  if (!data) return null;

  /* Built from the response rather than written out four times, so a new metric is one entry
     here instead of another copy-pasted block. Each one links to the screen it counts. */
  const tiles = [
    {
      to: '/trainer/workout-plans', tone: 't-tile-blue', icon: <IconDumbbell size={22} />,
      title: 'Active Workout Plans', value: data.activeWorkoutPlanCount,
      caption: 'plans you are running', action: 'Open plans',
    },
    {
      to: '/trainer/diet-plans', tone: 't-tile-green', icon: <IconFile size={22} />,
      title: 'Active Diet Plans', value: data.activeDietPlanCount,
      caption: 'meal programmes in use', action: 'Open diets',
    },
    {
      to: '/trainer/attendance', tone: 't-tile-orange', icon: <IconCheckSquare size={22} />,
      title: "Today's Check-ins", value: data.todayCheckInCount,
      caption: 'of your members', action: 'View attendance',
    },
    {
      to: '/trainer/members', tone: 't-tile-cyan', icon: <IconUsers size={22} />,
      title: 'My Members', value: data.assignedMemberCount,
      caption: 'assigned to you', action: 'Open roster',
    },
  ];

  return (
    <div className="page">
      {/* ------------------------------------------------------------ hero */}
      <div className="trainer-hero">
        <div className="trainer-hero-avatar">{initials(data.trainerName)}</div>
        <div className="grow">
          <div className="trainer-hero-title">Welcome back, {data.trainerName.split(' ')[0]}!</div>
          <div className="trainer-hero-sub">Here is how your members are doing today.</div>
          <div className="trainer-hero-badges">
            <span className="trainer-hero-badge">
              <IconUsers size={13} /> {data.assignedMemberCount} assigned member{data.assignedMemberCount === 1 ? '' : 's'}
            </span>
            <span className="trainer-hero-badge">
              <IconCheckSquare size={13} /> {data.todayCheckInCount} checked in today
            </span>
          </div>
        </div>
        <div className="trainer-hero-actions">
          <button
            type="button"
            className="btn btn-hero-outline"
            onClick={() => setReloadKey((key) => key + 1)}
            disabled={refreshing}
          >
            <IconRefresh size={14} /> {refreshing ? 'Refreshing…' : 'Refresh'}
          </button>
        </div>
      </div>

      {/* ------------------------------------------------------------ tiles */}
      <div className="trainer-tiles">
        {tiles.map((tile) => (
          <Link className={`t-tile ${tile.tone}`} to={tile.to} key={tile.to}>
            <span className="t-tile-icon">{tile.icon}</span>
            <span className="grow">
              <span className="t-tile-title">{tile.title}</span>
              <span className="t-tile-value">{tile.value}</span>
              <span className="t-tile-caption">{tile.caption}</span>
              <span className="t-tile-action">{tile.action} <IconArrowRight size={13} /></span>
            </span>
          </Link>
        ))}
      </div>

      {/* ----------------------------------------------------- expiring soon */}
      <PageCard>
        <PageCardHeader
          icon={<IconWarning size={20} />}
          title="Memberships Expiring Soon"
          subtitle="Members of yours whose plan ends within 10 days — a renewal chat waiting to happen."
        />
        {data.expiringSoon.length === 0 ? (
          <EmptyState
            icon={<IconCalendar size={30} />}
            title="Nothing expiring soon"
            message="None of your members run out within the next 10 days."
          />
        ) : (
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th className="wide">Member</th>
                  <th className="fit">Ends On</th>
                  <th className="fit">Time Left</th>
                </tr>
              </thead>
              <tbody>
                {data.expiringSoon.map((row) => (
                  <tr key={row.memberId}>
                    <td><div className="cell-main">{row.memberName}</div></td>
                    <td className="fit">{date(row.endDate)}</td>
                    <td className="fit"><Pill tone={daysLeftTone(row.daysLeft)}>{daysLeftText(row.daysLeft)}</Pill></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </PageCard>

      {/* ------------------------------------------------------- my members */}
      <PageCard>
        <PageCardHeader
          icon={<IconUsers size={20} />}
          title="My Members"
          subtitle="Your roster at a glance — measurements, progress and plans live on the members screen."
          actions={<Link className="btn btn-outline" to="/trainer/members">Open My Members</Link>}
        />
        {data.myMembers.length === 0 ? (
          <EmptyState
            icon={<IconUsers size={30} />}
            title="No members assigned"
            message="When the office assigns members to you, they appear here."
          />
        ) : (
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th className="wide">Member</th>
                  <th>Plan</th>
                  <th className="fit">Ends On</th>
                  <th className="fit">Time Left</th>
                  <th className="fit">Last Check-in</th>
                </tr>
              </thead>
              <tbody>
                {data.myMembers.map((row) => (
                  <tr key={row.memberId}>
                    <td>
                      <div className="cell-main">{row.memberName}</div>
                      <div className="cell-sub">{row.memberCode}</div>
                    </td>
                    <td>{row.planName || '—'}</td>
                    <td className="fit">{row.endDate ? date(row.endDate) : '—'}</td>
                    <td className="fit">
                      {row.daysLeft === null || row.daysLeft === undefined
                        ? <span className="muted">—</span>
                        : <Pill tone={daysLeftTone(row.daysLeft)}>{daysLeftText(row.daysLeft)}</Pill>}
                    </td>
                    <td className="fit">{row.lastCheckInDate ? date(row.lastCheckInDate) : <span className="muted">never</span>}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </PageCard>
    </div>
  );
}
