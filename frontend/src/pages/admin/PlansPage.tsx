import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { plansApi, type PlanInput, type PlanQuery } from '@/api/endpoints/plans';
import { useAuth } from '@/auth/AuthContext';
import {
  Alert, ConfirmModal, EmptyState, ErrorAlert, Field, FilterField, FilterMenu, FilterStrip,
  Loading, Modal, PageCard, PageCardHeader, Pager, Pill, StatusPill,
} from '@/components/ui';
import {
  IconCheck, IconCrown, IconDashboard, IconEdit, IconFilter, IconPlus, IconRefresh, IconTrash,
} from '@/components/icons';
import { money, words } from '@/lib/format';
import { PlanDurationType, PlanStatus, type MembershipPlanDto, type PagedResult } from '@/api/types';
import './billing.css';

/* --------------------------------------------------------------------- helpers */

const DURATION_LABELS: Record<PlanDurationType, string> = {
  [PlanDurationType.Day]: 'day',
  [PlanDurationType.Week]: 'week',
  [PlanDurationType.Month]: 'month',
  [PlanDurationType.Quarter]: 'quarter',
  [PlanDurationType.HalfYear]: 'half-year',
  [PlanDurationType.Year]: 'year',
  [PlanDurationType.Custom]: 'day',
};

/** "1 month", "3 months", "90 days" — the phrasing the plan tiles use. */
function durationText(plan: Pick<MembershipPlanDto, 'durationType' | 'durationValue' | 'durationTypeText'>): string {
  // A duration type this build does not know falls back to the API's own name, split into
  // words first — otherwise `HalfYear` reads as "halfyear".
  const unit = DURATION_LABELS[plan.durationType] ?? words(plan.durationTypeText).toLowerCase();
  if (!unit) return String(plan.durationValue);
  return `${plan.durationValue} ${unit}${plan.durationValue === 1 ? '' : 's'}`;
}

/** `features` arrives as one newline-separated blob; the tile renders it as a ticked list. */
function featureList(features: string | null | undefined): string[] {
  return (features ?? '')
    .split(/\r?\n/)
    .map((line) => line.replace(/^[-*•]\s*/, '').trim())
    .filter(Boolean);
}

const BLANK_FORM: PlanInput = {
  id: 0,
  name: '',
  description: '',
  features: '',
  durationType: PlanDurationType.Month,
  durationValue: 1,
  price: 0,
  registrationFee: null,
  taxPercent: 0,
  maxDiscountPercent: 100,
  gracePeriodDays: 0,
  maxFreezeDays: 0,
  sessionLimit: null,
  trainerIncluded: false,
  displayOrder: 0,
  status: PlanStatus.Active,
};

function toForm(plan: MembershipPlanDto): PlanInput {
  return {
    id: plan.id,
    name: plan.name,
    description: plan.description ?? '',
    features: plan.features ?? '',
    durationType: plan.durationType,
    durationValue: plan.durationValue,
    price: plan.price,
    registrationFee: plan.registrationFee ?? null,
    taxPercent: plan.taxPercent,
    maxDiscountPercent: plan.maxDiscountPercent,
    gracePeriodDays: plan.gracePeriodDays,
    maxFreezeDays: plan.maxFreezeDays,
    sessionLimit: plan.sessionLimit ?? null,
    trainerIncluded: plan.trainerIncluded,
    displayOrder: plan.displayOrder,
    status: plan.status,
  };
}

/* ------------------------------------------------------------------- the page */

