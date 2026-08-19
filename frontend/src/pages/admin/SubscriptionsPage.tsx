import {
  useCallback, useEffect, useMemo, useRef, useState, type ReactNode,
} from 'react';
import {
  lookupMembers, subscriptionsApi,
  type CollectPaymentInline, type QuoteRequest, type SubscriptionHistoryDto, type SubscriptionQuery,
  type SubscriptionQuoteDto, type SubscriptionTrainer,
} from '@/api/endpoints/subscriptions';
import { plansApi } from '@/api/endpoints/plans';
import { paymentsApi, type PaymentMethodDto, type UpiPaymentIntentDto } from '@/api/endpoints/payments';
import { trainersApi } from '@/api/endpoints/trainers';
import { useAuth } from '@/auth/AuthContext';
import {
  Alert, EmptyState, ErrorAlert, Field, FilterField, FilterMenu, FilterStrip, Loading, Modal,
  PageCard, PageCardHeader, Pager, Pill, StatusPill, type PillTone,
} from '@/components/ui';
import {
  IconCheck, IconCheckSquare, IconClock, IconCrown, IconFile, IconMoney, IconQr,
  IconPlus, IconRefresh, IconSearch, IconUser,
} from '@/components/icons';
import { date as fmtDate, dateTime, money, words } from '@/lib/format';
import {
  PaymentStatus, SubscriptionStatus,
  type Lookup, type PagedResult, type SubscriptionDto,
} from '@/api/types';
import { UpiModal } from './upiQr';
import './billing.css';

/* ------------------------------------------------------------------- date help */

/** Local yyyy-MM-dd. `isoDate` in lib/format goes through UTC, which can slip a day. */
export function todayIso(): string {
  const d = new Date();
  return `${d.getFullYear()}-${`${d.getMonth() + 1}`.padStart(2, '0')}-${`${d.getDate()}`.padStart(2, '0')}`;
}

export function addDaysIso(iso: string, days: number): string {
  const d = new Date(`${iso}T00:00:00`);
  if (Number.isNaN(d.getTime())) return todayIso();
  d.setDate(d.getDate() + days);
  return `${d.getFullYear()}-${`${d.getMonth() + 1}`.padStart(2, '0')}-${`${d.getDate()}`.padStart(2, '0')}`;
}

function toIsoDay(value: string | null | undefined): string {
  return value ? value.slice(0, 10) : '';
}

function daysLeftTone(days: number, status: SubscriptionStatus): PillTone {
  if (status === SubscriptionStatus.Cancelled || status === SubscriptionStatus.Expired) return 'neutral';
  if (days <= 0) return 'danger';
  if (days <= 7) return 'warning';
  return 'success';
}

/* --------------------------------------------------------------- member picker */

/**
 * Type-ahead over `GET /api/members/lookup`. Shared by every billing screen that needs to
 * name a member (new subscription, record payment, gym check-in).
 */
