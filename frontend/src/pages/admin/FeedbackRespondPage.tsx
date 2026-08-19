import { useEffect, useRef, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { ApiError } from '@/api/client';
import {
  FeedbackStatus, feedbackStatusLabel, getFeedback, respondToFeedback, type FeedbackDto,
} from '@/api/endpoints/operations';
import { useAuth } from '@/auth/AuthContext';
import {
  Alert, ErrorAlert, Field, FormSection, Loading, PageCard, PageCardHeader, Pill,
} from '@/components/ui';
import type { PillTone } from '@/components/ui';
import { IconArrowLeft, IconCalendar, IconMessage, IconUser } from '@/components/icons';
import { date as fmtDate, initials } from '@/lib/format';
import './ops.css';

function statusTone(status: FeedbackStatus): PillTone {
  switch (status) {
    case FeedbackStatus.New: return 'info';
    case FeedbackStatus.Reviewed: return 'warning';
    case FeedbackStatus.Resolved: return 'success';
    case FeedbackStatus.Dismissed: return 'neutral';
    default: return 'neutral';
  }
}

function Stars({ rating }: { rating?: number | null }) {
  if (rating === null || rating === undefined) return <span className="muted">—</span>;
  const filled = Math.max(0, Math.min(5, Math.round(rating)));
  return (
    <span className="ops-stars" title={`${rating} out of 5`}>
      {'★'.repeat(filled)}
      <span className="ops-star-off">{'★'.repeat(5 - filled)}</span>
    </span>
  );
}

/** The feedback DTO may carry either name for its timestamp; take whichever arrived. */
function receivedOn(row: FeedbackDto): string | null | undefined {
  return row.createdAt ?? row.createdAtUtc;
}

interface FormState {
  response: string;
}

const BLANK: FormState = { response: '' };

/** Server validation keys arrive in the C# PascalCase; match them case-insensitively. */
function fieldError(errors: Record<string, string[]>, name: string): string | undefined {
  const key = Object.keys(errors).find((k) => k.toLowerCase() === name.toLowerCase());
  return key ? errors[key][0] : undefined;
}

/**
 * Always an edit of one existing record — a response has nothing to attach to without the
 * feedback it answers, so there is no create mode and the id is read straight off the route.
 */
export default function FeedbackRespondPage() {
  const navigate = useNavigate();
  const { id } = useParams<{ id?: string }>();
  const feedbackId = id !== undefined && id !== '' && Number.isFinite(Number(id)) ? Number(id) : null;
  const { can } = useAuth();

  const [feedback, setFeedback] = useState<FeedbackDto | null>(null);
  const [form, setForm] = useState<FormState>(BLANK);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<unknown>(null);
  // Kept apart from `error`: a save that fails must leave the reply on screen, but a record that
  // never loaded has nothing to reply to, so the form is replaced entirely.
  const [loadError, setLoadError] = useState<unknown>(null);
  const [errors, setErrors] = useState<Record<string, string[]>>({});

  const alive = useRef(true);
  useEffect(() => {
    alive.current = true;
    return () => { alive.current = false; };
  }, []);

  const allowed = can('feedback.manage');

  // The list projection omits the message body, so the whole record is read here — the same
  // fetch the dialog used to run when it opened.
  useEffect(() => {
    if (feedbackId === null) {
      setLoading(false);
      setLoadError(new Error('No feedback was named in the address.'));
      return;
    }
    const controller = new AbortController();
    let current = true;
    setLoading(true);
    getFeedback(feedbackId, controller.signal)
      .then((row) => {
        if (!current) return;
        setFeedback(row);
        setForm({ response: row.adminResponse ?? '' });
        setError(null);
        setLoadError(null);
      })
      .catch((err) => { if (current) { setLoadError(err); setError(null); } })
      .finally(() => { if (current) setLoading(false); });
    return () => { current = false; controller.abort(); };
  }, [feedbackId]);

  function set<K extends keyof FormState>(key: K, value: FormState[K]) {
    setForm((f) => ({ ...f, [key]: value }));
  }

  async function save() {
    if (feedbackId === null) return;
    setSaving(true);
    setError(null);
    setErrors({});

    try {
      await respondToFeedback(feedbackId, form.response.trim());
      if (!alive.current) return;
      navigate('/admin/feedback');
    } catch (err) {
      if (!alive.current) return;
      setError(err);
      if (err instanceof ApiError) setErrors(err.validationErrors);
      window.scrollTo({ top: 0, behavior: 'smooth' });
    } finally {
      if (alive.current) setSaving(false);
    }
  }

  // The record could not be read — the id is wrong, or someone else deleted the feedback. A reply
  // box over nothing offers a Send that cannot mean anything, so the page shows the failure and a
  // way back instead.
  if (!loading && loadError) {
    return (
      <div className="page">
        <PageCard>
          <PageCardHeader
            icon={<IconMessage size={20} />}
            title="Respond to Feedback"
            actions={
              <button className="btn btn-outline" onClick={() => navigate('/admin/feedback')}>
                <IconArrowLeft size={15} /> Back to Feedback
              </button>
            }
          />
          <div className="page-card-body">
            <ErrorAlert error={loadError} />
            <div className="form-note">
              Nothing can be answered until a feedback entry that exists is opened.
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
          icon={<IconMessage size={20} />}
          title="Respond to Feedback"
          subtitle="Answer what a member told you. Your reply shows on their feedback page."
          actions={
            <button className="btn btn-outline" onClick={() => navigate('/admin/feedback')}>
              <IconArrowLeft size={15} /> Back to Feedback
            </button>
          }
        />

        <div className="page-card-body">
          {!allowed ? (
            <Alert tone="warning">
              You do not have permission to respond to feedback.
            </Alert>
          ) : loading || !feedback ? (
            <Loading message="Loading feedback…" />
          ) : (
            <>
              {error ? <div style={{ marginBottom: 18 }}><ErrorAlert error={error} /></div> : null}

              {/* Read-only recap: what the member sent, shown as labelled values rather than
                  disabled inputs, so nothing here reads as editable. */}
              <FormSection
                title="Feedback"
                caption="What the member submitted."
                icon={<IconUser size={15} />}
              >
                <div className="form-grid">
                  <Field label="Member">
                    <div>
                      <div className="cell-main">{feedback.memberName || 'Anonymous'}</div>
                      <div className="cell-sub">
                        {feedback.memberCode || initials(feedback.memberName) || '—'}
                      </div>
                    </div>
                  </Field>
                  <Field label="Subject">
                    <div className="cell-main">{feedback.subject || '—'}</div>
                  </Field>
                  <Field label="Rating">
                    <div><Stars rating={feedback.rating} /></div>
                  </Field>
                  <Field label="Submitted">
                    <div className="cell-icon">
                      <IconCalendar size={13} />{fmtDate(receivedOn(feedback))}
                    </div>
                  </Field>
                  <Field label="Status">
                    <div className="ops-chip-row">
                      <Pill tone={statusTone(feedback.status)}>
                        {feedbackStatusLabel(feedback.status, feedback.statusText)}
                      </Pill>
                      {feedback.hasResponse ? <Pill tone="success">Replied</Pill> : null}
                      {feedback.isPrivate ? <Pill tone="dark">Private</Pill> : null}
                    </div>
                  </Field>
                </div>

                <div style={{ marginTop: 'var(--sp-4)' }}>
                  <Field label="Message">
                    <div className="ops-plan-detail">
                      {feedback.message || 'No message was submitted.'}
                    </div>
                  </Field>
                </div>
              </FormSection>

              <FormSection
                title="Your Response"
                caption="The reply the member reads back."
                icon={<IconMessage size={15} />}
              >
                <Field
                  label="Response"
                  required
                  help="The member sees this reply on their feedback page."
                  error={fieldError(errors, 'Response')}
                >
                  <textarea
                    className={`textarea ${fieldError(errors, 'Response') ? 'input-invalid' : ''}`}
                    style={{ minHeight: 160 }}
                    value={form.response}
                    onChange={(e) => set('response', e.target.value)}
                    autoFocus
                  />
                </Field>
              </FormSection>

              <div className="form-footer">
                <button
                  className="btn btn-dark"
                  onClick={() => void save()}
                  disabled={saving || !form.response.trim()}
                >
                  {saving ? 'Sending...' : 'Send Response'}
                </button>
                <button
                  className="btn btn-outline"
                  onClick={() => navigate('/admin/feedback')}
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
