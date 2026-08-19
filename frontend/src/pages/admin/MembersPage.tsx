import { useEffect, useMemo, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  GENDER_OPTIONS, MEMBER_STATUS_OPTIONS, genderLabel, memberStatusLabel,
  membersApi, planLookup, type MemberQuery,
} from '@/api/endpoints/members';
import { trainersApi } from '@/api/endpoints/trainers';
import type { Gender, Lookup, MemberListDto, MemberStatus, PagedResult } from '@/api/types';
import { useAuth } from '@/auth/AuthContext';
import {
  ConfirmModal, EmptyState, ErrorAlert, FilterField, FilterMenu, FilterStrip, Loading,
  PageCard, PageCardHeader, Pager, Pill, StatusPill, type PillTone,
} from '@/components/ui';
import {
  IconCalendar, IconMoney, IconPhone, IconPlus, IconSearch,
  IconUser, IconUsers,
} from '@/components/icons';
import { date, money } from '@/lib/format';
import './admin.css';

/** What the operator has typed; only copied into the request when Filter is pressed. */
interface Filters {
  search: string;
  status: string;
  gender: string;
  trainerId: string;
  planId: string;
  joinedFrom: string;
  joinedTo: string;
  show: '' | 'expiring' | 'outstanding';
}

const EMPTY: Filters = {
  search: '', status: '', gender: '', trainerId: '', planId: '',
  joinedFrom: '', joinedTo: '', show: '',
};

/** Red once the subscription has lapsed, amber in the final week, green otherwise. */
function DaysLeft({ days }: { days: number | null | undefined }) {
  if (days === null || days === undefined) return <span className="muted">&mdash;</span>;
  const tone: PillTone = days < 0 ? 'danger' : days <= 7 ? 'warning' : 'success';
  return <Pill tone={tone}>{days < 0 ? `${Math.abs(days)} overdue` : `${days} days`}</Pill>;
}

