/**
 * The trainer's roster. The list is asked for with the trainer's own id, and the server
 * force-scopes it to the trainerId claim anyway, so a trainer can only ever see their people.
 * From each row: record a measurement, chart progress, or start a workout/diet plan.
 */
import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import {
  CartesianGrid, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis,
} from 'recharts';
import { useAuth } from '@/auth/AuthContext';
import {
  Alert, EmptyState, ErrorAlert, Field, Loading, Modal, PageCard, PageCardHeader, Pager, Pill,
  StatusPill,
} from '@/components/ui';
import {
  IconChart, IconDumbbell, IconFile, IconPlus, IconSearch, IconUsers,
} from '@/components/icons';
import { trainersApi } from '@/api/endpoints/trainers';
import {
  membersApi, type MemberProgressDto, type SaveMeasurementDto,
} from '@/api/endpoints/members';
import type { MemberListDto, PagedResult } from '@/api/types';
import { date, isoDate } from '@/lib/format';
import './trainer.css';

/* ------------------------------------------------------------- measurement modal */

interface MeasureFormState {
  measuredOn: string;
  weightKg: string; heightCm: string; bodyFatPercent: string; muscleMassKg: string;
  chestCm: string; waistCm: string; notes: string;
}

const BLANK_MEASURE: MeasureFormState = {
  measuredOn: isoDate(new Date()),
  weightKg: '', heightCm: '', bodyFatPercent: '', muscleMassKg: '',
  chestCm: '', waistCm: '', notes: '',
};

/** '' → undefined, anything else → number (NaN is caught by validation first). */
const num = (value: string): number | undefined => (value.trim() === '' ? undefined : Number(value));

