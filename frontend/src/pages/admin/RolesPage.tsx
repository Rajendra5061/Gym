/**
 * Roles and the permission matrix.
 *
 * The left pane lists roles; the right pane edits the selected role's grants, grouped into the
 * modules the API reports. Permission changes are staged locally and written in one
 * `PUT /api/roles/{id}/permissions` call so a half-applied matrix is impossible.
 */

import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  Alert, ConfirmModal, EmptyState, ErrorAlert, Field, Loading, Modal, PageCard, PageCardHeader, Pill,
} from '@/components/ui';
import { IconPlus, IconShield, IconUsers, IconWarning } from '@/components/icons';
import { rolesApi } from '@/api/endpoints/system';
import type { PermissionGroupDto, RoleDto, SaveRoleInput } from '@/api/endpoints/system';
import { useAuth } from '@/auth/AuthContext';

interface RoleForm { id: number | null; name: string; description: string; }

export default function RolesPage() {
  const { can, isInRole } = useAuth();
  const mayManage = can('roles.manage');

  const [roles, setRoles] = useState<RoleDto[]>([]);
  const [groups, setGroups] = useState<PermissionGroupDto[]>([]);
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const [granted, setGranted] = useState<Set<string>>(new Set());
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<unknown>(null);
  const [saving, setSaving] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [reloadKey, setReloadKey] = useState(0);
  const reload = useCallback(() => setReloadKey((k) => k + 1), []);

  const [form, setForm] = useState<RoleForm | null>(null);
  const [formError, setFormError] = useState<unknown>(null);
  const [confirmDelete, setConfirmDelete] = useState<RoleDto | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    const ctrl = new AbortController();
    setLoading(true);
    setError(null);
    Promise.all([rolesApi.all(ctrl.signal), rolesApi.permissionGroups(ctrl.signal)])
      .then(([roleList, groupList]) => {
        if (ctrl.signal.aborted) return;
        setRoles(roleList);
        setGroups(groupList);
        setSelectedId((current) => {
          const next = current !== null && roleList.some((r) => r.id === current)
            ? current
            : roleList[0]?.id ?? null;
          const role = roleList.find((r) => r.id === next);
          setGranted(new Set(role?.permissions ?? []));
          return next;
        });
      })
      .catch((e) => { if (!ctrl.signal.aborted) setError(e); })
      .finally(() => { if (!ctrl.signal.aborted) setLoading(false); });
    return () => ctrl.abort();
  }, [reloadKey]);

  const selected = useMemo(
    () => roles.find((r) => r.id === selectedId) ?? null,
    [roles, selectedId],
  );

  const original = useMemo(() => new Set(selected?.permissions ?? []), [selected]);
  const dirty = useMemo(() => {
    if (granted.size !== original.size) return true;
    for (const code of granted) if (!original.has(code)) return true;
    return false;
  }, [granted, original]);

  function selectRole(role: RoleDto) {
    setSelectedId(role.id);
    setGranted(new Set(role.permissions));
    setNotice(null);
  }

  function toggle(code: string, on: boolean) {
    setGranted((current) => {
      const next = new Set(current);
      if (on) next.add(code); else next.delete(code);
      return next;
    });
  }

  function setModule(group: PermissionGroupDto, on: boolean) {
    setGranted((current) => {
      const next = new Set(current);
      for (const permission of group.permissions) {
        if (on) next.add(permission.code); else next.delete(permission.code);
      }
      return next;
    });
  }

  async function savePermissions() {
    if (!selected) return;
    setSaving(true);
    setError(null);
    try {
      await rolesApi.setPermissions(selected.id, [...granted]);
      setNotice(`Permissions for ${selected.name} saved.`);
      reload();
    } catch (e) {
      setError(e);
    } finally {
      setSaving(false);
    }
  }

  async function saveRole() {
    if (!form) return;
    setBusy(true);
    setFormError(null);
    try {
      const body: SaveRoleInput = {
        name: form.name.trim(),
        description: form.description.trim() || null,
        permissions: form.id === null ? [] : (roles.find((r) => r.id === form.id)?.permissions ?? []),
      };
      if (form.id === null) await rolesApi.create(body);
      else await rolesApi.update(form.id, body);
      setForm(null);
      setNotice(form.id === null ? 'Role created.' : 'Role updated.');
      reload();
    } catch (e) {
      setFormError(e);
    } finally {
      setBusy(false);
    }
  }

  async function deleteRole() {
    if (!confirmDelete) return;
    setBusy(true);
    setError(null);
    try {
      await rolesApi.remove(confirmDelete.id);
      setNotice(`Role ${confirmDelete.name} deleted.`);
      setConfirmDelete(null);
      setSelectedId(null);
      reload();
    } catch (e) {
      setError(e);
      setConfirmDelete(null);
    } finally {
      setBusy(false);
    }
  }

  const editingOwnRole = selected !== null && isInRole(selected.name);

  return (
    <div className="page">
      <PageCard>
        <PageCardHeader
          icon={<IconShield size={20} />}
          title="Roles & Permissions"
          subtitle="Grant each role only the modules it needs; users inherit every permission of every role they hold."
          actions={mayManage ? (
            <button
              className="btn btn-dark"
              onClick={() => { setFormError(null); setForm({ id: null, name: '', description: '' }); }}
            >
              <IconPlus size={15} /> New Role
            </button>
          ) : null}
        />

        {error ? <div style={{ padding: 20, paddingBottom: 0 }}><ErrorAlert error={error} /></div> : null}

        {loading ? <Loading message="Loading roles…" /> : roles.length === 0 ? (
          <EmptyState icon={<IconShield size={34} />} title="No roles defined" message="Create the first role to start granting permissions." />
        ) : (
          <div style={{ display: 'flex', alignItems: 'stretch', minHeight: 420 }}>
            {/* ------------------------------------------------------ roles */}
            <aside style={{ width: 300, flex: 'none', borderRight: '1px solid var(--divider)', background: '#fafbfd' }}>
              <div style={{ padding: '14px 16px', borderBottom: '1px solid var(--divider)', fontWeight: 700, fontSize: 13, color: 'var(--text-2)' }}>
                Roles ({roles.length})
              </div>
              <div>
                {roles.map((role) => {
                  const active = role.id === selectedId;
                  return (
                    <button
                      key={role.id}
                      onClick={() => selectRole(role)}
                      style={{
                        display: 'block', width: '100%', textAlign: 'left', cursor: 'pointer',
                        padding: '12px 16px', border: 0, borderBottom: '1px solid var(--divider)',
                        borderLeft: `3px solid ${active ? 'var(--primary)' : 'transparent'}`,
                        background: active ? '#fff' : 'transparent',
                      }}
                    >
                      <div className="row" style={{ gap: 8 }}>
                        <span className="grow" style={{ fontWeight: 700 }}>{role.name}</span>
                        {role.isSystemRole ? <Pill tone="dark">Built-in</Pill> : null}
                      </div>
                      <div className="cell-sub" style={{ marginTop: 4 }}>{role.description || 'No description'}</div>
                      <div className="row" style={{ gap: 6, marginTop: 8 }}>
                        <span className="cell-icon" style={{ fontSize: 12 }}>
                          <IconUsers size={13} />{role.userCount} user{role.userCount === 1 ? '' : 's'}
                        </span>
                        <span className="muted" style={{ fontSize: 12 }}>· {role.permissions.length} permissions</span>
                      </div>
                    </button>
                  );
                })}
              </div>
            </aside>

            {/* ------------------------------------------------ permissions */}
            <section className="grow" style={{ minWidth: 0, display: 'flex', flexDirection: 'column' }}>
              {!selected ? (
                <EmptyState icon={<IconShield size={34} />} title="Select a role" message="Pick a role on the left to review its permissions." />
              ) : (
                <>
                  <div className="row" style={{ padding: '16px 20px', borderBottom: '1px solid var(--divider)', gap: 12 }}>
                    <div className="grow">
                      <div className="row" style={{ gap: 8 }}>
                        <h3 style={{ fontSize: 16 }}>{selected.name}</h3>
                        {selected.isSystemRole ? <Pill tone="dark">Built-in</Pill> : null}
                      </div>
                      <div className="cell-sub" style={{ marginTop: 3 }}>
                        {granted.size} of {groups.reduce((n, g) => n + g.permissions.length, 0)} permissions granted
                      </div>
                    </div>
                    {mayManage && (
                      <>
                        <button
                          className="btn btn-edit"
                          onClick={() => {
                            setFormError(null);
                            setForm({ id: selected.id, name: selected.name, description: selected.description ?? '' });
                          }}
                        >
                          Edit role
                        </button>
                        <button
                          className="btn btn-del"
                          disabled={selected.isSystemRole}
                          title={selected.isSystemRole ? 'Built-in roles cannot be deleted.' : undefined}
                          onClick={() => setConfirmDelete(selected)}
                        >
                          Delete
                        </button>
                      </>
                    )}
                  </div>

                  <div style={{ padding: 20, display: 'flex', flexDirection: 'column', gap: 16, flex: 1 }}>
                    {notice && <Alert tone="success">{notice}</Alert>}
                    {editingOwnRole && (
                      <Alert tone="warning">
                        <IconWarning size={16} />
                        <div>
                          You hold the <strong>{selected.name}</strong> role. Removing a permission here removes
                          it from your own account as well, and screens you use may disappear as soon as you
                          sign in again.
                        </div>
                      </Alert>
                    )}

                    {groups.map((group) => {
                      const all = group.permissions.every((p) => granted.has(p.code));
                      const none = group.permissions.every((p) => !granted.has(p.code));
                      return (
                        <div key={group.module} className="form-section" style={{ background: '#fff' }}>
                          <div className="row" style={{ marginBottom: 14 }}>
                            <div className="grow">
                              <div className="form-section-title">{group.module}</div>
                              <div className="form-section-caption">
                                {group.permissions.filter((p) => granted.has(p.code)).length} of {group.permissions.length} granted
                              </div>
                            </div>
                            {mayManage && (
                              <div className="row" style={{ gap: 12 }}>
                                <a
                                  href="#"
                                  style={{ fontSize: 12, fontWeight: 600, opacity: all ? 0.4 : 1 }}
                                  onClick={(e) => { e.preventDefault(); if (!all) setModule(group, true); }}
                                >
                                  Select all
                                </a>
                                <a
                                  href="#"
                                  style={{ fontSize: 12, fontWeight: 600, color: 'var(--danger)', opacity: none ? 0.4 : 1 }}
                                  onClick={(e) => { e.preventDefault(); if (!none) setModule(group, false); }}
                                >
                                  Clear
                                </a>
                              </div>
                            )}
                          </div>

                          <div style={{ display: 'grid', gap: 10, gridTemplateColumns: 'repeat(auto-fill, minmax(260px, 1fr))' }}>
                            {group.permissions.map((permission) => {
                              const on = granted.has(permission.code);
                              return (
                                <label
                                  key={permission.code}
                                  style={{
                                    display: 'flex', gap: 10, alignItems: 'flex-start', cursor: mayManage ? 'pointer' : 'default',
                                    padding: '10px 12px', borderRadius: 'var(--r-md)',
                                    border: `1px solid ${on ? '#c7cffb' : 'var(--divider)'}`,
                                    background: on ? 'var(--primary-soft)' : '#fff',
                                  }}
                                >
                                  <input
                                    type="checkbox"
                                    style={{ marginTop: 2 }}
                                    checked={on}
                                    disabled={!mayManage}
                                    onChange={(e) => toggle(permission.code, e.target.checked)}
                                  />
                                  <span style={{ minWidth: 0 }}>
                                    <span style={{ display: 'block', fontSize: 13, fontWeight: 600 }}>
                                      {permission.description}
                                    </span>
                                    <code style={{ display: 'block', fontSize: 11, color: 'var(--text-muted)', marginTop: 2 }}>
                                      {permission.code}
                                    </code>
                                  </span>
                                </label>
                              );
                            })}
                          </div>
                        </div>
                      );
                    })}
                  </div>

                  {mayManage && (
                    <div
                      className="row"
                      style={{
                        position: 'sticky', bottom: 0, gap: 12, padding: '14px 20px',
                        borderTop: '1px solid var(--divider)', background: '#fff',
                      }}
                    >
                      <span className="grow muted" style={{ fontSize: 12 }}>
                        {dirty ? 'Unsaved permission changes.' : 'All changes saved.'}
                      </span>
                      <button
                        className="btn btn-outline"
                        disabled={!dirty || saving}
                        onClick={() => setGranted(new Set(selected.permissions))}
                      >
                        Discard
                      </button>
                      <button className="btn btn-dark" disabled={!dirty || saving} onClick={() => void savePermissions()}>
                        {saving ? 'Saving…' : 'Save permissions'}
                      </button>
                    </div>
                  )}
                </>
              )}
            </section>
          </div>
        )}
      </PageCard>

      {form && (
        <Modal
          title={form.id === null ? 'New role' : `Edit ${form.name}`}
          icon={<IconShield size={18} />}
          width={520}
          onClose={() => setForm(null)}
          footer={
            <>
              <button className="btn btn-outline" onClick={() => setForm(null)} disabled={busy}>Cancel</button>
              <button className="btn btn-dark" onClick={() => void saveRole()} disabled={busy}>
                {busy ? 'Saving…' : 'Save role'}
              </button>
            </>
          }
        >
          <div className="stack">
            <ErrorAlert error={formError} />
            <Field label="Role name" required>
              <input className="input" value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} />
            </Field>
            <Field label="Description" help="Shown in the roles list to explain what the role is for.">
              <textarea
                className="textarea"
                value={form.description}
                onChange={(e) => setForm({ ...form, description: e.target.value })}
              />
            </Field>
          </div>
        </Modal>
      )}

      {confirmDelete && (
        <ConfirmModal
          title="Delete role"
          confirmLabel="Delete role"
          busy={busy}
          message={
            <>Delete the role <strong>{confirmDelete.name}</strong>? It is held by {confirmDelete.userCount}{' '}
              user{confirmDelete.userCount === 1 ? '' : 's'}, who will lose every permission it granted.</>
          }
          onConfirm={() => void deleteRole()}
          onClose={() => setConfirmDelete(null)}
        />
      )}
    </div>
  );
}
