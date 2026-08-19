import { useEffect, useRef, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { ApiError } from '@/api/client';
import {
  rolesApi, usersApi,
  type CreateUserInput, type RoleDto, type TemporaryPasswordDto, type UpdateUserInput,
} from '@/api/endpoints/system';
import { UserStatus } from '@/api/types';
import { useAuth } from '@/auth/AuthContext';
import {
  Alert, ErrorAlert, Field, FormSection, Loading, Modal, PageCard, PageCardHeader,
} from '@/components/ui';
import {
  IconArrowLeft, IconInfo, IconLock, IconShield, IconUser,
} from '@/components/icons';
import './admin.css';

const STATUS_OPTIONS: { value: UserStatus; label: string }[] = [
  { value: UserStatus.Active, label: 'Active' },
  { value: UserStatus.Inactive, label: 'Inactive' },
  { value: UserStatus.Locked, label: 'Locked' },
];

interface FormState {
  userName: string; fullName: string; email: string; phone: string;
  status: string; roleIds: number[]; mustChangePassword: boolean;
}

const BLANK: FormState = {
  userName: '', fullName: '', email: '', phone: '',
  status: String(UserStatus.Active), roleIds: [], mustChangePassword: true,
};

/** Server validation keys arrive in the C# PascalCase; match them case-insensitively. */
function fieldError(errors: Record<string, string[]>, name: string): string | undefined {
  const key = Object.keys(errors).find((k) => k.toLowerCase() === name.toLowerCase());
  return key ? errors[key][0] : undefined;
}

export default function UserFormPage() {
  const navigate = useNavigate();
  const { id } = useParams<{ id?: string }>();
  const userId = id ? Number(id) : null;
  const isEdit = userId !== null;
  const { can, user: signedIn } = useAuth();

  const [form, setForm] = useState<FormState>(BLANK);
  const [loading, setLoading] = useState(isEdit);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<unknown>(null);
  // Kept apart from `error`: a save that fails must leave the form on screen, but a record that
  // never loaded has nothing to save, so the form is replaced entirely.
  const [loadError, setLoadError] = useState<unknown>(null);
  const [errors, setErrors] = useState<Record<string, string[]>>({});
  const [roles, setRoles] = useState<RoleDto[]>([]);
  const [credentials, setCredentials] = useState<TemporaryPasswordDto | null>(null);
  const [copied, setCopied] = useState(false);

  const alive = useRef(true);
  useEffect(() => {
    alive.current = true;
    return () => { alive.current = false; };
  }, []);

  const allowed = can('users.manage');

  useEffect(() => {
    if (userId === null) return;
    const controller = new AbortController();
    let current = true;
    setLoading(true);
    usersApi.byId(userId, controller.signal)
      .then((u) => {
        if (!current) return;
        setForm({
          userName: u.userName,
          fullName: u.fullName,
          email: u.email,
          phone: u.phone ?? '',
          status: String(u.status),
          roleIds: u.roleIds ?? [],
          mustChangePassword: u.mustChangePassword,
        });
        setError(null);
        setLoadError(null);
      })
      .catch((err) => { if (current) { setLoadError(err); setError(null); } })
      .finally(() => { if (current) setLoading(false); });
    return () => { current = false; controller.abort(); };
  }, [userId]);

  useEffect(() => {
    const controller = new AbortController();
    let current = true;
    rolesApi.all(controller.signal)
      .then((rows) => { if (current) setRoles(rows); })
      .catch(() => { /* the roles grid simply stays empty when roles cannot be read */ });
    return () => { current = false; controller.abort(); };
  }, []);

  function set<K extends keyof FormState>(key: K, value: FormState[K]) {
    setForm((f) => ({ ...f, [key]: value }));
  }

  function toggleRole(roleId: number, checked: boolean) {
    setForm((f) => ({
      ...f,
      roleIds: checked ? [...f.roleIds, roleId] : f.roleIds.filter((r) => r !== roleId),
    }));
  }

  async function save() {
    setSaving(true);
    setError(null);
    setErrors({});

    try {
      if (isEdit && userId !== null) {
        const body: UpdateUserInput = {
          fullName: form.fullName.trim(),
          email: form.email.trim(),
          phone: form.phone.trim() || null,
          status: Number(form.status) as UserStatus,
          roleIds: form.roleIds,
        };
        await usersApi.update(userId, body);
        if (!alive.current) return;
        navigate('/admin/users');
      } else {
        const body: CreateUserInput = {
          userName: form.userName.trim(),
          fullName: form.fullName.trim(),
          email: form.email.trim(),
          phone: form.phone.trim() || null,
          mustChangePassword: form.mustChangePassword,
          roleIds: form.roleIds,
          status: Number(form.status) as UserStatus,
        };
        const created = await usersApi.create(body);
        if (!alive.current) return;
        if (created?.temporaryPassword) setCredentials(created);
        else navigate('/admin/users');
      }
    } catch (err) {
      if (!alive.current) return;
      setError(err);
      if (err instanceof ApiError) setErrors(err.validationErrors);
      window.scrollTo({ top: 0, behavior: 'smooth' });
    } finally {
      if (alive.current) setSaving(false);
    }
  }

  async function copyPassword() {
    if (!credentials) return;
    try {
      await navigator.clipboard.writeText(credentials.temporaryPassword);
      if (alive.current) setCopied(true);
    } catch {
      /* clipboard access can be blocked; the value stays selectable on screen */
    }
  }

  // The record could not be read — the id is wrong, or someone else deleted the account. An
  // editable form over nothing offers a Save that cannot mean anything, so the page shows the
  // failure and a way back instead.
  if (isEdit && !loading && loadError) {
    return (
      <div className="page">
        <PageCard>
          <PageCardHeader
            icon={<IconUser size={20} />}
            title="Edit User"
            actions={
              <button className="btn btn-outline" onClick={() => navigate('/admin/users')}>
                <IconArrowLeft size={15} /> Back to Users
              </button>
            }
          />
          <div className="page-card-body">
            <ErrorAlert error={loadError} />
            <div className="form-note">
              Nothing can be edited until a user account that exists is opened.
            </div>
          </div>
        </PageCard>
      </div>
    );
  }

  return (
    <div className="page">
      <PageCard>
        <PageCardHeader
          icon={<IconUser size={20} />}
          title={isEdit ? 'Edit User' : 'Add User'}
          subtitle={isEdit
            ? 'Update the account details, status and role grants.'
            : 'Create a staff login account. A temporary password is generated and shown once.'}
          actions={
            <button className="btn btn-outline" onClick={() => navigate('/admin/users')}>
              <IconArrowLeft size={15} /> Back to Users
            </button>
          }
        />

        <div className="page-card-body">
          {!allowed ? (
            <Alert tone="warning">
              You do not have permission to {isEdit ? 'edit' : 'add'} user accounts.
            </Alert>
          ) : loading ? (
            <Loading message="Loading user..." />
          ) : (
            <>
              {error ? <div style={{ marginBottom: 18 }}><ErrorAlert error={error} /></div> : null}

              {isEdit && signedIn?.id === userId && (
                <div style={{ marginBottom: 18 }}>
                  <Alert tone="warning">
                    This is your own account. Removing your roles or deactivating it will end
                    your access.
                  </Alert>
                </div>
              )}

              <FormSection
                title="Account"
                caption="Who signs in with this account and how to reach them."
                icon={<IconUser size={15} />}
              >
                <div className="form-grid">
                  <Field
                    label="User name"
                    required
                    error={fieldError(errors, 'UserName')}
                    help={isEdit ? undefined : 'Used to sign in. It cannot be changed later.'}
                  >
                    <input
                      className={`input ${fieldError(errors, 'UserName') ? 'input-invalid' : ''}`}
                      value={form.userName}
                      disabled={isEdit}
                      onChange={(e) => set('userName', e.target.value)}
                      autoFocus={!isEdit}
                    />
                  </Field>
                  <Field label="Full name" required error={fieldError(errors, 'FullName')}>
                    <input
                      className={`input ${fieldError(errors, 'FullName') ? 'input-invalid' : ''}`}
                      value={form.fullName}
                      onChange={(e) => set('fullName', e.target.value)}
                    />
                  </Field>
                  <Field label="E-mail" required error={fieldError(errors, 'Email')}>
                    <input
                      className={`input ${fieldError(errors, 'Email') ? 'input-invalid' : ''}`}
                      type="email"
                      value={form.email}
                      onChange={(e) => set('email', e.target.value)}
                    />
                  </Field>
                  <Field label="Phone" error={fieldError(errors, 'Phone')}>
                    <input
                      className={`input ${fieldError(errors, 'Phone') ? 'input-invalid' : ''}`}
                      value={form.phone}
                      onChange={(e) => set('phone', e.target.value)}
                    />
                  </Field>
                  <Field label="Status" help="Inactive and locked accounts cannot sign in.">
                    <select className="select" value={form.status} onChange={(e) => set('status', e.target.value)}>
                      {STATUS_OPTIONS.map((o) => (
                        <option key={o.value} value={o.value}>{o.label}</option>
                      ))}
                    </select>
                  </Field>
                </div>
                {!isEdit && (
                  <div style={{ marginTop: 'var(--sp-4)' }}>
                    <div className="check-row">
                      <input
                        id="user-must-change"
                        type="checkbox"
                        checked={form.mustChangePassword}
                        onChange={(e) => set('mustChangePassword', e.target.checked)}
                      />
                      <label htmlFor="user-must-change">
                        <div className="check-row-label">Password must be changed at first sign-in</div>
                        <div className="check-row-help">
                          A temporary password is generated and shown once the account is created.
                        </div>
                      </label>
                    </div>
                  </div>
                )}
              </FormSection>

              <FormSection
                title="Roles"
                caption="Permissions come from the roles granted here."
                icon={<IconShield size={15} />}
              >
                {fieldError(errors, 'RoleIds') && (
                  <div style={{ marginBottom: 12 }}>
                    <Alert tone="error">{fieldError(errors, 'RoleIds')}</Alert>
                  </div>
                )}
                {roles.length === 0 ? (
                  <div className="form-note">No roles are available to grant.</div>
                ) : (
                  <div className="form-grid">
                    {roles.map((role) => (
                      <div className="check-row" key={role.id}>
                        <input
                          id={`user-role-${role.id}`}
                          type="checkbox"
                          checked={form.roleIds.includes(role.id)}
                          onChange={(e) => toggleRole(role.id, e.target.checked)}
                        />
                        <label htmlFor={`user-role-${role.id}`}>
                          <div className="check-row-label">
                            {role.name}{role.isSystemRole ? ' (built-in)' : ''}
                          </div>
                          {role.description ? (
                            <div className="check-row-help">{role.description}</div>
                          ) : null}
                        </label>
                      </div>
                    ))}
                  </div>
                )}
              </FormSection>

              <div className="form-footer">
                <button className="btn btn-dark" onClick={save} disabled={saving}>
                  {saving ? 'Saving...' : 'Save User'}
                </button>
                <button
                  className="btn btn-outline"
                  onClick={() => navigate('/admin/users')}
                  disabled={saving}
                >
                  Cancel
                </button>
              </div>
              <div className="form-note">Fields marked with * are mandatory.</div>
            </>
          )}
        </div>
      </PageCard>

      {credentials && (
        <Modal
          title="Temporary password"
          icon={<IconLock size={18} />}
          onClose={() => navigate('/admin/users')}
          width={520}
          footer={
            <button className="btn btn-dark" onClick={() => navigate('/admin/users')}>
              Done
            </button>
          }
        >
          <div className="stack">
            <Alert tone="warning">
              <IconInfo size={16} />
              <span>This temporary password is shown once. Hand it over before closing this dialog.</span>
            </Alert>
            <Field label="User name">
              <div className="credential-box">
                <span className="credential-value">{credentials.userName}</span>
              </div>
            </Field>
            <Field label="Temporary password">
              <div className="credential-box">
                <span className="credential-value">{credentials.temporaryPassword}</span>
                <button className="btn btn-outline btn-sm" onClick={copyPassword}>
                  {copied ? 'Copied' : 'Copy'}
                </button>
              </div>
            </Field>
          </div>
        </Modal>
      )}
    </div>
  );
}
