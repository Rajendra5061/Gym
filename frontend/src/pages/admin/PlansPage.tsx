import { useCallback, useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { plansApi, type PlanQuery } from '@/api/endpoints/plans';
import { useAuth } from '@/auth/AuthContext';
import {
  Alert, ConfirmModal, EmptyState, ErrorAlert, FilterField, FilterMenu, FilterStrip,
  Loading, PageCard, PageCardHeader, Pager, Pill, SearchField, StatusPill,
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

/* ------------------------------------------------------------------- the page */

export default function PlansPage() {
  const navigate = useNavigate();
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

  const [deleting, setDeleting] = useState<MembershipPlanDto | null>(null);
  const [busy, setBusy] = useState(false);

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
                <button className="btn btn-dark" onClick={() => navigate('/admin/plans/new')}>
                  <IconPlus size={15} /> Add Plan
                </button>
              )}
            </>
          }
        />

        {/* Only the search box stays in the open. Enter applies it; Status, Duration and the
            Apply / Clear all pair live in the header's Filters menu. */}
        <FilterStrip>
          <SearchField
            placeholder="Plan name or code"
            value={search}
            onChange={setSearch}
            onSearch={applyFilters}
          />
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
              ? <button className="btn btn-dark" onClick={() => navigate('/admin/plans/new')}><IconPlus size={15} /> Add Plan</button>
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
                onEdit={() => navigate(`/admin/plans/${plan.id}/edit`)}
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
                  <th className="wide">Plan Name</th>
                  <th className="fit">Duration</th>
                  <th className="num">Fee</th>
                  <th className="fit">Status</th>
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
                          <button className="btn btn-edit" onClick={() => navigate(`/admin/plans/${plan.id}/edit`)}>
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

