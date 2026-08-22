import { useState } from 'react';
import { api, ApiError } from '@/api/client';
import { useAuth } from '@/auth/AuthContext';
import { Alert, ErrorAlert, Field, FormSection, PageCard, PageCardHeader, Pill } from '@/components/ui';
import { QrCode } from '@/components/QrCode';
import {
  IconCheck, IconFile, IconInfo, IconLock, IconMail, IconPhone, IconQr, IconShield, IconUser,
} from '@/components/icons';
import { initials } from '@/lib/format';
import './admin.css';

/** Staff numbers are shown padded so they read as an identifier rather than a row count. */
const staffCode = (id: number) => `USR-${String(id).padStart(4, '0')}`;

/**
 * MECARD, not a bare id: a phone camera turns this into a saved contact, which is the one thing
 * someone actually wants to do with a colleague's badge. A semicolon inside a value would end the
 * field early, so separators are stripped rather than escaped — no name or address here needs one.
 */
function contactPayload(
  { fullName, userName, id, email, phone, org }:
  {
    fullName: string; userName: string; id: number;
    email?: string | null; phone?: string | null; org?: string | null;
  },
) {
  const clean = (value: string) => value.replace(/[;\\]/g, ' ').trim();
  const fields = [`N:${clean(fullName)};`];
  if (phone) fields.push(`TEL:${clean(phone)};`);
  if (email) fields.push(`EMAIL:${clean(email)};`);
  if (org) fields.push(`ORG:${clean(org)};`);
  fields.push(`NOTE:${staffCode(id)} - ${clean(userName)};`);
  return `MECARD:${fields.join('')};`;
}

/**
 * The signed-in operator's own account. Admin, Staff and Trainer roles all land here, so this is
 * the profile screen for every non-member role. Distinct from the Users screen, which administers
 * other people's accounts and needs `users.view`; this one only ever shows the caller their own
 * record. The card itself is the same object the member portal shows — see `.prof-summary`.
 */
export default function MyAccountPage() {
  const { user, gym, refreshUser } = useAuth();

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
  const code = user ? staffCode(user.id) : '—';

  return (
    <div className="page">
      <PageCard>
        <PageCardHeader
          icon={<IconFile size={20} />}
          title="My Profile"
          subtitle="View and manage your personal details."
          actions={(
            <button className="btn btn-outline" onClick={() => setShowPassword((open) => !open)}>
              <IconLock size={15} /> Change Password
            </button>
          )}
        />

        <div className="page-card-body">
          <div className="prof-grid">
            {/* The staff badge. Same card the member portal draws, so the two portals show one
                object rather than two unrelated screens. */}
            <aside className="prof-summary">
              <div className="prof-avatar">{initials(user?.fullName)}</div>
              <div className="prof-name">{user?.fullName}</div>
              <div className="prof-user">@{user?.userName}</div>

              <div className="prof-chips">
                {user?.roles.length
                  ? user.roles.map((role) => <span className="prof-chip" key={role}>{role}</span>)
                  : <span className="prof-chip">No role assigned</span>}
                <span className="prof-chip">{code}</span>
              </div>

              {user && (
                <div className="prof-qr">
                  <div className="prof-qr-tile">
                    <QrCode
                      text={contactPayload({
                        fullName: user.fullName,
                        userName: user.userName,
                        id: user.id,
                        email: user.email,
                        phone: user.phone,
                        org: gym?.gymName,
                      })}
                      title={`Contact QR code for ${user.fullName}`}
                    />
                  </div>
                  <div className="prof-qr-hint"><IconQr size={12} /> Scan to save contact</div>
                </div>
              )}

              <div className="prof-rule" />

              <div className="prof-quick-label"><IconInfo size={13} /> Quick Info</div>
              <div className="prof-quick-row">
                <span><IconFile size={14} /> User ID</span>
                <b>{code}</b>
              </div>
              <div className="prof-quick-row">
                <span><IconPhone size={14} /> Mobile</span>
                <b>{user?.phone || '—'}</b>
              </div>
              <div className="prof-quick-row">
                <span><IconMail size={14} /> E-mail</span>
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
                <div className="prof-row"><span>User ID</span><span>{code}</span></div>
                <div className="prof-row"><span>Full Name</span><span>{user?.fullName || '—'}</span></div>
                <div className="prof-row"><span>Username</span><span>{user?.userName || '—'}</span></div>
                <div className="prof-row"><span>E-mail</span><span>{user?.email || '—'}</span></div>
                <div className="prof-row"><span>Mobile</span><span>{user?.phone || '—'}</span></div>
                <div className="prof-row">
                  <span>Roles</span>
                  <span className="prof-role-pills">
                    {user?.roles.length
                      ? user.roles.map((role) => <Pill tone="primary" key={role}>{role}</Pill>)
                      : '—'}
                  </span>
                </div>
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
                        <div className="input-group">
                          <span className="input-icon"><IconLock size={14} /></span>
                          <input
                            className="input"
                            type="password"
                            autoComplete="current-password"
                            value={currentPassword}
                            onChange={(e) => setCurrentPassword(e.target.value)}
                            required
                          />
                        </div>
                      </Field>
                      <Field label="New password" required error={fieldErrors.NewPassword?.[0]}>
                        <div className="input-group">
                          <span className="input-icon"><IconLock size={14} /></span>
                          <input
                            className="input"
                            type="password"
                            autoComplete="new-password"
                            value={newPassword}
                            onChange={(e) => setNewPassword(e.target.value)}
                            required
                          />
                        </div>
                      </Field>
                      <Field label="Confirm new password" required error={fieldErrors.ConfirmPassword?.[0]}>
                        <div className="input-group">
                          <span className="input-icon"><IconLock size={14} /></span>
                          <input
                            className="input"
                            type="password"
                            autoComplete="new-password"
                            value={confirmPassword}
                            onChange={(e) => setConfirmPassword(e.target.value)}
                            required
                          />
                        </div>
                      </Field>
                    </div>

                    <div className="form-footer">
                      <button className="btn btn-dark" type="submit" disabled={saving}>
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
