import { useEffect, useMemo, useState, type ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis,
} from 'recharts';
import { useAuth } from '@/auth/AuthContext';
import {
  Alert, ConfirmModal, EmptyState, ErrorAlert, FilterField, FilterStrip, Loading,
  PageCard, PageCardHeader, Pager, Pill,
} from '@/components/ui';
import {
  IconCalendar, IconCard, IconChart, IconMoney, IconPlus, IconUsers,
} from '@/components/icons';
import {
  salariesApi, type SalaryPaymentDto, type SalarySummaryDto,
} from '@/api/endpoints/salaries';
import { trainersApi } from '@/api/endpoints/trainers';
import type { Lookup, PagedResult } from '@/api/types';
import { date as fmtDate, money } from '@/lib/format';
import './ops.css';
import './admin.css';

const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

const period = (month: number, year: number) => `${String(month).padStart(2, '0')}/${year}`;

/* The palette lives in tokens.css; the literal is only the fallback when the var is unreadable. */
function chartColor(): string {
  const value = getComputedStyle(document.documentElement).getPropertyValue('--chart-1').trim();
  return value || '#4e8cf5';
}

const AXIS = { fontSize: 11, fill: '#8e97ab' };
const GRID = '#e5e8f0';

function StatTile(
  { title, value, caption, icon }:
  { title: string; value: ReactNode; caption?: ReactNode; icon: ReactNode },
) {
  return (
    <div className="stat-card">
      <div className="stat-icon" style={{ background: 'var(--primary-soft)', color: 'var(--primary-dark)' }}>
        {icon}
      </div>
      <div className="grow">
        <div className="stat-title">{title}</div>
        <div className="stat-value">{value}</div>
        {caption ? <div className="stat-caption">{caption}</div> : null}
      </div>
    </div>
  );
}

