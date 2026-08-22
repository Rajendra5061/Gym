import { useCallback, useEffect, useState, type ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { saveBlob } from '@/api/client';
import {
  paymentsApi, savePaymentMethod,
  type OutstandingBalanceDto, type PagedQuery, type PaymentMethodDto,
  type PaymentQuery, type PaymentRefundDto,
} from '@/api/endpoints/payments';
import { lookupMembers } from '@/api/endpoints/subscriptions';
import { useAuth } from '@/auth/AuthContext';
import {
  Alert, EmptyState, ErrorAlert, Field, FilterField, FilterMenu, FilterStrip, Loading, Modal,
  PageCard, PageCardHeader, Pager, Pill, SearchField, StatusPill,
} from '@/components/ui';
import {
  IconCalendar, IconCard, IconCheck, IconCrown, IconDownload, IconMessage,
  IconMoney, IconPlus, IconRefresh, IconSettings, IconUser, IconWarning,
} from '@/components/icons';
import { date as fmtDate, money, words } from '@/lib/format';
import {
  PaymentStatus, RefundStatus,
  type Lookup, type PagedResult, type PaymentDto,
} from '@/api/types';
import './billing.css';

type Tab = 'payments' | 'outstanding' | 'refunds' | 'methods';

const TABS: { key: Tab; label: string; icon: ReactNode }[] = [
  { key: 'payments', label: 'Payments', icon: <IconCard size={15} /> },
  { key: 'outstanding', label: 'Outstanding', icon: <IconWarning size={15} /> },
  { key: 'refunds', label: 'Refunds', icon: <IconRefresh size={15} /> },
  { key: 'methods', label: 'Payment Methods', icon: <IconSettings size={15} /> },
];

/**
 * Payments-tab filters. They live on the page rather than in the tab because the Filters menu
 * that edits them is rendered in the page header, which the tab does not own.
 */
interface PaymentFilters {
  memberId: number | '';
  status: PaymentStatus | '';
}

const EMPTY_PAYMENT_FILTERS: PaymentFilters = { memberId: '', status: '' };

export default function PaymentsPage() {
  const { can, currency } = useAuth();
  const mayCollect = can('payments.collect');
  const mayRefund = can('payments.refund');

  const [tab, setTab] = useState<Tab>('payments');
  const [members, setMembers] = useState<Lookup[]>([]);
  const [notice, setNotice] = useState('');

  const [draft, setDraft] = useState<PaymentFilters>(EMPTY_PAYMENT_FILTERS);
  const [applied, setApplied] = useState<PaymentFilters>(EMPTY_PAYMENT_FILTERS);

  // Member filter is a plain select, matching the reference screen; the list comes from the
  // member lookup with a generous take so most gyms fit in one dropdown.
  useEffect(() => {
    const controller = new AbortController();
    lookupMembers('', controller.signal, 200).then(setMembers).catch(() => setMembers([]));
    return () => controller.abort();
  }, []);

  // Badge on the trigger, counted off the applied filters against the empty defaults.
  const activeFilterCount = (['memberId', 'status'] as const)
    .filter((key) => applied[key] !== EMPTY_PAYMENT_FILTERS[key]).length;

  function applyFilters() {
    setApplied(draft);
  }

  function resetFilters() {
    setDraft(EMPTY_PAYMENT_FILTERS);
    setApplied(EMPTY_PAYMENT_FILTERS);
  }

  return (
    <div className="page">
      <PageCard>
        <PageCardHeader
          icon={<IconCard size={20} />}
          title="Payments"
          subtitle="View and filter payments by member and status."
          actions={(
            <>
              {/* Only the Payments tab is filtered by these, so the trigger follows that tab. */}
              {tab === 'payments' && (
                <FilterMenu
                  activeCount={activeFilterCount}
                  onApply={applyFilters}
                  onReset={resetFilters}
                >
                  <FilterField label="Member">
                    <select
                      className="select"
                      value={draft.memberId}
                      onChange={(e) => setDraft({ ...draft, memberId: e.target.value === '' ? '' : Number(e.target.value) })}
                    >
                      <option value="">All members</option>
                      {members.map((m) => <option key={m.id} value={m.id}>{m.name}{m.code ? ` (${m.code})` : ''}</option>)}
                    </select>
                  </FilterField>

                  <FilterField label="Status">
                    <select
                      className="select"
                      value={draft.status}
                      onChange={(e) => setDraft({ ...draft, status: e.target.value === '' ? '' : (Number(e.target.value) as PaymentStatus) })}
                    >
                      <option value="">All statuses</option>
                      {Object.values(PaymentStatus).filter((v): v is PaymentStatus => typeof v === 'number')
                        .map((v) => <option key={v} value={v}>{words(PaymentStatus[v])}</option>)}
                    </select>
                  </FilterField>
                </FilterMenu>
              )}

              {mayCollect ? (
                <Link className="btn btn-dark" to="/admin/payments/new"><IconPlus size={15} /> Record Payment</Link>
              ) : null}
            </>
          )}
        />

        <div className="bill-tabs" role="tablist">
          {TABS.map((t) => (
            <button
              key={t.key}
              role="tab"
              aria-selected={tab === t.key}
              className={`bill-tab ${tab === t.key ? 'on' : ''}`}
              onClick={() => { setTab(t.key); setNotice(''); }}
            >
              {t.icon}{t.label}
            </button>
          ))}
        </div>

        {notice && <div className="section-pad" style={{ paddingBottom: 0 }}><Alert tone="success">{notice}</Alert></div>}

        {tab === 'payments' && (
          <PaymentsTab
            filters={applied} currency={currency}
            mayCollect={mayCollect} mayRefund={mayRefund}
            onNotice={setNotice}
          />
        )}
        {tab === 'outstanding' && <OutstandingTab currency={currency} />}
        {tab === 'refunds' && <RefundsTab currency={currency} mayRefund={mayRefund} onNotice={setNotice} />}
        {tab === 'methods' && <MethodsTab />}
      </PageCard>
    </div>
  );
}

/* ------------------------------------------------------------- payments tab */

function PaymentsTab(
  { filters, currency, mayCollect, mayRefund, onNotice }:
  {
    filters: PaymentFilters; currency: string; mayCollect: boolean; mayRefund: boolean;
    onNotice: (message: string) => void;
  },
) {
  const [query, setQuery] = useState<PaymentQuery>({ pageNumber: 1, pageSize: 25 });

  // The applied filters are owned by the page, since the Filters menu sits in its header. Fold
  // them in during render rather than from an effect, so a freshly applied filter never fires a
  // request for the page number the operator was on before.
  const [seen, setSeen] = useState(filters);
  if (seen !== filters) {
    setSeen(filters);
    setQuery((q) => ({ ...q, pageNumber: 1, memberId: filters.memberId, status: filters.status }));
  }

  const [data, setData] = useState<PagedResult<PaymentDto> | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<unknown>(null);
  const [downloading, setDownloading] = useState<number | null>(null);
  const [confirming, setConfirming] = useState<PaymentDto | null>(null);
  const [refunding, setRefunding] = useState<PaymentDto | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError(null);
    paymentsApi.paged(query, controller.signal)
      .then((result) => { if (!controller.signal.aborted) setData(result); })
      .catch((err) => { if (!controller.signal.aborted) setError(err); })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [query]);

  const reload = useCallback(() => setQuery((q) => ({ ...q })), []);

  async function downloadReceipt(payment: PaymentDto) {
    setDownloading(payment.id);
    try {
      const { blob, fileName } = await paymentsApi.receiptPdf(payment.id);
      saveBlob(blob, fileName === 'download' ? `${payment.receiptNumber}.pdf` : fileName);
    } catch (err) {
      setError(err);
    } finally {
      setDownloading(null);
    }
  }

  const items = data?.items ?? [];
  const firstIndex = ((data?.pageNumber ?? 1) - 1) * (data?.pageSize ?? 25);

  return (
    <>
      {/* No filter strip on this tab: both of its filters are in the header's Filters menu, and
          this tab has no search box, so the strip would have held nothing but a Reset button
          duplicating the menu's own "Clear all". */}

      {error ? <div className="section-pad"><ErrorAlert error={error} /></div> : null}

      {loading ? (
        <Loading message="Loading payments…" />
      ) : items.length === 0 ? (
        <EmptyState icon={<IconCard size={34} />} title="No payments found" message="Adjust the filters, or record a payment." />
      ) : (
        <div className="table-wrap">
          <table className="table">
            <thead>
              <tr>
                <th className="idx">#</th>
                <th className="wide">Member</th>
                <th className="fit">Plan</th>
                <th className="num">Amount</th>
                <th className="fit">Date</th>
                <th className="fit">Mode</th>
                <th className="fit">Status</th>
                <th>Notes</th>
                <th className="actions">Actions</th>
              </tr>
            </thead>
            <tbody>
              {items.map((row, i) => (
                <tr key={row.id}>
                  <td className="idx">{firstIndex + i + 1}</td>
                  <td>
                    <div className="cell-icon"><IconUser size={14} /><span className="cell-main">{row.memberName}</span></div>
                    <div className="cell-sub">{row.memberCode}</div>
                  </td>
                  <td className="fit">
                    {row.planName
                      ? <span className="cell-icon"><IconCrown size={14} /><Pill tone="primary">{row.planName}</Pill></span>
                      : <span className="muted">—</span>}
                  </td>
                  <td className="money-cell">{money(row.finalAmount, currency)}</td>
                  <td className="fit"><span className="cell-icon"><IconCalendar size={14} />{fmtDate(row.paymentDate)}</span></td>
                  <td className="fit"><Pill tone="dark">{row.paymentMethodName}</Pill></td>
                  <td className="fit"><StatusPill status={row.statusText} /></td>
                  <td>
                    {row.notes
                      ? (
                        <span className="cell-icon" title={row.notes}>
                          <IconMessage size={14} />
                          <span className="cell-clip" style={{ maxWidth: 260 }}>{row.notes}</span>
                        </span>
                      )
                      : <span className="muted">—</span>}
                  </td>
                  <td className="actions">
                    <button
                      className="btn btn-outline btn-sm"
                      disabled={downloading === row.id}
                      onClick={() => downloadReceipt(row)}
                    >
                      <IconDownload size={13} /> {downloading === row.id ? 'Preparing…' : 'Receipt'}
                    </button>
                    {mayCollect && row.status === PaymentStatus.AwaitingConfirmation && (
                      <button className="btn btn-success btn-sm" onClick={() => setConfirming(row)}>
                        <IconCheck size={13} /> Confirm
                      </button>
                    )}
                    {mayCollect && row.status === PaymentStatus.Paid && (
                      <button className="btn btn-edit" onClick={() => setRefunding(row)}>Refund</button>
                    )}
                  </td>
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

      {confirming && (
        <ConfirmPaymentModal
          payment={confirming} currency={currency}
          onDone={() => { setConfirming(null); onNotice('Payment confirmed.'); reload(); }}
          onClose={() => setConfirming(null)}
        />
      )}
      {refunding && (
        <RefundModal
          payment={refunding} currency={currency} mayApprove={mayRefund}
          onDone={() => { setRefunding(null); onNotice('Refund request created.'); reload(); }}
          onClose={() => setRefunding(null)}
        />
      )}
    </>
  );
}

/* ---------------------------------------------------------- outstanding tab */

function OutstandingTab({ currency }: { currency: string }) {
  const [query, setQuery] = useState<PagedQuery>({ pageNumber: 1, pageSize: 25 });
  const [search, setSearch] = useState('');
  const [data, setData] = useState<PagedResult<OutstandingBalanceDto> | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<unknown>(null);

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError(null);
    paymentsApi.outstanding(query, controller.signal)
      .then((result) => { if (!controller.signal.aborted) setData(result); })
      .catch((err) => { if (!controller.signal.aborted) setError(err); })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [query]);

  const items = data?.items ?? [];
  const firstIndex = ((data?.pageNumber ?? 1) - 1) * (data?.pageSize ?? 25);

  return (
    <>
      {/* Search only, applied with Enter. This tab has a single filter, so it never grew a
          Filters menu — clearing the box and pressing Enter is its reset. */}
      <FilterStrip>
        <SearchField
          placeholder="Member name or code"
          value={search}
          onChange={setSearch}
          onSearch={() => setQuery((q) => ({ ...q, pageNumber: 1, search: search.trim() }))}
        />
      </FilterStrip>

      {error ? <div className="section-pad"><ErrorAlert error={error} /></div> : null}

      {loading ? (
        <Loading message="Loading outstanding balances…" />
      ) : items.length === 0 ? (
        <EmptyState icon={<IconCheck size={34} />} title="Nothing outstanding" message="Every active subscription is fully paid." />
      ) : (
        <div className="table-wrap">
          <table className="table">
            <thead>
              <tr>
                <th className="idx">#</th>
                <th className="wide">Member</th>
                <th className="fit">Plan</th>
                <th className="fit">Term</th>
                <th className="num">Final</th>
                <th className="num">Paid</th>
                <th className="num">Outstanding</th>
                <th className="center fit">Overdue</th>
              </tr>
            </thead>
            <tbody>
              {items.map((row, i) => (
                <tr key={`${row.subscriptionId}-${row.memberId}`}>
                  <td className="idx">{firstIndex + i + 1}</td>
                  <td>
                    <div className="cell-icon"><IconUser size={14} /><span className="cell-main">{row.memberName}</span></div>
                    <div className="cell-sub">{row.memberCode}{row.phone ? ` · ${row.phone}` : ''}</div>
                  </td>
                  <td className="fit"><span className="cell-icon"><IconCrown size={14} /><Pill tone="primary">{row.planName}</Pill></span></td>
                  <td className="fit">
                    <div>{fmtDate(row.startDate)} → {fmtDate(row.endDate)}</div>
                    <div className="cell-sub">{row.subscriptionCode}</div>
                  </td>
                  <td className="money-cell">{money(row.finalAmount, currency)}</td>
                  <td className="money-cell">{money(row.paidAmount, currency)}</td>
                  <td className="money-cell"><strong>{money(row.outstandingAmount, currency)}</strong></td>
                  <td className="center fit">
                    <Pill tone={row.daysOverdue > 0 ? 'danger' : 'neutral'}>{row.daysOverdue} d</Pill>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {data && data.totalCount > 0 && (
        <Pager
          pageNumber={data.pageNumber} pageSize={data.pageSize} totalCount={data.totalCount}
          onPage={(page) => setQuery((q) => ({ ...q, pageNumber: page }))}
          onPageSize={(size) => setQuery((q) => ({ ...q, pageNumber: 1, pageSize: size }))}
        />
      )}
    </>
  );
}

/* -------------------------------------------------------------- refunds tab */

function RefundsTab(
  { currency, mayRefund, onNotice }:
  { currency: string; mayRefund: boolean; onNotice: (message: string) => void },
) {
  const [query, setQuery] = useState<PagedQuery>({ pageNumber: 1, pageSize: 25 });
  const [data, setData] = useState<PagedResult<PaymentRefundDto> | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<unknown>(null);
  const [deciding, setDeciding] = useState<{ refund: PaymentRefundDto; approve: boolean } | null>(null);
  const [remarks, setRemarks] = useState('');
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError(null);
    paymentsApi.refunds(query, controller.signal)
      .then((result) => { if (!controller.signal.aborted) setData(result); })
      .catch((err) => { if (!controller.signal.aborted) setError(err); })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [query]);

  async function decide() {
    if (!deciding) return;
    setBusy(true);
    try {
      await paymentsApi.approveRefund({
        refundId: deciding.refund.id,
        approve: deciding.approve,
        remarks: remarks.trim() || null,
      });
      onNotice(deciding.approve ? 'Refund approved.' : 'Refund rejected.');
      setDeciding(null);
      setRemarks('');
      setQuery((q) => ({ ...q }));
    } catch (err) {
      setError(err);
      setDeciding(null);
    } finally {
      setBusy(false);
    }
  }

  const items = data?.items ?? [];
  const firstIndex = ((data?.pageNumber ?? 1) - 1) * (data?.pageSize ?? 25);

  return (
    <>
      {error ? <div className="section-pad"><ErrorAlert error={error} /></div> : null}

      {loading ? (
        <Loading message="Loading refunds…" />
      ) : items.length === 0 ? (
        <EmptyState icon={<IconRefresh size={34} />} title="No refunds raised" message="Refund requests appear here for approval." />
      ) : (
        <div className="table-wrap">
          <table className="table">
            <thead>
              <tr>
                <th className="idx">#</th>
                <th className="fit">Refund</th>
                <th className="wide">Member</th>
                <th className="num">Amount</th>
                <th>Reason</th>
                <th className="fit">Requested</th>
                <th className="fit">Status</th>
                <th className="actions">Actions</th>
              </tr>
            </thead>
            <tbody>
              {items.map((row, i) => (
                <tr key={row.id}>
                  <td className="idx">{firstIndex + i + 1}</td>
                  <td className="fit">
                    <div className="cell-main">{row.refundNumber}</div>
                    <div className="cell-sub">against {row.receiptNumber}</div>
                  </td>
                  <td><span className="cell-icon"><IconUser size={14} />{row.memberName}</span></td>
                  <td className="money-cell">{money(row.amount, currency)}</td>
                  <td>{row.reason}</td>
                  <td className="fit">
                    <div>{fmtDate(row.requestedAt)}</div>
                    {row.requestedByName ? <div className="cell-sub">by {row.requestedByName}</div> : null}
                  </td>
                  <td><StatusPill status={row.statusText} /></td>
                  <td className="actions">
                    {mayRefund && row.status === RefundStatus.Pending ? (
                      <>
                        <button className="btn btn-success btn-sm" onClick={() => setDeciding({ refund: row, approve: true })}>Approve</button>
                        <button className="btn btn-del" onClick={() => setDeciding({ refund: row, approve: false })}>Reject</button>
                      </>
                    ) : <span className="muted">—</span>}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {data && data.totalCount > 0 && (
        <Pager
          pageNumber={data.pageNumber} pageSize={data.pageSize} totalCount={data.totalCount}
          onPage={(page) => setQuery((q) => ({ ...q, pageNumber: page }))}
          onPageSize={(size) => setQuery((q) => ({ ...q, pageNumber: 1, pageSize: size }))}
        />
      )}

      {deciding && (
        <Modal
          title={deciding.approve ? 'Approve refund' : 'Reject refund'}
          icon={<IconMoney size={18} />}
          onClose={() => setDeciding(null)}
          width={520}
          footer={
            <>
              <button className="btn btn-outline" onClick={() => setDeciding(null)} disabled={busy}>Cancel</button>
              <button className={`btn btn-${deciding.approve ? 'success' : 'danger'}`} onClick={decide} disabled={busy}>
                {busy ? 'Working…' : deciding.approve ? 'Approve refund' : 'Reject refund'}
              </button>
            </>
          }
        >
          <div className="stack">
            <Alert tone={deciding.approve ? 'info' : 'warning'}>
              <div>
                <div style={{ fontWeight: 600 }}>{deciding.refund.refundNumber} — {deciding.refund.memberName}</div>
                <div>{money(deciding.refund.amount, currency)} · {deciding.refund.reason}</div>
              </div>
            </Alert>
            <Field label="Remarks">
              <textarea className="textarea" style={{ minHeight: 80 }} maxLength={512} value={remarks} onChange={(e) => setRemarks(e.target.value)} />
            </Field>
          </div>
        </Modal>
      )}
    </>
  );
}

/* -------------------------------------------------------------- methods tab */

/** A new method starts inactive-safe: active, no reference required, appended to the end. */
function blankMethod(nextOrder: number): PaymentMethodDto {
  return {
    id: 0, code: '', name: '',
    requiresReference: false, supportsQrCode: false, isActive: true,
    displayOrder: nextOrder,
  };
}

function MethodsTab() {
  const { can } = useAuth();
  const mayManage = can('settings.manage');

  const [methods, setMethods] = useState<PaymentMethodDto[] | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [reloadKey, setReloadKey] = useState(0);

  const [editing, setEditing] = useState<PaymentMethodDto | null>(null);
  const [saving, setSaving] = useState(false);
  const [formError, setFormError] = useState<unknown>(null);

  async function save() {
    if (!editing) return;
    setSaving(true);
    setFormError(null);
    try {
      await savePaymentMethod(editing);
      setEditing(null);
      setReloadKey((k) => k + 1);
    } catch (err) {
      setFormError(err);
    } finally {
      setSaving(false);
    }
  }

  useEffect(() => {
    const controller = new AbortController();
    paymentsApi.methods(controller.signal, false)
      .then((rows) => { if (!controller.signal.aborted) setMethods(rows); })
      .catch((err) => { if (!controller.signal.aborted) setError(err); });
    return () => controller.abort();
  }, [reloadKey]);

  if (error) return <div className="section-pad"><ErrorAlert error={error} /></div>;
  if (!methods) return <Loading message="Loading payment methods…" />;
  if (methods.length === 0 && !mayManage) {
    return (
      <EmptyState
        icon={<IconSettings size={34} />}
        title="No payment methods configured"
        message="Ask an administrator to add one."
      />
    );
  }

  return (
    <>
      <div className="section-pad" style={{ paddingBottom: 0 }}>
        <div className="row">
          <span className="muted" style={{ fontSize: 'var(--fs-sm)' }}>
            {mayManage
              ? 'Methods offered at the desk and on the payment form.'
              : 'Read-only — changing these needs the settings.manage permission.'}
          </span>
          {mayManage && (
            <button
              className="btn btn-dark btn-sm"
              style={{ marginLeft: 'auto' }}
              onClick={() => {
                setFormError(null);
                setEditing(blankMethod((methods.at(-1)?.displayOrder ?? 0) + 1));
              }}
            >
              <IconPlus size={14} /> Add Method
            </button>
          )}
        </div>
      </div>
      <div className="table-wrap">
        <table className="table">
          <thead>
            <tr>
              <th className="idx">#</th>
              <th className="fit">Code</th>
              <th className="wide">Name</th>
              <th className="center fit">Reference required</th>
              <th className="center fit">Supports QR</th>
              <th className="center fit">Order</th>
              <th className="fit">Status</th>
              {mayManage && <th className="actions">Actions</th>}
            </tr>
          </thead>
          <tbody>
            {methods.map((m, i) => (
              <tr key={m.id}>
                <td className="idx">{i + 1}</td>
                <td><Pill tone="dark">{m.code}</Pill></td>
                <td className="cell-main">{m.name}</td>
                <td className="center">{m.requiresReference ? <IconCheck size={15} /> : <span className="muted">—</span>}</td>
                <td className="center">{m.supportsQrCode ? <IconCheck size={15} /> : <span className="muted">—</span>}</td>
                <td className="center">{m.displayOrder}</td>
                <td><StatusPill status={m.isActive ? 'Active' : 'Inactive'} /></td>
                {mayManage && (
                  <td className="actions">
                    <button
                      className="btn btn-edit"
                      onClick={() => { setFormError(null); setEditing({ ...m }); }}
                    >
                      Edit
                    </button>
                  </td>
                )}
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {editing && (
        <Modal
          title={editing.id === 0 ? 'Add payment method' : `Edit ${editing.name || 'method'}`}
          onClose={() => setEditing(null)}
          footer={(
            <>
              <button className="btn btn-outline" onClick={() => setEditing(null)}>Cancel</button>
              <button
                className="btn btn-primary"
                onClick={() => void save()}
                disabled={saving || !editing.code.trim() || !editing.name.trim()}
              >
                {saving ? 'Saving…' : 'Save method'}
              </button>
            </>
          )}
        >
          {formError ? <ErrorAlert error={formError} /> : null}

          <div className="form-grid-2">
            <Field
              label="Code"
              required
              help="Short key used by the API, e.g. CASH or UPI. Not shown to members."
            >
              <input
                className="input"
                value={editing.code}
                onChange={(e) => setEditing({ ...editing, code: e.target.value.toUpperCase() })}
              />
            </Field>
            <Field label="Name" required help="What the desk sees on the payment form.">
              <input
                className="input"
                value={editing.name}
                onChange={(e) => setEditing({ ...editing, name: e.target.value })}
              />
            </Field>
            <Field label="Display order" help="Lowest first in the payment method list.">
              <input
                className="input"
                type="number"
                value={editing.displayOrder}
                onChange={(e) => setEditing({ ...editing, displayOrder: Number(e.target.value) })}
              />
            </Field>
          </div>

          <div className="stack" style={{ marginTop: 14 }}>
            <label className="check-inline">
              <input
                type="checkbox"
                checked={editing.requiresReference}
                onChange={(e) => setEditing({ ...editing, requiresReference: e.target.checked })}
              />
              <span>Reference required — the desk must enter a transaction id before saving.</span>
            </label>
            <label className="check-inline">
              <input
                type="checkbox"
                checked={editing.supportsQrCode}
                onChange={(e) => setEditing({ ...editing, supportsQrCode: e.target.checked })}
              />
              <span>Supports QR — offers the UPI collection screen for this method.</span>
            </label>
            <label className="check-inline">
              <input
                type="checkbox"
                checked={editing.isActive}
                onChange={(e) => setEditing({ ...editing, isActive: e.target.checked })}
              />
              <span>Active — inactive methods stay on past payments but are not offered.</span>
            </label>
          </div>
        </Modal>
      )}
    </>
  );
}

/* ------------------------------------------------------------------- modals */

function ConfirmPaymentModal(
  { payment, currency, onDone, onClose }:
  { payment: PaymentDto; currency: string; onDone: () => void; onClose: () => void },
) {
  const [transactionReference, setReference] = useState(payment.transactionReference ?? '');
  const [remarks, setRemarks] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);

  async function save() {
    setBusy(true); setError(null);
    try {
      await paymentsApi.confirm({
        paymentId: payment.id,
        transactionReference: transactionReference.trim() || null,
        remarks: remarks.trim() || null,
      });
      onDone();
    } catch (err) { setError(err); } finally { setBusy(false); }
  }

  return (
    <Modal
      title="Confirm payment" icon={<IconCheck size={18} />} onClose={onClose} width={560}
      footer={
        <>
          <button className="btn btn-outline" onClick={onClose} disabled={busy}>Cancel</button>
          <button className="btn btn-success" onClick={save} disabled={busy}>{busy ? 'Confirming…' : 'Confirm payment'}</button>
        </>
      }
    >
      <div className="stack">
        {error ? <ErrorAlert error={error} /> : null}
        <Alert tone="warning">
          <div>
            <div style={{ fontWeight: 600 }}>{payment.receiptNumber} — {payment.memberName}</div>
            <div>
              {money(payment.finalAmount, currency)} via {payment.paymentMethodName}. This transfer has
              <strong> not</strong> been verified automatically — only confirm it once you have seen the
              money arrive against the reference below.
            </div>
          </div>
        </Alert>
        <Field label="Transaction reference" help="The bank / UPI reference you reconciled against.">
          <input className="input" maxLength={128} value={transactionReference} onChange={(e) => setReference(e.target.value)} />
        </Field>
        <Field label="Remarks">
          <textarea className="textarea" style={{ minHeight: 70 }} maxLength={512} value={remarks} onChange={(e) => setRemarks(e.target.value)} />
        </Field>
      </div>
    </Modal>
  );
}

function RefundModal(
  { payment, currency, mayApprove, onDone, onClose }:
  { payment: PaymentDto; currency: string; mayApprove: boolean; onDone: () => void; onClose: () => void },
) {
  const [amount, setAmount] = useState(String(payment.netAmount));
  const [reason, setReason] = useState('');
  const [refundMethod, setRefundMethod] = useState('');
  const [transactionReference, setReference] = useState('');
  const [remarks, setRemarks] = useState('');
  const [approveImmediately, setApprove] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);

  async function save() {
    setBusy(true); setError(null);
    try {
      await paymentsApi.createRefund({
        paymentId: payment.id,
        amount: Number(amount),
        reason: reason.trim(),
        refundMethod: refundMethod.trim() || null,
        transactionReference: transactionReference.trim() || null,
        remarks: remarks.trim() || null,
        approveImmediately: mayApprove && approveImmediately,
      });
      onDone();
    } catch (err) { setError(err); } finally { setBusy(false); }
  }

  return (
    <Modal
      title="Raise a refund" icon={<IconRefresh size={18} />} onClose={onClose} width={620}
      footer={
        <>
          <button className="btn btn-outline" onClick={onClose} disabled={busy}>Cancel</button>
          <button className="btn btn-dark" onClick={save} disabled={busy || reason.trim().length < 5 || !amount}>
            {busy ? 'Saving…' : 'Create refund request'}
          </button>
        </>
      }
    >
      <div className="stack">
        {error ? <ErrorAlert error={error} /> : null}
        <Alert tone="info">
          <div>
            <div style={{ fontWeight: 600 }}>{payment.receiptNumber} — {payment.memberName}</div>
            <div>
              Paid {money(payment.finalAmount, currency)}, already refunded {money(payment.refundedAmount, currency)},
              refundable {money(payment.netAmount, currency)} (server figures).
            </div>
          </div>
        </Alert>
        <div className="form-grid">
          <Field label="Refund amount" required>
            <div className="input-group">
              <span className="input-icon">{currency}</span>
              <input className="input" type="number" min={0} step="0.01" value={amount} onChange={(e) => setAmount(e.target.value)} />
            </div>
          </Field>
          <Field label="Refund method" help="Free text, e.g. Cash, UPI, Bank transfer.">
            <input className="input" maxLength={64} value={refundMethod} onChange={(e) => setRefundMethod(e.target.value)} />
          </Field>
          <Field label="Transaction reference">
            <input className="input" maxLength={128} value={transactionReference} onChange={(e) => setReference(e.target.value)} />
          </Field>
        </div>
        <Field label="Reason" required help="At least 5 characters.">
          <textarea className="textarea" style={{ minHeight: 80 }} maxLength={512} value={reason} onChange={(e) => setReason(e.target.value)} />
        </Field>
        <Field label="Remarks">
          <input className="input" maxLength={512} value={remarks} onChange={(e) => setRemarks(e.target.value)} />
        </Field>
        {mayApprove && (
          <label className="check-inline">
            <input type="checkbox" checked={approveImmediately} onChange={(e) => setApprove(e.target.checked)} />
            Approve this refund immediately
          </label>
        )}
      </div>
    </Modal>
  );
}