export default function MembersPage() {
  const navigate = useNavigate();
  const { can, currency } = useAuth();

  const [draft, setDraft] = useState<Filters>(EMPTY);
  const [applied, setApplied] = useState<Filters>(EMPTY);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [sortBy, setSortBy] = useState('fullName');
  const [sortDesc, setSortDesc] = useState(false);

  const [result, setResult] = useState<PagedResult<MemberListDto> | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<unknown>(null);
  const [reloadKey, setReloadKey] = useState(0);

  const [trainers, setTrainers] = useState<Lookup[]>([]);
  const [plans, setPlans] = useState<Lookup[]>([]);

  const [toDelete, setToDelete] = useState<MemberListDto | null>(null);
  const [deleting, setDeleting] = useState(false);

  const alive = useRef(true);
  useEffect(() => {
    alive.current = true;
    return () => { alive.current = false; };
  }, []);

  const query = useMemo<MemberQuery>(() => ({
    Search: applied.search.trim() || undefined,
    Status: applied.status ? (Number(applied.status) as MemberStatus) : undefined,
    Gender: applied.gender ? (Number(applied.gender) as Gender) : undefined,
    TrainerId: applied.trainerId ? Number(applied.trainerId) : undefined,
    MembershipPlanId: applied.planId ? Number(applied.planId) : undefined,
    JoinedFrom: applied.joinedFrom || undefined,
    JoinedTo: applied.joinedTo || undefined,
    OnlyExpiringSoon: applied.show === 'expiring' ? true : undefined,
    OnlyWithOutstanding: applied.show === 'outstanding' ? true : undefined,
    PageNumber: page,
    PageSize: pageSize,
    SortBy: sortBy,
    SortDescending: sortDesc,
  }), [applied, page, pageSize, sortBy, sortDesc]);

  useEffect(() => {
    const controller = new AbortController();
    let current = true;
    setLoading(true);
    membersApi.list(query, controller.signal)
      .then((res) => { if (current) { setResult(res); setError(null); } })
      .catch((err) => { if (current) { setError(err); setResult(null); } })
      .finally(() => { if (current) setLoading(false); });
    return () => { current = false; controller.abort(); };
  }, [query, reloadKey]);

  // Filter lookups are optional extras: a caller without the permission simply gets no select.
  useEffect(() => {
    const controller = new AbortController();
    let current = true;
    if (can('trainers.view')) {
      trainersApi.lookup(true, controller.signal)
        .then((rows) => { if (current) setTrainers(rows); })
        .catch(() => { /* the filter degrades to "All trainers" */ });
    }
    if (can('plans.view')) {
      planLookup(controller.signal)
        .then((rows) => { if (current) setPlans(rows); })
        .catch(() => { /* the filter degrades to "All plans" */ });
    }
    return () => { current = false; controller.abort(); };
  }, [can]);

  // Badge on the trigger. Counts only the folded-away filters — search has its own visible box,
  // so including it would show a badge for something the user can already see.
  const activeFilterCount = (['status', 'gender', 'planId', 'trainerId', 'show', 'joinedFrom', 'joinedTo'] as const)
    .filter((key) => applied[key] !== EMPTY[key]).length;

  function applyFilters() {
    setPage(1);
    setApplied(draft);
  }

  function resetFilters() {
    setDraft(EMPTY);
    setApplied(EMPTY);
    setPage(1);
  }

  function toggleSort(column: string) {
    setPage(1);
    if (sortBy === column) setSortDesc((d) => !d);
    else { setSortBy(column); setSortDesc(false); }
  }

  async function confirmDelete() {
    if (!toDelete) return;
    setDeleting(true);
    try {
      await membersApi.remove(toDelete.id);
      if (!alive.current) return;
      setToDelete(null);
      // Stepping back avoids landing on a page that no longer exists.
      if (result && result.items.length === 1 && page > 1) setPage(page - 1);
      else setReloadKey((k) => k + 1);
    } catch (err) {
      if (alive.current) setError(err);
    } finally {
      if (alive.current) setDeleting(false);
    }
  }

  function SortHeader({ column, label, className }: { column: string; label: string; className?: string }) {
    const active = sortBy === column;
    return (
      <th
        className={`${className ?? ''} sortable ${active ? 'sorted' : ''}`}
        onClick={() => toggleSort(column)}
      >
        {label}
        <span className="sort-caret">{active ? (sortDesc ? '▼' : '▲') : '⇅'}</span>
      </th>
    );
  }

  const rows = result?.items ?? [];
  const canEdit = can('members.edit');
  const canDelete = can('members.delete');

  return (
    <div className="page">
      <PageCard>
        <PageCardHeader
          icon={<IconUsers size={20} />}
          title="Members"
          subtitle="Everyone registered at the gym, with their live membership status."
          actions={(
            <>
              <FilterMenu
                activeCount={activeFilterCount}
                onApply={applyFilters}
                onReset={resetFilters}
              >
                <FilterField label="Status">
            <select
              className="select"
              value={draft.status}
              onChange={(e) => setDraft({ ...draft, status: e.target.value })}
            >
              <option value="">All statuses</option>
              {MEMBER_STATUS_OPTIONS.map((s) => (
                <option key={s} value={s}>{memberStatusLabel(s)}</option>
              ))}
            </select>
          </FilterField>

          <FilterField label="Gender">
            <select
              className="select"
              value={draft.gender}
              onChange={(e) => setDraft({ ...draft, gender: e.target.value })}
            >
              <option value="">All</option>
              {GENDER_OPTIONS.map((g) => (
                <option key={g} value={g}>{genderLabel(g)}</option>
              ))}
            </select>
          </FilterField>

          {plans.length > 0 && (
            <FilterField label="Plan">
              <select
                className="select"
                value={draft.planId}
                onChange={(e) => setDraft({ ...draft, planId: e.target.value })}
              >
                <option value="">All plans</option>
                {plans.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
              </select>
            </FilterField>
          )}

          {trainers.length > 0 && (
            <FilterField label="Trainer">
              <select
                className="select"
                value={draft.trainerId}
                onChange={(e) => setDraft({ ...draft, trainerId: e.target.value })}
              >
                <option value="">All trainers</option>
                {trainers.map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}
              </select>
            </FilterField>
          )}

          <FilterField label="Show">
            <select
              className="select"
              value={draft.show}
              onChange={(e) => setDraft({ ...draft, show: e.target.value as Filters['show'] })}
            >
              <option value="">Everyone</option>
              <option value="expiring">Expiring soon</option>
              <option value="outstanding">With outstanding dues</option>
            </select>
          </FilterField>

          <FilterField label="Joined from">
            <input
              className="input"
              type="date"
              value={draft.joinedFrom}
              onChange={(e) => setDraft({ ...draft, joinedFrom: e.target.value })}
            />
          </FilterField>

          <FilterField label="Joined to">
            <input
              className="input"
              type="date"
              value={draft.joinedTo}
              onChange={(e) => setDraft({ ...draft, joinedTo: e.target.value })}
            />
          </FilterField>
              </FilterMenu>

              {can('members.create') ? (
                <button className="btn btn-dark" onClick={() => navigate('/admin/members/new')}>
                  <IconPlus size={15} /> Add Member
                </button>
              ) : null}
            </>
          )}
        />

        {/* Search and Reset stay in the open, since they are the two controls reached most often. */}
        <FilterStrip>
          <FilterField label="Search">
            <div className="input-group">
              <span className="input-icon"><IconSearch size={15} /></span>
              <input
                className="input"
                placeholder="Name, code, phone or email"
                value={draft.search}
                onChange={(e) => setDraft({ ...draft, search: e.target.value })}
                onKeyDown={(e) => { if (e.key === 'Enter') applyFilters(); }}
              />
            </div>
          </FilterField>
        </FilterStrip>

        {error ? <div style={{ padding: 'var(--sp-5)' }}><ErrorAlert error={error} /></div> : null}

        {loading ? (
          <Loading message="Loading members..." />
        ) : rows.length > 0 ? (
          <>
            <div className="table-wrap">
              <table className="table">
                <thead>
                  <tr>
                    <th className="idx">#</th>
                    <SortHeader column="fullName" label="Member" />
                    <th>Phone</th>
                    <th>Plan</th>
                    <SortHeader column="subscriptionEndDate" label="Ends" />
                    <th>Days left</th>
                    <SortHeader column="status" label="Status" />
                    <th className="num">Outstanding</th>
                    <th className="actions">Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {rows.map((member, index) => (
                    <tr
                      key={member.id}
                      className="row-link"
                      onDoubleClick={() => navigate(`/admin/members/${member.id}`)}
                    >
                      <td className="idx">{(page - 1) * pageSize + index + 1}</td>
                      <td>
                        <div className="cell-icon">
                          <IconUser size={14} />
                          <span className="cell-main">{member.fullName}</span>
                        </div>
                        <div className="cell-sub">{member.memberCode}</div>
                      </td>
                      <td>
                        <span className="cell-icon"><IconPhone size={13} />{member.phone}</span>
                      </td>
                      <td>
                        {member.currentPlanName
                          ? <Pill tone="primary">{member.currentPlanName}</Pill>
                          : <span className="muted">&mdash;</span>}
                      </td>
                      <td>
                        <span className="cell-icon">
                          <IconCalendar size={13} />{date(member.subscriptionEndDate)}
                        </span>
                      </td>
                      <td><DaysLeft days={member.daysRemaining} /></td>
                      <td><StatusPill status={member.statusText} /></td>
                      <td className="num">
                        <span className="cell-icon">
                          <IconMoney size={13} />{money(member.outstandingAmount, currency)}
                        </span>
                      </td>
                      <td className="actions">
                        {canEdit && (
                          <button
                            className="btn btn-edit"
                            onClick={() => navigate(`/admin/members/${member.id}/edit`)}
                          >
                            Edit
                          </button>
                        )}
                        {canDelete && (
                          <button className="btn btn-del" onClick={() => setToDelete(member)}>
                            Delete
                          </button>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <Pager
              pageNumber={result?.pageNumber ?? page}
              pageSize={pageSize}
              totalCount={result?.totalCount ?? 0}
              onPage={setPage}
              onPageSize={(size) => { setPageSize(size); setPage(1); }}
            />
          </>
        ) : error ? null : (
          <EmptyState
            icon={<IconUsers size={40} />}
            title="No members found"
            message="Adjust the filters, or register the first member."
            action={can('members.create') ? (
              <button className="btn btn-dark" onClick={() => navigate('/admin/members/new')}>
                <IconPlus size={15} /> Add Member
              </button>
            ) : null}
          />
        )}
      </PageCard>

      {toDelete && (
        <ConfirmModal
          title="Delete member"
          message={
            <>
              <strong>{toDelete.fullName}</strong> ({toDelete.memberCode}) will be moved to the
              recycle bin and can be restored from there.
            </>
          }
          confirmLabel="Delete member"
          busy={deleting}
          onConfirm={confirmDelete}
          onClose={() => setToDelete(null)}
        />
      )}
    </div>
  );
}
