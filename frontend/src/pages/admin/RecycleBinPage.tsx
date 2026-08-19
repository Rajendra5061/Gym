/**
 * Soft-deleted records from every module, with restore and permanent purge.
 *
 * A purge is irreversible, so it is gated on `recyclebin.purge` and always demands the exact
 * phrase the API itself requires before it will destroy anything.
 */

import { useCallback, useEffect, useState } from 'react';
import {
  Alert, ConfirmModal, EmptyState, ErrorAlert, FilterField, FilterMenu, FilterStrip, Loading,
  PageCard, PageCardHeader, Pager, Pill,
} from '@/components/ui';
import { IconCalendar, IconRefresh, IconTrash, IconUser } from '@/components/icons';
import { PURGE_CONFIRMATION, recycleBinApi } from '@/api/endpoints/system';
import type { RecycleBinItemDto, RecycleBinQuery } from '@/api/endpoints/system';
import type { Lookup, PagedResult } from '@/api/types';
import { useAuth } from '@/auth/AuthContext';
import { dateTime, words } from '@/lib/format';

export default function RecycleBinPage() {
  const { can } = useAuth();
  const mayRestore = can('recyclebin.restore');
  const mayPurge = can('recyclebin.purge');

  const [entityName, setEntityName] = useState('');
  const [fromDate, setFromDate] = useState('');
  const [toDate, setToDate] = useState('');
  const [search, setSearch] = useState('');

  const [query, setQuery] = useState<RecycleBinQuery>({ pageNumber: 1, pageSize: 25 });
  const [reloadKey, setReloadKey] = useState(0);
  const reload = useCallback(() => setReloadKey((k) => k + 1), []);

  const [page, setPage] = useState<PagedResult<RecycleBinItemDto> | null>(null);
  const [types, setTypes] = useState<Lookup[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<unknown>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [confirm, setConfirm] = useState<{ kind: 'restore' | 'purge'; row: RecycleBinItemDto } | null>(null);

  useEffect(() => {
    const ctrl = new AbortController();
    setLoading(true);
    setError(null);
    recycleBinApi.paged(query, ctrl.signal)
      .then((result) => { if (!ctrl.signal.aborted) setPage(result); })
      .catch((e) => { if (!ctrl.signal.aborted) setError(e); })
      .finally(() => { if (!ctrl.signal.aborted) setLoading(false); });
    return () => ctrl.abort();
  }, [query, reloadKey]);

  useEffect(() => {
    const ctrl = new AbortController();
    recycleBinApi.entityTypes(ctrl.signal)
      .then((result) => { if (!ctrl.signal.aborted) setTypes(result); })
      .catch(() => { /* the filter falls back to "all entities" */ });
    return () => ctrl.abort();
  }, [reloadKey]);

  function applyFilters() {
    setQuery((q) => ({
      ...q,
      pageNumber: 1,
      search,
      entityName,
      fromDate: fromDate || undefined,
      toDate: toDate || undefined,
    }));
  }

  function resetFilters() {
    setEntityName(''); setFromDate(''); setToDate(''); setSearch('');
    setQuery({ pageNumber: 1, pageSize: query.pageSize ?? 25 });
  }

  async function runConfirmed() {
    if (!confirm) return;
    setBusy(true);
    setError(null);
    try {
      if (confirm.kind === 'restore') {
        await recycleBinApi.restore(confirm.row.entityName, [confirm.row.entityId]);
        setNotice(`${confirm.row.displayName} was restored.`);
      } else {
        await recycleBinApi.purge(confirm.row.entityName, [confirm.row.entityId], PURGE_CONFIRMATION);
        setNotice(`${confirm.row.displayName} was permanently deleted.`);
      }
      setConfirm(null);
      reload();
    } catch (e) {
      setError(e);
      setConfirm(null);
    } finally {
      setBusy(false);
    }
  }

  const rows = page?.items ?? [];
  const pageNumber = page?.pageNumber ?? query.pageNumber ?? 1;
  const pageSize = page?.pageSize ?? query.pageSize ?? 25;
  const totalDeleted = types.reduce((n, t) => n + Number(t.extra ?? 0), 0);
  // `types` lists every entity type the bin *can* hold, so counting it billed empty modules as
  // if they held deleted rows ("6 records across 10 entities"). Only the types that actually
  // have something in the bin are worth naming.
  const typesWithDeleted = types.filter((t) => Number(t.extra ?? 0) > 0).length;

  // Badge on the trigger. Read from the applied query, not the draft, and counting only the
  // folded-away filters — search keeps its own visible box, so a badge for it would be noise.
  const activeFilterCount = (['entityName', 'fromDate', 'toDate'] as const)
    .filter((key) => {
      const value = query[key];
      return value !== undefined && value !== '';
    }).length;

  return (
    <div className="page">
      <PageCard>
        <PageCardHeader
          icon={<IconTrash size={20} />}
          title="Recycle Bin"
          subtitle={`${totalDeleted} deleted record${totalDeleted === 1 ? '' : 's'} across ${typesWithDeleted} entity type${typesWithDeleted === 1 ? '' : 's'} — restore them, or remove them for good.`}
          actions={(
            <>
              <FilterMenu
                activeCount={activeFilterCount}
                onApply={applyFilters}
                onReset={resetFilters}
              >
                <FilterField label="Entity type">
                  <select className="select" value={entityName} onChange={(e) => setEntityName(e.target.value)}>
                    <option value="">All entities</option>
                    {types.map((t) => (
                      <option key={t.id} value={t.code ?? t.name}>
                        {words(t.name)} ({t.extra ?? 0})
                      </option>
                    ))}
                  </select>
                </FilterField>

                <FilterField label="Deleted from">
                  <input className="input" type="date" value={fromDate} onChange={(e) => setFromDate(e.target.value)} />
                </FilterField>

                <FilterField label="Deleted to">
                  <input className="input" type="date" value={toDate} onChange={(e) => setToDate(e.target.value)} />
                </FilterField>
              </FilterMenu>

              <button className="btn btn-dark" onClick={reload}><IconRefresh size={15} /> Refresh</button>
            </>
          )}
        />

        {/* Search and Reset stay in the open, since they are the two controls reached most often. */}
        <FilterStrip>
          <FilterField label="Search">
            <input
              className="input"
              placeholder="Name or details"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              onKeyDown={(e) => { if (e.key === 'Enter') applyFilters(); }}
            />
          </FilterField>
        </FilterStrip>

        {notice && <div style={{ padding: '16px 20px 0' }}><Alert tone="success">{notice}</Alert></div>}
        {error ? <div style={{ padding: 20 }}><ErrorAlert error={error} /></div> : null}

        {loading ? <Loading message="Loading deleted records…" /> : rows.length === 0 ? (
          <EmptyState
            icon={<IconTrash size={34} />}
            title="The recycle bin is empty"
            message="Deleted members, payments, plans and accounts show up here until they are purged."
          />
        ) : (
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th className="idx">#</th>
                  <th>Entity</th>
                  <th>Name</th>
                  <th>Details</th>
                  <th>Deleted at</th>
                  <th>Deleted by</th>
                  <th className="actions">Actions</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((row, index) => (
                  <tr key={`${row.entityName}-${row.entityId}`}>
                    <td className="idx">{(pageNumber - 1) * pageSize + index + 1}</td>
                    <td><Pill tone="neutral">{words(row.entityName)}</Pill></td>
                    <td>
                      <div className="cell-main">{row.displayName}</div>
                      <div className="cell-sub">Id {row.entityId}</div>
                    </td>
                    <td style={{ maxWidth: 320 }}>{row.details || '—'}</td>
                    <td><span className="cell-icon"><IconCalendar size={14} />{dateTime(row.deletedAt)}</span></td>
                    <td><span className="cell-icon"><IconUser size={14} />{row.deletedByName || '—'}</span></td>
                    <td className="actions">
                      {mayRestore && (
                        <button className="btn btn-edit" onClick={() => setConfirm({ kind: 'restore', row })}>
                          Restore
                        </button>
                      )}
                      {mayPurge && (
                        <button className="btn btn-del" onClick={() => setConfirm({ kind: 'purge', row })}>
                          Purge
                        </button>
                      )}
                      {!mayRestore && !mayPurge && <span className="muted">View only</span>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {page && page.totalCount > 0 && (
          <Pager
            pageNumber={pageNumber}
            pageSize={pageSize}
            totalCount={page.totalCount}
            onPage={(p) => setQuery((q) => ({ ...q, pageNumber: p }))}
            onPageSize={(size) => setQuery((q) => ({ ...q, pageNumber: 1, pageSize: size }))}
          />
        )}
      </PageCard>

      {confirm?.kind === 'restore' && (
        <ConfirmModal
          title="Restore record"
          tone="primary"
          confirmLabel="Restore"
          busy={busy}
          message={
            <>Restore <strong>{confirm.row.displayName}</strong> ({words(confirm.row.entityName)})? It returns to its
              module exactly as it was before deletion.</>
          }
          onConfirm={() => void runConfirmed()}
          onClose={() => setConfirm(null)}
        />
      )}

      {confirm?.kind === 'purge' && (
        <ConfirmModal
          title="Permanently delete record"
          confirmLabel="Permanently delete"
          confirmText={PURGE_CONFIRMATION}
          busy={busy}
          message={
            <>
              <strong>{confirm.row.displayName}</strong> ({words(confirm.row.entityName)}, id {confirm.row.entityId}) will be
              erased from the database. This cannot be undone and the record cannot be recovered from any
              screen afterwards.
            </>
          }
          onConfirm={() => void runConfirmed()}
          onClose={() => setConfirm(null)}
        />
      )}
    </div>
  );
}