export function MemberPicker(
  { value, onChange, placeholder = 'Search by name, code or phone', autoFocus, disabled }:
  {
    value: Lookup | null;
    onChange: (member: Lookup | null) => void;
    placeholder?: string;
    autoFocus?: boolean;
    disabled?: boolean;
  },
) {
  const [term, setTerm] = useState('');
  const [results, setResults] = useState<Lookup[]>([]);
  const [open, setOpen] = useState(false);
  const [searching, setSearching] = useState(false);
  const [highlight, setHighlight] = useState(0);
  const boxRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (value || term.trim().length < 1) { setResults([]); setSearching(false); return; }
    const controller = new AbortController();
    setSearching(true);
    const timer = setTimeout(() => {
      lookupMembers(term.trim(), controller.signal)
        .then((rows) => { if (!controller.signal.aborted) { setResults(rows); setHighlight(0); setOpen(true); } })
        .catch(() => { if (!controller.signal.aborted) setResults([]); })
        .finally(() => { if (!controller.signal.aborted) setSearching(false); });
    }, 300);
    return () => { clearTimeout(timer); controller.abort(); };
  }, [term, value]);

  // Any click outside dismisses the suggestion list.
  useEffect(() => {
    if (!open) return;
    const onDown = (e: MouseEvent) => {
      if (boxRef.current && !boxRef.current.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener('mousedown', onDown);
    return () => document.removeEventListener('mousedown', onDown);
  }, [open]);

  function choose(member: Lookup) {
    onChange(member);
    setTerm('');
    setResults([]);
    setOpen(false);
  }

  if (value) {
    return (
      <div className="combo-chosen">
        <IconUser size={15} />
        <div className="grow">
          <div className="cell-main">{value.name}</div>
          {value.code ? <div className="cell-sub">{value.code}{value.extra ? ` · ${value.extra}` : ''}</div> : null}
        </div>
        {!disabled && (
          <button className="btn btn-ghost btn-sm" onClick={() => onChange(null)} aria-label="Clear member">Change</button>
        )}
      </div>
    );
  }

  return (
    <div className="combo" ref={boxRef}>
      <div className="input-group">
        <span className="input-icon"><IconSearch size={15} /></span>
        <input
          className="input"
          value={term}
          autoFocus={autoFocus}
          disabled={disabled}
          placeholder={placeholder}
          onChange={(e) => setTerm(e.target.value)}
          onFocus={() => { if (results.length) setOpen(true); }}
          onKeyDown={(e) => {
            if (!open || !results.length) return;
            if (e.key === 'ArrowDown') { e.preventDefault(); setHighlight((h) => Math.min(h + 1, results.length - 1)); }
            else if (e.key === 'ArrowUp') { e.preventDefault(); setHighlight((h) => Math.max(h - 1, 0)); }
            else if (e.key === 'Enter') { e.preventDefault(); choose(results[highlight]); }
            else if (e.key === 'Escape') setOpen(false);
          }}
        />
      </div>
      {open && (
        <div className="combo-list">
          {searching && <div className="combo-empty">Searching…</div>}
          {!searching && results.length === 0 && <div className="combo-empty">No members matched.</div>}
          {results.map((member, i) => (
            <button
              key={member.id}
              type="button"
              className={`combo-item ${i === highlight ? 'on' : ''}`}
              onMouseEnter={() => setHighlight(i)}
              onClick={() => choose(member)}
            >
              <div className="cell-main">{member.name}</div>
              <div className="cell-sub">{member.code}{member.extra ? ` · ${member.extra}` : ''}</div>
            </button>
          ))}
        </div>
      )}
    </div>
  );
}

/* ----------------------------------------------------------------- quote panel */

/**
 * Debounced call to `POST /api/subscriptions/quote`.
 *
 * `api.post` takes no AbortSignal, so a superseded reply is discarded with a ticket check and
 * the unmount guard rather than cancelled on the wire.
 */
export function useQuote(request: QuoteRequest | null, enabled: boolean) {
  const [quote, setQuote] = useState<SubscriptionQuoteDto | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<unknown>(null);

  const ticket = useRef(0);
  const alive = useRef(true);
  const latest = useRef(request);
  latest.current = request;

  // Re-arm on every mount: StrictMode mounts, unmounts and mounts again in development, so a
  // one-way `alive = false` in the cleanup would latch off and swallow every reply.
  useEffect(() => {
    alive.current = true;
    return () => { alive.current = false; };
  }, []);

  const key = request && enabled && request.membershipPlanId > 0 ? JSON.stringify(request) : '';

  useEffect(() => {
    if (!key) { setQuote(null); setError(null); setLoading(false); return; }
    const mine = ++ticket.current;
    setLoading(true);
    const timer = setTimeout(() => {
      const body = latest.current;
      if (!body) return;
      subscriptionsApi.quote(body)
        .then((result) => {
          if (!alive.current || mine !== ticket.current) return;
          setQuote(result); setError(null); setLoading(false);
        })
        .catch((err) => {
          if (!alive.current || mine !== ticket.current) return;
          setQuote(null); setError(err); setLoading(false);
        });
    }, 400);
    return () => clearTimeout(timer);
  }, [key]);

  return { quote, loading, error };
}

/** Renders the server's breakdown verbatim. Nothing on this panel is computed client-side. */
export function QuotePanel(
  { quote, loading, error, currency, title = 'Price quote' }:
  { quote: SubscriptionQuoteDto | null; loading: boolean; error: unknown; currency: string; title?: string },
) {
  return (
    <div className={`quote-panel ${loading ? 'is-stale' : ''}`}>
      <div className="quote-head"><IconMoney size={16} /> {title}</div>
      <div className="quote-note">
        The server calculates the payable amount. This breakdown is returned by
        <code> POST /api/subscriptions/quote</code> and is what will be charged.
      </div>

      {error ? <ErrorAlert error={error} /> : null}

      {!quote && !error && (
        <div className="quote-placeholder">
          {loading ? 'Asking the server for a price…' : 'Choose a plan to see the price.'}
        </div>
      )}

      {quote && (
        <table className="quote-table">
          <tbody>
            <tr>
              <td className="quote-label">Plan</td>
              <td>{quote.planName}</td>
            </tr>
            <tr>
              <td className="quote-label">Term</td>
              <td>
                {fmtDate(quote.startDate)} → {fmtDate(quote.endDate)}
                {' '}({quote.totalDays} day{quote.totalDays === 1 ? '' : 's'})
              </td>
            </tr>
            <tr>
              <td className="quote-label">Plan amount</td>
              <td>{money(quote.planAmount, currency)}</td>
            </tr>
            {quote.registrationFee > 0 && (
              <tr>
                <td className="quote-label">Registration fee</td>
                <td>{money(quote.registrationFee, currency)}</td>
              </tr>
            )}
            {quote.proratedCredit !== 0 && (
              <tr>
                <td className="quote-label">Prorated credit</td>
                <td>− {money(quote.proratedCredit, currency)}</td>
              </tr>
            )}
            <tr>
              <td className="quote-label">
                Discount
                <span className="muted"> (max {money(quote.maxAllowedDiscount, currency)})</span>
              </td>
              <td>− {money(quote.discountAmount, currency)}</td>
            </tr>
            <tr>
              <td className="quote-label">Tax ({quote.taxPercent}%)</td>
              <td>{money(quote.taxAmount, currency)}</td>
            </tr>
            <tr className="quote-total">
              <td>Final Amount</td>
              <td>{money(quote.finalAmount, currency)}</td>
            </tr>
          </tbody>
        </table>
      )}
    </div>
  );
}

/* -------------------------------------------------------------- payment block */

export interface InlinePaymentState {
  collect: boolean;
  paymentMethodId: number | '';
  amount: string;
  transactionReference: string;
  payerVpa: string;
  markConfirmed: boolean;
  notes: string;
}

export const BLANK_INLINE_PAYMENT: InlinePaymentState = {
  collect: false, paymentMethodId: '', amount: '', transactionReference: '',
  payerVpa: '', markConfirmed: true, notes: '',
};

export function toInlinePayment(state: InlinePaymentState): CollectPaymentInline | null {
  if (!state.collect || state.paymentMethodId === '' || !state.amount) return null;
  return {
    paymentMethodId: Number(state.paymentMethodId),
    amount: Number(state.amount),
    transactionReference: state.transactionReference.trim() || null,
    payerVpa: state.payerVpa.trim() || null,
    notes: state.notes.trim() || null,
    markConfirmed: state.markConfirmed,
  };
}

/**
 * Optional "collect now" block. Only ever asks for a transaction reference and a payer VPA —
 * card numbers, CVVs and UPI PINs are never displayed or collected anywhere in this app.
 */
/**
 * True when the inline payment cannot be submitted yet. The server rejects a payment whose method
 * carries `requiresReference` without one ("Payment method 'UPI' requires a transaction reference"),
 * so the modals check the same rule before enabling their save button rather than letting the
 * operator fill in a whole form and be refused.
 */
export function inlinePaymentBlocked(
  state: InlinePaymentState, methods: PaymentMethodDto[],
): boolean {
  if (!state.collect) return false;
  if (state.paymentMethodId === '') return true;
  const method = methods.find((m) => m.id === Number(state.paymentMethodId));
  return !!method?.requiresReference && !state.transactionReference.trim();
}

export function PaymentBlock(
  { state, onChange, methods, currency, suggested, memberId }:
  {
    state: InlinePaymentState;
    onChange: (next: InlinePaymentState) => void;
    methods: PaymentMethodDto[];
    currency: string;
    suggested: number | null;
    /** Needed to ask the server for a UPI intent; without it the QR button is not offered. */
    memberId?: number | null;
  },
) {
  const set = <K extends keyof InlinePaymentState>(key: K, next: InlinePaymentState[K]) =>
    onChange({ ...state, [key]: next });

  const [intent, setIntent] = useState<UpiPaymentIntentDto | null>(null);
  const [qrBusy, setQrBusy] = useState(false);
  const [qrError, setQrError] = useState<unknown>(null);
  const [referenceTouched, setReferenceTouched] = useState(false);

  // Offered only for methods flagged `supportsQrCode`, so cash and cheque never show it.
  const method = methods.find((m) => m.id === Number(state.paymentMethodId));
  const referenceRequired = !!method?.requiresReference;
  const canShowQr = !!method?.supportsQrCode && !!memberId && Number(state.amount) > 0;

  async function showQr() {
    if (!memberId) return;
    setQrBusy(true);
    setQrError(null);
    try {
      setIntent(await paymentsApi.upiIntent({
        memberId,
        amount: Number(state.amount),
        notes: null,
      }));
    } catch (err) {
      setQrError(err);
    } finally {
      setQrBusy(false);
    }
  }

  return (
    <div className="form-section form-section-optional">
      <label className="check-inline" style={{ marginBottom: state.collect ? 14 : 0 }}>
        <input
          type="checkbox"
          checked={state.collect}
          onChange={(e) => onChange({
            ...state,
            collect: e.target.checked,
            amount: e.target.checked && !state.amount && suggested !== null ? String(suggested) : state.amount,
          })}
        />
        Collect a payment with this transaction
      </label>

      {state.collect && (
        <>
          <div className="form-grid">
            <Field label="Payment method" required>
              <select
                className="select"
                value={state.paymentMethodId}
                onChange={(e) => set('paymentMethodId', e.target.value === '' ? '' : Number(e.target.value))}
              >
                <option value="">Select…</option>
                {methods.map((m) => <option key={m.id} value={m.id}>{m.name}</option>)}
              </select>
            </Field>
            <Field label="Amount" required help={suggested !== null ? `Quote total is ${money(suggested, currency)}.` : undefined}>
              <div className="input-group">
                <span className="input-icon">{currency}</span>
                <input
                  className="input" type="number" min={0} step="0.01"
                  value={state.amount}
                  onChange={(e) => set('amount', e.target.value)}
                />
              </div>
            </Field>
            <Field
              label="Transaction reference"
              required={referenceRequired}
              error={referenceRequired && !state.transactionReference.trim() && referenceTouched
                ? `${method?.name ?? 'This method'} requires a reference — the server will refuse the payment without one.`
                : undefined}
              help={referenceRequired
                ? 'UTR / transaction id from the payer’s app, or the cheque number.'
                : 'Optional for this method.'}
            >
              <input
                className="input" maxLength={128}
                value={state.transactionReference}
                onChange={(e) => set('transactionReference', e.target.value)}
                onBlur={() => setReferenceTouched(true)}
              />
            </Field>
            <Field label="Payer VPA" help="Optional. e.g. name@bank">
              <input
                className="input" maxLength={128}
                value={state.payerVpa}
                onChange={(e) => set('payerVpa', e.target.value)}
              />
            </Field>
          </div>
          {canShowQr && (
            <div className="row" style={{ marginTop: 12 }}>
              <button className="btn btn-outline btn-sm" onClick={() => void showQr()} disabled={qrBusy}>
                <IconQr size={15} /> {qrBusy ? 'Building QR…' : `Show ${method?.name ?? 'UPI'} QR`}
              </button>
              <span className="muted" style={{ fontSize: 'var(--fs-xs)' }}>
                Let the member scan, then paste the reference above.
              </span>
            </div>
          )}
          {qrError ? <div style={{ marginTop: 10 }}><ErrorAlert error={qrError} /></div> : null}

          <label className="check-inline" style={{ marginTop: 12 }}>
            <input type="checkbox" checked={state.markConfirmed} onChange={(e) => set('markConfirmed', e.target.checked)} />
            Mark confirmed — I have verified this transfer
          </label>
          <div className="form-note">
            Leave unticked for a UPI transfer you have not yet reconciled; the payment is stored as
            <strong> Awaiting Confirmation</strong> until someone confirms the reference.
          </div>

          {intent && <UpiModal intent={intent} onClose={() => setIntent(null)} />}
        </>
      )}
    </div>
  );
}

/* -------------------------------------------------------------- overflow menu */

function OverflowMenu({ label = '⋯', children }: { label?: string; children: (close: () => void) => ReactNode }) {
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    const onDown = (e: MouseEvent) => { if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false); };
    document.addEventListener('mousedown', onDown);
    return () => document.removeEventListener('mousedown', onDown);
  }, [open]);

  return (
    <div className="bill-menu-wrap" ref={ref}>
      <button className="btn btn-outline btn-sm" onClick={() => setOpen((o) => !o)} aria-label="More actions">{label}</button>
      {open && <div className="bill-menu">{children(() => setOpen(false))}</div>}
    </div>
  );
}

