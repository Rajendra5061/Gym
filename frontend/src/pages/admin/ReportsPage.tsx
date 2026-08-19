import { useEffect, useMemo, useState } from 'react';
import { saveBlob } from '@/api/client';
import { useAuth } from '@/auth/AuthContext';
import {
  EmptyState, ErrorAlert, FilterField, FilterMenu, Loading, PageCard, PageCardHeader, Pager,
} from '@/components/ui';
import {
  IconChart, IconDownload, IconFile, IconSearch,
} from '@/components/icons';
import {
  QUICK_RANGES, REPORT_CATALOGUE, REPORT_GROUPS, ReportType, alignmentFor, exportReport,
  formatCell, readCell, reportMemberLookup, reportMethodLookup, reportPlanLookup,
  reportTrainerLookup, reportUserLookup, resolveQuickRange, runReport,
  type PaymentMethodLookup, type QuickRange, type ReportFilterKey, type ReportRequestDto,
  type ReportResultDto,
} from '@/api/endpoints/reports';
import type { Lookup } from '@/api/types';
import { MemberStatus, SubscriptionStatus } from '@/api/types';
import { date as fmtDate, money } from '@/lib/format';
import './ops.css';

const MEMBER_STATUSES = [
  { value: MemberStatus.Active, label: 'Active' },
  { value: MemberStatus.Inactive, label: 'Inactive' },
  { value: MemberStatus.Suspended, label: 'Suspended' },
  { value: MemberStatus.Expired, label: 'Expired' },
];

const SUBSCRIPTION_STATUSES = [
  { value: SubscriptionStatus.Pending, label: 'Pending' },
  { value: SubscriptionStatus.Active, label: 'Active' },
  { value: SubscriptionStatus.Frozen, label: 'Frozen' },
  { value: SubscriptionStatus.Expired, label: 'Expired' },
  { value: SubscriptionStatus.Cancelled, label: 'Cancelled' },
];

interface Criteria {
  from: string;
  to: string;
  memberId: string;
  trainerId: string;
  planId: string;
  methodId: string;
  userId: string;
  status: string;
  groupBy: string;
}

function defaultCriteria(): Criteria {
  const { from, to } = resolveQuickRange('month');
  return { from, to, memberId: '', trainerId: '', planId: '', methodId: '', userId: '', status: '', groupBy: 'Day' };
}

