/**
 * The audit trail and the sign-in attempt history.
 *
 * Both tabs page on the server. Expanding an audit row shows the before/after JSON the API
 * recorded, side by side in a fixed-height scroll box so a large payload can never stretch
 * the table out of the page.
 */

import { Fragment, useCallback, useEffect, useState } from 'react';
import {
  EmptyState, ErrorAlert, FilterField, FilterMenu, FilterStrip, Loading, PageCard, PageCardHeader,
  Pager, Pill, SearchField, StatusPill,
} from '@/components/ui';
import { IconCalendar, IconFile, IconInfo, IconRefresh, IconUser } from '@/components/icons';
import { auditApi, usersApi } from '@/api/endpoints/system';
import type { AuditLogDto, AuditQuery, LoginAttemptDto, PagedQuery } from '@/api/endpoints/system';
import type { Lookup, PagedResult } from '@/api/types';
import { dateTime, words } from '@/lib/format';

/** Pretty-prints a recorded JSON snapshot, leaving unparseable text as it was stored. */
function pretty(value: string | null | undefined): string {
  if (!value) return '—';
  try {
    return JSON.stringify(JSON.parse(value), null, 2);
  } catch {
    return value;
  }
}

const MONO: React.CSSProperties = {
  fontFamily: 'Consolas, "Cascadia Mono", "Courier New", monospace',
  fontSize: 12,
  whiteSpace: 'pre',
  margin: 0,
};

const SCROLL_BOX: React.CSSProperties = {
  border: '1px solid var(--divider)',
  borderRadius: 'var(--r-md)',
  background: '#fbfcfe',
  padding: 12,
  maxHeight: 260,
  overflow: 'auto',
};

function actionTone(action: string) {
  const a = action.toLowerCase();
  if (a.includes('delete') || a.includes('purge')) return 'danger' as const;
  if (a.includes('create') || a.includes('restore')) return 'success' as const;
  if (a.includes('update') || a.includes('edit')) return 'warning' as const;
  if (a.includes('login') || a.includes('logout')) return 'info' as const;
  return 'neutral' as const;
}

