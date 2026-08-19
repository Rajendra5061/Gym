import { useEffect, useRef, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { ApiError } from '@/api/client';
import {
  ENQUIRY_SOURCES, ENQUIRY_STATUSES, EnquirySource, EnquiryStatus, getEnquiry,
  membershipPlanLookup, saveEnquiry, userLookup, type EnquiryDto,
} from '@/api/endpoints/operations';
import type { Lookup } from '@/api/types';
import { useAuth } from '@/auth/AuthContext';
import {
  Alert, ErrorAlert, Field, FormSection, Loading, PageCard, PageCardHeader,
} from '@/components/ui';
import {
  IconArrowLeft, IconMail, IconMessage, IconPhone, IconUser,
} from '@/components/icons';
import './ops.css';

/**
 * yyyy-MM-dd for a `<input type="date">`.
 *
 * The API sends a plain calendar date, so the day is taken straight off the string. Routing
 * through `toISOString` would convert local midnight to the previous evening UTC and slip the
 * follow-up date back a day in any zone ahead of UTC.
 */
function toDateInput(value: string | null | undefined): string {
  return value ? value.slice(0, 10) : '';
}

interface FormState {
  fullName: string; phone: string; email: string;
  source: string; interestedPlanId: string; status: string;
  followUpDate: string; assignedToUserId: string;
  message: string; notes: string;
}

const BLANK: FormState = {
  fullName: '', phone: '', email: '',
  source: String(EnquirySource.WalkIn), interestedPlanId: '', status: String(EnquiryStatus.New),
  followUpDate: '', assignedToUserId: '',
  message: '', notes: '',
};

/** Server validation keys arrive in the C# PascalCase; match them case-insensitively. */
function fieldError(errors: Record<string, string[]>, name: string): string | undefined {
  const key = Object.keys(errors).find((k) => k.toLowerCase() === name.toLowerCase());
  return key ? errors[key][0] : undefined;
}

export default function EnquiryFormPage() {
  const navigate = useNavigate();
  const { id } = useParams<{ id?: string }>();
  const enquiryId = id ? Number(id) : null;
  const isEdit = enquiryId !== null;
  const { can } = useAuth();

  const [form, setForm] = useState<FormState>(BLANK);
  const [loading, setLoading] = useState(isEdit);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<unknown>(null);
  // Kept apart from `error`: a save that fails must leave the form on screen, but a record that
  // never loaded has nothing to save, so the form is replaced entirely.
  const [loadError, setLoadError] = useState<unknown>(null);
  const [errors, setErrors] = useState<Record<string, string[]>>({});

  const [plans, setPlans] = useState<Lookup[]>([]);
  const [users, setUsers] = useState<Lookup[]>([]);

  const alive = useRef(true);
  useEffect(() => {
    alive.current = true;
    return () => { alive.current = false; };
  }, []);

  const allowed = can('enquiries.manage');

  useEffect(() => {
    const controller = new AbortController();
    (async () => {
      try {
        const rows = await membershipPlanLookup(controller.signal);
        if (!controller.signal.aborted) setPlans(rows);
      } catch {
        if (!controller.signal.aborted) setPlans([]);
      }
      try {
        const rows = await userLookup(controller.signal);
        if (!controller.signal.aborted) setUsers(rows);
      } catch {
        if (!controller.signal.aborted) setUsers([]);
      }
    })();
    return () => controller.abort();
  }, []);

  useEffect(() => {
    if (enquiryId === null) return;
    const controller = new AbortController();
    let current = true;
    setLoading(true);
    getEnquiry(enquiryId, controller.signal)
      .then((e) => {
        if (!current) return;
        setForm({
          fullName: e.fullName,
          phone: e.phone ?? '',
          email: e.email ?? '',
          source: String(e.source),
          interestedPlanId: e.interestedPlanId === null || e.interestedPlanId === undefined
            ? '' : String(e.interestedPlanId),
          status: String(e.status),
          followUpDate: toDateInput(e.followUpDate),
          assignedToUserId: e.assignedToUserId === null || e.assignedToUserId === undefined
            ? '' : String(e.assignedToUserId),
          message: e.message ?? '',
          notes: e.notes ?? '',
        });
        setError(null);
        setLoadError(null);
      })
      .catch((err) => { if (current) { setLoadError(err); setError(null); } })
      .finally(() => { if (current) setLoading(false); });
    return () => { current = false; controller.abort(); };
  }, [enquiryId]);

  function set<K extends keyof FormState>(key: K, value: FormState[K]) {
    setForm((f) => ({ ...f, [key]: value }));
  }

  async function save() {
    setSaving(true);
    setError(null);
    setErrors({});

    const body: Partial<EnquiryDto> = {
      id: isEdit && enquiryId !== null ? enquiryId : 0,
      fullName: form.fullName.trim(),
      phone: form.phone.trim(),
      email: form.email.trim() || null,
      source: Number(form.source) as EnquirySource,
      interestedPlanId: form.interestedPlanId ? Number(form.interestedPlanId) : null,
      message: form.message.trim() || null,
      status: Number(form.status) as EnquiryStatus,
      followUpDate: form.followUpDate || null,
      assignedToUserId: form.assignedToUserId ? Number(form.assignedToUserId) : null,
      notes: form.notes.trim() || null,
    };

    try {
      await saveEnquiry(body);
      if (!alive.current) return;
      navigate('/admin/enquiries');
    } catch (err) {
      if (!alive.current) return;
      setError(err);
      if (err instanceof ApiError) setErrors(err.validationErrors);
      window.scrollTo({ top: 0, behavior: 'smooth' });
    } finally {
      if (alive.current) setSaving(false);
    }
  }

  // The record could not be read — the id is wrong, or someone else deleted the enquiry. An
  // editable form over nothing offers a Save that cannot mean anything, so the page shows the
  // failure and a way back instead.
  if (isEdit && !loading && loadError) {
    return (
      <div className="page">
        <PageCard>
          <PageCardHeader
            icon={<IconUser size={20} />}
            title="Edit Enquiry"
            actions={
              <button className="btn btn-outline" onClick={() => navigate('/admin/enquiries')}>
                <IconArrowLeft size={15} /> Back to Enquiries
              </button>
            }
          />
          <div className="page-card-body">
            <ErrorAlert error={loadError} />
            <div className="form-note">
              Nothing can be edited until an enquiry that exists is opened.
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
          title={isEdit ? 'Edit Enquiry' : 'Add Enquiry'}
          subtitle={isEdit
            ? 'Update the lead’s details, follow-up plan and pipeline status.'
            : 'Log a walk-in, call or web lead so nobody slips through the follow-up net.'}
          actions={
            <button className="btn btn-outline" onClick={() => navigate('/admin/enquiries')}>
              <IconArrowLeft size={15} /> Back to Enquiries
            </button>
          }
        />

        <div className="page-card-body">
          {!allowed ? (
            <Alert tone="warning">
              You do not have permission to {isEdit ? 'edit' : 'add'} enquiries.
            </Alert>
          ) : loading ? (
            <Loading message="Loading enquiry..." />
          ) : (
            <>
              {error ? <div style={{ marginBottom: 18 }}><ErrorAlert error={error} /></div> : null}

              <FormSection
                title="Contact Details"
                caption="Who asked, and how to reach them."
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
                  <Field
                    label="Phone"
                    required
                    error={fieldError(errors, 'Phone')}
                    help="The number your team will call back on."
                  >
                    <div className="input-group">
                      <span className="input-icon"><IconPhone size={14} /></span>
                      <input
                        className={`input ${fieldError(errors, 'Phone') ? 'input-invalid' : ''}`}
                        value={form.phone}
                        onChange={(e) => set('phone', e.target.value)}
                      />
                    </div>
                  </Field>
                  <Field label="Email" error={fieldError(errors, 'Email')}>
                    <div className="input-group">
                      <span className="input-icon"><IconMail size={14} /></span>
                      <input
                        className={`input ${fieldError(errors, 'Email') ? 'input-invalid' : ''}`}
                        type="email"
                        value={form.email}
                        onChange={(e) => set('email', e.target.value)}
                      />
                    </div>
                  </Field>
                </div>
              </FormSection>

              <FormSection
                title="Enquiry"
                caption="Where the lead came from and how the follow-up is tracked."
                icon={<IconMessage size={15} />}
              >
                <div className="form-grid">
                  <Field label="Source" required error={fieldError(errors, 'Source')}>
                    <select
                      className="select"
                      value={form.source}
                      onChange={(e) => set('source', e.target.value)}
                    >
                      {ENQUIRY_SOURCES.map((s) => (
                        <option key={s.value} value={s.value}>{s.label}</option>
                      ))}
                    </select>
                  </Field>
                  <Field label="Interested plan" error={fieldError(errors, 'InterestedPlanId')}>
                    <select
                      className="select"
                      value={form.interestedPlanId}
                      onChange={(e) => set('interestedPlanId', e.target.value)}
                    >
                      <option value="">Not decided</option>
                      {plans.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
                    </select>
                  </Field>
                  <Field label="Status" required error={fieldError(errors, 'Status')}>
                    <select
                      className="select"
                      value={form.status}
                      onChange={(e) => set('status', e.target.value)}
                    >
                      {ENQUIRY_STATUSES.map((s) => (
                        <option key={s.value} value={s.value}>{s.label}</option>
                      ))}
                    </select>
                  </Field>
                  <Field
                    label="Follow-up date"
                    error={fieldError(errors, 'FollowUpDate')}
                    help="When to call this lead back."
                  >
                    <input
                      className={`input ${fieldError(errors, 'FollowUpDate') ? 'input-invalid' : ''}`}
                      type="date"
                      value={form.followUpDate}
                      onChange={(e) => set('followUpDate', e.target.value)}
                    />
                  </Field>
                  <Field label="Assigned to" error={fieldError(errors, 'AssignedToUserId')}>
                    <select
                      className="select"
                      value={form.assignedToUserId}
                      onChange={(e) => set('assignedToUserId', e.target.value)}
                    >
                      <option value="">Unassigned</option>
                      {users.map((u) => <option key={u.id} value={u.id}>{u.name}</option>)}
                    </select>
                  </Field>
                </div>
                <div style={{ marginTop: 'var(--sp-4)' }}>
                  <Field label="Message" error={fieldError(errors, 'Message')} help="What the lead asked about.">
                    <textarea
                      className={`textarea ${fieldError(errors, 'Message') ? 'input-invalid' : ''}`}
                      value={form.message}
                      onChange={(e) => set('message', e.target.value)}
                    />
                  </Field>
                </div>
                <div style={{ marginTop: 'var(--sp-4)' }}>
                  <Field label="Notes" error={fieldError(errors, 'Notes')}>
                    <textarea
                      className={`textarea ${fieldError(errors, 'Notes') ? 'input-invalid' : ''}`}
                      value={form.notes}
                      onChange={(e) => set('notes', e.target.value)}
                      placeholder="Objections, budget, best time to call..."
                    />
                  </Field>
                </div>
              </FormSection>

              <div className="form-footer">
                <button className="btn btn-dark" onClick={() => void save()} disabled={saving}>
                  {saving ? 'Saving...' : 'Save Enquiry'}
                </button>
                <button
                  className="btn btn-outline"
                  onClick={() => navigate('/admin/enquiries')}
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
    </div>
  );
}
