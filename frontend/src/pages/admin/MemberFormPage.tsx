import { useEffect, useMemo, useRef, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { ApiError } from '@/api/client';
import {
  GENDER_OPTIONS, genderLabel, membersApi,
  type SaveMemberDto, type TemporaryPasswordDto,
} from '@/api/endpoints/members';
import { trainersApi } from '@/api/endpoints/trainers';
import { Gender, MemberStatus, type Lookup } from '@/api/types';
import { useAuth } from '@/auth/AuthContext';
import {
  Alert, ErrorAlert, Field, FormSection, Loading, Modal, PageCard, PageCardHeader,
} from '@/components/ui';
import {
  IconArrowLeft, IconBell, IconCrown, IconInfo, IconLock, IconUser, IconBox,
} from '@/components/icons';
import './admin.css';

/**
 * yyyy-MM-dd for a `<input type="date">`.
 *
 * The API sends a plain calendar date ("2026-08-17T00:00:00"), so the day is taken straight off
 * the string. `isoDate` in lib/format routes through `toISOString`, which turns local midnight
 * into the previous evening UTC and slips the date back a day in any zone ahead of UTC — which
 * would walk joining date and date of birth backwards once per edit-and-save.
 */
function toDateInput(value: string | null | undefined): string {
  return value ? value.slice(0, 10) : '';
}

/** Today as a local yyyy-MM-dd, for the same reason. */
function todayInput(): string {
  const d = new Date();
  return `${d.getFullYear()}-${`${d.getMonth() + 1}`.padStart(2, '0')}-${`${d.getDate()}`.padStart(2, '0')}`;
}

interface FormState {
  fullName: string; gender: string; dateOfBirth: string; phone: string; email: string;
  bloodGroup: string; address: string; city: string; postalCode: string;
  emergencyContactName: string; emergencyContactPhone: string; emergencyContactRelation: string;
  joiningDate: string; assignedTrainerId: string; heightCm: string; weightKg: string; notes: string;
  createUserAccount: boolean;
}

const BLANK: FormState = {
  fullName: '', gender: String(Gender.Unspecified), dateOfBirth: '', phone: '', email: '',
  bloodGroup: '', address: '', city: '', postalCode: '',
  emergencyContactName: '', emergencyContactPhone: '', emergencyContactRelation: '',
  joiningDate: todayInput(), assignedTrainerId: '', heightCm: '', weightKg: '', notes: '',
  createUserAccount: false,
};

/** Server validation keys arrive in the C# PascalCase; match them case-insensitively. */
function fieldError(errors: Record<string, string[]>, name: string): string | undefined {
  const key = Object.keys(errors).find((k) => k.toLowerCase() === name.toLowerCase());
  return key ? errors[key][0] : undefined;
}

function numberOrNull(value: string): number | null {
  const trimmed = value.trim();
  if (!trimmed) return null;
  const parsed = Number(trimmed);
  return Number.isFinite(parsed) ? parsed : null;
}

/** Whole years between the date of birth and today, blank when no date is set. */
function ageFrom(dateOfBirth: string): string {
  if (!dateOfBirth) return '';
  const dob = new Date(dateOfBirth);
  if (Number.isNaN(dob.getTime())) return '';
  const today = new Date();
  let years = today.getFullYear() - dob.getFullYear();
  const monthDelta = today.getMonth() - dob.getMonth();
  if (monthDelta < 0 || (monthDelta === 0 && today.getDate() < dob.getDate())) years -= 1;
  return years >= 0 ? `${years} years` : '';
}

export default function MemberFormPage() {
  const navigate = useNavigate();
  const { id } = useParams<{ id?: string }>();
  const memberId = id ? Number(id) : null;
  const isEdit = memberId !== null;
  const { can } = useAuth();

  const [form, setForm] = useState<FormState>(BLANK);
  const [loading, setLoading] = useState(isEdit);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [errors, setErrors] = useState<Record<string, string[]>>({});
  const [trainers, setTrainers] = useState<Lookup[]>([]);
  const [credentials, setCredentials] = useState<TemporaryPasswordDto | null>(null);
  const [copied, setCopied] = useState(false);
  const [savedId, setSavedId] = useState<number | null>(null);

  // Fields the form does not render but must not silently drop on update.
  const carried = useRef<{ status: MemberStatus; profilePhotoPath: string | null }>({
    status: MemberStatus.Active, profilePhotoPath: null,
  });

  const alive = useRef(true);
  useEffect(() => {
    alive.current = true;
    return () => { alive.current = false; };
  }, []);

  const allowed = isEdit ? can('members.edit') : can('members.create');

  useEffect(() => {
    if (memberId === null) return;
    const controller = new AbortController();
    let current = true;
    setLoading(true);
    membersApi.get(memberId, controller.signal)
      .then((m) => {
        if (!current) return;
        carried.current = { status: m.status, profilePhotoPath: m.profilePhotoPath ?? null };
        setForm({
          fullName: m.fullName,
          gender: String(m.gender),
          dateOfBirth: toDateInput(m.dateOfBirth),
          phone: m.phone,
          email: m.email ?? '',
          bloodGroup: m.bloodGroup ?? '',
          address: m.address ?? '',
          city: m.city ?? '',
          postalCode: m.postalCode ?? '',
          emergencyContactName: m.emergencyContactName ?? '',
          emergencyContactPhone: m.emergencyContactPhone ?? '',
          emergencyContactRelation: m.emergencyContactRelation ?? '',
          joiningDate: toDateInput(m.joiningDate),
          assignedTrainerId: m.assignedTrainerId ? String(m.assignedTrainerId) : '',
          heightCm: m.heightCm === null || m.heightCm === undefined ? '' : String(m.heightCm),
          weightKg: m.weightKg === null || m.weightKg === undefined ? '' : String(m.weightKg),
          notes: m.notes ?? '',
          createUserAccount: false,
        });
        setError(null);
      })
      .catch((err) => { if (current) setError(err); })
      .finally(() => { if (current) setLoading(false); });
    return () => { current = false; controller.abort(); };
  }, [memberId]);

  useEffect(() => {
    if (!can('trainers.view')) return;
    const controller = new AbortController();
    let current = true;
    trainersApi.lookup(true, controller.signal)
      .then((rows) => { if (current) setTrainers(rows); })
      .catch(() => { /* the select degrades to "No trainer" only */ });
    return () => { current = false; controller.abort(); };
  }, [can]);

  const age = useMemo(() => ageFrom(form.dateOfBirth), [form.dateOfBirth]);

  function set<K extends keyof FormState>(key: K, value: FormState[K]) {
    setForm((f) => ({ ...f, [key]: value }));
  }

  async function save() {
    setSaving(true);
    setError(null);
    setErrors({});

    const body: SaveMemberDto = {
      fullName: form.fullName.trim(),
      gender: Number(form.gender) as Gender,
      dateOfBirth: form.dateOfBirth || null,
      phone: form.phone.trim(),
      email: form.email.trim() || null,
      address: form.address.trim() || null,
      city: form.city.trim() || null,
      postalCode: form.postalCode.trim() || null,
      emergencyContactName: form.emergencyContactName.trim() || null,
      emergencyContactPhone: form.emergencyContactPhone.trim() || null,
      emergencyContactRelation: form.emergencyContactRelation.trim() || null,
      joiningDate: form.joiningDate,
      profilePhotoPath: isEdit ? carried.current.profilePhotoPath : null,
      bloodGroup: form.bloodGroup.trim() || null,
      heightCm: numberOrNull(form.heightCm),
      weightKg: numberOrNull(form.weightKg),
      notes: form.notes.trim() || null,
      assignedTrainerId: form.assignedTrainerId ? Number(form.assignedTrainerId) : null,
      createUserAccount: isEdit ? false : form.createUserAccount,
    };
    if (isEdit) body.status = carried.current.status;

    try {
      if (isEdit && memberId !== null) {
        await membersApi.update(memberId, body);
        if (!alive.current) return;
        navigate(`/admin/members/${memberId}`);
      } else {
        const created = await membersApi.create(body);
        if (!alive.current) return;
        setSavedId(created.member.id);
        if (created.account) setCredentials(created.account);
        else navigate(`/admin/members/${created.member.id}`);
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

  return (
    <div className="page">
      <PageCard>
        <PageCardHeader
          icon={<IconUser size={20} />}
          title={isEdit ? 'Edit Member' : 'Register Member'}
          subtitle={isEdit
            ? 'Update the member profile. Membership status is managed from the member details screen.'
            : 'Register a new member and optionally issue them a login account.'}
          actions={
            <button className="btn btn-outline" onClick={() => navigate('/admin/members')}>
              <IconArrowLeft size={15} /> Back to Members
            </button>
          }
        />

        <div className="page-card-body">
          {!allowed ? (
            <Alert tone="warning">
              You do not have permission to {isEdit ? 'edit' : 'register'} members.
            </Alert>
          ) : loading ? (
            <Loading message="Loading member..." />
          ) : (
            <>
              {error ? <div style={{ marginBottom: 18 }}><ErrorAlert error={error} /></div> : null}

              {!isEdit && (
                <FormSection
                  title="Login Credentials"
                  caption="Give the member access to the self-service portal."
                  icon={<IconLock size={15} />}
                >
                  <div className="form-grid">
                    <Field
                      label="User name"
                      help="Assigned automatically from the member code the API generates on save."
                    >
                      <input className="input" value="Auto-generated" disabled readOnly />
                    </Field>
                  </div>
                  <div style={{ marginTop: 'var(--sp-4)' }}>
                    <div className="check-row">
                      <input
                        id="member-account"
                        type="checkbox"
                        checked={form.createUserAccount}
                        onChange={(e) => set('createUserAccount', e.target.checked)}
                      />
                      <label htmlFor="member-account">
                        <div className="check-row-label">Create a login account</div>
                        <div className="check-row-help">
                          A temporary password is issued once and shown after saving.
                          An email address is required.
                        </div>
                      </label>
                    </div>
                  </div>
                </FormSection>
              )}

              <FormSection
                title="Basic Information"
                caption="Who the member is and how to reach them."
                icon={<IconUser size={15} />}
              >
                <div className="form-grid">
                  <Field label="Full name" required error={fieldError(errors, 'FullName')}>
                    <input
                      className={`input ${fieldError(errors, 'FullName') ? 'input-invalid' : ''}`}
                      value={form.fullName}
                      onChange={(e) => set('fullName', e.target.value)}
                      autoFocus
                    />
                  </Field>
                  <Field label="Gender" required>
                    <select className="select" value={form.gender} onChange={(e) => set('gender', e.target.value)}>
                      {GENDER_OPTIONS.map((g) => <option key={g} value={g}>{genderLabel(g)}</option>)}
                    </select>
                  </Field>
                  <Field label="Date of birth" error={fieldError(errors, 'DateOfBirth')}>
                    <input
                      className={`input ${fieldError(errors, 'DateOfBirth') ? 'input-invalid' : ''}`}
                      type="date"
                      value={form.dateOfBirth}
                      onChange={(e) => set('dateOfBirth', e.target.value)}
                    />
                  </Field>
                  <Field label="Age" help="Calculated from the date of birth.">
                    <input className="input" value={age} placeholder="—" disabled readOnly />
                  </Field>
                  <Field label="Mobile" required error={fieldError(errors, 'Phone')}>
                    <input
                      className={`input ${fieldError(errors, 'Phone') ? 'input-invalid' : ''}`}
                      value={form.phone}
                      onChange={(e) => set('phone', e.target.value)}
                    />
                  </Field>
                  <Field
                    label="Email"
                    error={fieldError(errors, 'Email')}
                    help="Required when a login account is created."
                  >
                    <input
                      className={`input ${fieldError(errors, 'Email') ? 'input-invalid' : ''}`}
                      type="email"
                      value={form.email}
                      onChange={(e) => set('email', e.target.value)}
                    />
                  </Field>
                  <Field label="Blood group">
                    <input
                      className="input"
                      value={form.bloodGroup}
                      onChange={(e) => set('bloodGroup', e.target.value)}
                      placeholder="O+"
                    />
                  </Field>
                </div>
              </FormSection>

              <FormSection title="Address" icon={<IconBox size={15} />}>
                <div className="form-grid">
                  <Field label="Address">
                    <input className="input" value={form.address} onChange={(e) => set('address', e.target.value)} />
                  </Field>
                  <Field label="City">
                    <input className="input" value={form.city} onChange={(e) => set('city', e.target.value)} />
                  </Field>
                  <Field label="Postal code">
                    <input className="input" value={form.postalCode} onChange={(e) => set('postalCode', e.target.value)} />
                  </Field>
                </div>
              </FormSection>

              <FormSection
                title="Emergency Contact"
                caption="Who to call if something happens on the gym floor."
                icon={<IconBell size={15} />}
                optional
              >
                <div className="form-grid">
                  <Field label="Contact name">
                    <input
                      className="input"
                      value={form.emergencyContactName}
                      onChange={(e) => set('emergencyContactName', e.target.value)}
                    />
                  </Field>
                  <Field label="Contact phone">
                    <input
                      className="input"
                      value={form.emergencyContactPhone}
                      onChange={(e) => set('emergencyContactPhone', e.target.value)}
                    />
                  </Field>
                  <Field label="Relationship">
                    <input
                      className="input"
                      value={form.emergencyContactRelation}
                      onChange={(e) => set('emergencyContactRelation', e.target.value)}
                      placeholder="Spouse, parent, friend..."
                    />
                  </Field>
                </div>
              </FormSection>

              <FormSection
                title="Membership"
                caption="Joining details and starting body metrics."
                icon={<IconCrown size={15} />}
              >
                <div className="form-grid">
                  <Field label="Joining date" required error={fieldError(errors, 'JoiningDate')}>
                    <input
                      className={`input ${fieldError(errors, 'JoiningDate') ? 'input-invalid' : ''}`}
                      type="date"
                      value={form.joiningDate}
                      onChange={(e) => set('joiningDate', e.target.value)}
                    />
                  </Field>
                  <Field label="Assigned trainer" error={fieldError(errors, 'AssignedTrainerId')}>
                    <select
                      className="select"
                      value={form.assignedTrainerId}
                      onChange={(e) => set('assignedTrainerId', e.target.value)}
                    >
                      <option value="">No trainer</option>
                      {trainers.map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}
                    </select>
                  </Field>
                  <Field label="Height (cm)">
                    <input
                      className="input" type="number" min={0} step="0.1"
                      value={form.heightCm}
                      onChange={(e) => set('heightCm', e.target.value)}
                    />
                  </Field>
                  <Field label="Weight (kg)">
                    <input
                      className="input" type="number" min={0} step="0.1"
                      value={form.weightKg}
                      onChange={(e) => set('weightKg', e.target.value)}
                    />
                  </Field>
                </div>
                <div style={{ marginTop: 'var(--sp-4)' }}>
                  <Field label="Notes">
                    <textarea
                      className="textarea"
                      value={form.notes}
                      onChange={(e) => set('notes', e.target.value)}
                      placeholder="Injuries, goals, preferences..."
                    />
                  </Field>
                </div>
              </FormSection>

              <div className="form-footer">
                <button className="btn btn-dark" onClick={save} disabled={saving}>
                  {saving ? 'Saving...' : 'Save Member'}
                </button>
                <button
                  className="btn btn-outline"
                  onClick={() => navigate(isEdit && memberId !== null ? `/admin/members/${memberId}` : '/admin/members')}
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
          title="Login account created"
          icon={<IconLock size={18} />}
          onClose={() => navigate(savedId ? `/admin/members/${savedId}` : '/admin/members')}
          width={520}
          footer={
            <button
              className="btn btn-dark"
              onClick={() => navigate(savedId ? `/admin/members/${savedId}` : '/admin/members')}
            >
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