export default function PlansPage() {
  const { can, currency } = useAuth();
  const mayManage = can('plans.manage');

  const [view, setView] = useState<'cards' | 'table'>('cards');
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState<PlanStatus | ''>('');
  const [durationType, setDurationType] = useState<PlanDurationType | ''>('');
  const [query, setQuery] = useState<PlanQuery>({ pageNumber: 1, pageSize: 25, sortBy: 'displayOrder' });

  const [data, setData] = useState<PagedResult<MembershipPlanDto> | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<unknown>(null);
  const [notice, setNotice] = useState('');

  const [editing, setEditing] = useState<PlanInput | null>(null);
  const [deleting, setDeleting] = useState<MembershipPlanDto | null>(null);
  const [busy, setBusy] = useState(false);
  const [formError, setFormError] = useState<unknown>(null);

  const load = useCallback((signal: AbortSignal) => {
    setLoading(true);
    setError(null);
    plansApi.paged(query, signal)
      .then((result) => { if (!signal.aborted) setData(result); })
      .catch((err) => { if (!signal.aborted) setError(err); })
      .finally(() => { if (!signal.aborted) setLoading(false); });
  }, [query]);

  useEffect(() => {
    const controller = new AbortController();
    load(controller.signal);
    return () => controller.abort();
  }, [load]);

  const reload = useCallback(() => setQuery((q) => ({ ...q })), []);

  // Badge on the trigger. Counted off the applied query, and only over the folded-away filters —
  // search keeps its own visible box, so a badge for it would be noise.
  const activeFilterCount = (['status', 'durationType'] as const)
    // `resetFilters` drops the keys outright; `applyFilters` writes '' for an unset one.
    .filter((key) => query[key] !== undefined && query[key] !== '').length;

  function applyFilters() {
    setQuery((q) => ({ ...q, pageNumber: 1, search: search.trim(), status, durationType }));
  }

  function resetFilters() {
    setSearch(''); setStatus(''); setDurationType('');
    setQuery({ pageNumber: 1, pageSize: 25, sortBy: 'displayOrder' });
  }

  async function save() {
    if (!editing) return;
    setBusy(true);
    setFormError(null);
    try {
      const payload: PlanInput = {
        ...editing,
        name: editing.name.trim(),
        description: editing.description?.trim() || null,
        features: editing.features?.trim() || null,
        registrationFee: editing.registrationFee === null ? null : Number(editing.registrationFee),
        sessionLimit: editing.sessionLimit === null ? null : Number(editing.sessionLimit),
      };
      if (payload.id > 0) await plansApi.update(payload.id, payload);
      else await plansApi.create(payload);
      setNotice(payload.id > 0 ? 'Membership plan updated.' : 'Membership plan created.');
      setEditing(null);
      reload();
    } catch (err) {
      setFormError(err);
    } finally {
      setBusy(false);
    }
  }

  async function remove() {
    if (!deleting) return;
    setBusy(true);
    try {
      await plansApi.remove(deleting.id);
      setNotice(`Plan "${deleting.name}" deleted.`);
      setDeleting(null);
      reload();
    } catch (err) {
      setError(err);
      setDeleting(null);
    } finally {
      setBusy(false);
    }
  }

  const items = data?.items ?? [];
  const firstIndex = ((data?.pageNumber ?? 1) - 1) * (data?.pageSize ?? 25);

  return (
    <div className="page">
      <PageCard>
        <PageCardHeader
          icon={<IconCrown size={20} />}
          title="Membership Plans"
          subtitle="Price, duration and the rules every subscription is sold against."
          actions={
            <>
              <FilterMenu
                activeCount={activeFilterCount}
                onApply={applyFilters}
                onReset={resetFilters}
              >
                <FilterField label="Status">
                  <select
                    className="select"
                    value={status}
                    onChange={(e) => setStatus(e.target.value === '' ? '' : (Number(e.target.value) as PlanStatus))}
                  >
                    <option value="">All</option>
                    <option value={PlanStatus.Active}>Active</option>
                    <option value={PlanStatus.Inactive}>Inactive</option>
                  </select>
                </FilterField>

                <FilterField label="Duration">
                  <select
                    className="select"
                    value={durationType}
                    onChange={(e) => setDurationType(e.target.value === '' ? '' : (Number(e.target.value) as PlanDurationType))}
                  >
                    <option value="">All</option>
                    {Object.values(PlanDurationType)
                      .filter((v): v is PlanDurationType => typeof v === 'number')
                      .map((v) => <option key={v} value={v}>{words(PlanDurationType[v])}</option>)}
                  </select>
                </FilterField>
              </FilterMenu>

              <div className="view-toggle" role="group" aria-label="Layout">
                <button className={view === 'cards' ? 'on' : ''} onClick={() => setView('cards')}>
                  <IconDashboard size={14} /> Cards
                </button>
                <button className={view === 'table' ? 'on' : ''} onClick={() => setView('table')}>
                  <IconFilter size={14} /> Table
                </button>
              </div>
              <button className="btn btn-outline btn-icon" onClick={reload} aria-label="Refresh">
                <IconRefresh size={15} />
              </button>
              {mayManage && (
                <button className="btn btn-dark" onClick={() => { setFormError(null); setEditing({ ...BLANK_FORM }); }}>
                  <IconPlus size={15} /> Add Plan
                </button>
              )}
            </>
          }
        />

        {/* Only the search box stays in the open. Enter applies it; Status, Duration and the
            Apply / Clear all pair live in the header's Filters menu. */}
        <FilterStrip>
          <FilterField label="Search">
            <input
              className="input"
              placeholder="Plan name or code"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              onKeyDown={(e) => { if (e.key === 'Enter') applyFilters(); }}
            />
          </FilterField>
        </FilterStrip>

        {notice && <div className="section-pad" style={{ paddingBottom: 0 }}><Alert tone="success">{notice}</Alert></div>}
        {error ? <div className="section-pad"><ErrorAlert error={error} /></div> : null}

        {loading ? (
          <Loading message="Loading plans…" />
        ) : items.length === 0 ? (
          <EmptyState
            icon={<IconCrown size={34} />}
            title="No membership plans yet"
            message="Create a plan so subscriptions have something to sell."
            action={mayManage
              ? <button className="btn btn-dark" onClick={() => setEditing({ ...BLANK_FORM })}><IconPlus size={15} /> Add Plan</button>
              : undefined}
          />
        ) : view === 'cards' ? (
          <div className="plan-gallery">
            {items.map((plan) => (
              <PlanTile
                key={plan.id}
                plan={plan}
                currency={currency}
                mayManage={mayManage}
                onEdit={() => { setFormError(null); setEditing(toForm(plan)); }}
                onDelete={() => setDeleting(plan)}
              />
            ))}
          </div>
        ) : (
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th className="idx">#</th>
                  <th>Plan Name</th>
                  <th>Duration</th>
                  <th className="num">Fee</th>
                  <th>Status</th>
                  <th className="actions">Actions</th>
                </tr>
              </thead>
              <tbody>
                {items.map((plan, i) => (
                  <tr key={plan.id}>
                    <td className="idx">{firstIndex + i + 1}</td>
                    <td>
                      <div className="cell-main">{plan.name}</div>
                      <div className="cell-sub">{plan.planCode}</div>
                    </td>
                    <td>{durationText(plan)}</td>
                    <td className="money-cell">{money(plan.price, currency)}</td>
                    <td><StatusPill status={plan.statusText} /></td>
                    <td className="actions">
                      {mayManage ? (
                        <>
                          <button className="btn btn-edit" onClick={() => { setFormError(null); setEditing(toForm(plan)); }}>
                            <IconEdit size={12} /> Edit
                          </button>
                          <button className="btn btn-del" onClick={() => setDeleting(plan)}>
                            <IconTrash size={12} /> Delete
                          </button>
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
            pageNumber={data.pageNumber}
            pageSize={data.pageSize}
            totalCount={data.totalCount}
            onPage={(page) => setQuery((q) => ({ ...q, pageNumber: page }))}
            onPageSize={(size) => setQuery((q) => ({ ...q, pageNumber: 1, pageSize: size }))}
          />
        )}
      </PageCard>

      {editing && (
        <PlanFormModal
          value={editing}
          currency={currency}
          busy={busy}
          error={formError}
          onChange={setEditing}
          onSave={save}
          onClose={() => setEditing(null)}
        />
      )}

      {deleting && (
        <ConfirmModal
          title="Delete membership plan"
          message={
            <>
              <p style={{ margin: 0 }}>
                <strong>{deleting.name}</strong> will be removed from the catalogue.
                {deleting.activeSubscriptionCount > 0 && (
                  <> It currently backs <strong>{deleting.activeSubscriptionCount}</strong> active subscription(s).</>
                )}
              </p>
              <p className="muted" style={{ marginBottom: 0 }}>Deleted plans can be restored from the recycle bin.</p>
            </>
          }
          confirmLabel="Delete plan"
          busy={busy}
          onConfirm={remove}
          onClose={() => setDeleting(null)}
        />
      )}
    </div>
  );
}

/* ------------------------------------------------------------------ plan tile */

function PlanTile(
  { plan, currency, mayManage, onEdit, onDelete }:
  { plan: MembershipPlanDto; currency: string; mayManage: boolean; onEdit: () => void; onDelete: () => void },
) {
  const features = featureList(plan.features);

  return (
    <article className="plan-tile">
      <div className="plan-tile-head">
        <div className="grow">
          <div className="plan-tile-name">{plan.name}</div>
          <div className="plan-tile-code">{plan.planCode}</div>
        </div>
        <StatusPill status={plan.statusText} />
      </div>

      <div>
        <div className="plan-price">{money(plan.price, currency)}</div>
        <div className="plan-duration">
          {durationText(plan)} · {plan.totalDays} day{plan.totalDays === 1 ? '' : 's'}
        </div>
      </div>

      {plan.description ? <div className="plan-desc">{plan.description}</div> : null}

      {features.length > 0 && (
        <ul className="plan-features">
          {features.map((feature, i) => (
            <li key={i}><IconCheck size={14} /><span>{feature}</span></li>
          ))}
        </ul>
      )}

      <div className="plan-meta">
        <div className="plan-meta-row">
          <span>Registration</span>
          <span>{plan.registrationFee ? money(plan.registrationFee, currency) : 'None'}</span>
        </div>
        <div className="plan-meta-row"><span>Tax</span><span>{plan.taxPercent}%</span></div>
        <div className="plan-meta-row"><span>Grace</span><span>{plan.gracePeriodDays} d</span></div>
        <div className="plan-meta-row"><span>Freeze</span><span>{plan.maxFreezeDays} d</span></div>
        <div className="plan-meta-row">
          <span>Sessions</span>
          <span>{plan.sessionLimit ?? 'Unlimited'}</span>
        </div>
        <div className="plan-meta-row">
          <span>Trainer</span>
          <span>{plan.trainerIncluded ? 'Included' : 'No'}</span>
        </div>
      </div>

      <div className="plan-tile-actions">
        <Pill tone="neutral">{plan.activeSubscriptionCount} active</Pill>
        <span className="grow" />
        {mayManage && (
          <>
            <button className="btn btn-edit" onClick={onEdit}><IconEdit size={12} /> Edit</button>
            <button className="btn btn-del" onClick={onDelete}><IconTrash size={12} /> Delete</button>
          </>
        )}
      </div>
    </article>
  );
}

/* ------------------------------------------------------------------ form modal */

function PlanFormModal(
  { value, currency, busy, error, onChange, onSave, onClose }:
  {
    value: PlanInput; currency: string; busy: boolean; error: unknown;
    onChange: (next: PlanInput) => void; onSave: () => void; onClose: () => void;
  },
) {
  const set = <K extends keyof PlanInput>(key: K, next: PlanInput[K]) => onChange({ ...value, [key]: next });
  const nameRef = useRef<HTMLInputElement>(null);
  useEffect(() => { nameRef.current?.focus(); }, []);

  const previewFeatures = useMemo(() => featureList(value.features), [value.features]);

  return (
    <Modal
      title={value.id > 0 ? 'Edit membership plan' : 'Add membership plan'}
      icon={<IconCrown size={18} />}
      onClose={onClose}
      width={760}
      footer={
        <>
          <button className="btn btn-outline" onClick={onClose} disabled={busy}>Cancel</button>
          <button className="btn btn-dark" onClick={onSave} disabled={busy || !value.name.trim()}>
            {busy ? 'Saving…' : 'Save Plan'}
          </button>
        </>
      }
    >
      <div className="stack">
        {error ? <ErrorAlert error={error} /> : null}

        <div className="form-grid">
          <Field label="Plan name" required>
            <input
              ref={nameRef}
              className="input"
              value={value.name}
              maxLength={120}
              onChange={(e) => set('name', e.target.value)}
            />
          </Field>
          <Field label="Status">
            <select className="select" value={value.status} onChange={(e) => set('status', Number(e.target.value) as PlanStatus)}>
              <option value={PlanStatus.Active}>Active</option>
              <option value={PlanStatus.Inactive}>Inactive</option>
            </select>
          </Field>
          <Field label="Duration type">
            <select
              className="select"
              value={value.durationType}
              onChange={(e) => set('durationType', Number(e.target.value) as PlanDurationType)}
            >
              {Object.values(PlanDurationType)
                .filter((v): v is PlanDurationType => typeof v === 'number')
                .map((v) => <option key={v} value={v}>{words(PlanDurationType[v])}</option>)}
            </select>
          </Field>
          <Field label="Duration value" help="1 to 3650. The server derives the term length from this.">
            <input
              className="input" type="number" min={1} max={3650}
              value={value.durationValue}
              onChange={(e) => set('durationValue', Number(e.target.value))}
            />
          </Field>
          <Field label="Price" required help={`Charged per term, in ${currency}.`}>
            <div className="input-group">
              <span className="input-icon">{currency}</span>
              <input
                className="input" type="number" min={0} step="0.01"
                value={value.price}
                onChange={(e) => set('price', Number(e.target.value))}
              />
            </div>
          </Field>
          <Field label="Registration fee" help="Leave empty when the plan has no joining fee.">
            <div className="input-group">
              <span className="input-icon">{currency}</span>
              <input
                className="input" type="number" min={0} step="0.01"
                value={value.registrationFee ?? ''}
                onChange={(e) => set('registrationFee', e.target.value === '' ? null : Number(e.target.value))}
              />
            </div>
          </Field>
          <Field label="Tax percent">
            <input
              className="input" type="number" min={0} max={100} step="0.01"
              value={value.taxPercent}
              onChange={(e) => set('taxPercent', Number(e.target.value))}
            />
          </Field>
          <Field label="Max discount percent" help="Caps the discount an operator may key in.">
            <input
              className="input" type="number" min={0} max={100} step="0.01"
              value={value.maxDiscountPercent}
              onChange={(e) => set('maxDiscountPercent', Number(e.target.value))}
            />
          </Field>
          <Field label="Grace period (days)">
            <input
              className="input" type="number" min={0} max={90}
              value={value.gracePeriodDays}
              onChange={(e) => set('gracePeriodDays', Number(e.target.value))}
            />
          </Field>
          <Field label="Max freeze days">
            <input
              className="input" type="number" min={0} max={365}
              value={value.maxFreezeDays}
              onChange={(e) => set('maxFreezeDays', Number(e.target.value))}
            />
          </Field>
          <Field label="Session limit" help="Leave empty for unlimited sessions.">
            <input
              className="input" type="number" min={1}
              value={value.sessionLimit ?? ''}
              onChange={(e) => set('sessionLimit', e.target.value === '' ? null : Number(e.target.value))}
            />
          </Field>
          <Field label="Display order">
            <input
              className="input" type="number" min={0}
              value={value.displayOrder}
              onChange={(e) => set('displayOrder', Number(e.target.value))}
            />
          </Field>
        </div>

        <label className="check-inline">
          <input
            type="checkbox"
            checked={value.trainerIncluded}
            onChange={(e) => set('trainerIncluded', e.target.checked)}
          />
          Personal trainer included in this plan
        </label>

        <Field label="Description">
          <textarea
            className="textarea" style={{ minHeight: 70 }}
            value={value.description ?? ''}
            onChange={(e) => set('description', e.target.value)}
          />
        </Field>

        <Field label="Features" help="One feature per line. The plan card shows them as a ticked list.">
          <textarea
            className="textarea"
            value={value.features ?? ''}
            placeholder={'Unlimited gym access\nLocker included\n2 guest passes'}
            onChange={(e) => set('features', e.target.value)}
          />
        </Field>

        {previewFeatures.length > 0 && (
          <ul className="plan-features">
            {previewFeatures.map((feature, i) => <li key={i}><IconCheck size={14} /><span>{feature}</span></li>)}
          </ul>
        )}
      </div>
    </Modal>
  );
}
