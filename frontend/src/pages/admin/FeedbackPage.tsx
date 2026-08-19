import { useEffect, useState } from 'react';
import { useAuth } from '@/auth/AuthContext';
import {
  Alert, ConfirmModal, EmptyState, ErrorAlert, Field, FilterField, FilterMenu, FilterStrip, Loading,
  Modal, PageCard, PageCardHeader, Pager, Pill,
} from '@/components/ui';
import { IconCalendar, IconMessage, IconRefresh } from '@/components/icons';
import {
  FEEDBACK_STATUSES, FeedbackStatus, deleteFeedback, feedbackStatusLabel, getFeedback,
  isModuleMissing, listFeedback, respondToFeedback, type FeedbackDto,
} from '@/api/endpoints/operations';
import type { PillTone } from '@/components/ui';
import type { PagedResult } from '@/api/types';
import { date as fmtDate, initials } from '@/lib/format';
import './ops.css';

interface Filters {
  search: string;
  status: FeedbackStatus | '';
  rating: number | '';
}

const EMPTY_FILTERS: Filters = { search: '', status: '', rating: '' };

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

export default function FeedbackPage() {
  const { can } = useAuth();
  const manage = can('feedback.manage');

  const [draft, setDraft] = useState<Filters>(EMPTY_FILTERS);
  const [applied, setApplied] = useState<Filters>(EMPTY_FILTERS);
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [reloadKey, setReloadKey] = useState(0);

  const [data, setData] = useState<PagedResult<FeedbackDto> | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<unknown>(null);
  const [missing, setMissing] = useState(false);

  const [responding, setResponding] = useState<FeedbackDto | null>(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const [responseText, setResponseText] = useState('');
  const [formError, setFormError] = useState<unknown>(null);
  const [saving, setSaving] = useState(false);
  const [pendingDelete, setPendingDelete] = useState<FeedbackDto | null>(null);
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    (async () => {
      setLoading(true);
      try {
        const result = await listFeedback(
          {
            search: applied.search || undefined,
            status: applied.status,
            minRating: applied.rating,
            maxRating: applied.rating,
            pageNumber,
            pageSize,
          },
          controller.signal,
        );
        if (controller.signal.aborted) return;
        setData(result);
        setMissing(false);
        setError(null);
      } catch (err) {
        if (controller.signal.aborted) return;
        setData(null);
        if (isModuleMissing(err)) { setMissing(true); setError(null); }
        else { setMissing(false); setError(err); }
      } finally {
        if (!controller.signal.aborted) setLoading(false);
      }
    })();
    return () => controller.abort();
  }, [applied, pageNumber, pageSize, reloadKey]);

  /** The list row carries no message body, so open the dialog on the full record. */
  const openResponder = async (row: FeedbackDto) => {
    setResponding(row);
    setResponseText(row.adminResponse ?? '');
    setFormError(null);
    setDetailLoading(true);
    try {
      const full = await getFeedback(row.id);
      setResponding(full);
      setResponseText(full.adminResponse ?? '');
    } catch {
      /* the row we already have is enough to reply against */
    } finally {
      setDetailLoading(false);
    }
  };

  const submitResponse = async () => {
    if (!responding) return;
    setSaving(true);
    setFormError(null);
    try {
      await respondToFeedback(responding.id, responseText.trim());
      setResponding(null);
      setResponseText('');
      setNotice('Response sent to the member.');
      setReloadKey((k) => k + 1);
    } catch (err) {
      setFormError(err);
    } finally {
      setSaving(false);
    }
  };

  const confirmDelete = async () => {
    if (!pendingDelete) return;
    setBusy(true);
    try {
      await deleteFeedback(pendingDelete.id);
      setPendingDelete(null);
      setReloadKey((k) => k + 1);
    } catch (err) {
      setError(err);
      setPendingDelete(null);
    } finally {
      setBusy(false);
    }
  };

  const applyFilters = () => { setApplied(draft); setPageNumber(1); };
  const resetFilters = () => { setDraft(EMPTY_FILTERS); setApplied(EMPTY_FILTERS); setPageNumber(1); };

  // Badge on the trigger. Counts only the folded-away filters — search has its own visible box,
  // so including it would show a badge for something the user can already see.
  const activeFilterCount = (['status', 'rating'] as const)
    .filter((key) => applied[key] !== EMPTY_FILTERS[key]).length;

  const items = data?.items ?? [];
  const firstIndex = (pageNumber - 1) * pageSize;

  return (
    <div className="page">
      <PageCard>
        <PageCardHeader
          icon={<IconMessage size={20} />}
          title="Feedback"
          subtitle="What your members are telling you, and how your team replied."
          actions={(
            <>
              <FilterMenu
                activeCount={activeFilterCount}
                onApply={applyFilters}
                onReset={resetFilters}
              >
                <FilterField label="Status">
                  <select
                    className="select"
                    value={draft.status === '' ? '' : String(draft.status)}
                    onChange={(e) => setDraft({
                      ...draft,
                      status: e.target.value === '' ? '' : (Number(e.target.value) as FeedbackStatus),
                    })}
                  >
                    <option value="">All statuses</option>
                    {FEEDBACK_STATUSES.map((s) => <option key={s.value} value={s.value}>{s.label}</option>)}
                  </select>
                </FilterField>

                <FilterField label="Rating">
                  <select
                    className="select"
                    value={draft.rating === '' ? '' : String(draft.rating)}
                    onChange={(e) => setDraft({ ...draft, rating: e.target.value === '' ? '' : Number(e.target.value) })}
                  >
                    <option value="">Any rating</option>
                    {[5, 4, 3, 2, 1].map((r) => <option key={r} value={r}>{r} star{r === 1 ? '' : 's'}</option>)}
                  </select>
                </FilterField>
              </FilterMenu>

              <button className="btn btn-dark" onClick={() => setReloadKey((k) => k + 1)} disabled={loading}>
                <IconRefresh size={15} /> Refresh
              </button>
            </>
          )}
        />

        {/* Search and Reset stay in the open, since they are the two controls reached most often. */}
        <FilterStrip>
          <FilterField label="Search">
            <input
              className="input"
              placeholder="Member, subject or message"
              value={draft.search}
              onChange={(e) => setDraft({ ...draft, search: e.target.value })}
              onKeyDown={(e) => { if (e.key === 'Enter') applyFilters(); }}
            />
          </FilterField>
        </FilterStrip>

        {notice ? <div className="page-card-body" style={{ paddingBottom: 0 }}><Alert tone="success">{notice}</Alert></div> : null}

        {loading && <Loading message="Loading feedback…" />}
        {!loading && Boolean(error) && <div className="page-card-body"><ErrorAlert error={error} /></div>}

        {!loading && missing && (
          <EmptyState
            icon={<IconMessage size={34} />}
            title="Feedback module is not available yet"
            message="The feedback endpoints are still being added to the API. This screen will fill in as soon as they ship."
          />
        )}

        {!loading && !error && !missing && items.length === 0 && (
          <EmptyState
            icon={<IconMessage size={34} />}
            title="No feedback yet"
            message="Member comments and ratings will appear here as they are submitted."
          />
        )}

        {!loading && !error && !missing && items.length > 0 && (
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th className="idx">#</th>
                  <th>Member</th>
                  <th>Subject</th>
                  <th>Message</th>
                  <th className="center">Rating</th>
                  <th>Status</th>
                  <th className="center">Received</th>
                  <th className="actions">Actions</th>
                </tr>
              </thead>
              <tbody>
                {items.map((row, index) => (
                  <tr key={row.id}>
                    <td className="idx">{firstIndex + index + 1}</td>
                    <td>
                      <div className="cell-main">{row.memberName || 'Anonymous'}</div>
                      <div className="cell-sub">{row.memberCode || initials(row.memberName)}</div>
                    </td>
                    <td className="cell-main">{row.subject || '—'}</td>
                    <td><div className="ops-trunc-2">{row.message || '—'}</div></td>
                    <td className="center"><Stars rating={row.rating} /></td>
                    <td>
                      <div className="ops-chip-row">
                        <Pill tone={statusTone(row.status)}>
                          {feedbackStatusLabel(row.status, row.statusText)}
                        </Pill>
                        {row.hasResponse ? <Pill tone="success">Replied</Pill> : null}
                      </div>
                    </td>
                    <td className="center">
                      <span className="cell-icon"><IconCalendar size={13} />{fmtDate(receivedOn(row))}</span>
                    </td>
                    <td className="actions">
                      {manage && (
                        <>
                          <button className="btn btn-edit" onClick={() => void openResponder(row)}>
                            Respond
                          </button>
                          <button className="btn btn-del" onClick={() => setPendingDelete(row)}>Delete</button>
                        </>
                      )}
                      {!manage && <span className="muted">—</span>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {!loading && !error && !missing && data && data.totalCount > 0 && (
          <Pager
            pageNumber={pageNumber}
            pageSize={pageSize}
            totalCount={data.totalCount}
            onPage={setPageNumber}
            onPageSize={(size) => { setPageSize(size); setPageNumber(1); }}
          />
        )}
      </PageCard>

      {responding && (
        <Modal
          title="Respond to feedback"
          icon={<IconMessage size={18} />}
          onClose={() => setResponding(null)}
          footer={
            <>
              <button className="btn btn-outline" onClick={() => setResponding(null)} disabled={saving}>Cancel</button>
              <button
                className="btn btn-dark"
                onClick={() => void submitResponse()}
                disabled={saving || !responseText.trim()}
              >
                {saving ? 'Sending…' : 'Send response'}
              </button>
            </>
          }
        >
          <div className="stack">
            {formError ? <ErrorAlert error={formError} /> : null}
            <div className="form-section">
              <div className="form-section-title">{responding.subject || 'Feedback'}</div>
              <div className="form-section-caption">
                {responding.memberName || 'Anonymous'} · {fmtDate(receivedOn(responding))}
              </div>
              <div className="ops-plan-detail" style={{ marginTop: 12 }}>
                {detailLoading ? 'Loading the member’s message…' : (responding.message || 'No message was submitted.')}
              </div>
              <div className="ops-chip-row" style={{ marginTop: 12 }}>
                <Stars rating={responding.rating} />
                <Pill tone={statusTone(responding.status)}>
                  {feedbackStatusLabel(responding.status, responding.statusText)}
                </Pill>
                {responding.isPrivate ? <Pill tone="dark">Private</Pill> : null}
              </div>
            </div>
            <Field label="Response" required help="The member sees this reply on their feedback page.">
              <textarea
                className="textarea"
                style={{ minHeight: 140 }}
                value={responseText}
                onChange={(e) => setResponseText(e.target.value)}
                autoFocus
              />
            </Field>
            <div className="form-note">Fields marked with * are mandatory.</div>
          </div>
        </Modal>
      )}

      {pendingDelete && (
        <ConfirmModal
          title="Delete feedback"
          message={<>Delete the feedback from <strong>{pendingDelete.memberName || 'this member'}</strong>?</>}
          confirmLabel="Delete"
          busy={busy}
          onConfirm={() => void confirmDelete()}
          onClose={() => setPendingDelete(null)}
        />
      )}
    </div>
  );
}