/* -------------------------------------------------------------------- the page */

type RowAction = 'renew' | 'change' | 'freeze' | 'resume' | 'cancel' | 'history' | 'collect';

export default function SubscriptionsPage() {
  const { can, isInRole, currency } = useAuth();
  const mayCreate = can('subscriptions.create');
  const mayManage = can('subscriptions.manage');
  const mayCancel = can('subscriptions.cancel');
  const mayCollect = can('payments.collect');

  const [plans, setPlans] = useState<Lookup[]>([]);
  const [methods, setMethods] = useState<PaymentMethodDto[]>([]);
  const [trainers, setTrainers] = useState<Lookup[]>([]);

  const [search, setSearch] = useState('');
  const [planId, setPlanId] = useState<number | ''>('');
  const [status, setStatus] = useState<SubscriptionStatus | ''>('');
  const [paymentStatus, setPaymentStatus] = useState<PaymentStatus | ''>('');
  const [startFrom, setStartFrom] = useState('');
  const [startTo, setStartTo] = useState('');
  const [expiringSoon, setExpiringSoon] = useState(false);
  const [withOutstanding, setWithOutstanding] = useState(false);

  const [query, setQuery] = useState<SubscriptionQuery>({ pageNumber: 1, pageSize: 25 });
  const [data, setData] = useState<PagedResult<SubscriptionDto> | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<unknown>(null);
  const [notice, setNotice] = useState('');

  const [creating, setCreating] = useState(false);
  const [action, setAction] = useState<{ kind: RowAction; row: SubscriptionDto } | null>(null);
  const [expiryResult, setExpiryResult] = useState<string | null>(null);
  const [expiryBusy, setExpiryBusy] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    plansApi.lookup(controller.signal).then(setPlans).catch(() => setPlans([]));
    paymentsApi.methods(controller.signal).then(setMethods).catch(() => setMethods([]));
    return () => controller.abort();
  }, []);

  // Optional: the trainer select degrades to "No trainer" when the lookup is not permitted.
  useEffect(() => {
    if (!can('trainers.view')) return;
    const controller = new AbortController();
    trainersApi.lookup(true, controller.signal).then(setTrainers).catch(() => setTrainers([]));
    return () => controller.abort();
  }, [can]);

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError(null);
    subscriptionsApi.paged(query, controller.signal)
      .then((result) => { if (!controller.signal.aborted) setData(result); })
      .catch((err) => { if (!controller.signal.aborted) setError(err); })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [query]);

  const reload = useCallback(() => setQuery((q) => ({ ...q })), []);

  // Badge on the trigger. Counted off the applied query, and only over the folded-away filters —
  // search keeps its own visible box, so a badge for it would be noise.
  const activeFilterCount = ([
    'membershipPlanId', 'status', 'paymentStatus', 'startFrom', 'startTo',
    'onlyExpiringSoon', 'onlyWithOutstanding',
  ] as const).filter((key) => {
    // `resetFilters` drops the keys outright; `applyFilters` writes '' for an unset one.
    const value = query[key];
    return value !== undefined && value !== '' && value !== false;
  }).length;

  function applyFilters() {
    setQuery((q) => ({
      ...q,
      pageNumber: 1,
      search: search.trim(),
      membershipPlanId: planId,
      status,
      paymentStatus,
      startFrom: startFrom || undefined,
      startTo: startTo || undefined,
      onlyExpiringSoon: expiringSoon ? true : '',
      onlyWithOutstanding: withOutstanding ? true : '',
    }));
  }

  function resetFilters() {
    setSearch(''); setPlanId(''); setStatus(''); setPaymentStatus('');
    setStartFrom(''); setStartTo(''); setExpiringSoon(false); setWithOutstanding(false);
    setQuery({ pageNumber: 1, pageSize: 25 });
  }

  async function processExpiries() {
    setExpiryBusy(true);
    try {
      const result = await subscriptionsApi.processExpiries();
      setExpiryResult(
        `Expiry sweep finished — ${result.expiredCount} expired, ${result.resumedFromFreeze} resumed from freeze, ` +
        `${result.notificationsCreated} notification(s) raised.`,
      );
      reload();
    } catch (err) {
      setError(err);
    } finally {
      setExpiryBusy(false);
    }
  }

  function afterMutation(message: string) {
    setAction(null);
    setCreating(false);
    setNotice(message);
    reload();
  }

  const items = data?.items ?? [];
  const firstIndex = ((data?.pageNumber ?? 1) - 1) * (data?.pageSize ?? 25);

  return (
    <div className="page">
      <PageCard>
        <PageCardHeader
          icon={<IconCheckSquare size={20} />}
          title="Subscriptions"
          subtitle="Sales, renewals, freezes and cancellations across every membership."
          actions={
            <>
              <FilterMenu
                activeCount={activeFilterCount}
                onApply={applyFilters}
                onReset={resetFilters}
              >
                <FilterField label="Plan">
                  <select className="select" value={planId} onChange={(e) => setPlanId(e.target.value === '' ? '' : Number(e.target.value))}>
                    <option value="">All plans</option>
                    {plans.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
                  </select>
                </FilterField>

                <FilterField label="Status">
                  <select
                    className="select" value={status}
                    onChange={(e) => setStatus(e.target.value === '' ? '' : (Number(e.target.value) as SubscriptionStatus))}
                  >
                    <option value="">All</option>
                    {Object.values(SubscriptionStatus).filter((v): v is SubscriptionStatus => typeof v === 'number')
                      .map((v) => <option key={v} value={v}>{words(SubscriptionStatus[v])}</option>)}
                  </select>
                </FilterField>

                <FilterField label="Payment status">
                  <select
                    className="select" value={paymentStatus}
                    onChange={(e) => setPaymentStatus(e.target.value === '' ? '' : (Number(e.target.value) as PaymentStatus))}
                  >
                    <option value="">All</option>
                    {Object.values(PaymentStatus).filter((v): v is PaymentStatus => typeof v === 'number')
                      .map((v) => <option key={v} value={v}>{words(PaymentStatus[v])}</option>)}
                  </select>
                </FilterField>

                <FilterField label="Starts from">
                  <input className="input" type="date" value={startFrom} onChange={(e) => setStartFrom(e.target.value)} />
                </FilterField>

                <FilterField label="Starts to">
                  <input className="input" type="date" value={startTo} onChange={(e) => setStartTo(e.target.value)} />
                </FilterField>

                <FilterField label="Flags">
                  {/* Stacked, not side by side: a menu column is far narrower than the old strip. */}
                  <div style={{ display: 'flex', flexDirection: 'column', gap: 10, minHeight: 38, justifyContent: 'center' }}>
                    <label className="check-inline">
                      <input type="checkbox" checked={expiringSoon} onChange={(e) => setExpiringSoon(e.target.checked)} />
                      Expiring soon
                    </label>
                    <label className="check-inline">
                      <input type="checkbox" checked={withOutstanding} onChange={(e) => setWithOutstanding(e.target.checked)} />
                      With outstanding
                    </label>
                  </div>
                </FilterField>
              </FilterMenu>

              <button className="btn btn-outline btn-icon" onClick={reload} aria-label="Refresh"><IconRefresh size={15} /></button>
              {mayCreate && (
                <button className="btn btn-dark" onClick={() => setCreating(true)}>
                  <IconPlus size={15} /> New Subscription
                </button>
              )}
              <OverflowMenu>
                {(close) => (
                  <button
                    className="bill-menu-item"
                    disabled={!isInRole('Admin') || expiryBusy}
                    title={isInRole('Admin') ? 'Expire finished terms and auto-resume freezes' : 'Admins only'}
                    onClick={() => { close(); void processExpiries(); }}
                  >
                    <IconClock size={15} /> {expiryBusy ? 'Processing…' : 'Process expiries (Admin)'}
                  </button>
                )}
              </OverflowMenu>
            </>
          }
        />

        {/* Only the search box stays in the open. Enter applies it; every other filter, and the
            Apply / Clear all pair, live in the header's Filters menu. */}
        <FilterStrip>
          <FilterField label="Search">
            <input
              className="input" placeholder="Code, member name or phone"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              onKeyDown={(e) => { if (e.key === 'Enter') applyFilters(); }}
            />
          </FilterField>
        </FilterStrip>

        {(notice || expiryResult) && (
          <div className="section-pad" style={{ paddingBottom: 0 }}>
            <Alert tone="success">{notice || expiryResult}</Alert>
          </div>
        )}
        {error ? <div className="section-pad"><ErrorAlert error={error} /></div> : null}

        {loading ? (
          <Loading message="Loading subscriptions…" />
        ) : items.length === 0 ? (
          <EmptyState
            icon={<IconCheckSquare size={34} />}
            title="No subscriptions found"
            message="Adjust the filters, or sell a new membership."
          />
        ) : (
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th className="idx">#</th>
                  <th>Code</th>
                  <th>Member</th>
                  <th>Plan</th>
                  <th>Start</th>
                  <th>End</th>
                  <th className="center">Days left</th>
                  <th className="num">Final</th>
                  <th className="num">Paid</th>
                  <th className="num">Outstanding</th>
                  <th>Payment</th>
                  <th>Status</th>
                  <th className="actions">Actions</th>
                </tr>
              </thead>
              <tbody>
                {items.map((row, i) => (
                  <tr key={row.id}>
                    <td className="idx">{firstIndex + i + 1}</td>
                    <td><span className="cell-main">{row.subscriptionCode}</span></td>
                    <td>
                      <div className="cell-main">{row.memberName}</div>
                      <div className="cell-sub">{row.memberCode}{row.memberPhone ? ` · ${row.memberPhone}` : ''}</div>
                    </td>
                    <td><Pill tone="primary">{row.planName}</Pill></td>
                    <td>{fmtDate(row.startDate)}</td>
                    <td>{fmtDate(row.endDate)}</td>
                    <td className="center">
                      <Pill tone={daysLeftTone(row.daysRemaining, row.status)}>{row.daysRemaining}</Pill>
                    </td>
                    <td className="money-cell">{money(row.finalAmount, currency)}</td>
                    <td className="money-cell">{money(row.paidAmount, currency)}</td>
                    <td className="money-cell">{money(row.outstandingAmount, currency)}</td>
                    <td><StatusPill status={row.paymentStatusText} /></td>
                    <td><StatusPill status={row.statusText} /></td>
                    <td className="actions">
                      <div className="row" style={{ justifyContent: 'flex-end', gap: 6 }}>
                        {mayCreate && (
                          <button className="btn btn-outline btn-sm" onClick={() => setAction({ kind: 'renew', row })}>
                            Renew
                          </button>
                        )}
                        <OverflowMenu>
                          {(close) => (
                            <>
                              <button
                                className="bill-menu-item" disabled={!mayCollect || row.outstandingAmount <= 0}
                                onClick={() => { close(); setAction({ kind: 'collect', row }); }}
                              >
                                <IconMoney size={15} /> Collect payment
                              </button>
                              <button
                                className="bill-menu-item" disabled={!mayManage}
                                onClick={() => { close(); setAction({ kind: 'change', row }); }}
                              >
                                <IconCrown size={15} /> Change plan
                              </button>
                              {row.status === SubscriptionStatus.Frozen ? (
                                <button
                                  className="bill-menu-item" disabled={!mayManage}
                                  onClick={() => { close(); setAction({ kind: 'resume', row }); }}
                                >
                                  <IconCheck size={15} /> Resume
                                </button>
                              ) : (
                                <button
                                  className="bill-menu-item" disabled={!mayManage || row.status !== SubscriptionStatus.Active}
                                  onClick={() => { close(); setAction({ kind: 'freeze', row }); }}
                                >
                                  <IconClock size={15} /> Freeze
                                </button>
                              )}
                              <button
                                className="bill-menu-item" disabled={!mayCancel || row.status === SubscriptionStatus.Cancelled}
                                onClick={() => { close(); setAction({ kind: 'cancel', row }); }}
                              >
                                <IconFile size={15} /> Cancel
                              </button>
                              <button className="bill-menu-item" onClick={() => { close(); setAction({ kind: 'history', row }); }}>
                                <IconClock size={15} /> History
                              </button>
                            </>
                          )}
                        </OverflowMenu>
                      </div>
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
      </PageCard>

      {creating && (
        <NewSubscriptionModal
          plans={plans} methods={methods} trainers={trainers} currency={currency}
          onDone={() => afterMutation('Subscription created.')}
          onClose={() => setCreating(false)}
        />
      )}

      {action?.kind === 'renew' && (
        <RenewModal
          row={action.row} plans={plans} methods={methods} trainers={trainers} currency={currency}
          onDone={() => afterMutation('Subscription renewed.')} onClose={() => setAction(null)}
        />
      )}
      {action?.kind === 'change' && (
        <ChangePlanModal
          row={action.row} plans={plans} methods={methods} currency={currency}
          onDone={() => afterMutation('Plan changed.')} onClose={() => setAction(null)}
        />
      )}
      {action?.kind === 'freeze' && (
        <FreezeModal row={action.row} onDone={() => afterMutation('Subscription frozen.')} onClose={() => setAction(null)} />
      )}
      {action?.kind === 'resume' && (
        <ResumeModal row={action.row} onDone={() => afterMutation('Subscription resumed.')} onClose={() => setAction(null)} />
      )}
      {action?.kind === 'cancel' && (
        <CancelModal row={action.row} currency={currency} onDone={() => afterMutation('Subscription cancelled.')} onClose={() => setAction(null)} />
      )}
      {action?.kind === 'collect' && (
        <CollectModal
          row={action.row} methods={methods} currency={currency}
          onDone={() => afterMutation('Payment recorded.')} onClose={() => setAction(null)}
        />
      )}
      {action?.kind === 'history' && (
        <HistoryModal row={action.row} currency={currency} onClose={() => setAction(null)} />
      )}
    </div>
  );
}

/* ------------------------------------------------------------ create / renew */

function NewSubscriptionModal(
  { plans, methods, trainers, currency, onDone, onClose }:
  {
    plans: Lookup[]; methods: PaymentMethodDto[]; trainers: Lookup[]; currency: string;
    onDone: () => void; onClose: () => void;
  },
) {
  const [member, setMember] = useState<Lookup | null>(null);
  const [membershipPlanId, setPlanId] = useState<number | ''>('');
  const [startDate, setStartDate] = useState(todayIso());
  const [discount, setDiscount] = useState('0');
  const [assignedTrainerId, setTrainerId] = useState<number | ''>('');
  const [chargeRegistrationFee, setCharge] = useState(true);
  const [notes, setNotes] = useState('');
  const [payment, setPayment] = useState<InlinePaymentState>(BLANK_INLINE_PAYMENT);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);

  const request = useMemo<QuoteRequest | null>(() => (
    membershipPlanId === '' ? null : {
      membershipPlanId: Number(membershipPlanId),
      startDate,
      discountAmount: Number(discount) || 0,
      chargeRegistrationFee,
    }
  ), [membershipPlanId, startDate, discount, chargeRegistrationFee]);

  const { quote, loading, error: quoteError } = useQuote(request, true);

  async function save() {
    if (!member || membershipPlanId === '') return;
    setBusy(true); setError(null);
    try {
      await subscriptionsApi.create({
        memberId: member.id,
        membershipPlanId: Number(membershipPlanId),
        startDate,
        discountAmount: Number(discount) || 0,
        chargeRegistrationFee,
        assignedTrainerId: assignedTrainerId === '' ? null : Number(assignedTrainerId),
        notes: notes.trim() || null,
        payment: toInlinePayment(payment),
      });
      onDone();
    } catch (err) {
      setError(err);
    } finally {
      setBusy(false);
    }
  }

  return (
    <Modal
      title="New subscription" icon={<IconCheckSquare size={18} />} onClose={onClose} width={860}
      footer={
        <>
          <button className="btn btn-outline" onClick={onClose} disabled={busy}>Cancel</button>
          <button className="btn btn-dark" onClick={save} disabled={busy || !member || membershipPlanId === '' || inlinePaymentBlocked(payment, methods)}>
            {busy ? 'Saving…' : 'Create subscription'}
          </button>
        </>
      }
    >
      <div className="stack">
        {error ? <ErrorAlert error={error} /> : null}

        <Field label="Member" required>
          <MemberPicker value={member} onChange={setMember} autoFocus />
        </Field>

        <div className="form-grid">
          <Field label="Membership plan" required>
            <select className="select" value={membershipPlanId} onChange={(e) => setPlanId(e.target.value === '' ? '' : Number(e.target.value))}>
              <option value="">Select a plan…</option>
              {plans.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
            </select>
          </Field>
          <Field label="Start date" required help="Up to 30 days back and 90 days ahead.">
            <input className="input" type="date" value={startDate} onChange={(e) => setStartDate(e.target.value)} />
          </Field>
          <Field label="Discount" help={quote ? `Server allows up to ${money(quote.maxAllowedDiscount, currency)}.` : undefined}>
            <div className="input-group">
              <span className="input-icon">{currency}</span>
              <input className="input" type="number" min={0} step="0.01" value={discount} onChange={(e) => setDiscount(e.target.value)} />
            </div>
          </Field>
          <Field label="Assigned trainer" help="Optional. Recorded against this subscription term.">
            <select
              className="select"
              value={assignedTrainerId}
              onChange={(e) => setTrainerId(e.target.value === '' ? '' : Number(e.target.value))}
            >
              <option value="">No trainer</option>
              {trainers.map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}
            </select>
          </Field>
        </div>

        <label className="check-inline">
          <input type="checkbox" checked={chargeRegistrationFee} onChange={(e) => setCharge(e.target.checked)} />
          Charge the registration fee
        </label>

        <QuotePanel quote={quote} loading={loading} error={quoteError} currency={currency} />

        <Field label="Notes">
          <textarea className="textarea" style={{ minHeight: 70 }} maxLength={2000} value={notes} onChange={(e) => setNotes(e.target.value)} />
        </Field>

        <PaymentBlock
          state={payment} onChange={setPayment} methods={methods}
          currency={currency} suggested={quote?.finalAmount ?? null}
          memberId={member?.id ?? null}
        />
      </div>
    </Modal>
  );
}

function RenewModal(
  { row, plans, methods, trainers, currency, onDone, onClose }:
  {
    row: SubscriptionDto; plans: Lookup[]; methods: PaymentMethodDto[]; trainers: Lookup[];
    currency: string; onDone: () => void; onClose: () => void;
  },
) {
  const [membershipPlanId, setPlanId] = useState<number | ''>(row.membershipPlanId);
  const [startDate, setStartDate] = useState(addDaysIso(toIsoDay(row.endDate) || todayIso(), 1));
  const [discount, setDiscount] = useState('0');
  // Carry the current term's trainer into the new one instead of silently dropping it.
  const [assignedTrainerId, setTrainerId] = useState<number | ''>(
    (row as SubscriptionDto & SubscriptionTrainer).assignedTrainerId ?? '',
  );
  const [notes, setNotes] = useState('');
  const [payment, setPayment] = useState<InlinePaymentState>(BLANK_INLINE_PAYMENT);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);

  // A renewal never re-charges the joining fee, so the quote is asked without it.
  const request = useMemo<QuoteRequest | null>(() => (
    membershipPlanId === '' ? null : {
      membershipPlanId: Number(membershipPlanId),
      startDate,
      discountAmount: Number(discount) || 0,
      chargeRegistrationFee: false,
    }
  ), [membershipPlanId, startDate, discount]);

  const { quote, loading, error: quoteError } = useQuote(request, true);

  async function save() {
    setBusy(true); setError(null);
    try {
      await subscriptionsApi.renew({
        memberId: row.memberId,
        previousSubscriptionId: row.id,
        membershipPlanId: membershipPlanId === '' ? null : Number(membershipPlanId),
        startDate: startDate || null,
        discountAmount: Number(discount) || 0,
        assignedTrainerId: assignedTrainerId === '' ? null : Number(assignedTrainerId),
        notes: notes.trim() || null,
        payment: toInlinePayment(payment),
      });
      onDone();
    } catch (err) {
      setError(err);
    } finally {
      setBusy(false);
    }
  }

  return (
    <Modal
      title={`Renew — ${row.memberName}`} icon={<IconRefresh size={18} />} onClose={onClose} width={860}
      footer={
        <>
          <button className="btn btn-outline" onClick={onClose} disabled={busy}>Cancel</button>
          <button className="btn btn-dark" onClick={save} disabled={busy || inlinePaymentBlocked(payment, methods)}>{busy ? 'Saving…' : 'Renew subscription'}</button>
        </>
      }
    >
      <div className="stack">
        {error ? <ErrorAlert error={error} /> : null}
        <Alert tone="info">
          Renewing <strong>{row.subscriptionCode}</strong> ({row.planName}), which currently ends {fmtDate(row.endDate)}.
        </Alert>

        <div className="form-grid">
          <Field label="Plan for the new term" help="Defaults to the plan being renewed.">
            <select className="select" value={membershipPlanId} onChange={(e) => setPlanId(e.target.value === '' ? '' : Number(e.target.value))}>
              <option value="">Keep the previous plan</option>
              {plans.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
            </select>
          </Field>
          <Field label="Start date" help="Defaults to the day after the current term ends.">
            <input className="input" type="date" value={startDate} onChange={(e) => setStartDate(e.target.value)} />
          </Field>
          <Field label="Discount">
            <div className="input-group">
              <span className="input-icon">{currency}</span>
              <input className="input" type="number" min={0} step="0.01" value={discount} onChange={(e) => setDiscount(e.target.value)} />
            </div>
          </Field>
          <Field label="Assigned trainer" help="Optional. Defaults to the trainer on the current term.">
            <select
              className="select"
              value={assignedTrainerId}
              onChange={(e) => setTrainerId(e.target.value === '' ? '' : Number(e.target.value))}
            >
              <option value="">No trainer</option>
              {trainers.map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}
            </select>
          </Field>
        </div>

        <QuotePanel quote={quote} loading={loading} error={quoteError} currency={currency} title="Renewal quote" />

        <Field label="Notes">
          <textarea className="textarea" style={{ minHeight: 70 }} maxLength={2000} value={notes} onChange={(e) => setNotes(e.target.value)} />
        </Field>

        <PaymentBlock
          state={payment} onChange={setPayment} methods={methods}
          currency={currency} suggested={quote?.finalAmount ?? null}
          memberId={row.memberId}
        />
      </div>
    </Modal>
  );
}

function ChangePlanModal(
  { row, plans, methods, currency, onDone, onClose }:
  { row: SubscriptionDto; plans: Lookup[]; methods: PaymentMethodDto[]; currency: string; onDone: () => void; onClose: () => void },
) {
  const [newMembershipPlanId, setPlanId] = useState<number | ''>('');
  const [effectiveDate, setEffectiveDate] = useState(todayIso());
  const [prorate, setProrate] = useState(true);
  const [discount, setDiscount] = useState('0');
  const [reason, setReason] = useState('');
  const [payment, setPayment] = useState<InlinePaymentState>(BLANK_INLINE_PAYMENT);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);

  const request = useMemo<QuoteRequest | null>(() => (
    newMembershipPlanId === '' ? null : {
      membershipPlanId: Number(newMembershipPlanId),
      startDate: effectiveDate,
      discountAmount: Number(discount) || 0,
      chargeRegistrationFee: false,
      currentSubscriptionId: row.id,
      prorateUnusedAmount: prorate,
    }
  ), [newMembershipPlanId, effectiveDate, discount, prorate, row.id]);

  const { quote, loading, error: quoteError } = useQuote(request, true);

  async function save() {
    if (newMembershipPlanId === '') return;
    setBusy(true); setError(null);
    try {
      await subscriptionsApi.changePlan({
        subscriptionId: row.id,
        newMembershipPlanId: Number(newMembershipPlanId),
        effectiveDate,
        prorateUnusedAmount: prorate,
        discountAmount: Number(discount) || 0,
        reason: reason.trim() || null,
        payment: toInlinePayment(payment),
      });
      onDone();
    } catch (err) {
      setError(err);
    } finally {
      setBusy(false);
    }
  }

  return (
    <Modal
      title={`Change plan — ${row.memberName}`} icon={<IconCrown size={18} />} onClose={onClose} width={860}
      footer={
        <>
          <button className="btn btn-outline" onClick={onClose} disabled={busy}>Cancel</button>
          <button className="btn btn-dark" onClick={save} disabled={busy || newMembershipPlanId === '' || inlinePaymentBlocked(payment, methods)}>
            {busy ? 'Saving…' : 'Change plan'}
          </button>
        </>
      }
    >
      <div className="stack">
        {error ? <ErrorAlert error={error} /> : null}
        <Alert tone="info">Currently on <strong>{row.planName}</strong> until {fmtDate(row.endDate)}.</Alert>

        <div className="form-grid">
          <Field label="New plan" required>
            <select className="select" value={newMembershipPlanId} onChange={(e) => setPlanId(e.target.value === '' ? '' : Number(e.target.value))}>
              <option value="">Select a plan…</option>
              {plans.filter((p) => p.id !== row.membershipPlanId).map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
            </select>
          </Field>
          <Field label="Effective date" required>
            <input className="input" type="date" value={effectiveDate} onChange={(e) => setEffectiveDate(e.target.value)} />
          </Field>
          <Field label="Discount">
            <div className="input-group">
              <span className="input-icon">{currency}</span>
              <input className="input" type="number" min={0} step="0.01" value={discount} onChange={(e) => setDiscount(e.target.value)} />
            </div>
          </Field>
        </div>

        <label className="check-inline">
          <input type="checkbox" checked={prorate} onChange={(e) => setProrate(e.target.checked)} />
          Credit the unused part of the current plan
        </label>

        <QuotePanel quote={quote} loading={loading} error={quoteError} currency={currency} title="Plan change quote" />

        <Field label="Reason">
          <input className="input" maxLength={512} value={reason} onChange={(e) => setReason(e.target.value)} />
        </Field>

        <PaymentBlock
          state={payment} onChange={setPayment} methods={methods}
          currency={currency} suggested={quote?.finalAmount ?? null}
          memberId={row.memberId}
        />
      </div>
    </Modal>
  );
}

/* ------------------------------------------------- freeze / resume / cancel */

function FreezeModal({ row, onDone, onClose }: { row: SubscriptionDto; onDone: () => void; onClose: () => void }) {
  const [freezeStartDate, setStart] = useState(todayIso());
  const [freezeEndDate, setEnd] = useState(addDaysIso(todayIso(), 7));
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);

  async function save() {
    setBusy(true); setError(null);
    try {
      await subscriptionsApi.freeze({ subscriptionId: row.id, freezeStartDate, freezeEndDate, reason: reason.trim() || null });
      onDone();
    } catch (err) { setError(err); } finally { setBusy(false); }
  }

  return (
    <Modal
      title={`Freeze — ${row.memberName}`} icon={<IconClock size={18} />} onClose={onClose} width={560}
      footer={
        <>
          <button className="btn btn-outline" onClick={onClose} disabled={busy}>Cancel</button>
          <button className="btn btn-dark" onClick={save} disabled={busy}>{busy ? 'Freezing…' : 'Freeze'}</button>
        </>
      }
    >
      <div className="stack">
        {error ? <ErrorAlert error={error} /> : null}
        <Alert tone="info">The end date is pushed out by the frozen days, within the plan's freeze allowance.</Alert>
        <div className="form-grid">
          <Field label="Freeze from" required><input className="input" type="date" value={freezeStartDate} onChange={(e) => setStart(e.target.value)} /></Field>
          <Field label="Freeze until" required><input className="input" type="date" value={freezeEndDate} onChange={(e) => setEnd(e.target.value)} /></Field>
        </div>
        <Field label="Reason"><input className="input" maxLength={512} value={reason} onChange={(e) => setReason(e.target.value)} /></Field>
      </div>
    </Modal>
  );
}

function ResumeModal({ row, onDone, onClose }: { row: SubscriptionDto; onDone: () => void; onClose: () => void }) {
  const [resumeDate, setResumeDate] = useState(todayIso());
  const [remarks, setRemarks] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);

  async function save() {
    setBusy(true); setError(null);
    try {
      await subscriptionsApi.resume({ subscriptionId: row.id, resumeDate, remarks: remarks.trim() || null });
      onDone();
    } catch (err) { setError(err); } finally { setBusy(false); }
  }

  return (
    <Modal
      title={`Resume — ${row.memberName}`} icon={<IconCheck size={18} />} onClose={onClose} width={560}
      footer={
        <>
          <button className="btn btn-outline" onClick={onClose} disabled={busy}>Cancel</button>
          <button className="btn btn-dark" onClick={save} disabled={busy}>{busy ? 'Resuming…' : 'Resume'}</button>
        </>
      }
    >
      <div className="stack">
        {error ? <ErrorAlert error={error} /> : null}
        <Field label="Resume from" required><input className="input" type="date" value={resumeDate} onChange={(e) => setResumeDate(e.target.value)} /></Field>
        <Field label="Remarks"><input className="input" maxLength={512} value={remarks} onChange={(e) => setRemarks(e.target.value)} /></Field>
      </div>
    </Modal>
  );
}

function CancelModal(
  { row, currency, onDone, onClose }:
  { row: SubscriptionDto; currency: string; onDone: () => void; onClose: () => void },
) {
  const [reason, setReason] = useState('');
  const [refund, setRefund] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);

  async function save() {
    setBusy(true); setError(null);
    try {
      await subscriptionsApi.cancel({ subscriptionId: row.id, reason: reason.trim(), refundRemainingAmount: refund });
      onDone();
    } catch (err) { setError(err); } finally { setBusy(false); }
  }

  return (
    <Modal
      title={`Cancel — ${row.memberName}`} icon={<IconFile size={18} />} onClose={onClose} width={560}
      footer={
        <>
          <button className="btn btn-outline" onClick={onClose} disabled={busy}>Keep subscription</button>
          <button className="btn btn-danger" onClick={save} disabled={busy || reason.trim().length < 5}>
            {busy ? 'Cancelling…' : 'Cancel subscription'}
          </button>
        </>
      }
    >
      <div className="stack">
        {error ? <ErrorAlert error={error} /> : null}
        <Alert tone="warning">
          <div>
            <div style={{ fontWeight: 600 }}>{row.subscriptionCode} — {row.planName}</div>
            <div>Paid {money(row.paidAmount, currency)} of {money(row.finalAmount, currency)}.</div>
          </div>
        </Alert>
        <Field label="Reason" required help="At least 5 characters — it is written to the subscription history.">
          <textarea className="textarea" style={{ minHeight: 80 }} maxLength={512} value={reason} onChange={(e) => setReason(e.target.value)} />
        </Field>
        <label className="check-inline">
          <input type="checkbox" checked={refund} onChange={(e) => setRefund(e.target.checked)} />
          Raise a refund for the unused part of the term
        </label>
        <div className="form-note">The server works out any refundable amount; it is not calculated here.</div>
      </div>
    </Modal>
  );
}

/* ---------------------------------------------------------- collect / history */

function CollectModal(
  { row, methods, currency, onDone, onClose }:
  { row: SubscriptionDto; methods: PaymentMethodDto[]; currency: string; onDone: () => void; onClose: () => void },
) {
  const [paymentMethodId, setMethodId] = useState<number | ''>('');
  const [amount, setAmount] = useState(String(row.outstandingAmount));
  const [transactionReference, setReference] = useState('');
  const [payerVpa, setVpa] = useState('');
  const [markConfirmed, setConfirmed] = useState(true);
  const [notes, setNotes] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);

  async function save() {
    if (paymentMethodId === '') return;
    setBusy(true); setError(null);
    try {
      await paymentsApi.create({
        memberId: row.memberId,
        subscriptionId: row.id,
        amount: Number(amount),
        discountAmount: 0,
        taxAmount: 0,
        paymentMethodId: Number(paymentMethodId),
        transactionReference: transactionReference.trim() || null,
        payerVpa: payerVpa.trim() || null,
        notes: notes.trim() || null,
        markConfirmed,
      });
      onDone();
    } catch (err) { setError(err); } finally { setBusy(false); }
  }

  return (
    <Modal
      title={`Collect payment — ${row.memberName}`} icon={<IconMoney size={18} />} onClose={onClose} width={620}
      footer={
        <>
          <button className="btn btn-outline" onClick={onClose} disabled={busy}>Cancel</button>
          <button className="btn btn-dark" onClick={save} disabled={busy || paymentMethodId === '' || !amount}>
            {busy ? 'Saving…' : 'Record payment'}
          </button>
        </>
      }
    >
      <div className="stack">
        {error ? <ErrorAlert error={error} /> : null}
        <Alert tone="info">
          <div>
            <div style={{ fontWeight: 600 }}>{row.subscriptionCode} — {row.planName}</div>
            <div>
              Final {money(row.finalAmount, currency)} · paid {money(row.paidAmount, currency)} ·
              outstanding <strong>{money(row.outstandingAmount, currency)}</strong> (server figures).
            </div>
          </div>
        </Alert>
        <div className="form-grid">
          <Field label="Payment method" required>
            <select className="select" value={paymentMethodId} onChange={(e) => setMethodId(e.target.value === '' ? '' : Number(e.target.value))}>
              <option value="">Select…</option>
              {methods.map((m) => <option key={m.id} value={m.id}>{m.name}</option>)}
            </select>
          </Field>
          <Field label="Amount" required>
            <div className="input-group">
              <span className="input-icon">{currency}</span>
              <input className="input" type="number" min={0} step="0.01" value={amount} onChange={(e) => setAmount(e.target.value)} />
            </div>
          </Field>
          <Field label="Transaction reference">
            <input className="input" maxLength={128} value={transactionReference} onChange={(e) => setReference(e.target.value)} />
          </Field>
          <Field label="Payer VPA" help="Optional. e.g. name@bank">
            <input className="input" maxLength={128} value={payerVpa} onChange={(e) => setVpa(e.target.value)} />
          </Field>
        </div>
        <label className="check-inline">
          <input type="checkbox" checked={markConfirmed} onChange={(e) => setConfirmed(e.target.checked)} />
          Mark confirmed — I have verified this transfer
        </label>
        <Field label="Notes">
          <textarea className="textarea" style={{ minHeight: 70 }} maxLength={2000} value={notes} onChange={(e) => setNotes(e.target.value)} />
        </Field>
      </div>
    </Modal>
  );
}

function HistoryModal(
  { row, currency, onClose }: { row: SubscriptionDto; currency: string; onClose: () => void },
) {
  const [entries, setEntries] = useState<SubscriptionHistoryDto[] | null>(null);
  const [error, setError] = useState<unknown>(null);

  useEffect(() => {
    const controller = new AbortController();
    subscriptionsApi.history(row.id, controller.signal)
      .then((rows) => { if (!controller.signal.aborted) setEntries(rows); })
      .catch((err) => { if (!controller.signal.aborted) setError(err); });
    return () => controller.abort();
  }, [row.id]);

  return (
    <Modal
      title={`History — ${row.subscriptionCode}`} icon={<IconClock size={18} />} onClose={onClose} width={640}
      footer={<button className="btn btn-outline" onClick={onClose}>Close</button>}
    >
      {error ? <ErrorAlert error={error} /> : null}
      {!entries && !error && <Loading message="Loading history…" />}
      {entries && entries.length === 0 && <EmptyState icon={<IconClock size={30} />} title="Nothing recorded yet" />}
      {entries && entries.length > 0 && (
        <ul className="timeline">
          {entries.map((entry) => (
            <li key={entry.id}>
              <span className="when">{dateTime(entry.actionDate)}</span>
              <span className="grow">
                <div className="cell-main">
                  {words(entry.actionText)}
                  {entry.amount != null ? <> · {money(entry.amount, currency)}</> : null}
                </div>
                {entry.newEndDate ? <div className="cell-sub">New end date {fmtDate(entry.newEndDate)}</div> : null}
                {entry.remarks ? <div className="cell-sub">{entry.remarks}</div> : null}
                {entry.performedByName ? <div className="cell-sub">by {entry.performedByName}</div> : null}
              </span>
              {entry.newStatus != null ? <StatusPill status={SubscriptionStatus[entry.newStatus]} /> : null}
            </li>
          ))}
        </ul>
      )}
    </Modal>
  );
}