export default function SalariesPage() {
  const navigate = useNavigate();
  const { can, currency } = useAuth();
  const canView = can('salary.view');
  const manage = can('salary.manage');

  const now = new Date();
  const currentYear = now.getFullYear();
  const currentMonth = now.getMonth() + 1;
  const years = useMemo(
    () => Array.from({ length: 8 }, (_, i) => currentYear + 1 - i),
    [currentYear],
  );

  const [year, setYear] = useState(currentYear);
  const [month, setMonth] = useState<number | ''>('');
  const [trainerFilter, setTrainerFilter] = useState('');
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [reloadKey, setReloadKey] = useState(0);

  const [data, setData] = useState<PagedResult<SalaryPaymentDto> | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<unknown>(null);

  const [summary, setSummary] = useState<SalarySummaryDto | null>(null);
  const [summaryError, setSummaryError] = useState<unknown>(null);

  const [trainers, setTrainers] = useState<Lookup[]>([]);

  const [pendingDelete, setPendingDelete] = useState<SalaryPaymentDto | null>(null);
  const [busy, setBusy] = useState(false);

  /* Payments list ------------------------------------------------------------------- */
  useEffect(() => {
    if (!canView) return;
    const controller = new AbortController();
    (async () => {
      setLoading(true);
      try {
        const result = await salariesApi.list(
          {
            year,
            month: month === '' ? '' : month,
            trainerId: trainerFilter ? Number(trainerFilter) : '',
            pageNumber,
            pageSize,
          },
          controller.signal,
        );
        if (controller.signal.aborted) return;
        setData(result);
        setError(null);
      } catch (err) {
        if (controller.signal.aborted) return;
        setError(err);
        setData(null);
      } finally {
        if (!controller.signal.aborted) setLoading(false);
      }
    })();
    return () => controller.abort();
  }, [canView, year, month, trainerFilter, pageNumber, pageSize, reloadKey]);

  /* Year summary — tiles and the per-month chart ------------------------------------- */
  useEffect(() => {
    if (!canView) return;
    const controller = new AbortController();
    (async () => {
      try {
        const result = await salariesApi.summary(year, controller.signal);
        if (!controller.signal.aborted) { setSummary(result); setSummaryError(null); }
      } catch (err) {
        if (!controller.signal.aborted) { setSummary(null); setSummaryError(err); }
      }
    })();
    return () => controller.abort();
  }, [canView, year, reloadKey]);

  /* Lookups -------------------------------------------------------------------------- */
  useEffect(() => {
    if (!canView) return;
    const controller = new AbortController();
    (async () => {
      try {
        const rows = await trainersApi.lookup(true, controller.signal);
        if (!controller.signal.aborted) setTrainers(rows);
      } catch {
        if (!controller.signal.aborted) setTrainers([]);
      }
    })();
    return () => controller.abort();
  }, [canView]);

  const chartRows = useMemo(() => MONTHS.map((label, index) => ({
    label,
    value: summary?.months.find((m) => m.month === index + 1)?.totalNet ?? 0,
  })), [summary]);

  const thisMonth = year === currentYear
    ? summary?.months.find((m) => m.month === currentMonth)
    : undefined;

  const barColor = useMemo(chartColor, []);

  const confirmDelete = async () => {
    if (!pendingDelete) return;
    setBusy(true);
    try {
      await salariesApi.remove(pendingDelete.id);
      setPendingDelete(null);
      setReloadKey((k) => k + 1);
    } catch (err) {
      setError(err);
      setPendingDelete(null);
    } finally {
      setBusy(false);
    }
  };

  if (!canView) {
    return (
      <div className="page">
        <PageCard>
          <PageCardHeader icon={<IconMoney size={20} />} title="Trainer Salaries" />
          <div className="page-card-body">
            <Alert tone="warning">You do not have permission to view salary payments.</Alert>
          </div>
        </PageCard>
      </div>
    );
  }

  const items = data?.items ?? [];
  const firstIndex = (pageNumber - 1) * pageSize;

  return (
    <div className="page">
      {/* ------------------------------------------------------------------ tiles */}
      <div className="stat-grid">
        <StatTile
          title={`Paid in ${year}`}
          value={money(summary?.totalYear ?? 0, currency)}
          caption="Net salaries across the year"
          icon={<IconMoney size={20} />}
        />
        <StatTile
          title="This month"
          value={year === currentYear ? money(thisMonth?.totalNet ?? 0, currency) : '—'}
          caption={year === currentYear
            ? `${MONTHS[currentMonth - 1]} ${currentYear}`
            : `Select ${currentYear} to see the current month`}
          icon={<IconCalendar size={20} />}
        />
        <StatTile
          title="Trainers paid this month"
          value={year === currentYear ? (thisMonth?.payments ?? 0) : '—'}
          caption="One payment per trainer per month"
          icon={<IconUsers size={20} />}
        />
      </div>

      {/* ------------------------------------------------------------------ chart */}
      <div className="chart-card">
        <div className="chart-head">
          <div className="chart-title"><IconChart size={17} />Salaries by month — {year}</div>
        </div>
        {summaryError ? <ErrorAlert error={summaryError} /> : null}
        {!summaryError && (summary?.totalYear ?? 0) === 0 && (
          <div className="chart-empty">No salaries recorded for {year} yet.</div>
        )}
        {!summaryError && (summary?.totalYear ?? 0) > 0 && (
          <ResponsiveContainer width="100%" height={220}>
            <BarChart data={chartRows} margin={{ top: 8, right: 12, left: 4, bottom: 0 }}>
              <CartesianGrid stroke={GRID} strokeDasharray="3 3" vertical={false} />
              <XAxis dataKey="label" tick={AXIS} axisLine={false} tickLine={false} />
              <YAxis tick={AXIS} axisLine={false} tickLine={false} width={62} />
              <Tooltip formatter={(value) => money(Number(value), currency)} />
              <Bar dataKey="value" fill={barColor} radius={[5, 5, 0, 0]} maxBarSize={34} isAnimationActive={false} />
            </BarChart>
          </ResponsiveContainer>
        )}
      </div>

      {/* ------------------------------------------------------------------- list */}
      <PageCard>
        <PageCardHeader
          icon={<IconMoney size={20} />}
          title="Trainer Salaries"
          subtitle="Monthly salary payments; each one also books an expense under the Salaries category."
          actions={manage ? (
            <button className="btn btn-dark" onClick={() => navigate('/admin/salaries/new')}>
              <IconPlus size={15} /> New Payment
            </button>
          ) : undefined}
        />

        <FilterStrip>
          <FilterField label="Year">
            <select
              className="select"
              value={year}
              onChange={(e) => { setYear(Number(e.target.value)); setPageNumber(1); }}
            >
              {years.map((y) => <option key={y} value={y}>{y}</option>)}
            </select>
          </FilterField>
          <FilterField label="Month">
            <select
              className="select"
              value={month === '' ? '' : String(month)}
              onChange={(e) => { setMonth(e.target.value === '' ? '' : Number(e.target.value)); setPageNumber(1); }}
            >
              <option value="">All months</option>
              {MONTHS.map((label, index) => <option key={label} value={index + 1}>{label}</option>)}
            </select>
          </FilterField>
          <FilterField label="Trainer">
            <select
              className="select"
              value={trainerFilter}
              onChange={(e) => { setTrainerFilter(e.target.value); setPageNumber(1); }}
            >
              <option value="">All trainers</option>
              {trainers.map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}
            </select>
          </FilterField>
        </FilterStrip>

        {loading && <Loading message="Loading salary payments…" />}
        {!loading && Boolean(error) && <div className="page-card-body"><ErrorAlert error={error} /></div>}

        {!loading && !error && items.length === 0 && (
          <EmptyState
            icon={<IconMoney size={34} />}
            title="No salary payments"
            message="Record a trainer's monthly salary to see it here and in the expense ledger."
            action={manage
              ? (
                <button className="btn btn-dark" onClick={() => navigate('/admin/salaries/new')}>
                  <IconPlus size={15} /> New Payment
                </button>
              )
              : undefined}
          />
        )}

        {!loading && !error && items.length > 0 && (
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th className="idx">#</th>
                  <th className="wide">Trainer</th>
                  <th className="center fit">Period</th>
                  <th className="num">Base</th>
                  <th className="num">Bonus</th>
                  <th className="num">Deduction</th>
                  <th className="num">Net</th>
                  <th className="center fit">Paid on</th>
                  <th className="fit">Method</th>
                  <th className="fit">Reference</th>
                  <th className="actions">Actions</th>
                </tr>
              </thead>
              <tbody>
                {items.map((row, index) => (
                  <tr key={row.id}>
                    <td className="idx">{firstIndex + index + 1}</td>
                    <td>
                      <div className="cell-main">{row.trainerName}</div>
                      <div className="cell-sub">{row.notes || '—'}</div>
                    </td>
                    <td className="center fit"><Pill tone="primary">{period(row.periodMonth, row.periodYear)}</Pill></td>
                    <td className="num">{money(row.baseAmount, currency)}</td>
                    <td className="num">{row.bonus ? money(row.bonus, currency) : '—'}</td>
                    <td className="num">{row.deduction ? money(row.deduction, currency) : '—'}</td>
                    <td className="num"><strong>{money(row.netAmount, currency)}</strong></td>
                    <td className="center fit">
                      <span className="cell-icon"><IconCalendar size={13} />{fmtDate(row.paymentDate)}</span>
                    </td>
                    <td className="fit">
                      <span className="cell-icon"><IconCard size={13} />{row.paymentMethodName || '—'}</span>
                    </td>
                    <td className="muted fit">{row.transactionReference || '—'}</td>
                    <td className="actions">
                      {manage
                        ? <button className="btn btn-del" onClick={() => setPendingDelete(row)}>Delete</button>
                        : <span className="muted">—</span>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {!loading && !error && data && data.totalCount > 0 && (
          <Pager
            pageNumber={pageNumber}
            pageSize={pageSize}
            totalCount={data.totalCount}
            onPage={setPageNumber}
            onPageSize={(size) => { setPageSize(size); setPageNumber(1); }}
          />
        )}
      </PageCard>

      {pendingDelete && (
        <ConfirmModal
          title="Delete salary payment"
          message={(
            <>
              Delete the {period(pendingDelete.periodMonth, pendingDelete.periodYear)} payment for{' '}
              <strong>{pendingDelete.trainerName}</strong> ({money(pendingDelete.netAmount, currency)})?
              Only the salary record is removed — the booked expense stays in the ledger.
            </>
          )}
          confirmLabel="Delete payment"
          busy={busy}
          onConfirm={() => void confirmDelete()}
          onClose={() => setPendingDelete(null)}
        />
      )}
    </div>
  );
}