export default function ReportsPage() {
  const { can, currency } = useAuth();
  const canExport = can('reports.export');

  const [selected, setSelected] = useState<ReportType>(ReportType.MemberList);
  const [criteria, setCriteria] = useState<Criteria>(defaultCriteria);
  const [quick, setQuick] = useState<QuickRange | ''>('month');
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [runToken, setRunToken] = useState(0);

  const [report, setReport] = useState<ReportResultDto | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [exporting, setExporting] = useState<'excel' | 'pdf' | null>(null);
  const [exportError, setExportError] = useState<unknown>(null);

  const [memberTerm, setMemberTerm] = useState('');
  const [members, setMembers] = useState<Lookup[]>([]);
  const [trainers, setTrainers] = useState<Lookup[]>([]);
  const [plans, setPlans] = useState<Lookup[]>([]);
  const [users, setUsers] = useState<Lookup[]>([]);
  const [methods, setMethods] = useState<PaymentMethodLookup[]>([]);

  const meta = useMemo(
    () => REPORT_CATALOGUE.find((r) => r.type === selected) ?? REPORT_CATALOGUE[0],
    [selected],
  );
  const uses = (key: ReportFilterKey) => meta.filters.includes(key);

  const body: ReportRequestDto = useMemo(() => ({
    reportType: selected,
    fromDate: criteria.from || null,
    toDate: criteria.to || null,
    memberId: criteria.memberId ? Number(criteria.memberId) : null,
    trainerId: criteria.trainerId ? Number(criteria.trainerId) : null,
    membershipPlanId: criteria.planId ? Number(criteria.planId) : null,
    paymentMethodId: criteria.methodId ? Number(criteria.methodId) : null,
    userId: criteria.userId ? Number(criteria.userId) : null,
    status: criteria.status ? Number(criteria.status) : null,
    groupBy: criteria.groupBy || 'Day',
    pageNumber,
    pageSize,
  }), [selected, criteria, pageNumber, pageSize]);

  /* Contextual lookups, fetched once ------------------------------------------------ */
  useEffect(() => {
    const controller = new AbortController();
    (async () => {
      const settle = async <T,>(load: () => Promise<T[]>, apply: (v: T[]) => void) => {
        try {
          const rows = await load();
          if (!controller.signal.aborted) apply(rows);
        } catch {
          if (!controller.signal.aborted) apply([]);
        }
      };
      await Promise.all([
        settle(() => reportPlanLookup(controller.signal), setPlans),
        settle(() => reportTrainerLookup(controller.signal), setTrainers),
        settle(() => reportUserLookup(controller.signal), setUsers),
        settle(() => reportMethodLookup(controller.signal), setMethods),
      ]);
    })();
    return () => controller.abort();
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    const timer = window.setTimeout(() => {
      (async () => {
        try {
          const rows = await reportMemberLookup(memberTerm, controller.signal);
          if (!controller.signal.aborted) setMembers(rows);
        } catch {
          if (!controller.signal.aborted) setMembers([]);
        }
      })();
    }, memberTerm ? 300 : 0);
    return () => { window.clearTimeout(timer); controller.abort(); };
  }, [memberTerm]);

  /* Running the report -------------------------------------------------------------- */
  useEffect(() => {
    const controller = new AbortController();
    (async () => {
      setLoading(true);
      try {
        const result = await runReport(body, controller.signal);
        if (controller.signal.aborted) return;
        setReport(result);
        setError(null);
      } catch (err) {
        if (controller.signal.aborted) return;
        setError(err);
        setReport(null);
      } finally {
        if (!controller.signal.aborted) setLoading(false);
      }
    })();
    return () => controller.abort();
    // Filter edits only take effect through Run report / the Filter button (runToken).
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selected, pageNumber, pageSize, runToken]);

  const run = () => { setPageNumber(1); setRunToken((t) => t + 1); };

  // Badge on the trigger. The date range is deliberately excluded: every report always carries
  // one, so counting it would leave the badge permanently lit and meaningless. A non-default
  // quick range does count, since that is a choice the operator made.
  const activeFilterCount =
    (['memberId', 'trainerId', 'planId', 'methodId', 'userId', 'status'] as const)
      .filter((key) => criteria[key] !== '').length
    + (criteria.groupBy !== 'Day' ? 1 : 0)
    + (quick !== 'month' ? 1 : 0);

  const reset = () => {
    setCriteria(defaultCriteria());
    setQuick('month');
    setMemberTerm('');
    setPageNumber(1);
    setRunToken((t) => t + 1);
  };

  const applyQuick = (key: QuickRange) => {
    const { from, to } = resolveQuickRange(key);
    setQuick(key);
    setCriteria((c) => ({ ...c, from, to }));
  };

  const doExport = async (format: 'excel' | 'pdf') => {
    setExporting(format);
    setExportError(null);
    try {
      const { blob, fileName } = await exportReport(format, body);
      saveBlob(blob, fileName);
    } catch (err) {
      setExportError(err);
    } finally {
      setExporting(null);
    }
  };

  const pickReport = (type: ReportType) => {
    setSelected(type);
    setPageNumber(1);
    setReport(null);
  };

  const symbol = report?.currencySymbol || currency;
  const columns = report?.columns ?? [];
  const rows = report?.rows ?? [];
  const totals = Object.entries(report?.totals ?? {});
  const firstIndex = ((report?.pageNumber ?? pageNumber) - 1) * (report?.pageSize ?? pageSize);

  const statusOptions = selected === ReportType.SubscriptionReport
    ? SUBSCRIPTION_STATUSES
    : MEMBER_STATUSES;

  return (
    <div className="page">
      {/* The catalogue used to be a fixed left rail. Sixteen reports fit a single grouped select
          just as well, and dropping the rail hands its ~260px back to the report table, which is
          the part that actually needs the width. */}
      <PageCard>
          <PageCardHeader
            icon={<IconChart size={20} />}
            title={meta.name}
            subtitle={meta.hint}
            actions={(
              // The report's parameters live in the menu; Run report and the exports stay out in
              // the open below, since those are the actions this screen exists to perform.
              <FilterMenu
                activeCount={activeFilterCount}
                onApply={run}
                onReset={reset}
                label="Filters"
              >
                <FilterField label="From">
              <input
                className="input"
                type="date"
                value={criteria.from}
                onChange={(e) => { setCriteria({ ...criteria, from: e.target.value }); setQuick(''); }}
              />
            </FilterField>
            <FilterField label="To">
              <input
                className="input"
                type="date"
                value={criteria.to}
                onChange={(e) => { setCriteria({ ...criteria, to: e.target.value }); setQuick(''); }}
              />
            </FilterField>
            <FilterField label="Quick range">
              <div className="ops-quick-ranges">
                {QUICK_RANGES.map((range) => (
                  <button
                    key={range.key}
                    type="button"
                    className={`ops-quick-range ${quick === range.key ? 'active' : ''}`}
                    onClick={() => applyQuick(range.key)}
                  >
                    {range.label}
                  </button>
                ))}
              </div>
            </FilterField>

            {uses('member') && (
              <FilterField label="Member">
                <div className="input-group">
                  <span className="input-icon"><IconSearch size={14} /></span>
                  <input
                    className="input"
                    placeholder="Search member"
                    value={memberTerm}
                    onChange={(e) => setMemberTerm(e.target.value)}
                  />
                </div>
                <select
                  className="select"
                  value={criteria.memberId}
                  onChange={(e) => setCriteria({ ...criteria, memberId: e.target.value })}
                >
                  <option value="">All members</option>
                  {members.map((m) => (
                    <option key={m.id} value={m.id}>{m.name}{m.code ? ` (${m.code})` : ''}</option>
                  ))}
                </select>
              </FilterField>
            )}

            {uses('trainer') && (
              <FilterField label="Trainer">
                <select
                  className="select"
                  value={criteria.trainerId}
                  onChange={(e) => setCriteria({ ...criteria, trainerId: e.target.value })}
                >
                  <option value="">All trainers</option>
                  {trainers.map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}
                </select>
              </FilterField>
            )}

            {uses('plan') && (
              <FilterField label="Membership plan">
                <select
                  className="select"
                  value={criteria.planId}
                  onChange={(e) => setCriteria({ ...criteria, planId: e.target.value })}
                >
                  <option value="">All plans</option>
                  {plans.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
                </select>
              </FilterField>
            )}

            {uses('method') && (
              <FilterField label="Payment method">
                <select
                  className="select"
                  value={criteria.methodId}
                  onChange={(e) => setCriteria({ ...criteria, methodId: e.target.value })}
                >
                  <option value="">All methods</option>
                  {methods.map((m) => <option key={m.id} value={m.id}>{m.name}</option>)}
                </select>
              </FilterField>
            )}

            {uses('user') && (
              <FilterField label="User">
                <select
                  className="select"
                  value={criteria.userId}
                  onChange={(e) => setCriteria({ ...criteria, userId: e.target.value })}
                >
                  <option value="">All users</option>
                  {users.map((u) => <option key={u.id} value={u.id}>{u.name}</option>)}
                </select>
              </FilterField>
            )}

            {uses('status') && (
              <FilterField label="Status">
                <select
                  className="select"
                  value={criteria.status}
                  onChange={(e) => setCriteria({ ...criteria, status: e.target.value })}
                >
                  <option value="">Any status</option>
                  {statusOptions.map((s) => <option key={s.value} value={s.value}>{s.label}</option>)}
                </select>
              </FilterField>
            )}

            {uses('groupBy') && (
              <FilterField label="Group by">
                <select
                  className="select"
                  value={criteria.groupBy}
                  onChange={(e) => setCriteria({ ...criteria, groupBy: e.target.value })}
                >
                  <option value="Day">Day</option>
                  <option value="Week">Week</option>
                  <option value="Month">Month</option>
                </select>
              </FilterField>
            )}

              </FilterMenu>
            )}
          />

          <div className="ops-report-actions">
            <label className="ops-report-picker">
              <span>Report</span>
              <select
                className="select"
                value={selected}
                onChange={(e) => pickReport(Number(e.target.value) as ReportType)}
              >
                {REPORT_GROUPS.map((group) => {
                  const entries = REPORT_CATALOGUE.filter((r) => r.group === group);
                  if (entries.length === 0) return null;
                  return (
                    <optgroup key={group} label={group}>
                      {entries.map((entry) => (
                        <option key={entry.type} value={entry.type}>{entry.name}</option>
                      ))}
                    </optgroup>
                  );
                })}
              </select>
            </label>

            <button className="btn btn-dark" onClick={run} disabled={loading}>
              {loading ? 'Running…' : 'Run report'}
            </button>
            <button
              className="btn btn-outline"
              onClick={() => void doExport('excel')}
              disabled={!canExport || exporting !== null || !report}
            >
              <IconDownload size={14} /> {exporting === 'excel' ? 'Exporting…' : 'Export Excel'}
            </button>
            <button
              className="btn btn-outline"
              onClick={() => void doExport('pdf')}
              disabled={!canExport || exporting !== null || !report}
            >
              <IconFile size={14} /> {exporting === 'pdf' ? 'Exporting…' : 'Export PDF'}
            </button>
            <button className="btn btn-outline" onClick={() => window.print()} disabled={!report}>
              Print
            </button>
          </div>

          {exportError ? <div className="page-card-body"><ErrorAlert error={exportError} /></div> : null}

          {report && (
            <div className="ops-report-head">
              <div className="ops-report-head-title">{report.title}</div>
              <div className="ops-report-head-meta">
                {report.subtitle ? `${report.subtitle} · ` : ''}
                {report.fromDate || report.toDate
                  ? `${fmtDate(report.fromDate)} – ${fmtDate(report.toDate)}`
                  : 'All dates'}
                {report.generatedByName ? ` · Generated by ${report.generatedByName}` : ''}
                {` · ${report.totalCount} row${report.totalCount === 1 ? '' : 's'}`}
              </div>
            </div>
          )}

          {loading && <Loading message="Running report…" />}

          {!loading && Boolean(error) && <div className="page-card-body"><ErrorAlert error={error} /></div>}

          {!loading && !error && report && rows.length === 0 && (
            <EmptyState
              icon={<IconChart size={34} />}
              title="No rows for these filters"
              message="Widen the date range or clear a filter, then run the report again."
            />
          )}

          {!loading && !error && report && rows.length > 0 && (
            <div className="table-wrap">
              <table className="table">
                <thead>
                  <tr>
                    <th className="idx">#</th>
                    {columns.map((column) => (
                      <th key={column.key} className={alignmentFor(column)}>{column.header}</th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {rows.map((row, index) => (
                    <tr key={index}>
                      <td className="idx">{firstIndex + index + 1}</td>
                      {columns.map((column) => (
                        <td key={column.key} className={alignmentFor(column)}>
                          {formatCell(readCell(row, column.key), column.dataType, symbol)}
                        </td>
                      ))}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          {!loading && !error && report && totals.length > 0 && (
            <div className="ops-totals-strip">
              {totals.map(([label, value]) => (
                <div className="ops-total-chip" key={label}>
                  <div className="ops-total-chip-label">{label}</div>
                  <div className="ops-total-chip-value">{money(value, symbol)}</div>
                </div>
              ))}
            </div>
          )}

          {!loading && !error && report && report.totalCount > 0 && (
            <Pager
              pageNumber={report.pageNumber || pageNumber}
              pageSize={report.pageSize || pageSize}
              totalCount={report.totalCount}
              onPage={setPageNumber}
              onPageSize={(size) => { setPageSize(size); setPageNumber(1); }}
            />
          )}
      </PageCard>
    </div>
  );
}
