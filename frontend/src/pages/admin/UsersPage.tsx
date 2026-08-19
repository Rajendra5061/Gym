/**
 * User accounts: search, reset password, unlock and soft delete.
 *
 * Listing is server-side paged and filtered — the grid never receives more than one page.
 * Creating and editing accounts live on the dedicated form page (/admin/users/new and
 * /admin/users/:id/edit). A password reset still surfaces its generated password here, in a
 * modal, because the API only ever returns it once.
 */

import { useCallback, useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  Alert, ConfirmModal, ErrorAlert, FilterField, FilterMenu, FilterStrip, Loading, Modal,
  Pager, PageCard, PageCardHeader, Pill, SearchField, StatusPill, EmptyState,
} from '@/components/ui';
import {
  IconCheck, IconLock, IconMail, IconPlus, IconUser, IconUsers,
} from '@/components/icons';
import { usersApi, rolesApi } from '@/api/endpoints/system';
import type {
  RoleDto, TemporaryPasswordDto, UserListDto, UserQuery,
} from '@/api/endpoints/system';
import type { PagedResult } from '@/api/types';
import { UserStatus } from '@/api/types';
import { useAuth } from '@/auth/AuthContext';
import { dateTime, initials } from '@/lib/format';

const STATUS_OPTIONS: { value: UserStatus; label: string }[] = [
  { value: UserStatus.Active, label: 'Active' },
  { value: UserStatus.Inactive, label: 'Inactive' },
  { value: UserStatus.Locked, label: 'Locked' },
];

