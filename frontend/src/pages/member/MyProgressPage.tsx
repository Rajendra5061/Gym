/**
 * The member's body progress: weight and BMI charts drawn from the progress series, plus the
 * full measurement history their trainer has recorded.
 *
 * Both endpoints are self-access guarded server-side; they still run through `optional()` so an
 * account that may not read them sees the friendly empty state rather than an error.
 */

import { useEffect, useMemo, useState } from 'react';
import {
  CartesianGrid, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis,
} from 'recharts';
import { EmptyState, ErrorAlert, Loading, PageCard, PageCardHeader } from '@/components/ui';
import { IconCalendar, IconChart, IconUser } from '@/components/icons';
import { optional } from '@/api/endpoints/member';
import { membersApi } from '@/api/endpoints/members';
import type { MemberMeasurementDto, MemberProgressDto, ProgressPointDto } from '@/api/endpoints/members';
import { useAuth } from '@/auth/AuthContext';
import { date } from '@/lib/format';
import './member.css';

/** A number cell: the value with its unit, or a muted dash. */
function num(value: number | null | undefined, unit = ''): React.ReactNode {
  if (value === null || value === undefined) return <span className="muted">—</span>;
  return `${value}${unit}`;
}

/** One recharts-ready series, oldest point first. */
function toSeries(points: ProgressPointDto[] | null | undefined) {
  return [...(points ?? [])]
    .sort((a, b) => new Date(a.date).getTime() - new Date(b.date).getTime())
    .map((point) => ({ label: date(point.date), value: point.value }));
}

function ProgressChart(
  { title, data, unit, color }:
  { title: string; data: { label: string; value: number }[]; unit: string; color: string },
) {
  return (
    <div className="m-chart-panel">
      <div className="m-chart-panel-title"><IconChart size={14} /> {title}</div>
      {data.length === 0 ? (
        <div className="m-chart-empty">No readings recorded yet.</div>
      ) : (
        <div style={{ width: '100%', height: 240 }}>
          <ResponsiveContainer>
            <LineChart data={data} margin={{ top: 8, right: 16, bottom: 8, left: 0 }}>
              <CartesianGrid stroke="var(--divider)" strokeDasharray="3 3" vertical={false} />
              <XAxis dataKey="label" tick={{ fontSize: 12, fill: 'var(--text-muted)' }} tickLine={false} axisLine={{ stroke: 'var(--divider)' }} />
              <YAxis domain={['auto', 'auto']} tick={{ fontSize: 12, fill: 'var(--text-muted)' }} tickLine={false} axisLine={false} width={42} />
              <Tooltip
                contentStyle={{ borderRadius: 8, border: '1px solid var(--divider)', fontSize: 12 }}
                formatter={(value: number) => [`${value}${unit}`, title]}
              />
              <Line
                type="monotone"
                dataKey="value"
                stroke={color}
                strokeWidth={2.5}
                dot={{ r: 3, fill: color }}
                activeDot={{ r: 5 }}
              />
            </LineChart>
          </ResponsiveContainer>
        </div>
      )}
    </div>
  );
}

