import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '@/auth/AuthContext';
import {
  Alert, ConfirmModal, EmptyState, ErrorAlert, Field, FilterField, FilterMenu, FilterStrip, Loading,
  Modal, PageCard, PageCardHeader, Pager, Pill, SearchField,
} from '@/components/ui';
import {
  IconCalendar, IconPhone, IconPlus, IconSearch, IconUsers,
} from '@/components/icons';
import {
  ENQUIRY_SOURCES, ENQUIRY_STATUSES, EnquirySource, EnquiryStatus, convertEnquiry, deleteEnquiry,
  enquiryStatusLabel, isModuleMissing, listEnquiries, sourceLabel, type EnquiryDto,
} from '@/api/endpoints/operations';
import { memberLookup } from '@/api/endpoints/workouts';
import type { PillTone } from '@/components/ui';
import type { Lookup, PagedResult } from '@/api/types';
import { date as fmtDate } from '@/lib/format';
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

export default function EnquiriesPage() {
  const navigate = useNavigate();
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
                <button className="btn btn-dark" onClick={() => navigate('/admin/enquiries/new')}>
                  <IconPlus size={15} /> Add Enquiry
                </button>
              ) : null}
            </>
          )}
        />

        {/* Search and Reset stay in the open, since they are the two controls reached most often. */}
        <FilterStrip>
          <SearchField
            placeholder="Name, phone or email"
            value={draft.search}
            onChange={(value) => setDraft({ ...draft, search: value })}
            onSearch={applyFilters}
          />
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
              ? <button className="btn btn-dark" onClick={() => navigate('/admin/enquiries/new')}><IconPlus size={15} /> Add Enquiry</button>
              : undefined}
          />
        )}

        {!loading && !error && !missing && items.length > 0 && (
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th className="idx">#</th>
                  <th className="wide">Name</th>
                  <th className="fit">Source</th>
                  <th>Interested plan</th>
                  <th className="fit">Status</th>
                  <th className="center fit">Follow-up</th>
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
                    <td className="fit"><Pill tone="neutral">{sourceLabel(row.source, row.sourceText)}</Pill></td>
                    <td>{row.interestedPlanName || <span className="muted">—</span>}</td>
                    <td className="fit">
                      <Pill tone={statusTone(row.status)}>
                        {enquiryStatusLabel(row.status, row.statusText)}
                      </Pill>
                    </td>
                    <td className="center fit">
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
                            onClick={() => navigate(`/admin/enquiries/${row.id}/edit`)}
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
