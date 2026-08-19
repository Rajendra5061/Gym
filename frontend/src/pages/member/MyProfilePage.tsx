/**
 * The member's own profile.
 *
 * The record is read for `useAuth().user?.memberId` only. Personal details are maintained by
 * the gym team, so the member can read them here and change their password — nothing else on
 * this screen writes to their record.
 */

import { useEffect, useState } from 'react';
import {
  Alert, EmptyState, ErrorAlert, Field, Loading, Modal, PageCard, PageCardHeader, StatusPill,
} from '@/components/ui';
import {
  IconCalendar, IconCheck, IconCrown, IconEye, IconEyeOff, IconLock, IconMail, IconPhone,
  IconUser,
} from '@/components/icons';
import { memberApi, optional } from '@/api/endpoints/member';
import type { MemberDetailDto } from '@/api/endpoints/member';
import { useAuth } from '@/auth/AuthContext';
import { date, initials } from '@/lib/format';
import { Gender, MemberStatus } from '@/api/types';
import './member.css';

const GENDERS: Record<number, string> = {
  [Gender.Unspecified]: 'Not specified',
  [Gender.Male]: 'Male',
  [Gender.Female]: 'Female',
  [Gender.Other]: 'Other',
};

const STATUSES: Record<number, string> = {
  [MemberStatus.Active]: 'Active',
  [MemberStatus.Inactive]: 'Inactive',
  [MemberStatus.Suspended]: 'Suspended',
  [MemberStatus.Expired]: 'Expired',
};

function Row({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div className="m-detail-row">
      <div className="label">{label}</div>
      <div className="value">{value === null || value === undefined || value === '' ? '—' : value}</div>
    </div>
  );
}

