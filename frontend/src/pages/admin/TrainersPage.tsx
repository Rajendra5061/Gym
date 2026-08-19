import { useEffect, useMemo, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  TRAINER_STATUS_OPTIONS, trainerStatusLabel, trainersApi, type TrainerQuery,
} from '@/api/endpoints/trainers';
import { TrainerStatus, type PagedResult, type TrainerListDto } from '@/api/types';
import { useAuth } from '@/auth/AuthContext';
import {
  Alert, ConfirmModal, EmptyState, ErrorAlert, FilterField, FilterMenu, FilterStrip,
  Loading, PageCard, PageCardHeader, Pager, Pill, StatusPill,
} from '@/components/ui';
import {
  IconCalendar, IconPhone, IconPlus, IconSearch, IconUser,
} from '@/components/icons';
import { date } from '@/lib/format';
import './admin.css';

interface Filters { search: string; status: string; specialization: string; }
const EMPTY: Filters = { search: '', status: '', specialization: '' };

export default function TrainersPage() {
  const navigate = useNavigate();
  const { can } = useAuth();

  const [draft, setDraft] = useState<Filters>(EMPTY);
  const [applied, setApplied] = useState<Filters>(EMPTY);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [sortBy, setSortBy] = useState('fullName');
  const [sortDesc, setSortDesc] = useState(false);

  const [result, setResult] = useState<PagedResult<TrainerListDto> | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<unknown>(null);
  const [reloadKey, setReloadKey] = useState(0);
  const [notice, setNotice] = useState<string | null>(null);

  const [toDelete, setToDelete] = useState<TrainerListDto | null>(null);
  const [deleting, setDeleting] = useState(false);

  const alive = useRef(true);
  useEffect(() => {
    alive.current = true;
    return () => { alive.current = false; };
  }, []);

  const query = useMemo<TrainerQuery>(() => ({
    Search: applied.search.trim() || undefined,
    Status: applied.status ? (Number(applied.status) as TrainerStatus) : undefined,
    Specialization: applied.specialization.trim() || undefined,
    PageNumber: page,
    PageSize: pageSize,
    SortBy: sortBy,
    SortDescending: sortDesc,
  }), [applied, page, pageSize, sortBy, sortDesc]);

  useEffect(() => {
    const controller = new AbortController();
    let current = true;
    setLoading(true);
    trainersApi.list(query, controller.signal)
      .then((res) => { if (current) { setResult(res); setError(null); } })
      .catch((err) => { if (current) { setError(err); setResult(null); } })
      .finally(() => { if (current) setLoading(false); });
    return () => { current = false; controller.abort(); };
  }, [query, reloadKey]);

  // Badge on the trigger. Counts only the folded-away filters — search has its own visible box,
  // so including it would show a badge for something the user can already see.
  const activeFilterCount = (['status', 'specialization'] as const)
    .filter((key) => applied[key] !== EMPTY[key]).length;

  function applyFilters() { setPage(1); setApplied(draft); }
  function resetFilters() { setDraft(EMPTY); setApplied(EMPTY); setPage(1); }

  function toggleSort(column: string) {
    setPage(1);
    if (sortBy === column) setSortDesc((d) => !d);
    else { setSortBy(column); setSortDesc(false); }
  }

  async function confirmDelete() {
    if (!toDelete) return;
    setDeleting(true);
    try {
      await trainersApi.remove(toDelete.id);
      if (!alive.current) return;
      setNotice(`${toDelete.fullName} was moved to the recycle bin.`);
      setToDelete(null);
      if (result && result.items.length === 1 && page > 1) setPage(page - 1);
      else setReloadKey((k) => k + 1);
    } catch (err) {
      if (alive.current) setError(err);
    } finally {
      if (alive.current) setDeleting(false);
    }
  }

  function SortHeader({ column, label }: { column: string; label: string }) {
    const active = sortBy === column;
    return (
      <th className={`sortable ${active ? 'sorted' : ''}`} onClick={() => toggleSort(column)}>
        {label}
        <span className="sort-caret">{active ? (sortDesc ? '▼' : '▲') : '⇅'}</span>
      </th>
    );
  }

  const rows = result?.items ?? [];
  const canManage = can('trainers.manage');

  return (
    <div className="page">
      <PageCard>
        <PageCardHeader
          icon={<IconUser size={20} />}
          title="Trainers"
          subtitle="Coaching staff, their specialisations and how many members they carry."
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
                    {TRAINER_STATUS_OPTIONS.map((s) => (
                      <option key={s} value={s}>{trainerStatusLabel(s)}</option>
                    ))}
                  </select>
                </FilterField>

                <FilterField label="Specialization">
                  <input
                    className="input"
                    placeholder="Any specialization"
                    value={draft.specialization}
                    onChange={(e) => setDraft({ ...draft, specialization: e.target.value })}
                    onKeyDown={(e) => { if (e.key === 'Enter') applyFilters(); }}
                  />
                </FilterField>
              </FilterMenu>

              {canManage ? (
                <button className="btn btn-dark" onClick={() => navigate('/admin/trainers/new')}>
                  <IconPlus size={15} /> Add Trainer
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

        {(error || notice) && (
          <div style={{ padding: 'var(--sp-5)' }} className="stack">
            {error ? <ErrorAlert error={error} /> : null}
            {notice ? <Alert tone="success">{notice}</Alert> : null}
          </div>
        )}

        {loading ? (
          <Loading message="Loading trainers..." />
        ) : rows.length > 0 ? (
          <>
            <div className="table-wrap">
              <table className="table">
                <thead>
                  <tr>
                    <th className="idx">#</th>
                    <SortHeader column="fullName" label="Name" />
                    <th>Mobile</th>
                    <th>Specialization</th>
                    <th>Experience</th>
                    <SortHeader column="joiningDate" label="Joining date" />
                    <th className="center">Members</th>
                    <SortHeader column="status" label="Status" />
                    <th className="actions">Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {rows.map((trainer, index) => (
                    <tr key={trainer.id}>
                      <td className="idx">{(page - 1) * pageSize + index + 1}</td>
                      <td>
                        <div className="cell-icon">
                          <IconUser size={14} />
                          <span className="cell-main">{trainer.fullName}</span>
                        </div>
                        <div className="cell-sub">{trainer.trainerCode}</div>
                      </td>
                      <td><span className="cell-icon"><IconPhone size={13} />{trainer.phone}</span></td>
                      <td>
                        {trainer.specialization
                          ? <Pill tone="info">{trainer.specialization}</Pill>
                          : <span className="muted">&mdash;</span>}
                      </td>
                      <td>
                        {trainer.experienceYears === null || trainer.experienceYears === undefined
                          ? <span className="muted">&mdash;</span>
                          : `${trainer.experienceYears} yr${trainer.experienceYears === 1 ? '' : 's'}`}
                      </td>
                      <td>
                        <span className="cell-icon">
                          <IconCalendar size={13} />{date(trainer.joiningDate)}
                        </span>
                      </td>
                      <td className="center">{trainer.assignedMemberCount}</td>
                      <td><StatusPill status={trainer.statusText} /></td>
                      <td className="actions">
                        {canManage && (
                          <button
                            className="btn btn-edit"
                            onClick={() => navigate(`/admin/trainers/${trainer.id}/edit`)}
                          >
                            Edit
                          </button>
                        )}
                        {canManage && (
                          <button className="btn btn-del" onClick={() => setToDelete(trainer)}>
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
            icon={<IconUser size={40} />}
            title="No trainers found"
            message="Adjust the filters, or add the first trainer."
            action={canManage ? (
              <button className="btn btn-dark" onClick={() => navigate('/admin/trainers/new')}>
                <IconPlus size={15} /> Add Trainer
              </button>
            ) : null}
          />
        )}
      </PageCard>

      {toDelete && (
        <ConfirmModal
          title="Delete trainer"
          message={
            <>
              <strong>{toDelete.fullName}</strong> ({toDelete.trainerCode}) will be moved to the
              recycle bin and can be restored from there.
            </>
          }
          confirmLabel="Delete trainer"
          busy={deleting}
          onConfirm={confirmDelete}
          onClose={() => setToDelete(null)}
        />
      )}
    </div>
  );
}
