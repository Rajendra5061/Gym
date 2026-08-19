/**
 * Member feedback: a submit form beside everything this member has sent before.
 *
 * The feedback module may not be deployed on every installation, so a 404 from either call is
 * reported as "not available yet" rather than being surfaced as a failure.
 */

import { useEffect, useState } from 'react';
import {
  Alert, EmptyState, ErrorAlert, Field, Loading, PageCard, PageCardHeader, Pill, StatusPill,
} from '@/components/ui';
import { IconInfo, IconMessage, IconUser } from '@/components/icons';
import { feedbackItems, isUnavailable, memberApi } from '@/api/endpoints/member';
import type { FeedbackDto } from '@/api/endpoints/member';
import { useAuth } from '@/auth/AuthContext';
import { dateTime } from '@/lib/format';
import './member.css';

function Stars({ value, onChange }: { value: number; onChange?: (rating: number) => void }) {
  return (
    <div className="m-star-row">
      {[1, 2, 3, 4, 5].map((star) => (
        <button
          key={star}
          type="button"
          className={`m-star ${star <= value ? 'on' : ''}`}
          disabled={!onChange}
          aria-label={`${star} star${star === 1 ? '' : 's'}`}
          onClick={() => onChange?.(star === value ? 0 : star)}
        >
          ★
        </button>
      ))}
      {onChange && value > 0 && (
        <button type="button" className="btn btn-ghost btn-sm" onClick={() => onChange(0)}>Clear</button>
      )}
    </div>
  );
}

export default function MyFeedbackPage() {
  const { user } = useAuth();
  const memberId = user?.memberId ?? null;

  const [items, setItems] = useState<FeedbackDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<unknown>(null);
  const [unavailable, setUnavailable] = useState(false);
  const [reloadKey, setReloadKey] = useState(0);

  const [subject, setSubject] = useState('');
  const [message, setMessage] = useState('');
  const [rating, setRating] = useState(0);
  const [saving, setSaving] = useState(false);
  const [formError, setFormError] = useState<unknown>(null);
  const [notice, setNotice] = useState<string | null>(null);

  useEffect(() => {
    const ctrl = new AbortController();
    setLoading(true);
    setError(null);
    memberApi.myFeedback(ctrl.signal)
      .then((payload) => {
        if (ctrl.signal.aborted) return;
        setItems(feedbackItems(payload));
        setUnavailable(false);
      })
      .catch((e) => {
        if (ctrl.signal.aborted) return;
        if (isUnavailable(e)) setUnavailable(true); else setError(e);
      })
      .finally(() => { if (!ctrl.signal.aborted) setLoading(false); });
    return () => ctrl.abort();
  }, [reloadKey]);

  async function submit() {
    setSaving(true);
    setFormError(null);
    setNotice(null);
    try {
      await memberApi.submitFeedback({
        subject: subject.trim() || null,
        message: message.trim(),
        rating: rating > 0 ? rating : null,
      });
      setSubject(''); setMessage(''); setRating(0);
      setNotice('Thank you — your feedback has been sent to the gym team.');
      setReloadKey((k) => k + 1);
    } catch (e) {
      setFormError(isUnavailable(e) ? new Error('The feedback module is not available on this installation yet.') : e);
    } finally {
      setSaving(false);
    }
  }

  if (memberId === null) {
    return (
      <div className="page">
        <PageCard>
          <EmptyState icon={<IconUser size={34} />} title="No member record linked" message="This login is not attached to a member profile." />
        </PageCard>
      </div>
    );
  }

  return (
    <div className="page">
      <PageCard>
        <PageCardHeader
          icon={<IconMessage size={20} />}
          title="Feedback"
          subtitle="Tell the gym what is working and what is not — the team reads every message."
        />
        <div className="page-card-body">
          {unavailable && (
            <div style={{ marginBottom: 16 }}>
              <Alert tone="info">
                <IconInfo size={16} /> The feedback module is not available on this installation yet.
              </Alert>
            </div>
          )}

          <div className="member-panes member-panes-even">
            {/* ------------------------------------------------ submit form */}
            <div className="page-card" style={{ marginTop: 0 }}>
              <div className="caption-bar"><IconMessage size={16} /> Submit Feedback</div>
              <div className="page-card-body stack">
                {notice && <Alert tone="success">{notice}</Alert>}
                <ErrorAlert error={formError} />

                <Field label="Your feedback" required help="Tell us what happened, what you liked or what should change.">
                  <textarea
                    className="textarea"
                    style={{ minHeight: 150 }}
                    value={message}
                    disabled={unavailable}
                    onChange={(e) => setMessage(e.target.value)}
                    placeholder="Share your experience with the gym…"
                  />
                </Field>

                <Field label="Subject" help="Optional — a short heading helps the team route it.">
                  <input
                    className="input"
                    value={subject}
                    disabled={unavailable}
                    onChange={(e) => setSubject(e.target.value)}
                    placeholder="e.g. Equipment in the free-weights area"
                  />
                </Field>

                <Field label="Rating" help="Optional — one to five stars.">
                  <Stars value={rating} onChange={unavailable ? undefined : setRating} />
                </Field>

                <div className="form-note">
                  Your feedback is visible to the gym administrator along with your name and member code.
                </div>

                <div className="form-footer">
                  <button
                    className="btn btn-dark"
                    disabled={saving || unavailable || message.trim().length === 0}
                    onClick={() => void submit()}
                  >
                    {saving ? 'Sending…' : 'Submit Feedback'}
                  </button>
                  <button
                    className="btn btn-outline"
                    disabled={saving}
                    onClick={() => { setSubject(''); setMessage(''); setRating(0); setFormError(null); }}
                  >
                    Clear
                  </button>
                </div>
              </div>
            </div>

            {/* -------------------------------------------- previous feedback */}
            <div className="page-card" style={{ marginTop: 0 }}>
              <div className="caption-bar"><IconMessage size={16} /> My Previous Feedback</div>
              <div className="page-card-body">
                {error ? <ErrorAlert error={error} /> : loading ? <Loading message="Loading your feedback…" /> : items.length === 0 ? (
                  <EmptyState
                    icon={<IconMessage size={34} />}
                    title="No feedback submitted yet"
                    message="Anything you send appears here with the gym's reply."
                  />
                ) : (
                  <div>
                    {items.map((item) => (
                      <div className="m-feedback-item" key={item.id}>
                        <div className="row" style={{ gap: 8, flexWrap: 'wrap' }}>
                          <span className="grow cell-main">{item.subject || 'Feedback'}</span>
                          <StatusPill status={item.statusText} />
                          {item.rating ? <Pill tone="warning">{'★'.repeat(item.rating)}</Pill> : null}
                        </div>
                        <div className="cell-sub">{dateTime(item.createdAt)}</div>
                        <div style={{ marginTop: 8, whiteSpace: 'pre-wrap' }}>{item.message}</div>
                        {item.adminResponse && (
                          <div className="m-feedback-response">
                            <div className="field-label" style={{ marginBottom: 4 }}>
                              Reply{item.respondedByUserName ? ` from ${item.respondedByUserName}` : ''}
                              {item.respondedAt ? ` · ${dateTime(item.respondedAt)}` : ''}
                            </div>
                            <div style={{ whiteSpace: 'pre-wrap' }}>{item.adminResponse}</div>
                          </div>
                        )}
                      </div>
                    ))}
                  </div>
                )}
              </div>
            </div>
          </div>
        </div>
      </PageCard>
    </div>
  );
}
