/**
 * The member's own visit history.
 *
 * The self-service endpoints hand back the member's complete history in one response — there is
 * no paged member-scoped attendance endpoint — so the date filter and pager work over the list
 * already held in memory rather than issuing a new request per page.
 */

import { useEffect, useMemo, useState } from 'react';
import {
  EmptyState, ErrorAlert, FilterField, FilterMenu, Loading, PageCard, PageCardHeader, Pager, Pill,
} from '@/components/ui';
import { IconCalendar, IconCheckSquare, IconClock, IconUser } from '@/components/icons';
import { memberApi, optional } from '@/api/endpoints/member';
import type { AttendanceDto } from '@/api/types';
import { useAuth } from '@/auth/AuthContext';
import { date, time } from '@/lib/format';
import './member.css';

export default function MyAttendancePage() {
  const { user } = useAuth();
  const memberId = user?.memberId ?? null;

  const [rows, setRows] = useState<AttendanceDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<unknown>(null);
  const [partial, setPartial] = useState(false);

  const [fromDraft, setFromDraft] = useState('');
  const [toDraft, setToDraft] = useState('');
  const [range, setRange] = useState<{ from: string; to: string }>({ from: '', to: '' });
  // Badge counts the applied range, not what is typed but unapplied.
  const activeFilterCount = (range.from ? 1 : 0) + (range.to ? 1 : 0);
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(25);

  useEffect(() => {
    if (memberId === null) { setLoading(false); return; }
    const ctrl = new AbortController();
    setLoading(true);
    setError(null);

    optional(() => memberApi.history(memberId, ctrl.signal))
      .then(async (history) => {
        if (ctrl.signal.aborted) return;
        if (history) {
          setRows(history.attendance ?? []);
          setPartial(false);
          return;
        }
        // Without the staff permission only the dashboard's recent slice is readable.
        const dashboard = await memberApi.dashboard(memberId, ctrl.signal);
        if (ctrl.signal.aborted) return;
        setRows(dashboard.recentAttendance ?? []);
        setPartial(true);
      })
      .catch((e) => { if (!ctrl.signal.aborted) setError(e); })
      .finally(() => { if (!ctrl.signal.aborted) setLoading(false); });

    return () => ctrl.abort();
  }, [memberId]);

  const filtered = useMemo(() => {
    const from = range.from ? new Date(`${range.from}T00:00:00`).getTime() : null;
    const to = range.to ? new Date(`${range.to}T23:59:59`).getTime() : null;
    return rows
      .filter((row) => {
        const when = new Date(row.attendanceDate).getTime();
        if (from !== null && when < from) return false;
        if (to !== null && when > to) return false;
        return true;
      })
      .sort((a, b) => new Date(b.attendanceDate).getTime() - new Date(a.attendanceDate).getTime());
  }, [rows, range]);

  const pageRows = useMemo(
    () => filtered.slice((pageNumber - 1) * pageSize, pageNumber * pageSize),
    [filtered, pageNumber, pageSize],
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

  return (
    <div className="page">
      <PageCard>
        <PageCardHeader
          icon={<IconCalendar size={20} />}
          title="My Attendance"
          subtitle={`${filtered.length} visit${filtered.length === 1 ? '' : 's'}${partial ? ' (most recent)' : ''} recorded against your membership.`}
          actions={(
            <>
              {/* This screen has no search box, so the whole strip folds away and the menu's own
                  Apply / Clear all replace the buttons that used to sit in it. */}
              <FilterMenu
                activeCount={activeFilterCount}
                onApply={() => { setRange({ from: fromDraft, to: toDraft }); setPageNumber(1); }}
                onReset={() => {
                  setFromDraft('');
                  setToDraft('');
                  setRange({ from: '', to: '' });
                  setPageNumber(1);
                }}
              >
                <FilterField label="From date">
                  <input className="input" type="date" value={fromDraft} onChange={(e) => setFromDraft(e.target.value)} />
                </FilterField>
                <FilterField label="To date">
                  <input className="input" type="date" value={toDraft} onChange={(e) => setToDraft(e.target.value)} />
                </FilterField>
              </FilterMenu>

              <span className="btn btn-dark" style={{ cursor: 'default' }}>
                <IconCheckSquare size={15} /> {filtered.length} Visits
              </span>
            </>
          )}
        />

        {error ? <div style={{ padding: 20 }}><ErrorAlert error={error} /></div> : null}

        {loading ? <Loading message="Loading your attendance…" /> : pageRows.length === 0 ? (
          <EmptyState
            icon={<IconCalendar size={34} />}
            title="No attendance records"
            message={range.from || range.to ? 'No visits fall inside the selected dates.' : 'Your visits appear here once you check in at the gym.'}
          />
        ) : (
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th className="idx">#</th>
                  <th>Date</th>
                  <th>Time In</th>
                </tr>
              </thead>
              <tbody>
                {pageRows.map((row, index) => (
                  <tr key={row.id}>
                    <td className="idx">{(pageNumber - 1) * pageSize + index + 1}</td>
                    <td><span className="cell-icon"><IconCalendar size={14} />{date(row.attendanceDate)}</span></td>
                    <td><Pill tone="success"><IconClock size={12} /> {time(row.checkInTime)}</Pill></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {filtered.length > 0 && (
          <Pager
            pageNumber={pageNumber}
            pageSize={pageSize}
            totalCount={filtered.length}
            onPage={setPageNumber}
            onPageSize={(size) => { setPageSize(size); setPageNumber(1); }}
          />
        )}
      </PageCard>
    </div>
  );
}
