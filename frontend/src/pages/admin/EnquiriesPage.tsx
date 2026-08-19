import { useEffect, useState } from 'react';
import { useAuth } from '@/auth/AuthContext';
import {
  Alert, ConfirmModal, EmptyState, ErrorAlert, Field, FilterField, FilterMenu, FilterStrip, Loading,
  Modal, PageCard, PageCardHeader, Pager, Pill,
} from '@/components/ui';
import {
  IconCalendar, IconMail, IconPhone, IconPlus, IconSearch, IconUser,
  IconUsers,
} from '@/components/icons';
import {
  ENQUIRY_SOURCES, ENQUIRY_STATUSES, EnquirySource, EnquiryStatus, convertEnquiry, deleteEnquiry,
  enquiryStatusLabel, isModuleMissing, listEnquiries, membershipPlanLookup, saveEnquiry,
  sourceLabel, userLookup, type EnquiryDto,
} from '@/api/endpoints/operations';
import { memberLookup } from '@/api/endpoints/workouts';
import type { PillTone } from '@/components/ui';
import type { Lookup, PagedResult } from '@/api/types';
import { date as fmtDate, isoDate } from '@/lib/format';
import './ops.css';

interface Filters {
  search: string;
  status: EnquiryStatus | '';
  source: EnquirySource | '';
}

const EMPTY_FILTERS: Filters = { search: '', status: '', source: '' };

function statusTone(status: EnquiryStatus): PillTone {
  switch (status) {
    case EnquiryStatus.New: return 'info';
    case EnquiryStatus.Contacted: return 'primary';
    case EnquiryStatus.FollowUp: return 'warning';
    case EnquiryStatus.Converted: return 'success';
    case EnquiryStatus.Lost: return 'danger';
    default: return 'neutral';
  }
}

function blankEnquiry(): Partial<EnquiryDto> {
  return {
    id: 0,
    fullName: '',
    phone: '',
    email: '',
    source: EnquirySource.WalkIn,
    interestedPlanId: null,
    message: '',
    status: EnquiryStatus.New,
    followUpDate: '',
    assignedToUserId: null,
    notes: '',
  };
}

