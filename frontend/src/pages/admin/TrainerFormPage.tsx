import { useEffect, useRef, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { ApiError } from '@/api/client';
import {
  GENDER_OPTIONS, genderLabel, type TemporaryPasswordDto,
} from '@/api/endpoints/members';
import {
  TRAINER_STATUS_OPTIONS, trainerStatusLabel, trainersApi, type SaveTrainerDto,
} from '@/api/endpoints/trainers';
import { Gender, TrainerStatus } from '@/api/types';
import { useAuth } from '@/auth/AuthContext';
import {
  Alert, ErrorAlert, Field, FormSection, Loading, Modal, PageCard, PageCardHeader,
} from '@/components/ui';
import {
  IconArrowLeft, IconDumbbell, IconInfo, IconLock, IconUser,
} from '@/components/icons';
import './admin.css';

/**
 * yyyy-MM-dd for a `<input type="date">`.
 *
 * The API sends a plain calendar date ("2026-08-18T00:00:00"), so the day is taken straight off
 * the string. `isoDate` in lib/format routes through `toISOString`, which converts local midnight
 * to the previous evening UTC and slips the date back a day in any zone ahead of UTC — enough to
 * walk a joining date backwards once per edit-and-save.
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
  fullName: string; gender: string; phone: string; email: string; address: string;
  specialization: string; certifications: string; experienceYears: string; joiningDate: string;
  monthlySalary: string; commissionPercent: string; notes: string;
  createUserAccount: boolean; status: string;
}

const BLANK: FormState = {
  fullName: '', gender: String(Gender.Unspecified), phone: '', email: '', address: '',
  specialization: '', certifications: '', experienceYears: '', joiningDate: todayInput(),
  monthlySalary: '', commissionPercent: '', notes: '',
  createUserAccount: false, status: String(TrainerStatus.Active),
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

export default function TrainerFormPage() {
  const navigate = useNavigate();
  const { id } = useParams<{ id?: string }>();
  const trainerId = id ? Number(id) : null;
  const isEdit = trainerId !== null;
  const { can } = useAuth();

  const [form, setForm] = useState<FormState>(BLANK);
  const [loading, setLoading] = useState(isEdit);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<unknown>(null);
  // Kept apart from `error`: a save that fails must leave the form on screen, but a record that
  // never loaded has nothing to save, so the form is replaced entirely.
  const [loadError, setLoadError] = useState<unknown>(null);
  const [errors, setErrors] = useState<Record<string, string[]>>({});
  const [credentials, setCredentials] = useState<TemporaryPasswordDto | null>(null);
  const [copied, setCopied] = useState(false);

  // The API keeps a photo path the form does not render; carry it through an update.
  const carried = useRef<{ photoPath: string | null }>({ photoPath: null });

  const alive = useRef(true);
  useEffect(() => {
    alive.current = true;
    return () => { alive.current = false; };
  }, []);

  const allowed = can('trainers.manage');

  useEffect(() => {
    if (trainerId === null) return;
    const controller = new AbortController();
    let current = true;
    setLoading(true);
    trainersApi.get(trainerId, controller.signal)
      .then((t) => {
        if (!current) return;
        carried.current = { photoPath: t.photoPath ?? null };
        setForm({
          fullName: t.fullName,
          gender: String(t.gender),
          phone: t.phone,
          email: t.email ?? '',
          address: t.address ?? '',
          specialization: t.specialization ?? '',
          certifications: t.certifications ?? '',
          experienceYears: t.experienceYears === null || t.experienceYears === undefined
            ? '' : String(t.experienceYears),
          joiningDate: toDateInput(t.joiningDate),
          monthlySalary: t.monthlySalary === null || t.monthlySalary === undefined
            ? '' : String(t.monthlySalary),
          commissionPercent: t.commissionPercent === null || t.commissionPercent === undefined
            ? '' : String(t.commissionPercent),
          notes: t.notes ?? '',
          createUserAccount: false,
          status: String(t.status),
        });
        setError(null);
        setLoadError(null);
      })
      .catch((err) => { if (current) { setLoadError(err); setError(null); } })
      .finally(() => { if (current) setLoading(false); });
    return () => { current = false; controller.abort(); };
  }, [trainerId]);

  function set<K extends keyof FormState>(key: K, value: FormState[K]) {
    setForm((f) => ({ ...f, [key]: value }));
  }

  async function save() {
    setSaving(true);
    setError(null);
    setErrors({});

    const body: SaveTrainerDto = {
      fullName: form.fullName.trim(),
      gender: Number(form.gender) as Gender,
      phone: form.phone.trim(),
      email: form.email.trim() || null,
      address: form.address.trim() || null,
      specialization: form.specialization.trim() || null,
      certifications: form.certifications.trim() || null,
      experienceYears: numberOrNull(form.experienceYears),
      joiningDate: form.joiningDate,
      monthlySalary: numberOrNull(form.monthlySalary),
      commissionPercent: numberOrNull(form.commissionPercent),
      photoPath: isEdit ? carried.current.photoPath : null,
      notes: form.notes.trim() || null,
      createUserAccount: isEdit ? false : form.createUserAccount,
    };
    if (isEdit) body.status = Number(form.status) as TrainerStatus;

    try {
      if (isEdit && trainerId !== null) {
        await trainersApi.update(trainerId, body);
        if (!alive.current) return;
        navigate('/admin/trainers');
      } else {
        const created = await trainersApi.create(body);
        if (!alive.current) return;
        if (created.account) setCredentials(created.account);
        else navigate('/admin/trainers');
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

  // The record could not be read — the id is wrong, or someone else deleted the trainer. An
  // editable form over nothing offers a Save that cannot mean anything, so the page shows the
  // failure and a way back instead, as the member details screen does.
  if (isEdit && !loading && loadError) {
    return (
      <div className="page">
        <PageCard>
          <PageCardHeader
            icon={<IconUser size={20} />}
            title="Edit Trainer"
            actions={
              <button className="btn btn-outline" onClick={() => navigate('/admin/trainers')}>
                <IconArrowLeft size={15} /> Back to Trainers
              </button>
            }
          />
          <div className="page-card-body">
            <ErrorAlert error={loadError} />
            <div className="form-note">
              Nothing can be edited until a trainer that exists is opened.
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
          title={isEdit ? 'Edit Trainer' : 'Add Trainer'}
          subtitle={isEdit
            ? 'Update the trainer profile, pay terms and employment status.'
            : 'Add a coach to the roster and optionally issue them a login account.'}
          actions={
            <button className="btn btn-outline" onClick={() => navigate('/admin/trainers')}>
              <IconArrowLeft size={15} /> Back to Trainers
            </button>
          }
        />

        <div className="page-card-body">
          {!allowed ? (
            <Alert tone="warning">
              You do not have permission to {isEdit ? 'edit' : 'add'} trainers.
            </Alert>
          ) : loading ? (
            <Loading message="Loading trainer..." />
          ) : (
            <>
              {error ? <div style={{ marginBottom: 18 }}><ErrorAlert error={error} /></div> : null}

              {!isEdit && (
                <FormSection
                  title="Login Credentials"
                  caption="Give the trainer access to the staff portal."
                  icon={<IconLock size={15} />}
                >
                  <div className="form-grid">
                    <Field
                      label="User name"
                      help="Assigned automatically from the trainer code the API generates on save."
                    >
                      <input className="input" value="Auto-generated" disabled readOnly />
                    </Field>
                  </div>
                  <div style={{ marginTop: 'var(--sp-4)' }}>
                    <div className="check-row">
                      <input
                        id="trainer-account"
                        type="checkbox"
                        checked={form.createUserAccount}
                        onChange={(e) => set('createUserAccount', e.target.checked)}
                      />
                      <label htmlFor="trainer-account">
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
                caption="Who the trainer is and how to reach them."
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
                  <Field label="Address">
                    <input className="input" value={form.address} onChange={(e) => set('address', e.target.value)} />
                  </Field>
                  {isEdit && (
                    <Field label="Status" help="Drives the pill shown on the trainer list.">
                      <select className="select" value={form.status} onChange={(e) => set('status', e.target.value)}>
                        {TRAINER_STATUS_OPTIONS.map((s) => (
                          <option key={s} value={s}>{trainerStatusLabel(s)}</option>
                        ))}
                      </select>
                    </Field>
                  )}
                </div>
              </FormSection>

              <FormSection
                title="Professional"
                caption="Coaching background and the terms they are engaged on."
                icon={<IconDumbbell size={15} />}
              >
                <div className="form-grid">
                  <Field label="Specialization" help="Shown as a pill on the trainer list.">
                    <input
                      className="input"
                      value={form.specialization}
                      onChange={(e) => set('specialization', e.target.value)}
                      placeholder="Strength, Yoga, Cardio..."
                    />
                  </Field>
                  <Field label="Experience (years)" error={fieldError(errors, 'ExperienceYears')}>
                    <input
                      className={`input ${fieldError(errors, 'ExperienceYears') ? 'input-invalid' : ''}`}
                      type="number" min={0}
                      value={form.experienceYears}
                      onChange={(e) => set('experienceYears', e.target.value)}
                    />
                  </Field>
                  <Field label="Joining date" required error={fieldError(errors, 'JoiningDate')}>
                    <input
                      className={`input ${fieldError(errors, 'JoiningDate') ? 'input-invalid' : ''}`}
                      type="date"
                      value={form.joiningDate}
                      onChange={(e) => set('joiningDate', e.target.value)}
                    />
                  </Field>
                  <Field label="Certifications">
                    <input
                      className="input"
                      value={form.certifications}
                      onChange={(e) => set('certifications', e.target.value)}
                      placeholder="ACE, NASM, K-11..."
                    />
                  </Field>
                  <Field label="Monthly salary" error={fieldError(errors, 'MonthlySalary')}>
                    <input
                      className={`input ${fieldError(errors, 'MonthlySalary') ? 'input-invalid' : ''}`}
                      type="number" min={0} step="0.01"
                      value={form.monthlySalary}
                      onChange={(e) => set('monthlySalary', e.target.value)}
                    />
                  </Field>
                  <Field label="Commission %" error={fieldError(errors, 'CommissionPercent')}>
                    <input
                      className={`input ${fieldError(errors, 'CommissionPercent') ? 'input-invalid' : ''}`}
                      type="number" min={0} max={100} step="0.01"
                      value={form.commissionPercent}
                      onChange={(e) => set('commissionPercent', e.target.value)}
                    />
                  </Field>
                </div>
                <div style={{ marginTop: 'var(--sp-4)' }}>
                  <Field label="Notes">
                    <textarea
                      className="textarea"
                      value={form.notes}
                      onChange={(e) => set('notes', e.target.value)}
                      placeholder="Shift preferences, availability, remarks..."
                    />
                  </Field>
                </div>
              </FormSection>

              <div className="form-footer">
                <button className="btn btn-dark" onClick={save} disabled={saving}>
                  {saving ? 'Saving...' : 'Save Trainer'}
                </button>
                <button
                  className="btn btn-outline"
                  onClick={() => navigate('/admin/trainers')}
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
          onClose={() => navigate('/admin/trainers')}
          width={520}
          footer={
            <button className="btn btn-dark" onClick={() => navigate('/admin/trainers')}>
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
