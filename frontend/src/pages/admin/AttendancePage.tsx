import { useCallback, useEffect, useState } from 'react';
import {
  attendanceApi, CHECK_IN_METHODS,
  type AttendanceQuery, type CheckInMethod,
} from '@/api/endpoints/attendance';
import { useAuth } from '@/auth/AuthContext';
import {
  Alert, EmptyState, ErrorAlert, Field, FilterField, FilterMenu, Loading,
  PageCard, PageCardHeader, Pager, Pill,
} from '@/components/ui';
import {
  IconCalendar, IconCheck, IconPlus, IconRefresh, IconUser, IconUsers,
} from '@/components/icons';
import { date as fmtDate, initials, time as fmtTime } from '@/lib/format';
import type { AttendanceDto, Lookup, PagedResult } from '@/api/types';
import { MemberPicker, todayIso } from './SubscriptionsPage';
import './billing.css';

/** 95 → "1h 35m". */
function durationText(minutes: number | null | undefined): string {
  if (minutes === null || minutes === undefined) return '—';
  if (minutes < 60) return `${minutes}m`;
  return `${Math.floor(minutes / 60)}h ${minutes % 60}m`;
}

export default function AttendancePage() {
  const { can } = useAuth();
  const mayManage = can('attendance.manage');

  const [day, setDay] = useState(todayIso());
  const [member, setMember] = useState<Lookup | null>(null);
  const [query, setQuery] = useState<AttendanceQuery>({
    pageNumber: 1, pageSize: 25, fromDate: todayIso(), toDate: todayIso(),
  });

  const [data, setData] = useState<PagedResult<AttendanceDto> | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<unknown>(null);

  const [inGym, setInGym] = useState<AttendanceDto[]>([]);
  // Closed on arrival. This is the check-in form, not a filter, so it belongs behind the
  // "Mark Attendance" button that already toggles it rather than occupying a band on every visit.
  const [panelOpen, setPanelOpen] = useState(false);

  const [refreshKey, setRefreshKey] = useState(0);
  const refreshAll = useCallback(() => {
    setRefreshKey((k) => k + 1);
    setQuery((q) => ({ ...q }));
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError(null);
    attendanceApi.paged(query, controller.signal)
      .then((result) => { if (!controller.signal.aborted) setData(result); })
      .catch((err) => { if (!controller.signal.aborted) setError(err); })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [query]);

  // The in-gym list is always "right now", so it does not follow the date filter — only an
  // explicit refresh or a check-in / check-out reloads it.
  useEffect(() => {
    const controller = new AbortController();
    attendanceApi.inGym(controller.signal)
      .then((rows) => { if (!controller.signal.aborted) setInGym(rows); })
      .catch(() => { if (!controller.signal.aborted) setInGym([]); });
    return () => controller.abort();
  }, [refreshKey]);

  function applyFilters() {
    setQuery((q) => ({
      ...q,
      pageNumber: 1,
      fromDate: day || undefined,
      toDate: day || undefined,
      memberId: member ? member.id : '',
    }));
  }

  function resetFilters() {
    setDay(todayIso());
    setMember(null);
    setQuery({ pageNumber: 1, pageSize: 25, fromDate: todayIso(), toDate: todayIso() });
  }

  // Badge on the trigger, counted off the applied query against the defaults `resetFilters`
  // restores — today's date and no particular member.
  const activeFilterCount = ((query.fromDate || todayIso()) !== todayIso() ? 1 : 0)
    + (query.memberId ? 1 : 0);

  const items = data?.items ?? [];
  const firstIndex = ((data?.pageNumber ?? 1) - 1) * (data?.pageSize ?? 25);

  return (
    <div className="page">
      <PageCard>
        <PageCardHeader
          icon={<IconCalendar size={20} />}
          title={`Attendance (${query.fromDate || todayIso()})`}
          subtitle="View and filter daily check-ins by date and member."
          actions={
            <>
              <FilterMenu
                activeCount={activeFilterCount}
                onApply={applyFilters}
                onReset={resetFilters}
              >
                <FilterField label="Date">
                  <input className="input" type="date" value={day} onChange={(e) => setDay(e.target.value)} />
                </FilterField>

                <FilterField label="Member">
                  <MemberPicker value={member} onChange={setMember} placeholder="Any member" />
                </FilterField>
              </FilterMenu>

              <button className="btn btn-outline btn-icon" onClick={refreshAll} aria-label="Refresh"><IconRefresh size={15} /></button>
              {mayManage && (
                <button className="btn btn-dark" onClick={() => setPanelOpen((open) => !open)}>
                  <IconPlus size={15} /> Mark Attendance
                </button>
              )}
            </>
          }
        />

        {mayManage && panelOpen && <CheckInPanel onDone={refreshAll} />}

        {/* No filter strip: both filters are in the header's Filters menu, and this page has no
            search box, so the strip would have held nothing but a Reset button duplicating the
            menu's own "Clear all". */}

        {error ? <div className="section-pad"><ErrorAlert error={error} /></div> : null}

        {loading ? (
          <Loading message="Loading attendance…" />
        ) : items.length === 0 ? (
          <EmptyState
            icon={<IconCalendar size={34} />}
            title="No check-ins for this day"
            message="Pick another date, or record a check-in above."
          />
        ) : (
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th className="idx">#</th>
                  <th>Member</th>
                  <th>Date</th>
                  <th>Time In</th>
                  <th>Time Out</th>
                  <th>Duration</th>
                  <th>Method</th>
                </tr>
              </thead>
              <tbody>
                {items.map((row, i) => (
                  <tr key={row.id}>
                    <td className="idx">{firstIndex + i + 1}</td>
                    <td>
                      <div className="cell-main">{row.memberName}</div>
                      <div className="cell-sub">{row.memberCode}{row.planName ? ` · ${row.planName}` : ''}</div>
                    </td>
                    <td><span className="cell-icon"><IconCalendar size={14} />{fmtDate(row.attendanceDate)}</span></td>
                    <td><Pill tone="success">{fmtTime(row.checkInTime)}</Pill></td>
                    <td>
                      {row.checkOutTime
                        ? <Pill tone="info">{fmtTime(row.checkOutTime)}</Pill>
                        : <span className="muted">Still in</span>}
                    </td>
                    <td>{durationText(row.durationMinutes)}</td>
                    <td><Pill tone="dark">{row.checkInMethod}</Pill></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {data && data.totalCount > 0 && (
          <Pager
            pageNumber={data.pageNumber}
            pageSize={data.pageSize}
            totalCount={data.totalCount}
            onPage={(page) => setQuery((q) => ({ ...q, pageNumber: page }))}
            onPageSize={(size) => setQuery((q) => ({ ...q, pageNumber: 1, pageSize: size }))}
          />
        )}
      </PageCard>

      <InGymCard rows={inGym} mayManage={mayManage} onChanged={refreshAll} />
    </div>
  );
}

/* ------------------------------------------------------------ check-in panel */

function CheckInPanel({ onDone }: { onDone: () => void }) {
  const { can } = useAuth();
  const [member, setMember] = useState<Lookup | null>(null);
  const [memberCode, setMemberCode] = useState('');
  const [method, setMethod] = useState<CheckInMethod>('Manual');
  const [override, setOverride] = useState(false);
  const [notes, setNotes] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [success, setSuccess] = useState('');

  const ready = !!member || memberCode.trim().length > 0;

  async function checkIn() {
    if (!ready) return;
    setBusy(true);
    setError(null);
    setSuccess('');
    try {
      const record = await attendanceApi.checkIn({
        memberId: member ? member.id : null,
        memberCode: member ? null : memberCode.trim(),
        checkInMethod: method,
        notes: notes.trim() || null,
        overrideExpiredMembership: override,
      });
      setSuccess(`${record.memberName} (${record.memberCode}) checked in at ${fmtTime(record.checkInTime)}.`);
      setMember(null);
      setMemberCode('');
      setNotes('');
      setOverride(false);
      onDone();
    } catch (err) {
      // Duplicate check-in, expired membership and every other rule is decided server-side;
      // its message is what the desk needs to read.
      setError(err);
    } finally {
      setBusy(false);
    }
  }

  return (
    <>
      {(error || success) && (
        <div className="section-pad" style={{ paddingBottom: 0 }}>
          {error ? <ErrorAlert error={error} /> : <Alert tone="success">{success}</Alert>}
        </div>
      )}

      <div className="checkin-panel">
        <div className="grow">
          <Field label="Member" help="Search the register, or scan / type a member code below.">
            <MemberPicker value={member} onChange={setMember} placeholder="Search by name, code or phone" />
          </Field>
        </div>

        <Field label="Member code">
          <input
            className="input"
            placeholder="e.g. MBR-0001"
            value={memberCode}
            disabled={!!member}
            maxLength={64}
            onChange={(e) => setMemberCode(e.target.value)}
            onKeyDown={(e) => { if (e.key === 'Enter' && ready) void checkIn(); }}
          />
        </Field>

        <Field label="Method">
          <select className="select" value={method} onChange={(e) => setMethod(e.target.value as CheckInMethod)}>
            {CHECK_IN_METHODS.map((m) => <option key={m} value={m}>{m}</option>)}
          </select>
        </Field>

        <Field label="Notes">
          <input className="input" maxLength={2000} value={notes} onChange={(e) => setNotes(e.target.value)} />
        </Field>

        {can('attendance.manage') && (
          <label className="check-inline" style={{ height: 38 }}>
            <input type="checkbox" checked={override} onChange={(e) => setOverride(e.target.checked)} />
            Override expired membership
          </label>
        )}

        <button className="btn btn-success" style={{ height: 38, minWidth: 140 }} onClick={checkIn} disabled={busy || !ready}>
          <IconCheck size={16} /> {busy ? 'Checking in…' : 'Check In'}
        </button>
      </div>
    </>
  );
}

/* ------------------------------------------------------------ in-gym listing */

function InGymCard(
  { rows, mayManage, onChanged }:
  { rows: AttendanceDto[]; mayManage: boolean; onChanged: () => void },
) {
  const [busyId, setBusyId] = useState<number | null>(null);
  const [error, setError] = useState<unknown>(null);

  async function checkOut(row: AttendanceDto) {
    setBusyId(row.id);
    setError(null);
    try {
      await attendanceApi.checkOut({ attendanceId: row.id });
      onChanged();
    } catch (err) {
      setError(err);
    } finally {
      setBusyId(null);
    }
  }

  return (
    <PageCard>
      <PageCardHeader
        icon={<IconUsers size={20} />}
        title="Currently in gym"
        subtitle={`${rows.length} member${rows.length === 1 ? '' : 's'} checked in and not yet checked out.`}
      />

      {error ? <div className="section-pad"><ErrorAlert error={error} /></div> : null}

      {rows.length === 0 ? (
        <EmptyState icon={<IconUser size={30} />} title="The floor is empty" message="Nobody is checked in right now." />
      ) : (
        <div className="in-gym-list">
          {rows.map((row) => (
            <div className="in-gym-row" key={row.id}>
              <div className="avatar">{initials(row.memberName)}</div>
              <div className="grow">
                <div className="cell-main">{row.memberName}</div>
                <div className="cell-sub">{row.memberCode}{row.planName ? ` · ${row.planName}` : ''}</div>
              </div>
              <Pill tone="success">In since {fmtTime(row.checkInTime)}</Pill>
              <Pill tone="dark">{row.checkInMethod}</Pill>
              {mayManage && (
                <button className="btn btn-outline btn-sm" disabled={busyId === row.id} onClick={() => checkOut(row)}>
                  {busyId === row.id ? 'Working…' : 'Check Out'}
                </button>
              )}
            </div>
          ))}
        </div>
      )}
    </PageCard>
  );
}

