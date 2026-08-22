/**
 * The member's own payment history, with the receipt PDF for each row.
 *
 * The member-scoped payment endpoint answers with the whole history in one response, so the
 * pager here works over the loaded list; only the member id from the signed-in account is ever
 * requested.
 */

import { useEffect, useMemo, useState } from 'react';
import {
  Alert, EmptyState, ErrorAlert, Loading, PageCard, PageCardHeader, Pager, Pill, StatusPill,
} from '@/components/ui';
import { IconCalendar, IconCard, IconCrown, IconDownload, IconMoney, IconUser } from '@/components/icons';
import { saveBlob } from '@/api/client';
import { isUnavailable, memberApi, optional } from '@/api/endpoints/member';
import type { PaymentDto } from '@/api/types';
import { useAuth } from '@/auth/AuthContext';
import { date, money } from '@/lib/format';
import './member.css';

export default function MyPaymentsPage() {
  const { user, currency } = useAuth();
  const memberId = user?.memberId ?? null;

  const [rows, setRows] = useState<PaymentDto[]>([]);
  const [partial, setPartial] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<unknown>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [downloading, setDownloading] = useState<number | null>(null);

  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(25);

  useEffect(() => {
    if (memberId === null) { setLoading(false); return; }
    const ctrl = new AbortController();
    setLoading(true);
    setError(null);

    optional(() => memberApi.payments(memberId, ctrl.signal))
      .then(async (payments) => {
        if (ctrl.signal.aborted) return;
        if (payments) { setRows(payments); setPartial(false); return; }
        const dashboard = await memberApi.dashboard(memberId, ctrl.signal);
        if (ctrl.signal.aborted) return;
        setRows(dashboard.recentPayments ?? []);
        setPartial(true);
      })
      .catch((e) => { if (!ctrl.signal.aborted) setError(e); })
      .finally(() => { if (!ctrl.signal.aborted) setLoading(false); });

    return () => ctrl.abort();
  }, [memberId]);

  const sorted = useMemo(
    () => [...rows].sort((a, b) => new Date(b.paymentDate).getTime() - new Date(a.paymentDate).getTime()),
    [rows],
  );
  const pageRows = useMemo(
    () => sorted.slice((pageNumber - 1) * pageSize, pageNumber * pageSize),
    [sorted, pageNumber, pageSize],
  );
  const totalPaid = useMemo(() => sorted.reduce((sum, row) => sum + row.netAmount, 0), [sorted]);

  async function downloadReceipt(payment: PaymentDto) {
    setDownloading(payment.id);
    setNotice(null);
    try {
      const file = await memberApi.receiptPdf(payment.id);
      saveBlob(file.blob, file.fileName || `${payment.receiptNumber}.pdf`);
    } catch (e) {
      setNotice(isUnavailable(e)
        ? 'Receipt downloads are not enabled for your account. Ask the front desk for a printed copy.'
        : 'The receipt could not be generated. Please try again.');
    } finally {
      setDownloading(null);
    }
  }

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
          icon={<IconCard size={20} />}
          title="My Payments"
          subtitle={`${sorted.length} receipt${sorted.length === 1 ? '' : 's'}${partial ? ' (most recent)' : ''} raised against your membership.`}
          actions={
            <span className="btn btn-dark" style={{ cursor: 'default' }}>
              <IconMoney size={15} /> {money(totalPaid, currency)} paid
            </span>
          }
        />

        {notice && <div style={{ padding: '16px 20px 0' }}><Alert tone="warning">{notice}</Alert></div>}
        {error ? <div style={{ padding: 20 }}><ErrorAlert error={error} /></div> : null}

        {loading ? <Loading message="Loading your payments…" /> : pageRows.length === 0 ? (
          <EmptyState
            icon={<IconCard size={34} />}
            title="No payments yet"
            message="Receipts appear here as soon as a payment is recorded against your membership."
          />
        ) : (
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th className="idx">#</th>
                  <th className="fit">Plan</th>
                  <th className="fit">Amount</th>
                  <th className="fit">Payment Date</th>
                  <th className="fit">Mode</th>
                  <th className="fit">Status</th>
                  <th className="wide">Notes</th>
                  <th className="actions">Receipt</th>
                </tr>
              </thead>
              <tbody>
                {pageRows.map((row, index) => (
                  <tr key={row.id}>
                    <td className="idx">{(pageNumber - 1) * pageSize + index + 1}</td>
                    <td className="fit">
                      <Pill tone="primary"><IconCrown size={12} /> {row.planName ?? 'General'}</Pill>
                      <div className="cell-sub">{row.receiptNumber}</div>
                    </td>
                    <td className="fit"><span className="cell-icon"><IconMoney size={14} />{money(row.finalAmount, currency)}</span></td>
                    <td className="fit"><span className="cell-icon"><IconCalendar size={14} />{date(row.paymentDate)}</span></td>
                    <td className="fit"><Pill tone="info">{row.paymentMethodName}</Pill></td>
                    <td className="fit"><StatusPill status={row.statusText} /></td>
                    <td className="muted"><span className="cell-clip" style={{ maxWidth: 240 }}>{row.notes || '—'}</span></td>
                    <td className="actions">
                      <button
                        className="btn btn-outline btn-sm"
                        disabled={downloading === row.id}
                        onClick={() => void downloadReceipt(row)}
                      >
                        <IconDownload size={13} /> {downloading === row.id ? 'Preparing…' : 'Receipt'}
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {sorted.length > 0 && (
          <Pager
            pageNumber={pageNumber}
            pageSize={pageSize}
            totalCount={sorted.length}
            onPage={setPageNumber}
            onPageSize={(size) => { setPageSize(size); setPageNumber(1); }}
          />
        )}
      </PageCard>
    </div>
  );
}
