import { useState } from 'react';
import { api, ApiError } from '@/api/client';
import { useAuth } from '@/auth/AuthContext';
import { Alert, ErrorAlert, Field, FormSection, PageCard, PageCardHeader } from '@/components/ui';
import {
  IconCheck, IconFile, IconInfo, IconLock, IconMail, IconPhone, IconShield, IconUser,
} from '@/components/icons';
import './admin.css';

/**
 * The signed-in operator's own account. Admin, Staff and Trainer roles all land here — there is
 * no separate trainer portal — so this is the profile screen for every non-member role. Distinct
 * from the Users screen, which administers *other* people's accounts and needs `users.view`;
 * this one only ever shows the caller their own record.
 */
export default function MyAccountPage() {
  const { user, refreshUser } = useAuth();

  const [showPassword, setShowPassword] = useState(false);
  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [saving, setSaving] = useState(false);
  const [done, setDone] = useState(false);
  const [error, setError] = useState<ApiError | Error | null>(null);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({});

  async function handleChangePassword(event: React.FormEvent) {
    event.preventDefault();
    setSaving(true);
    setError(null);
    setFieldErrors({});
    setDone(false);

    try {
      await api.post('/api/auth/change-password', { currentPassword, newPassword, confirmPassword });
      setDone(true);
      setCurrentPassword('');
      setNewPassword('');
      setConfirmPassword('');
      await refreshUser();
    } catch (err) {
      if (err instanceof ApiError && err.validationErrors) setFieldErrors(err.validationErrors);
      setError(err as Error);
    } finally {
      setSaving(false);
    }
  }

  const roles = user?.roles.join(', ') || '—';

  return (
    <div className="page">
      <PageCard>
        <PageCardHeader
          icon={<IconFile size={20} />}
          title="My Profile"
          subtitle="View and manage your personal details."
          actions={(
            <>
              <button className="btn btn-dark" onClick={() => setShowPassword(true)}>
                <IconUser size={15} /> Edit Profile
              </button>
              <button className="btn btn-outline" onClick={() => setShowPassword((open) => !open)}>
                <IconLock size={15} /> Change Password
              </button>
            </>
          )}
        />

        <div className="page-card-body">
          <div className="prof-grid">
            <aside className="prof-summary">
              <div className="prof-summary-head">
                <div className="prof-avatar"><IconUser size={28} /></div>
                <div className="grow">
                  <div className="prof-name">{user?.fullName}</div>
                  <div className="prof-user"><IconFile size={13} /> {user?.userName}</div>
                </div>
              </div>

              <div className="prof-rule" />

              <div className="prof-quick-label"><IconInfo size={14} /> Quick Info</div>
              <div className="prof-quick-row">
                <span><IconPhone size={14} /> Mobile</span>
                <b>{user?.phone || '—'}</b>
              </div>
              <div className="prof-quick-row">
                <span><IconMail size={14} /> Email</span>
                <b>{user?.email || '—'}</b>
              </div>
              <div className="prof-quick-row">
                <span><IconShield size={14} /> Roles</span>
                <b>{roles}</b>
              </div>
              <div className="prof-quick-row">
                <span><IconCheck size={14} /> Permissions</span>
                <b>{user?.permissions.length ?? 0}</b>
              </div>

              <div className="prof-note">
                <IconShield size={13} />
                Your roles decide what appears in the sidebar and what the server will allow.
              </div>
            </aside>

            <div>
              <section className="prof-details">
                <div className="prof-details-head"><IconFile size={17} /> Profile Details</div>
                <div className="prof-row"><span>Full Name</span><span>{user?.fullName || '—'}</span></div>
                <div className="prof-row"><span>Username</span><span>{user?.userName || '—'}</span></div>
                <div className="prof-row"><span>Email</span><span>{user?.email || '—'}</span></div>
                <div className="prof-row"><span>Mobile</span><span>{user?.phone || '—'}</span></div>
                <div className="prof-row"><span>Roles</span><span>{roles}</span></div>
                <div className="prof-row"><span>Permissions held</span><span>{user?.permissions.length ?? 0}</span></div>
              </section>

              {showPassword && (
                <FormSection
                  icon={<IconLock size={16} />}
                  title="Change password"
                  caption="At least 8 characters, with a letter and a digit."
                >
                  {done && (
                    <Alert tone="success">
                      <IconCheck size={15} /> Your password has been changed.
                    </Alert>
                  )}
                  {error && <ErrorAlert error={error} />}

                  <form onSubmit={handleChangePassword}>
                    <div className="form-grid-3">
                      <Field label="Current password" required error={fieldErrors.CurrentPassword?.[0]}>
                        <input
                          className="input"
                          type="password"
                          autoComplete="current-password"
                          value={currentPassword}
                          onChange={(e) => setCurrentPassword(e.target.value)}
                          required
                        />
                      </Field>
                      <Field label="New password" required error={fieldErrors.NewPassword?.[0]}>
                        <input
                          className="input"
                          type="password"
                          autoComplete="new-password"
                          value={newPassword}
                          onChange={(e) => setNewPassword(e.target.value)}
                          required
                        />
                      </Field>
                      <Field label="Confirm new password" required error={fieldErrors.ConfirmPassword?.[0]}>
                        <input
                          className="input"
                          type="password"
                          autoComplete="new-password"
                          value={confirmPassword}
                          onChange={(e) => setConfirmPassword(e.target.value)}
                          required
                        />
                      </Field>
                    </div>

                    <div className="form-footer">
                      <button className="btn btn-primary" type="submit" disabled={saving}>
                        <IconLock size={15} /> {saving ? 'Changing…' : 'Change password'}
                      </button>
                    </div>
                  </form>
                </FormSection>
              )}
            </div>
          </div>
        </div>
      </PageCard>
    </div>
  );
}