export default function EnquiriesPage() {
  const { can } = useAuth();
  const manage = can('enquiries.manage');

  const [draft, setDraft] = useState<Filters>(EMPTY_FILTERS);
  const [applied, setApplied] = useState<Filters>(EMPTY_FILTERS);
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [reloadKey, setReloadKey] = useState(0);

  const [data, setData] = useState<PagedResult<EnquiryDto> | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<unknown>(null);
  const [missing, setMissing] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);

  const [plans, setPlans] = useState<Lookup[]>([]);
  const [users, setUsers] = useState<Lookup[]>([]);

  const [editing, setEditing] = useState<Partial<EnquiryDto> | null>(null);
  const [formError, setFormError] = useState<unknown>(null);
  const [saving, setSaving] = useState(false);
  const [pendingDelete, setPendingDelete] = useState<EnquiryDto | null>(null);
  const [busy, setBusy] = useState(false);

  const [converting, setConverting] = useState<EnquiryDto | null>(null);
  const [memberTerm, setMemberTerm] = useState('');
  const [members, setMembers] = useState<Lookup[]>([]);
  const [convertMemberId, setConvertMemberId] = useState('');
  const [convertError, setConvertError] = useState<unknown>(null);

  useEffect(() => {
    const controller = new AbortController();
    (async () => {
      setLoading(true);
      try {
        const result = await listEnquiries(
          {
            search: applied.search || undefined,
            status: applied.status,
            source: applied.source,
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
    if (!converting) return;
    const controller = new AbortController();
    const timer = window.setTimeout(() => {
      (async () => {
        try {
          const rows = await memberLookup(memberTerm, controller.signal);
          if (!controller.signal.aborted) setMembers(rows);
        } catch {
          if (!controller.signal.aborted) setMembers([]);
        }
      })();
    }, memberTerm ? 300 : 0);
    return () => { window.clearTimeout(timer); controller.abort(); };
  }, [converting, memberTerm]);

  const submit = async () => {
    if (!editing) return;
    setSaving(true);
    setFormError(null);
    try {
      await saveEnquiry({ ...editing, followUpDate: editing.followUpDate || null });
      setEditing(null);
      setReloadKey((k) => k + 1);
    } catch (err) {
      setFormError(err);
    } finally {
      setSaving(false);
    }
  };

  const submitConvert = async () => {
    if (!converting || !convertMemberId) return;
    setBusy(true);
    setConvertError(null);
    try {
      await convertEnquiry(converting.id, Number(convertMemberId));
      setConverting(null);
      setConvertMemberId('');
      setMemberTerm('');
      setNotice('Enquiry converted and linked to the member.');
      setReloadKey((k) => k + 1);
    } catch (err) {
      setConvertError(err);
    } finally {
      setBusy(false);
    }
  };

  const confirmDelete = async () => {
    if (!pendingDelete) return;
    setBusy(true);
    try {
      await deleteEnquiry(pendingDelete.id);
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
  const activeFilterCount = (['status', 'source'] as const)
    .filter((key) => applied[key] !== EMPTY_FILTERS[key]).length;

  const items = data?.items ?? [];
  const firstIndex = (pageNumber - 1) * pageSize;

  return (
    <div className="page">
      <PageCard>
        <PageCardHeader
          icon={<IconUsers size={20} />}
          title="Enquiries"
          subtitle="Walk-ins, calls and web leads, from first contact to a signed-up member."
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
                      status: e.target.value === '' ? '' : (Number(e.target.value) as EnquiryStatus),
                    })}
                  >
                    <option value="">All statuses</option>
                    {ENQUIRY_STATUSES.map((s) => <option key={s.value} value={s.value}>{s.label}</option>)}
                  </select>
                </FilterField>

                <FilterField label="Source">
                  <select
                    className="select"
                    value={draft.source === '' ? '' : String(draft.source)}
                    onChange={(e) => setDraft({
                      ...draft,
                      source: e.target.value === '' ? '' : (Number(e.target.value) as EnquirySource),
                    })}
                  >
                    <option value="">All sources</option>
                    {ENQUIRY_SOURCES.map((s) => <option key={s.value} value={s.value}>{s.label}</option>)}
                  </select>
                </FilterField>
              </FilterMenu>

              {manage ? (
                <button className="btn btn-dark" onClick={() => { setEditing(blankEnquiry()); setFormError(null); }}>
                  <IconPlus size={15} /> Add Enquiry
                </button>
              ) : null}
            </>
          )}
        />

        {/* Search and Reset stay in the open, since they are the two controls reached most often. */}
        <FilterStrip>
          <FilterField label="Search">
            <input
              className="input"
              placeholder="Name, phone or email"
              value={draft.search}
              onChange={(e) => setDraft({ ...draft, search: e.target.value })}
              onKeyDown={(e) => { if (e.key === 'Enter') applyFilters(); }}
            />
          </FilterField>
        </FilterStrip>

        {notice ? <div className="page-card-body" style={{ paddingBottom: 0 }}><Alert tone="success">{notice}</Alert></div> : null}

        {loading && <Loading message="Loading enquiries…" />}
        {!loading && Boolean(error) && <div className="page-card-body"><ErrorAlert error={error} /></div>}

        {!loading && missing && (
          <EmptyState
            icon={<IconUsers size={34} />}
            title="Enquiries module is not available yet"
            message="The enquiry endpoints are still being added to the API. This screen will fill in as soon as they ship."
          />
        )}

        {!loading && !error && !missing && items.length === 0 && (
          <EmptyState
            icon={<IconUsers size={34} />}
            title="No enquiries yet"
            message="Log walk-ins and calls here so nobody slips through the follow-up net."
            action={manage
              ? <button className="btn btn-dark" onClick={() => setEditing(blankEnquiry())}><IconPlus size={15} /> Add Enquiry</button>
              : undefined}
          />
        )}

        {!loading && !error && !missing && items.length > 0 && (
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th className="idx">#</th>
                  <th>Name</th>
                  <th>Source</th>
                  <th>Interested plan</th>
                  <th>Status</th>
                  <th className="center">Follow-up</th>
                  <th>Assigned to</th>
                  <th className="actions">Actions</th>
                </tr>
              </thead>
              <tbody>
                {items.map((row, index) => (
                  <tr key={row.id}>
                    <td className="idx">{firstIndex + index + 1}</td>
                    <td>
                      <div className="cell-main">{row.fullName}</div>
                      <div className="cell-sub">
                        <span className="cell-icon"><IconPhone size={12} />{row.phone || '—'}</span>
                      </div>
                    </td>
                    <td><Pill tone="neutral">{sourceLabel(row.source, row.sourceText)}</Pill></td>
                    <td>{row.interestedPlanName || <span className="muted">—</span>}</td>
                    <td>
                      <Pill tone={statusTone(row.status)}>
                        {enquiryStatusLabel(row.status, row.statusText)}
                      </Pill>
                    </td>
                    <td className="center">
                      <span className="cell-icon"><IconCalendar size={13} />{fmtDate(row.followUpDate)}</span>
                    </td>
                    <td>{row.assignedToName || <span className="muted">Unassigned</span>}</td>
                    <td className="actions">
                      {manage && (
                        <>
                          {row.status !== EnquiryStatus.Converted && (
                            <button
                              className="btn btn-edit"
                              onClick={() => {
                                setConverting(row);
                                setConvertMemberId('');
                                setMemberTerm('');
                                setConvertError(null);
                              }}
                            >
                              Convert
                            </button>
                          )}
                          <button
                            className="btn btn-edit"
                            onClick={() => {
                              setEditing({ ...row, followUpDate: isoDate(row.followUpDate) });
                              setFormError(null);
                            }}
                          >
                            Edit
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

      {editing && (
        <Modal
          title={editing.id ? 'Edit enquiry' : 'Add enquiry'}
          icon={<IconUser size={18} />}
          onClose={() => setEditing(null)}
          width={780}
          footer={
            <>
              <button className="btn btn-outline" onClick={() => setEditing(null)} disabled={saving}>Cancel</button>
              <button className="btn btn-dark" onClick={() => void submit()} disabled={saving}>
                {saving ? 'Saving…' : 'Save enquiry'}
              </button>
            </>
          }
        >
          <div className="stack">
            {formError ? <ErrorAlert error={formError} /> : null}
            <div className="form-grid">
              <Field label="Full name" required>
                <input
                  className="input"
                  value={editing.fullName ?? ''}
                  onChange={(e) => setEditing({ ...editing, fullName: e.target.value })}
                />
              </Field>
              <Field label="Phone" required help="The number your team will call back on.">
                <div className="input-group">
                  <span className="input-icon"><IconPhone size={14} /></span>
                  <input
                    className="input"
                    value={editing.phone ?? ''}
                    onChange={(e) => setEditing({ ...editing, phone: e.target.value })}
                  />
                </div>
              </Field>
              <Field label="Email">
                <div className="input-group">
                  <span className="input-icon"><IconMail size={14} /></span>
                  <input
                    className="input"
                    type="email"
                    value={editing.email ?? ''}
                    onChange={(e) => setEditing({ ...editing, email: e.target.value })}
                  />
                </div>
              </Field>
              <Field label="Source" required>
                <select
                  className="select"
                  value={editing.source ?? EnquirySource.WalkIn}
                  onChange={(e) => setEditing({ ...editing, source: Number(e.target.value) as EnquirySource })}
                >
                  {ENQUIRY_SOURCES.map((s) => <option key={s.value} value={s.value}>{s.label}</option>)}
                </select>
              </Field>
              <Field label="Interested plan">
                <select
                  className="select"
                  value={editing.interestedPlanId ?? ''}
                  onChange={(e) => setEditing({
                    ...editing,
                    interestedPlanId: e.target.value ? Number(e.target.value) : null,
                  })}
                >
                  <option value="">Not decided</option>
                  {plans.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
                </select>
              </Field>
              <Field label="Status" required>
                <select
                  className="select"
                  value={editing.status ?? EnquiryStatus.New}
                  onChange={(e) => setEditing({ ...editing, status: Number(e.target.value) as EnquiryStatus })}
                >
                  {ENQUIRY_STATUSES.map((s) => <option key={s.value} value={s.value}>{s.label}</option>)}
                </select>
              </Field>
              <Field label="Follow-up date" help="When to call this lead back.">
                <input
                  className="input"
                  type="date"
                  value={editing.followUpDate ?? ''}
                  onChange={(e) => setEditing({ ...editing, followUpDate: e.target.value })}
                />
              </Field>
              <Field label="Assigned to">
                <select
                  className="select"
                  value={editing.assignedToUserId ?? ''}
                  onChange={(e) => setEditing({
                    ...editing,
                    assignedToUserId: e.target.value ? Number(e.target.value) : null,
                  })}
                >
                  <option value="">Unassigned</option>
                  {users.map((u) => <option key={u.id} value={u.id}>{u.name}</option>)}
                </select>
              </Field>
            </div>
            <Field label="Message" help="What the lead asked about.">
              <textarea
                className="textarea"
                value={editing.message ?? ''}
                onChange={(e) => setEditing({ ...editing, message: e.target.value })}
              />
            </Field>
            <Field label="Notes">
              <textarea
                className="textarea"
                value={editing.notes ?? ''}
                onChange={(e) => setEditing({ ...editing, notes: e.target.value })}
              />
            </Field>
            <div className="form-note">Fields marked with * are mandatory.</div>
          </div>
        </Modal>
      )}

      {converting && (
        <Modal
          title="Convert to member"
          icon={<IconUsers size={18} />}
          onClose={() => setConverting(null)}
          width={560}
          footer={
            <>
              <button className="btn btn-outline" onClick={() => setConverting(null)} disabled={busy}>Cancel</button>
              <button
                className="btn btn-dark"
                onClick={() => void submitConvert()}
                disabled={busy || !convertMemberId}
              >
                {busy ? 'Converting…' : 'Convert enquiry'}
              </button>
            </>
          }
        >
          <div className="stack">
            {convertError ? <ErrorAlert error={convertError} /> : null}
            <Alert tone="info">
              Register <strong>{converting.fullName}</strong> as a member first, then link the enquiry to that
              member record here.
            </Alert>
            <Field label="Member" required help="Search the member you created for this lead.">
              <div className="stack" style={{ gap: 6 }}>
                <div className="input-group">
                  <span className="input-icon"><IconSearch size={14} /></span>
                  <input
                    className="input"
                    placeholder="Search by name, code or phone"
                    value={memberTerm}
                    onChange={(e) => setMemberTerm(e.target.value)}
                  />
                </div>
                <select
                  className="select"
                  value={convertMemberId}
                  onChange={(e) => setConvertMemberId(e.target.value)}
                >
                  <option value="">Select a member…</option>
                  {members.map((m) => (
                    <option key={m.id} value={m.id}>{m.name}{m.code ? ` (${m.code})` : ''}</option>
                  ))}
                </select>
              </div>
            </Field>
          </div>
        </Modal>
      )}

      {pendingDelete && (
        <ConfirmModal
          title="Delete enquiry"
          message={<>Delete the enquiry from <strong>{pendingDelete.fullName}</strong>?</>}
          confirmLabel="Delete"
          busy={busy}
          onConfirm={() => void confirmDelete()}
          onClose={() => setPendingDelete(null)}
        />
      )}
    </div>
  );
}