export default function UsersPage() {
  const navigate = useNavigate();
  const { can } = useAuth();
  const mayManage = can('users.manage');

  /* -------------------------------------------------------------- filters */
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState<UserStatus | ''>('');
  const [roleId, setRoleId] = useState<number | ''>('');
  const [includeDeleted, setIncludeDeleted] = useState(false);

  const [query, setQuery] = useState<UserQuery>({ pageNumber: 1, pageSize: 25 });
  const [reloadKey, setReloadKey] = useState(0);
  const reload = useCallback(() => setReloadKey((k) => k + 1), []);

  /* ----------------------------------------------------------------- data */
  const [page, setPage] = useState<PagedResult<UserListDto> | null>(null);
  const [roles, setRoles] = useState<RoleDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<unknown>(null);

  useEffect(() => {
    const ctrl = new AbortController();
    setLoading(true);
    setError(null);
    usersApi.paged(query, ctrl.signal)
      .then((result) => { if (!ctrl.signal.aborted) setPage(result); })
      .catch((e) => { if (!ctrl.signal.aborted) setError(e); })
      .finally(() => { if (!ctrl.signal.aborted) setLoading(false); });
    return () => ctrl.abort();
  }, [query, reloadKey]);

  useEffect(() => {
    const ctrl = new AbortController();
    rolesApi.all(ctrl.signal)
      .then((result) => { if (!ctrl.signal.aborted) setRoles(result); })
      .catch(() => { /* the filter simply stays empty when roles cannot be read */ });
    return () => ctrl.abort();
  }, []);

  function applyFilters() {
    setQuery((q) => ({ ...q, pageNumber: 1, search, status, roleId, includeDeleted }));
  }

  function resetFilters() {
    setSearch(''); setStatus(''); setRoleId(''); setIncludeDeleted(false);
    setQuery({ pageNumber: 1, pageSize: query.pageSize ?? 25 });
  }

  // Badge on the trigger. Read from the applied query, not the draft, and counting only the
  // folded-away filters — search keeps its own visible box, so a badge for it would be noise.
  const activeFilterCount = (['status', 'roleId', 'includeDeleted'] as const)
    .filter((key) => {
      const value = query[key];
      return value !== undefined && value !== '' && value !== false;
    }).length;

  /* --------------------------------------------------------------- modals */
  const [tempPassword, setTempPassword] = useState<TemporaryPasswordDto | null>(null);
  const [copied, setCopied] = useState(false);
  const [confirm, setConfirm] = useState<
    { kind: 'reset' | 'delete'; row: UserListDto } | null
  >(null);
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);

  const rows = page?.items ?? [];
  const pageNumber = page?.pageNumber ?? query.pageNumber ?? 1;
  const pageSize = page?.pageSize ?? query.pageSize ?? 25;

  async function runConfirmed() {
    if (!confirm) return;
    setBusy(true);
    setError(null);
    try {
      if (confirm.kind === 'reset') {
        const result = await usersApi.resetPassword(confirm.row.id);
        setTempPassword(result);
        setCopied(false);
      } else {
        await usersApi.remove(confirm.row.id);
        setNotice(`${confirm.row.userName} was deleted and can be restored from the recycle bin.`);
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

  async function unlock(row: UserListDto) {
    setError(null);
    try {
      await usersApi.unlock(row.id);
      setNotice(`${row.userName} was unlocked.`);
      reload();
    } catch (e) {
      setError(e);
    }
  }

  return (
    <div className="page">
      <PageCard>
        <PageCardHeader
          icon={<IconUsers size={20} />}
          title="Users"
          subtitle="Login accounts, their roles and their sign-in state."
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
                    value={status}
                    onChange={(e) => setStatus(e.target.value === '' ? '' : (Number(e.target.value) as UserStatus))}
                  >
                    <option value="">All statuses</option>
                    {STATUS_OPTIONS.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
                  </select>
                </FilterField>

                <FilterField label="Role">
                  <select
                    className="select"
                    value={roleId}
                    onChange={(e) => setRoleId(e.target.value === '' ? '' : Number(e.target.value))}
                  >
                    <option value="">All roles</option>
                    {roles.map((r) => <option key={r.id} value={r.id}>{r.name}</option>)}
                  </select>
                </FilterField>

                <FilterField label="Deleted">
                  <label className="row" style={{ height: 38, gap: 8 }}>
                    <input
                      type="checkbox"
                      checked={includeDeleted}
                      onChange={(e) => setIncludeDeleted(e.target.checked)}
                    />
                    <span style={{ fontSize: 13 }}>Include deleted</span>
                  </label>
                </FilterField>
              </FilterMenu>

              {mayManage ? (
                <button className="btn btn-dark" onClick={() => navigate('/admin/users/new')}>
                  <IconPlus size={15} /> New User
                </button>
              ) : null}
            </>
          )}
        />

        {/* Search and Reset stay in the open, since they are the two controls reached most often. */}
        <FilterStrip>
          <SearchField
            placeholder="Name, user name or e-mail"
            value={search}
            onChange={setSearch}
            onSearch={applyFilters}
          />
        </FilterStrip>

        {notice && <div style={{ padding: '16px 20px 0' }}><Alert tone="success">{notice}</Alert></div>}
        {error ? <div style={{ padding: 20 }}><ErrorAlert error={error} /></div> : null}

        {loading ? <Loading message="Loading users…" /> : rows.length === 0 ? (
          <EmptyState
            icon={<IconUser size={34} />}
            title="No user accounts found"
            message="Adjust the filters, or create the first account."
            action={mayManage ? (
              <button className="btn btn-dark" onClick={() => navigate('/admin/users/new')}>
                <IconPlus size={15} /> New User
              </button>
            ) : null}
          />
        ) : (
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th className="idx">#</th>
                  <th>User name</th>
                  <th className="wide">Full name</th>
                  <th>E-mail</th>
                  <th className="fit">Roles</th>
                  <th className="fit">Status</th>
                  <th className="center fit">Locked</th>
                  <th className="actions">Actions</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((row, index) => (
                  <tr key={row.id}>
                    <td className="idx">{(pageNumber - 1) * pageSize + index + 1}</td>
                    <td>
                      <div className="cell-main">{row.userName}</div>
                      <div className="cell-sub">
                        {row.lastLoginAtUtc ? `Last sign-in ${dateTime(row.lastLoginAtUtc)}` : 'Never signed in'}
                      </div>
                    </td>
                    <td>
                      <div className="row" style={{ gap: 10 }}>
                        <span className="avatar">{initials(row.fullName)}</span>
                        <div>
                          <div className="cell-main">{row.fullName}</div>
                          <div className="cell-sub">{row.phone || '—'}</div>
                        </div>
                      </div>
                    </td>
                    <td><span className="cell-icon"><IconMail size={14} />{row.email || '—'}</span></td>
                    <td className="fit">
                      <div className="row" style={{ gap: 4, flexWrap: 'wrap' }}>
                        {(row.roles ? row.roles.split(',') : []).map((name) => (
                          <Pill key={name} tone="primary">{name.trim()}</Pill>
                        ))}
                        {!row.roles && <span className="muted">No role</span>}
                      </div>
                    </td>
                    <td className="fit"><StatusPill status={row.statusText} /></td>
                    <td className="center fit">
                      {row.isLockedOut ? <Pill tone="danger"><IconLock size={12} /> Locked</Pill> : <span className="muted">—</span>}
                    </td>
                    <td className="actions">
                      {mayManage ? (
                        <>
                          <button className="btn btn-edit" onClick={() => navigate(`/admin/users/${row.id}/edit`)}>Edit</button>
                          <button
                            className="btn btn-outline btn-sm"
                            onClick={() => setConfirm({ kind: 'reset', row })}
                          >
                            Reset password
                          </button>
                          {row.isLockedOut && (
                            <button className="btn btn-outline btn-sm" onClick={() => void unlock(row)}>Unlock</button>
                          )}
                          <button className="btn btn-del" onClick={() => setConfirm({ kind: 'delete', row })}>Delete</button>
                        </>
                      ) : <span className="muted">View only</span>}
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

      {/* ------------------------------------------------- temporary password */}
      {tempPassword && (
        <Modal
          title="Temporary password"
          icon={<IconLock size={18} />}
          width={480}
          onClose={() => setTempPassword(null)}
          footer={<button className="btn btn-dark" onClick={() => setTempPassword(null)}>Done</button>}
        >
          <div className="stack">
            <Alert tone="warning">
              This password is shown once. Copy it now and hand it to <strong>{tempPassword.userName}</strong>;
              they will be asked to change it at first sign-in.
            </Alert>
            <div
              className="row"
              style={{
                gap: 12, padding: '14px 16px', border: '1px dashed var(--divider)',
                borderRadius: 'var(--r-md)', background: 'var(--surface-alt)',
              }}
            >
              <code style={{ flex: 1, fontSize: 18, fontWeight: 700, letterSpacing: 1 }}>
                {tempPassword.temporaryPassword}
              </code>
              <button
                className="btn btn-outline btn-sm"
                onClick={() => {
                  void navigator.clipboard?.writeText(tempPassword.temporaryPassword)
                    .then(() => setCopied(true))
                    .catch(() => setCopied(false));
                }}
              >
                {copied ? <><IconCheck size={13} /> Copied</> : 'Copy'}
              </button>
            </div>
          </div>
        </Modal>
      )}

      {/* ------------------------------------------------------- confirmations */}
      {confirm?.kind === 'reset' && (
        <ConfirmModal
          title="Reset password"
          tone="primary"
          confirmLabel="Reset password"
          busy={busy}
          message={
            <>Reset the password for <strong>{confirm.row.userName}</strong>? Their current password
              stops working immediately and a new temporary one is shown to you.</>
          }
          onConfirm={() => void runConfirmed()}
          onClose={() => setConfirm(null)}
        />
      )}

      {confirm?.kind === 'delete' && (
        <ConfirmModal
          title="Delete user account"
          confirmLabel="Delete account"
          busy={busy}
          message={
            <>Delete <strong>{confirm.row.userName}</strong> ({confirm.row.fullName})? The account is
              moved to the recycle bin and can be restored from there.</>
          }
          onConfirm={() => void runConfirmed()}
          onClose={() => setConfirm(null)}
        />
      )}
    </div>
  );
}