function MeasurementModal(
  { member, onClose, onSaved }:
  { member: MemberListDto; onClose: () => void; onSaved: (name: string) => void },
) {
  const [form, setForm] = useState<MeasureFormState>(BLANK_MEASURE);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [fieldError, setFieldError] = useState<string | null>(null);

  const set = (patch: Partial<MeasureFormState>) => setForm((f) => ({ ...f, ...patch }));

  const save = async () => {
    const metrics = [form.weightKg, form.heightCm, form.bodyFatPercent, form.muscleMassKg, form.chestCm, form.waistCm];
    if (!metrics.some((v) => v.trim() !== '')) {
      setFieldError('Enter at least one measurement.');
      return;
    }
    if (metrics.some((v) => v.trim() !== '' && (Number.isNaN(Number(v)) || Number(v) < 0))) {
      setFieldError('Measurements must be positive numbers.');
      return;
    }
    setFieldError(null);
    setSaving(true);
    setError(null);
    try {
      const dto: SaveMeasurementDto = {
        measuredOn: form.measuredOn || isoDate(new Date()),
        weightKg: num(form.weightKg),
        heightCm: num(form.heightCm),
        bodyFatPercent: num(form.bodyFatPercent),
        muscleMassKg: num(form.muscleMassKg),
        chestCm: num(form.chestCm),
        waistCm: num(form.waistCm),
        notes: form.notes.trim() || null,
      };
      await membersApi.addMeasurement(member.id, dto);
      onSaved(member.fullName);
    } catch (err) {
      setError(err);
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal
      title={`Record measurement — ${member.fullName}`}
      icon={<IconPlus size={16} />}
      onClose={onClose}
      width={640}
      footer={
        <>
          <button className="btn btn-outline" onClick={onClose} disabled={saving}>Cancel</button>
          <button className="btn btn-dark" onClick={() => void save()} disabled={saving}>
            {saving ? 'Saving…' : 'Save Measurement'}
          </button>
        </>
      }
    >
      <div className="stack">
        {error ? <ErrorAlert error={error} /> : null}
        {fieldError ? <Alert tone="warning">{fieldError}</Alert> : null}
        <div className="form-grid">
          <Field label="Measured on" required>
            <input className="input" type="date" value={form.measuredOn} onChange={(e) => set({ measuredOn: e.target.value })} />
          </Field>
          <Field label="Weight (kg)" help="BMI is recalculated on the server from weight and height.">
            <input className="input" type="number" min={0} step="0.1" value={form.weightKg} onChange={(e) => set({ weightKg: e.target.value })} />
          </Field>
          <Field label="Height (cm)">
            <input className="input" type="number" min={0} step="0.1" value={form.heightCm} onChange={(e) => set({ heightCm: e.target.value })} />
          </Field>
          <Field label="Body fat (%)">
            <input className="input" type="number" min={0} step="0.1" value={form.bodyFatPercent} onChange={(e) => set({ bodyFatPercent: e.target.value })} />
          </Field>
          <Field label="Muscle mass (kg)">
            <input className="input" type="number" min={0} step="0.1" value={form.muscleMassKg} onChange={(e) => set({ muscleMassKg: e.target.value })} />
          </Field>
          <Field label="Chest (cm)">
            <input className="input" type="number" min={0} step="0.1" value={form.chestCm} onChange={(e) => set({ chestCm: e.target.value })} />
          </Field>
          <Field label="Waist (cm)">
            <input className="input" type="number" min={0} step="0.1" value={form.waistCm} onChange={(e) => set({ waistCm: e.target.value })} />
          </Field>
        </div>
        <Field label="Notes" help="Optional. Anything worth remembering about this check-up.">
          <textarea className="textarea" value={form.notes} onChange={(e) => set({ notes: e.target.value })} />
        </Field>
      </div>
    </Modal>
  );
}

/* ---------------------------------------------------------------- progress modal */

function ProgressChart({ title, points, color }: {
  title: string;
  points: { label: string; value: number }[];
  color: string;
}) {
  if (points.length === 0) {
    return <div className="muted" style={{ fontSize: 13 }}>{title}: no readings yet.</div>;
  }
  return (
    <div>
      <div className="field-label" style={{ marginBottom: 6 }}>{title}</div>
      <div style={{ width: '100%', height: 200 }}>
        <ResponsiveContainer>
          <LineChart data={points} margin={{ top: 8, right: 16, bottom: 8, left: 0 }}>
            <CartesianGrid stroke="var(--divider)" strokeDasharray="3 3" vertical={false} />
            <XAxis dataKey="label" tick={{ fontSize: 12, fill: 'var(--text-muted)' }} tickLine={false} axisLine={{ stroke: 'var(--divider)' }} />
            <YAxis tick={{ fontSize: 12, fill: 'var(--text-muted)' }} tickLine={false} axisLine={false} width={40} domain={['auto', 'auto']} />
            <Tooltip
              contentStyle={{
                background: 'var(--card)', border: '1px solid var(--divider)',
                borderRadius: 8, fontSize: 13, color: 'var(--text)',
              }}
            />
            <Line type="monotone" dataKey="value" stroke={color} strokeWidth={2} dot={{ r: 3 }} />
          </LineChart>
        </ResponsiveContainer>
      </div>
    </div>
  );
}

function ProgressModal({ member, onClose }: { member: MemberListDto; onClose: () => void }) {
  const [progress, setProgress] = useState<MemberProgressDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<unknown>(null);

  useEffect(() => {
    const controller = new AbortController();
    membersApi.progress(member.id, controller.signal)
      .then((result) => { if (!controller.signal.aborted) setProgress(result); })
      .catch((err) => { if (!controller.signal.aborted) setError(err); })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [member.id]);

  const toPoints = (series: { date: string; value: number }[] | undefined) =>
    (series ?? []).map((p) => ({ label: date(p.date), value: Number(p.value) }));

  const weight = toPoints(progress?.weight);
  const bmi = toPoints(progress?.bmi);

  return (
    <Modal title={`Progress — ${member.fullName}`} icon={<IconChart size={16} />} onClose={onClose} width={760}>
      <div className="stack">
        {loading && <Loading message="Loading progress…" />}
        {!loading && error != null && <ErrorAlert error={error} />}
        {!loading && !error && weight.length === 0 && bmi.length === 0 && (
          <EmptyState
            icon={<IconChart size={30} />}
            title="No readings yet"
            message="Record a measurement and the weight and BMI charts start here."
          />
        )}
        {!loading && !error && (weight.length > 0 || bmi.length > 0) && (
          <>
            <ProgressChart title="Weight (kg)" points={weight} color="#2f6be0" />
            <ProgressChart title="BMI" points={bmi} color="#16a34a" />
          </>
        )}
      </div>
    </Modal>
  );
}

/* ------------------------------------------------------------------------ page */

const PAGE_SIZE_DEFAULT = 25;

export default function TrainerMembersPage() {
  const { can } = useAuth();
  const canMeasure = can('measurements.manage');

  const [trainerId, setTrainerId] = useState<number | null>(null);
  const [meResolved, setMeResolved] = useState(false);

  const [search, setSearch] = useState('');
  const [appliedSearch, setAppliedSearch] = useState('');
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(PAGE_SIZE_DEFAULT);

  const [data, setData] = useState<PagedResult<MemberListDto> | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<unknown>(null);

  const [measuring, setMeasuring] = useState<MemberListDto | null>(null);
  const [viewing, setViewing] = useState<MemberListDto | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  // Who am I? A 404 just means the login has no trainer record (an admin having a look) —
  // the list below still works because the server scopes trainer accounts itself.
  useEffect(() => {
    const controller = new AbortController();
    trainersApi.me(controller.signal)
      .then((me) => { if (!controller.signal.aborted) setTrainerId(me.id); })
      .catch(() => { /* no trainer link — fall back to the server's own scoping */ })
      .finally(() => { if (!controller.signal.aborted) setMeResolved(true); });
    return () => controller.abort();
  }, []);

  useEffect(() => {
    if (!meResolved) return;
    const controller = new AbortController();
    setLoading(true);
    membersApi.list(
      {
        Search: appliedSearch || undefined,
        TrainerId: trainerId ?? undefined,
        PageNumber: pageNumber,
        PageSize: pageSize,
        SortBy: 'fullName',
      },
      controller.signal,
    )
      .then((result) => { if (!controller.signal.aborted) { setData(result); setError(null); } })
      .catch((err) => { if (!controller.signal.aborted) { setError(err); setData(null); } })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [meResolved, trainerId, appliedSearch, pageNumber, pageSize]);

  const applySearch = useCallback(() => {
    setAppliedSearch(search.trim());
    setPageNumber(1);
  }, [search]);

  const onSaved = (memberName: string) => {
    setMeasuring(null);
    setNotice(`Measurement saved for ${memberName}.`);
  };

  return (
    <div className="page">
      <PageCard>
        <PageCardHeader
          icon={<IconUsers size={20} />}
          title="My Members"
          subtitle="The members assigned to you. Record measurements, follow progress, and hand out plans."
        />

        <div className="page-card-body">
          <div className="stack">
            {notice ? <Alert tone="success">{notice}</Alert> : null}
            {error ? <ErrorAlert error={error} /> : null}

            <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
              <div className="input-group" style={{ flex: 1, minWidth: 220 }}>
                <span className="input-icon"><IconSearch size={14} /></span>
                <input
                  className="input"
                  placeholder="Search by name, code or phone"
                  value={search}
                  onChange={(e) => setSearch(e.target.value)}
                  onKeyDown={(e) => { if (e.key === 'Enter') applySearch(); }}
                />
              </div>
              <button className="btn btn-outline" onClick={applySearch}>Search</button>
            </div>

            {loading && <Loading message="Loading your members…" />}

            {!loading && data && data.items.length === 0 && (
              <EmptyState
                icon={<IconUsers size={30} />}
                title={appliedSearch ? 'No members match' : 'No members assigned'}
                message={appliedSearch
                  ? 'Nobody on your roster matches that search.'
                  : 'When the office assigns members to you, they appear here.'}
              />
            )}

            {!loading && data && data.items.length > 0 && (
              <>
                <div className="table-wrap">
                  <table className="table">
                    <thead>
                      <tr>
                        <th className="wide">Member</th>
                        <th>Plan</th>
                        <th className="fit">Ends On</th>
                        <th className="fit">Status</th>
                        <th className="actions">Actions</th>
                      </tr>
                    </thead>
                    <tbody>
                      {data.items.map((member) => (
                        <tr key={member.id}>
                          <td>
                            <div className="cell-main">{member.fullName}</div>
                            <div className="cell-sub">{member.memberCode} · {member.phone}</div>
                          </td>
                          <td>
                            <div>{member.currentPlanName || '—'}</div>
                            {member.daysRemaining !== null && member.daysRemaining !== undefined && (
                              <div className="cell-sub">
                                {member.daysRemaining < 0 ? 'expired' : `${member.daysRemaining} days left`}
                              </div>
                            )}
                          </td>
                          <td className="fit">{member.subscriptionEndDate ? date(member.subscriptionEndDate) : '—'}</td>
                          <td className="fit"><StatusPill status={member.statusText} /></td>
                          <td className="actions">
                            <div className="trainer-row-actions">
                              {canMeasure && (
                                <button
                                  className="btn btn-outline btn-sm"
                                  title="Record a body measurement"
                                  onClick={() => { setNotice(null); setMeasuring(member); }}
                                >
                                  <IconPlus size={13} /> Measure
                                </button>
                              )}
                              <button
                                className="btn btn-outline btn-sm"
                                title="Weight and BMI over time"
                                onClick={() => setViewing(member)}
                              >
                                <IconChart size={13} /> Progress
                              </button>
                              <Link
                                className="btn btn-outline btn-sm"
                                title="Start a workout plan for this member"
                                to={`/trainer/workout-plans/new?memberId=${member.id}`}
                              >
                                <IconDumbbell size={13} /> Workout
                              </Link>
                              <Link
                                className="btn btn-outline btn-sm"
                                title="Start a diet plan for this member"
                                to={`/trainer/diet-plans/new?memberId=${member.id}`}
                              >
                                <IconFile size={13} /> Diet
                              </Link>
                            </div>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>

                <Pager
                  pageNumber={data.pageNumber}
                  pageSize={data.pageSize}
                  totalCount={data.totalCount}
                  onPage={setPageNumber}
                  onPageSize={(size) => { setPageSize(size); setPageNumber(1); }}
                />
              </>
            )}
          </div>
        </div>
      </PageCard>

      {measuring && (
        <MeasurementModal member={measuring} onClose={() => setMeasuring(null)} onSaved={onSaved} />
      )}
      {viewing && <ProgressModal member={viewing} onClose={() => setViewing(null)} />}
    </div>
  );
}