export default function AuditPage() {
  const [tab, setTab] = useState<'audit' | 'logins'>('audit');

  /* -------------------------------------------------------------- filters */
  const [userId, setUserId] = useState<number | ''>('');
  const [action, setAction] = useState('');
  const [entityName, setEntityName] = useState('');
  const [fromDate, setFromDate] = useState('');
  const [toDate, setToDate] = useState('');

  const [query, setQuery] = useState<AuditQuery>({ pageNumber: 1, pageSize: 25 });
  const [loginQuery, setLoginQuery] = useState<PagedQuery>({ pageNumber: 1, pageSize: 25 });
  const [reloadKey, setReloadKey] = useState(0);
  const reload = useCallback(() => setReloadKey((k) => k + 1), []);

  /* ----------------------------------------------------------------- data */
  const [page, setPage] = useState<PagedResult<AuditLogDto> | null>(null);
  const [logins, setLogins] = useState<PagedResult<LoginAttemptDto> | null>(null);
  const [users, setUsers] = useState<Lookup[]>([]);
  const [actions, setActions] = useState<string[]>([]);
  const [entities, setEntities] = useState<string[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<unknown>(null);
  const [expanded, setExpanded] = useState<number | null>(null);

  useEffect(() => {
    const ctrl = new AbortController();
    Promise.all([
      auditApi.actions(ctrl.signal).catch(() => [] as string[]),
      auditApi.entities(ctrl.signal).catch(() => [] as string[]),
      usersApi.lookup(ctrl.signal).catch(() => [] as Lookup[]),
    ]).then(([actionList, entityList, userList]) => {
      if (ctrl.signal.aborted) return;
      setActions(actionList);
      setEntities(entityList);
      setUsers(userList);
    });
    return () => ctrl.abort();
  }, []);

  useEffect(() => {
    const ctrl = new AbortController();
    setLoading(true);
    setError(null);
    const work = tab === 'audit'
      ? auditApi.paged(query, ctrl.signal).then((r) => { if (!ctrl.signal.aborted) setPage(r); })
      : auditApi.loginAttempts(loginQuery, ctrl.signal).then((r) => { if (!ctrl.signal.aborted) setLogins(r); });

    work
      .catch((e) => { if (!ctrl.signal.aborted) setError(e); })
      .finally(() => { if (!ctrl.signal.aborted) setLoading(false); });
    return () => ctrl.abort();
  }, [tab, query, loginQuery, reloadKey]);

  function applyFilters() {
    setExpanded(null);
    setQuery((q) => ({
      ...q,
      pageNumber: 1,
      userId,
      action,
      entityName,
      fromDate: fromDate || undefined,
      toDate: toDate || undefined,
    }));
  }

  function resetFilters() {
    setUserId(''); setAction(''); setEntityName(''); setFromDate(''); setToDate('');
    setExpanded(null);
    setQuery({ pageNumber: 1, pageSize: query.pageSize ?? 25 });
  }

  // Badge on the trigger. Read from the applied query, not the draft state, so it always reflects
  // what the table is actually showing. Every audit filter is folded away, so all five count.
  const activeFilterCount = (['userId', 'action', 'entityName', 'fromDate', 'toDate'] as const)
    .filter((key) => {
      const value = query[key];
      return value !== undefined && value !== '';
    }).length;

  const rows = page?.items ?? [];
  const pageNumber = page?.pageNumber ?? query.pageNumber ?? 1;
  const pageSize = page?.pageSize ?? query.pageSize ?? 25;

  const loginRows = logins?.items ?? [];
  const loginPageNumber = logins?.pageNumber ?? loginQuery.pageNumber ?? 1;
  const loginPageSize = logins?.pageSize ?? loginQuery.pageSize ?? 25;

  return (
    <div className="page">
      <PageCard>
        <PageCardHeader
          icon={<IconFile size={20} />}
          title="Audit Logs"
          subtitle="Every recorded change and sign-in attempt, newest first."
          actions={(
            <>
              {/* The five filters below only drive the audit trail, so the trigger goes with it. */}
              {tab === 'audit' && (
                <FilterMenu
                  activeCount={activeFilterCount}
                  onApply={applyFilters}
                  onReset={resetFilters}
                >
                  <FilterField label="User">
                    <select
                      className="select"
                      value={userId}
                      onChange={(e) => setUserId(e.target.value === '' ? '' : Number(e.target.value))}
                    >
                      <option value="">All users</option>
                      {users.map((u) => <option key={u.id} value={u.id}>{u.name}</option>)}
                    </select>
                  </FilterField>

                  <FilterField label="Action">
                    <select className="select" value={action} onChange={(e) => setAction(e.target.value)}>
                      <option value="">All actions</option>
                      {/* The value stays the raw name the API filters on; only the wording is split. */}
                      {actions.map((a) => <option key={a} value={a}>{words(a)}</option>)}
                    </select>
                  </FilterField>

                  <FilterField label="Entity">
                    <select className="select" value={entityName} onChange={(e) => setEntityName(e.target.value)}>
                      <option value="">All entities</option>
                      {entities.map((e) => <option key={e} value={e}>{words(e)}</option>)}
                    </select>
                  </FilterField>

                  <FilterField label="From">
                    <input className="input" type="date" value={fromDate} onChange={(e) => setFromDate(e.target.value)} />
                  </FilterField>

                  <FilterField label="To">
                    <input className="input" type="date" value={toDate} onChange={(e) => setToDate(e.target.value)} />
                  </FilterField>
                </FilterMenu>
              )}

              <button className="btn btn-dark" onClick={reload}><IconRefresh size={15} /> Refresh</button>
            </>
          )}
        />

        {/* ------------------------------------------------------------ tabs */}
        <div className="row" style={{ gap: 4, padding: '0 20px', borderBottom: '1px solid var(--divider)' }}>
          {([['audit', 'Audit trail'], ['logins', 'Login attempts']] as const).map(([key, label]) => (
            <button
              key={key}
              onClick={() => { setTab(key); setExpanded(null); }}
              style={{
                border: 0, background: 'transparent', cursor: 'pointer', padding: '12px 14px',
                fontSize: 13, fontWeight: 700,
                color: tab === key ? 'var(--primary)' : 'var(--text-2)',
                borderBottom: `2px solid ${tab === key ? 'var(--primary)' : 'transparent'}`,
                marginBottom: -1,
              }}
            >
              {label}
            </button>
          ))}
        </div>

        {/*
          The audit trail has no search box, so once the duplicate Filter button goes the strip
          would be an empty band holding nothing but Reset — and the panel's own "Clear all"
          already is that button. Rather than keep a bar with one control in it, the whole strip
          goes and the header's Filters menu carries the tab on its own.
        */}

        {tab === 'logins' && (
          <FilterStrip>
            <SearchField
              placeholder="User name or e-mail"
              value={loginQuery.search ?? ''}
              onChange={(value) => setLoginQuery((q) => ({ ...q, search: value }))}
              onSearch={() => setLoginQuery((q) => ({ ...q, pageNumber: 1 }))}
            />
          </FilterStrip>
        )}

        {error ? <div style={{ padding: 20 }}><ErrorAlert error={error} /></div> : null}

        {loading ? <Loading message="Loading audit entries…" /> : tab === 'audit' ? (
          rows.length === 0 ? (
            <EmptyState icon={<IconFile size={34} />} title="No audit entries" message="Nothing matches the current filters." />
          ) : (
            <div className="table-wrap">
              <table className="table">
                <thead>
                  <tr>
                    <th className="idx">#</th>
                    <th className="fit">Timestamp</th>
                    <th>User</th>
                    <th className="fit">Action</th>
                    <th className="fit">Entity</th>
                    <th className="center fit">Entity id</th>
                    <th className="wide">Description</th>
                    <th className="fit">IP</th>
                    <th className="actions">Details</th>
                  </tr>
                </thead>
                <tbody>
                  {rows.map((row, index) => (
                    <Fragment key={row.id}>
                      <tr>
                        <td className="idx">{(pageNumber - 1) * pageSize + index + 1}</td>
                        <td className="fit"><span className="cell-icon"><IconCalendar size={14} />{dateTime(row.changedAtUtc)}</span></td>
                        <td>
                          <div className="cell-main">{row.userName || 'System'}</div>
                          <div className="cell-sub">{row.userId ? `User #${row.userId}` : 'Background task'}</div>
                        </td>
                        <td className="fit"><Pill tone={actionTone(row.action)}>{words(row.action)}</Pill></td>
                        <td className="fit">{words(row.entityName)}</td>
                        <td className="center">{row.entityId ?? '—'}</td>
                        <td style={{ maxWidth: 320 }}>{row.description || '—'}</td>
                        <td className="muted">{row.ipAddress || '—'}</td>
                        <td className="actions">
                          <button
                            className="btn btn-outline btn-sm"
                            onClick={() => setExpanded(expanded === row.id ? null : row.id)}
                          >
                            {expanded === row.id ? 'Hide' : 'View'}
                          </button>
                        </td>
                      </tr>
                      {expanded === row.id && (
                        <tr>
                          <td colSpan={9} style={{ background: '#f8f9fc' }}>
                            <div style={{ display: 'grid', gap: 16, gridTemplateColumns: 'repeat(auto-fit, minmax(280px, 1fr))' }}>
                              <div>
                                <div className="field-label" style={{ marginBottom: 6 }}>Old values</div>
                                <div style={SCROLL_BOX}><pre style={MONO}>{pretty(row.oldValues)}</pre></div>
                              </div>
                              <div>
                                <div className="field-label" style={{ marginBottom: 6 }}>New values</div>
                                <div style={SCROLL_BOX}><pre style={MONO}>{pretty(row.newValues)}</pre></div>
                              </div>
                            </div>
                            {row.deviceInfo && (
                              <div className="cell-sub" style={{ marginTop: 10 }}>
                                <IconInfo size={12} /> {row.deviceInfo}
                              </div>
                            )}
                          </td>
                        </tr>
                      )}
                    </Fragment>
                  ))}
                </tbody>
              </table>
            </div>
          )
        ) : loginRows.length === 0 ? (
          <EmptyState icon={<IconUser size={34} />} title="No sign-in attempts" message="Nothing has been recorded yet." />
        ) : (
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th className="idx">#</th>
                  <th className="fit">Attempted at</th>
                  <th className="wide">User name / e-mail</th>
                  <th className="fit">Result</th>
                  <th>Reason</th>
                  <th className="fit">IP</th>
                  <th>Device</th>
                </tr>
              </thead>
              <tbody>
                {loginRows.map((row, index) => (
                  <tr key={row.id}>
                    <td className="idx">{(loginPageNumber - 1) * loginPageSize + index + 1}</td>
                    <td className="fit"><span className="cell-icon"><IconCalendar size={14} />{dateTime(row.attemptedAtUtc)}</span></td>
                    <td>
                      <div className="cell-main">{row.userNameOrEmail}</div>
                      <div className="cell-sub">{row.userId ? `User #${row.userId}` : 'Unknown account'}</div>
                    </td>
                    <td><StatusPill status={row.resultText} /></td>
                    <td>{row.failureReason || '—'}</td>
                    <td className="muted">{row.ipAddress || '—'}</td>
                    <td className="muted" style={{ maxWidth: 280 }}>{row.deviceInfo || '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {tab === 'audit' && page && page.totalCount > 0 && (
          <Pager
            pageNumber={pageNumber}
            pageSize={pageSize}
            totalCount={page.totalCount}
            onPage={(p) => { setExpanded(null); setQuery((q) => ({ ...q, pageNumber: p })); }}
            onPageSize={(size) => setQuery((q) => ({ ...q, pageNumber: 1, pageSize: size }))}
          />
        )}

        {tab === 'logins' && logins && logins.totalCount > 0 && (
          <Pager
            pageNumber={loginPageNumber}
            pageSize={loginPageSize}
            totalCount={logins.totalCount}
            onPage={(p) => setLoginQuery((q) => ({ ...q, pageNumber: p }))}
            onPageSize={(size) => setLoginQuery((q) => ({ ...q, pageNumber: 1, pageSize: size }))}
          />
        )}
      </PageCard>
    </div>
  );
}