export default function MyProgressPage() {
  const { user } = useAuth();
  const memberId = user?.memberId ?? null;

  const [progress, setProgress] = useState<MemberProgressDto | null>(null);
  const [measurements, setMeasurements] = useState<MemberMeasurementDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<unknown>(null);

  useEffect(() => {
    if (memberId === null) { setLoading(false); return; }
    const ctrl = new AbortController();
    setLoading(true);
    setError(null);

    Promise.all([
      optional(() => membersApi.progress(memberId, ctrl.signal)),
      optional(() => membersApi.measurements(memberId, ctrl.signal)),
    ])
      .then(([progressResult, measurementResult]) => {
        if (ctrl.signal.aborted) return;
        setProgress(progressResult);
        setMeasurements(measurementResult ?? []);
      })
      .catch((e) => { if (!ctrl.signal.aborted) setError(e); })
      .finally(() => { if (!ctrl.signal.aborted) setLoading(false); });

    return () => ctrl.abort();
  }, [memberId]);

  const weightSeries = useMemo(() => toSeries(progress?.weight), [progress]);
  const bmiSeries = useMemo(() => toSeries(progress?.bmi), [progress]);

  const rows = useMemo(
    () => [...measurements].sort(
      (a, b) => new Date(b.measuredOn).getTime() - new Date(a.measuredOn).getTime(),
    ),
    [measurements],
  );

  if (memberId === null) {
    return (
      <div className="page">
        <PageCard>
          <EmptyState icon={<IconUser size={34} />} title="No member record linked" message="This login is not attached to a member profile." />
        </PageCard>
      </div>
    );
  }

  if (loading) return <div className="page"><PageCard><Loading message="Loading your progress…" /></PageCard></div>;
  if (error) return <div className="page"><PageCard><div style={{ padding: 20 }}><ErrorAlert error={error} /></div></PageCard></div>;

  const nothingYet = weightSeries.length === 0 && bmiSeries.length === 0 && rows.length === 0;

  if (nothingYet) {
    return (
      <div className="page">
        <PageCard>
          <PageCardHeader
            icon={<IconChart size={20} />}
            title="My Progress"
            subtitle="Your weight, BMI and measurement history."
          />
          <EmptyState
            icon={<IconChart size={34} />}
            title="No progress recorded yet"
            message="When your trainer records your measurements, your weight and BMI trends appear here."
          />
        </PageCard>
      </div>
    );
  }

  return (
    <div className="page">
      {/* -------------------------------------------------------------- charts */}
      <PageCard>
        <PageCardHeader
          icon={<IconChart size={20} />}
          title="My Progress"
          subtitle="How your weight and BMI have moved over recent measurements."
        />
        <div className="page-card-body">
          <div className="m-chart-grid">
            <ProgressChart title="Weight (kg)" data={weightSeries} unit=" kg" color="var(--chart-1)" />
            <ProgressChart title="BMI" data={bmiSeries} unit="" color="var(--chart-3)" />
          </div>
        </div>
      </PageCard>

      {/* -------------------------------------------------------- measurements */}
      <PageCard>
        <PageCardHeader
          icon={<IconCalendar size={20} />}
          title="Measurement History"
          subtitle={`${rows.length} measurement${rows.length === 1 ? '' : 's'} recorded, newest first.`}
        />
        {rows.length === 0 ? (
          <EmptyState
            icon={<IconCalendar size={30} />}
            title="No measurements recorded yet"
            message="Your trainer records these during check-ups; each entry appears here."
          />
        ) : (
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th className="idx">#</th>
                  <th className="fit">Date</th>
                  <th className="center fit">Weight</th>
                  <th className="center fit">Height</th>
                  <th className="center fit">BMI</th>
                  <th className="center fit">Body Fat</th>
                  <th className="center fit">Muscle</th>
                  <th className="center fit">Chest</th>
                  <th className="center fit">Waist</th>
                  <th>Recorded By</th>
                  <th className="wide">Notes</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((row, index) => (
                  <tr key={row.id}>
                    <td className="idx">{index + 1}</td>
                    <td className="fit"><span className="cell-icon"><IconCalendar size={14} />{date(row.measuredOn)}</span></td>
                    <td className="center fit">{num(row.weightKg, ' kg')}</td>
                    <td className="center fit">{num(row.heightCm, ' cm')}</td>
                    <td className="center fit">{num(row.bmi)}</td>
                    <td className="center fit">{num(row.bodyFatPercent, '%')}</td>
                    <td className="center fit">{num(row.muscleMassKg, ' kg')}</td>
                    <td className="center fit">{num(row.chestCm, ' cm')}</td>
                    <td className="center fit">{num(row.waistCm, ' cm')}</td>
                    <td>{row.recordedByTrainerName ?? <span className="muted">—</span>}</td>
                    <td className="muted">{row.notes || '—'}</td>
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
