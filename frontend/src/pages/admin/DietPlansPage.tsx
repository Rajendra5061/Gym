import { useEffect, useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { useAuth } from '@/auth/AuthContext';
import {
  ConfirmModal, EmptyState, ErrorAlert, FilterField, FilterMenu, FilterStrip, Loading,
  PageCard, PageCardHeader, Pager, Pill, SearchField,
} from '@/components/ui';
import { IconCalendar, IconFile, IconPlus, IconSearch } from '@/components/icons';
import {
  DIET_STATUS_OPTIONS, dietApi, dietStatusLabel, dietStatusTone,
  type DietPlanDto, type DietPlanStatus,
} from '@/api/endpoints/diet';
import { membersApi } from '@/api/endpoints/members';
import type { Lookup, PagedResult } from '@/api/types';
import { date as fmtDate } from '@/lib/format';
import './ops.css';

interface Filters {
  search: string;
  memberId: string;
  status: DietPlanStatus | '';
}

const EMPTY_FILTERS: Filters = { search: '', memberId: '', status: '' };

/**
 * Also rendered under /trainer/diet-plans, so every navigation is derived from the current
 * location instead of hardcoding /admin.
 */
export default function DietPlansPage() {
  const navigate = useNavigate();
  const location = useLocation();
  const basePath = location.pathname.replace(/\/+$/, '');
  const { can } = useAuth();
  const manage = can('diet.manage');

  const [draft, setDraft] = useState<Filters>(EMPTY_FILTERS);
  const [applied, setApplied] = useState<Filters>(EMPTY_FILTERS);
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [reloadKey, setReloadKey] = useState(0);

  const [data, setData] = useState<PagedResult<DietPlanDto> | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<unknown>(null);

  const [memberTerm, setMemberTerm] = useState('');
  const [members, setMembers] = useState<Lookup[]>([]);

  const [pendingDelete, setPendingDelete] = useState<DietPlanDto | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    (async () => {
      setLoading(true);
      try {
        const result = await dietApi.list(
          {
            Search: applied.search || undefined,
            MemberId: applied.memberId ? Number(applied.memberId) : undefined,
            Status: applied.status,
            PageNumber: pageNumber,
            PageSize: pageSize,
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
  }, [applied, pageNumber, pageSize, reloadKey]);

  /* Member filter search — debounced so typing does not hammer the lookup ------------ */
  useEffect(() => {
    const controller = new AbortController();
    const timer = window.setTimeout(() => {
      (async () => {
        try {
          const results = await membersApi.lookup(memberTerm, 20, controller.signal);
          if (!controller.signal.aborted) setMembers(results);
        } catch {
          if (!controller.signal.aborted) setMembers([]);
        }
      })();
    }, memberTerm ? 300 : 0);
    return () => { window.clearTimeout(timer); controller.abort(); };
  }, [memberTerm]);

  const confirmDelete = async () => {
    if (!pendingDelete) return;
    setBusy(true);
    try {
      await dietApi.remove(pendingDelete.id);
      setPendingDelete(null);
      setReloadKey((k) => k + 1);
    } catch (err) {
      setError(err);
      setPendingDelete(null);
    } finally {
      setBusy(false);
    }
  };

  const applyFilters = () => { setApplied(draft); setPageNumber(1); };
  const resetFilters = () => {
    setDraft(EMPTY_FILTERS);
    setApplied(EMPTY_FILTERS);
    setMemberTerm('');
    setPageNumber(1);
  };

  // Badge on the trigger. Counts only the folded-away filters — search has its own visible box,
  // so including it would show a badge for something the user can already see.
  const activeFilterCount = (['memberId', 'status'] as const)
    .filter((key) => applied[key] !== EMPTY_FILTERS[key]).length;

  const items = data?.items ?? [];
  const firstIndex = (pageNumber - 1) * pageSize;

  return (
    <div className="page">
      <PageCard>
        <PageCardHeader
          icon={<IconFile size={20} />}
          title="Diet Plans"
          subtitle="Meal-by-meal nutrition plans assigned to members."
          actions={
            <>
              <FilterMenu
                activeCount={activeFilterCount}
                onApply={applyFilters}
                onReset={resetFilters}
              >
                <FilterField label="Member">
                  <div className="stack" style={{ gap: 6 }}>
                    <div className="input-group">
                      <span className="input-icon"><IconSearch size={14} /></span>
                      <input
                        className="input"
                        placeholder="Search by name, code or phone"
                        value={memberTerm}
                        onChange={(e) => setMemberTerm(e.target.value)}
                      />
                    </div>
                    <select
                      className="select"
                      value={draft.memberId}
                      onChange={(e) => setDraft({ ...draft, memberId: e.target.value })}
                    >
                      <option value="">All members</option>
                      {members.map((m) => (
                        <option key={m.id} value={m.id}>
                          {m.name}{m.code ? ` (${m.code})` : ''}
                        </option>
                      ))}
                    </select>
                  </div>
                </FilterField>

                <FilterField label="Status">
                  <select
                    className="select"
                    value={draft.status === '' ? '' : String(draft.status)}
                    onChange={(e) => setDraft({
                      ...draft,
                      status: e.target.value === '' ? '' : (Number(e.target.value) as DietPlanStatus),
                    })}
                  >
                    <option value="">All statuses</option>
                    {DIET_STATUS_OPTIONS.map((s) => <option key={s.value} value={s.value}>{s.label}</option>)}
                  </select>
                </FilterField>
              </FilterMenu>

              {manage && (
                <button className="btn btn-dark" onClick={() => navigate(`${basePath}/new`)}>
                  <IconPlus size={15} /> New Diet Plan
                </button>
              )}
            </>
          }
        />

        {/* Search and Reset stay in the open, since they are the two controls reached most often. */}
        <FilterStrip>
          <SearchField
            placeholder="Title or goal"
            value={draft.search}
            onChange={(value) => setDraft({ ...draft, search: value })}
            onSearch={applyFilters}
          />
        </FilterStrip>

        {loading && <Loading message="Loading diet plans…" />}
        {!loading && Boolean(error) && <div className="page-card-body"><ErrorAlert error={error} /></div>}

        {!loading && !error && items.length === 0 && (
          <EmptyState
            icon={<IconFile size={34} />}
            title="No diet plans yet"
            message="Create a meal-by-meal nutrition plan for a member to see it here."
            action={manage
              ? (
                <button className="btn btn-dark" onClick={() => navigate(`${basePath}/new`)}>
                  <IconPlus size={15} /> New Diet Plan
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
                  <th>Member</th>
                  <th>Trainer</th>
                  <th className="wide">Plan</th>
                  <th className="center fit">Period</th>
                  <th className="fit">Status</th>
                  <th className="num fit">Meals</th>
                  <th className="actions">Actions</th>
                </tr>
              </thead>
              <tbody>
                {items.map((plan, index) => (
                  <tr key={plan.id}>
                    <td className="idx">{firstIndex + index + 1}</td>
                    <td className="cell-main">{plan.memberName}</td>
                    <td>{plan.trainerName || '—'}</td>
                    <td>
                      <div className="cell-main">{plan.title}</div>
                      <div className="cell-sub">{plan.goal || 'No goal set'}</div>
                    </td>
                    <td className="center fit">
                      <span className="cell-icon">
                        <IconCalendar size={13} />
                        {fmtDate(plan.startDate)} – {plan.endDate ? fmtDate(plan.endDate) : 'ongoing'}
                      </span>
                    </td>
                    <td className="fit">
                      <Pill tone={dietStatusTone(plan.status)}>
                        {dietStatusLabel(plan.status, plan.statusText)}
                      </Pill>
                    </td>
                    <td className="num"><Pill tone="info">{plan.meals?.length ?? 0}</Pill></td>
                    <td className="actions">
                      {manage
                        ? (
                          <>
                            <button
                              className="btn btn-edit"
                              onClick={() => navigate(`${basePath}/${plan.id}/edit`)}
                            >
                              Edit
                            </button>
                            <button className="btn btn-del" onClick={() => setPendingDelete(plan)}>Delete</button>
                          </>
                        )
                        : (
                          <button
                            className="btn btn-edit"
                            onClick={() => navigate(`${basePath}/${plan.id}/edit`)}
                          >
                            View
                          </button>
                        )}
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
          title="Delete diet plan"
          message={(
            <>
              Delete <strong>{pendingDelete.title}</strong> for{' '}
              <strong>{pendingDelete.memberName}</strong>? It moves to the recycle bin.
            </>
          )}
          confirmLabel="Delete plan"
          busy={busy}
          onConfirm={() => void confirmDelete()}
          onClose={() => setPendingDelete(null)}
        />
      )}
    </div>
  );
}