export default function MyProfilePage() {
  const { user } = useAuth();
  const memberId = user?.memberId ?? null;

  const [member, setMember] = useState<MemberDetailDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<unknown>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const [showEdit, setShowEdit] = useState(false);
  const [showPassword, setShowPassword] = useState(false);
  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [reveal, setReveal] = useState(false);
  const [passwordError, setPasswordError] = useState<unknown>(null);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    if (memberId === null) { setLoading(false); return; }
    const ctrl = new AbortController();
    setLoading(true);
    setError(null);

    // The full member record needs a staff permission; the dashboard payload carries the same
    // detail for the member themselves, so it is the fallback.
    optional(() => memberApi.profile(memberId, ctrl.signal))
      .then(async (detail) => {
        if (ctrl.signal.aborted) return;
        if (detail) { setMember(detail); return; }
        const dashboard = await memberApi.dashboard(memberId, ctrl.signal);
        if (!ctrl.signal.aborted) setMember(dashboard.member);
      })
      .catch((e) => { if (!ctrl.signal.aborted) setError(e); })
      .finally(() => { if (!ctrl.signal.aborted) setLoading(false); });

    return () => ctrl.abort();
  }, [memberId]);

  async function changePassword() {
    setSaving(true);
    setPasswordError(null);
    try {
      await memberApi.changePassword(currentPassword, newPassword, confirmPassword);
      setShowPassword(false);
      setCurrentPassword(''); setNewPassword(''); setConfirmPassword('');
      setNotice('Your password has been changed.');
    } catch (e) {
      setPasswordError(e);
    } finally {
      setSaving(false);
    }
  }

  if (memberId === null) {
    return (
      <div className="page">
        <PageCard>
          <EmptyState
            icon={<IconUser size={34} />}
            title="No member record linked"
            message="This login is not attached to a member profile."
          />
        </PageCard>
      </div>
    );
  }

  if (loading) return <div className="page"><PageCard><Loading message="Loading your profile…" /></PageCard></div>;

  return (
    <div className="page">
      <PageCard>
        <PageCardHeader
          icon={<IconUser size={20} />}
          title="My Profile"
          subtitle="Your membership details as the gym holds them."
          actions={
            <>
              <button className="btn btn-outline" onClick={() => setShowEdit(true)}>Edit Profile</button>
              <button className="btn btn-dark" onClick={() => { setPasswordError(null); setShowPassword(true); }}>
                <IconLock size={15} /> Change Password
              </button>
            </>
          }
        />
        <div className="page-card-body">
          {notice && <div style={{ marginBottom: 16 }}><Alert tone="success">{notice}</Alert></div>}
          {error ? <ErrorAlert error={error} /> : !member ? (
            <EmptyState icon={<IconUser size={34} />} title="Profile unavailable" />
          ) : (
            <div className="member-panes">
              {/* ------------------------------------------------- summary */}
              <div className="page-card profile-summary" style={{ marginTop: 0 }}>
                <div className="m-avatar-xl">{initials(member.fullName)}</div>
                <div className="profile-name">{member.fullName}</div>
                <div className="profile-username">@{user?.userName}</div>

                <div className="m-quick-info">
                  <div className="m-quick-info-heading">Quick Info</div>
                  <div className="m-quick-info-row">
                    <span className="label"><IconCrown size={14} /> Member code</span>
                    <span className="value">{member.memberCode}</span>
                  </div>
                  <div className="m-quick-info-row">
                    <span className="label"><IconPhone size={14} /> Phone</span>
                    <span className="value">{member.phone || '—'}</span>
                  </div>
                  <div className="m-quick-info-row">
                    <span className="label"><IconMail size={14} /> E-mail</span>
                    <span className="value">{member.email || '—'}</span>
                  </div>
                  <div className="m-quick-info-row">
                    <span className="label"><IconCalendar size={14} /> Member since</span>
                    <span className="value">{date(member.joiningDate)}</span>
                  </div>
                  <div className="m-quick-info-row">
                    <span className="label"><IconCheck size={14} /> Status</span>
                    <span className="value"><StatusPill status={STATUSES[member.status] ?? '—'} /></span>
                  </div>
                </div>

                <div className="form-note" style={{ textAlign: 'left' }}>
                  Personal details are maintained by the gym team. Ask at the front desk if anything
                  here needs correcting.
                </div>
              </div>

              {/* ------------------------------------------------- details */}
              <div className="page-card" style={{ marginTop: 0, overflow: 'hidden' }}>
                <div className="caption-bar"><IconUser size={16} /> Profile Details</div>
                <div className="m-detail-rows">
                  <Row label="Full name" value={member.fullName} />
                  <Row label="Member code" value={member.memberCode} />
                  <Row label="Gender" value={GENDERS[member.gender] ?? '—'} />
                  <Row label="Date of birth" value={member.dateOfBirth ? `${date(member.dateOfBirth)}${member.age ? ` (${member.age} years)` : ''}` : '—'} />
                  <Row label="Phone" value={member.phone} />
                  <Row label="E-mail" value={member.email} />
                  <Row label="Address" value={[member.address, member.city, member.postalCode].filter(Boolean).join(', ')} />
                  <Row
                    label="Emergency contact"
                    value={member.emergencyContactName
                      ? `${member.emergencyContactName}${member.emergencyContactPhone ? ` · ${member.emergencyContactPhone}` : ''}${member.emergencyContactRelation ? ` (${member.emergencyContactRelation})` : ''}`
                      : '—'}
                  />
                  <Row label="Blood group" value={member.bloodGroup} />
                  <Row label="Height" value={member.heightCm ? `${member.heightCm} cm` : '—'} />
                  <Row label="Weight" value={member.weightKg ? `${member.weightKg} kg` : '—'} />
                  <Row label="Assigned trainer" value={member.assignedTrainerName} />
                  <Row label="Joining date" value={date(member.joiningDate)} />
                  <Row label="Total visits" value={member.totalVisits} />
                  <Row label="Last visit" value={date(member.lastVisitDate)} />
                  <Row label="Status" value={<StatusPill status={STATUSES[member.status] ?? '—'} />} />
                </div>
              </div>
            </div>
          )}
        </div>
      </PageCard>

      {/* -------------------------------------------------------- edit modal */}
      {showEdit && (
        <Modal
          title="Edit Profile"
          icon={<IconUser size={18} />}
          width={520}
          onClose={() => setShowEdit(false)}
          footer={<button className="btn btn-dark" onClick={() => setShowEdit(false)}>Close</button>}
        >
          <div className="stack">
            <Alert tone="info">
              Your name, contact details and emergency contact are held by the gym and can only be
              changed by staff — this keeps the membership record and your receipts consistent. Ask at
              the front desk and they will update it while you wait.
            </Alert>
            <div className="form-grid">
              <Field label="Full name"><input className="input" value={member?.fullName ?? ''} disabled /></Field>
              <Field label="Phone"><input className="input" value={member?.phone ?? ''} disabled /></Field>
              <Field label="E-mail"><input className="input" value={member?.email ?? ''} disabled /></Field>
              <Field label="Address"><input className="input" value={member?.address ?? ''} disabled /></Field>
            </div>
            <div className="form-note">
              You can change your own password at any time with the Change Password button.
            </div>
          </div>
        </Modal>
      )}

      {/* ---------------------------------------------------- password modal */}
      {showPassword && (
        <Modal
          title="Change Password"
          icon={<IconLock size={18} />}
          width={480}
          onClose={() => setShowPassword(false)}
          footer={
            <>
              <button className="btn btn-outline" onClick={() => setShowPassword(false)} disabled={saving}>Cancel</button>
              <button
                className="btn btn-dark"
                disabled={saving || !currentPassword || !newPassword || newPassword !== confirmPassword}
                onClick={() => void changePassword()}
              >
                {saving ? 'Saving…' : 'Change password'}
              </button>
            </>
          }
        >
          <div className="stack">
            <ErrorAlert error={passwordError} />
            <Field label="Current password" required>
              <div className="input-group">
                <input
                  className="input"
                  style={{ paddingLeft: 12 }}
                  type={reveal ? 'text' : 'password'}
                  value={currentPassword}
                  onChange={(e) => setCurrentPassword(e.target.value)}
                  autoFocus
                />
                <button
                  className="btn btn-ghost btn-icon btn-sm input-suffix"
                  onClick={() => setReveal(!reveal)}
                  aria-label={reveal ? 'Hide passwords' : 'Show passwords'}
                >
                  {reveal ? <IconEyeOff size={15} /> : <IconEye size={15} />}
                </button>
              </div>
            </Field>
            <Field label="New password" required help="Use at least eight characters with a mix of letters and digits.">
              <input
                className="input"
                type={reveal ? 'text' : 'password'}
                value={newPassword}
                onChange={(e) => setNewPassword(e.target.value)}
              />
            </Field>
            <Field
              label="Confirm new password"
              required
              error={confirmPassword && newPassword !== confirmPassword ? 'The two passwords do not match.' : undefined}
            >
              <input
                className="input"
                type={reveal ? 'text' : 'password'}
                value={confirmPassword}
                onChange={(e) => setConfirmPassword(e.target.value)}
              />
            </Field>
          </div>
        </Modal>
      )}
    </div>
  );
}
