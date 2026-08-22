import { useEffect, useRef, useState, type ReactNode } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { download, saveBlob } from '@/api/client';
import {
  MEMBER_STATUS_OPTIONS, genderLabel, memberStatusLabel, membersApi,
  type MemberHistoryDto,
} from '@/api/endpoints/members';
import { MemberStatus } from '@/api/types';
import { useAuth } from '@/auth/AuthContext';
import {
  Alert, EmptyState, ErrorAlert, Field, FormSection, Loading, Modal,
  PageCard, PageCardHeader, Pill, StatusPill, type PillTone,
} from '@/components/ui';
import {
  IconArrowLeft, IconBell, IconBox, IconCalendar, IconCard, IconCheckSquare, IconCrown,
  IconDownload, IconDumbbell, IconEdit, IconFile, IconMail, IconMessage, IconPhone, IconUser,
} from '@/components/icons';
import { date, dateTime, initials, money } from '@/lib/format';
import { communicationsApi, type CommunicationLogDto } from '@/api/endpoints/communications';
import { ChannelTicks, KindPill } from './CommunicationsPage';
import './admin.css';
import './communications.css';

const TABS = [
  'Overview', 'Subscriptions', 'Payments', 'Attendance', 'Workouts', 'Measurements', 'Documents',
  'Communications',
] as const;
type Tab = typeof TABS[number];

function Detail({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="detail-item">
      <div className="detail-label">{label}</div>
      <div className="detail-value">{children}</div>
    </div>
  );
}

/** Renders a value or an em dash when the API left it empty. */
function orDash(value: string | number | null | undefined): ReactNode {
  if (value === null || value === undefined || value === '') return <span className="muted">&mdash;</span>;
  return value;
}

function DaysLeft({ days }: { days: number | null | undefined }) {
  if (days === null || days === undefined) return <span className="muted">&mdash;</span>;
  const tone: PillTone = days < 0 ? 'danger' : days <= 7 ? 'warning' : 'success';
  return <Pill tone={tone}>{days < 0 ? `${Math.abs(days)} overdue` : `${days} days`}</Pill>;
}

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

export default function MemberDetailsPage() {
  const navigate = useNavigate();
  const { id } = useParams<{ id: string }>();
  const memberId = Number(id);
  const { can, currency } = useAuth();

  const [history, setHistory] = useState<MemberHistoryDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<unknown>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [reloadKey, setReloadKey] = useState(0);
  const [tab, setTab] = useState<Tab>('Overview');

  // Loaded on demand the first time the Communications tab opens; null = not fetched yet.
  const [commLogs, setCommLogs] = useState<CommunicationLogDto[] | null>(null);
  const [commLoading, setCommLoading] = useState(false);
  const [commError, setCommError] = useState<unknown>(null);

  const [receiptBusy, setReceiptBusy] = useState<number | null>(null);
  const [documentBusy, setDocumentBusy] = useState<number | null>(null);
  const [statusOpen, setStatusOpen] = useState(false);
  const [nextStatus, setNextStatus] = useState<string>(String(MemberStatus.Active));
  const [statusBusy, setStatusBusy] = useState(false);

  const alive = useRef(true);
  useEffect(() => {
    alive.current = true;
    return () => { alive.current = false; };
  }, []);

  useEffect(() => {
    if (!Number.isFinite(memberId)) return;
    const controller = new AbortController();
    let current = true;
    setLoading(true);
    membersApi.history(memberId, controller.signal)
      .then((res) => {
        if (!current) return;
        setHistory(res);
        setNextStatus(String(res.member.status));
        setError(null);
      })
      .catch((err) => { if (current) { setError(err); setHistory(null); } })
      .finally(() => { if (current) setLoading(false); });
    return () => { current = false; controller.abort(); };
  }, [memberId, reloadKey]);

  useEffect(() => {
    if (tab !== 'Communications' || commLogs !== null || !Number.isFinite(memberId)) return;
    const controller = new AbortController();
    setCommLoading(true);
    setCommError(null);
    communicationsApi.forMember(memberId, controller.signal)
      .then((rows) => { if (!controller.signal.aborted) setCommLogs(rows); })
      .catch((err) => { if (!controller.signal.aborted) setCommError(err); })
      .finally(() => { if (!controller.signal.aborted) setCommLoading(false); });
    return () => controller.abort();
  }, [tab, commLogs, memberId]);

  async function downloadReceipt(paymentId: number) {
    setReceiptBusy(paymentId);
    try {
      const file = await download(`/api/payments/${paymentId}/receipt/pdf`);
      saveBlob(file.blob, file.fileName);
    } catch (err) {
      if (alive.current) setError(err);
    } finally {
      if (alive.current) setReceiptBusy(null);
    }
  }

  /** Same transport as the receipt download; the API answers with the stored file inline. */
  async function downloadDocument(documentId: number, fallbackName: string) {
    setDocumentBusy(documentId);
    try {
      const file = await download(`/api/members/documents/${documentId}/download`);
      saveBlob(file.blob, file.fileName === 'download' ? fallbackName : file.fileName);
    } catch (err) {
      if (alive.current) setError(err);
    } finally {
      if (alive.current) setDocumentBusy(null);
    }
  }

  async function applyStatus() {
    setStatusBusy(true);
    try {
      await membersApi.setStatus(memberId, Number(nextStatus) as MemberStatus);
      if (!alive.current) return;
      setStatusOpen(false);
      setNotice(`Status set to ${memberStatusLabel(Number(nextStatus) as MemberStatus)}.`);
      setReloadKey((k) => k + 1);
    } catch (err) {
      if (alive.current) setError(err);
    } finally {
      if (alive.current) setStatusBusy(false);
    }
  }

  if (loading) {
    return (
      <div className="page">
        <PageCard><Loading message="Loading member..." /></PageCard>
      </div>
    );
  }

  if (!history) {
    return (
      <div className="page">
        <PageCard>
          <PageCardHeader
            icon={<IconUser size={20} />}
            title="Member Details"
            actions={
              <button className="btn btn-outline" onClick={() => navigate('/admin/members')}>
                <IconArrowLeft size={15} /> Back to Members
              </button>
            }
          />
          <div className="page-card-body"><ErrorAlert error={error} /></div>
        </PageCard>
      </div>
    );
  }

  const member = history.member;
  const plan = member.activeSubscription;

  const counts: Record<Tab, number | null> = {
    Overview: null,
    Subscriptions: history.subscriptions.length,
    Payments: history.payments.length,
    Attendance: history.attendance.length,
    Workouts: history.workoutSessions.length,
    Measurements: history.measurements.length,
    Documents: history.documents.length,
    Communications: commLogs ? commLogs.length : null,
  };

  return (
    <div className="page">
      <PageCard>
        <div className="mem-hero">
          <div className="hero-avatar">{initials(member.fullName)}</div>

          <div className="hero-main">
            <div className="hero-name">
              {member.fullName}
              <StatusPill status={memberStatusLabel(member.status)} />
              {member.whatsAppOptOut ? <Pill tone="neutral">No WhatsApp</Pill> : null}
              {member.wishesOptOut ? <Pill tone="neutral">No wishes</Pill> : null}
            </div>
            <div className="hero-code">{member.memberCode}</div>

            <div className="hero-contact">
              <span className="cell-icon"><IconPhone size={14} />{member.phone}</span>
              {member.email ? <span className="cell-icon"><IconMail size={14} />{member.email}</span> : null}
              <span className="cell-icon"><IconCalendar size={14} />Joined {date(member.joiningDate)}</span>
            </div>

            <div className="hero-facts">
              <div className="hero-fact">
                <div className="hero-fact-label">Active plan</div>
                <div className="hero-fact-value" style={{ fontSize: 'var(--fs-base)' }}>
                  {plan ? plan.planName : <span className="muted">None</span>}
                </div>
              </div>
              <div className="hero-fact">
                <div className="hero-fact-label">Days remaining</div>
                <div className="hero-fact-value" style={{ fontSize: 'var(--fs-base)' }}>
                  <DaysLeft days={plan ? plan.daysRemaining : null} />
                </div>
              </div>
              <div className="hero-fact">
                <div className="hero-fact-label">Outstanding</div>
                <div className="hero-fact-value">{money(member.totalOutstanding, currency)}</div>
              </div>
              <div className="hero-fact">
                <div className="hero-fact-label">Total paid</div>
                <div className="hero-fact-value">{money(member.totalPaid, currency)}</div>
              </div>
              <div className="hero-fact">
                <div className="hero-fact-label">Visits</div>
                <div className="hero-fact-value">{member.totalVisits}</div>
              </div>
            </div>
          </div>

          <div className="hero-actions">
            {can('members.edit') && (
              <button className="btn btn-dark" onClick={() => navigate(`/admin/members/${member.id}/edit`)}>
                <IconEdit size={15} /> Edit Member
              </button>
            )}
            {can('members.edit') && (
              <button className="btn btn-outline" onClick={() => setStatusOpen(true)}>
                <IconCheckSquare size={15} /> Change Status
              </button>
            )}
            <button className="btn btn-outline" onClick={() => navigate('/admin/members')}>
              <IconArrowLeft size={15} /> Back to Members
            </button>
          </div>
        </div>

        <div className="detail-tabs">
          {TABS.map((name) => (
            <button
              key={name}
              className={`detail-tab ${tab === name ? 'active' : ''}`}
              onClick={() => setTab(name)}
            >
              {name}
              {counts[name] !== null ? <span className="detail-tab-count">{counts[name]}</span> : null}
            </button>
          ))}
        </div>

        {(error || notice) && (
          <div style={{ padding: 'var(--sp-5) var(--sp-5) 0' }} className="stack">
            {error ? <ErrorAlert error={error} /> : null}
            {notice ? <Alert tone="success">{notice}</Alert> : null}
          </div>
        )}

        {tab === 'Overview' && (
          <div className="page-card-body">
            <FormSection title="Personal Details" icon={<IconUser size={15} />}>
              <div className="detail-grid">
                <Detail label="Gender">{genderLabel(member.gender)}</Detail>
                <Detail label="Date of birth">{member.dateOfBirth ? date(member.dateOfBirth) : orDash(null)}</Detail>
                <Detail label="Age">{member.age ? `${member.age} years` : orDash(null)}</Detail>
                <Detail label="Blood group">{orDash(member.bloodGroup)}</Detail>
                <Detail label="Phone">{member.phone}</Detail>
                <Detail label="Email">{orDash(member.email)}</Detail>
              </div>
            </FormSection>

            <FormSection title="Address" icon={<IconBox size={15} />}>
              <div className="detail-grid">
                <Detail label="Address">{orDash(member.address)}</Detail>
                <Detail label="City">{orDash(member.city)}</Detail>
                <Detail label="Postal code">{orDash(member.postalCode)}</Detail>
              </div>
            </FormSection>

            <FormSection title="Emergency Contact" icon={<IconBell size={15} />} optional>
              <div className="detail-grid">
                <Detail label="Name">{orDash(member.emergencyContactName)}</Detail>
                <Detail label="Phone">{orDash(member.emergencyContactPhone)}</Detail>
                <Detail label="Relationship">{orDash(member.emergencyContactRelation)}</Detail>
              </div>
            </FormSection>

            <FormSection title="Membership" icon={<IconCrown size={15} />}>
              <div className="detail-grid">
                <Detail label="Joining date">{date(member.joiningDate)}</Detail>
                <Detail label="Assigned trainer">{orDash(member.assignedTrainerName)}</Detail>
                <Detail label="Height">{member.heightCm ? `${member.heightCm} cm` : orDash(null)}</Detail>
                <Detail label="Weight">{member.weightKg ? `${member.weightKg} kg` : orDash(null)}</Detail>
                <Detail label="Login account">
                  {member.hasUserAccount ? <Pill tone="success">Yes</Pill> : <Pill tone="neutral">No</Pill>}
                </Detail>
                <Detail label="Last visit">{member.lastVisitDate ? date(member.lastVisitDate) : orDash(null)}</Detail>
                <Detail label="Registered">{dateTime(member.createdAt)}</Detail>
                <Detail label="Last updated">{member.updatedAt ? dateTime(member.updatedAt) : orDash(null)}</Detail>
              </div>
              {member.notes ? (
                <div style={{ marginTop: 'var(--sp-4)' }}>
                  <Detail label="Notes">{member.notes}</Detail>
                </div>
              ) : null}
            </FormSection>
          </div>
        )}

        {tab === 'Subscriptions' && (
          history.subscriptions.length === 0 ? (
            <EmptyState icon={<IconCrown size={40} />} title="No subscriptions yet" />
          ) : (
            <div className="table-wrap">
              <table className="table">
                <thead>
                  <tr>
                    <th className="idx">#</th>
                    <th className="wide">Plan</th>
                    <th className="fit">Starts</th>
                    <th className="fit">Ends</th>
                    <th className="fit">Days left</th>
                    <th className="num">Final</th>
                    <th className="num">Paid</th>
                    <th className="num">Outstanding</th>
                    <th className="fit">Status</th>
                  </tr>
                </thead>
                <tbody>
                  {history.subscriptions.map((s, i) => (
                    <tr key={s.id}>
                      <td className="idx">{i + 1}</td>
                      <td>
                        <div className="cell-main">{s.planName}</div>
                        <div className="cell-sub">{s.subscriptionCode}</div>
                      </td>
                      <td className="fit"><span className="cell-icon"><IconCalendar size={13} />{date(s.startDate)}</span></td>
                      <td className="fit"><span className="cell-icon"><IconCalendar size={13} />{date(s.endDate)}</span></td>
                      <td className="fit"><DaysLeft days={s.daysRemaining} /></td>
                      <td className="num">{money(s.finalAmount, currency)}</td>
                      <td className="num">{money(s.paidAmount, currency)}</td>
                      <td className="num">{money(s.outstandingAmount, currency)}</td>
                      <td className="fit"><StatusPill status={s.statusText} /></td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )
        )}

        {tab === 'Payments' && (
          history.payments.length === 0 ? (
            <EmptyState icon={<IconCard size={40} />} title="No payments recorded" />
          ) : (
            <div className="table-wrap">
              <table className="table">
                <thead>
                  <tr>
                    <th className="idx">#</th>
                    <th className="fit">Receipt</th>
                    <th className="fit">Date</th>
                    <th className="wide">Plan</th>
                    <th className="fit">Method</th>
                    <th className="num">Amount</th>
                    <th className="fit">Status</th>
                    <th className="actions">Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {history.payments.map((p, i) => (
                    <tr key={p.id}>
                      <td className="idx">{i + 1}</td>
                      <td className="cell-main fit">{p.receiptNumber}</td>
                      <td className="fit"><span className="cell-icon"><IconCalendar size={13} />{date(p.paymentDate)}</span></td>
                      <td>{orDash(p.planName)}</td>
                      <td className="fit"><Pill tone="neutral">{p.paymentMethodName}</Pill></td>
                      <td className="num">{money(p.finalAmount, currency)}</td>
                      <td className="fit"><StatusPill status={p.statusText} /></td>
                      <td className="actions">
                        <button
                          className="btn btn-outline btn-sm"
                          disabled={receiptBusy === p.id}
                          onClick={() => downloadReceipt(p.id)}
                        >
                          <IconDownload size={13} /> {receiptBusy === p.id ? 'Preparing...' : 'Receipt'}
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )
        )}

        {tab === 'Attendance' && (
          history.attendance.length === 0 ? (
            <EmptyState icon={<IconCalendar size={40} />} title="No visits recorded" />
          ) : (
            <div className="table-wrap">
              <table className="table">
                <thead>
                  <tr>
                    <th className="idx">#</th>
                    <th className="fit">Date</th>
                    <th className="fit">Checked in</th>
                    <th className="fit">Checked out</th>
                    <th className="num">Duration</th>
                    <th className="fit">Method</th>
                    <th className="fit">Status</th>
                  </tr>
                </thead>
                <tbody>
                  {history.attendance.map((a, i) => (
                    <tr key={a.id}>
                      <td className="idx">{i + 1}</td>
                      <td className="fit"><span className="cell-icon"><IconCalendar size={13} />{date(a.attendanceDate)}</span></td>
                      <td className="fit">{dateTime(a.checkInTime)}</td>
                      <td className="fit">{a.checkOutTime ? dateTime(a.checkOutTime) : orDash(null)}</td>
                      <td className="num">
                        {a.durationMinutes === null || a.durationMinutes === undefined
                          ? orDash(null)
                          : `${a.durationMinutes} min`}
                      </td>
                      <td className="fit"><Pill tone="neutral">{a.checkInMethod}</Pill></td>
                      <td className="fit"><StatusPill status={a.statusText} /></td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )
        )}

        {tab === 'Workouts' && (
          history.workoutSessions.length === 0 ? (
            <EmptyState icon={<IconDumbbell size={40} />} title="No workout sessions logged" />
          ) : (
            <div className="table-wrap">
              <table className="table">
                <thead>
                  <tr>
                    <th className="idx">#</th>
                    <th className="fit">Date</th>
                    <th className="wide">Plan</th>
                    <th>Trainer</th>
                    <th className="num">Duration</th>
                    <th className="num">Exercises</th>
                    <th className="num">Volume</th>
                    <th className="num">Calories</th>
                  </tr>
                </thead>
                <tbody>
                  {history.workoutSessions.map((w, i) => (
                    <tr key={w.id}>
                      <td className="idx">{i + 1}</td>
                      <td className="fit"><span className="cell-icon"><IconCalendar size={13} />{date(w.sessionDate)}</span></td>
                      <td>{orDash(w.workoutPlanName)}</td>
                      <td>{orDash(w.trainerName)}</td>
                      <td className="num">
                        {w.durationMinutes === null || w.durationMinutes === undefined
                          ? orDash(null)
                          : `${w.durationMinutes} min`}
                      </td>
                      <td className="num">{w.exerciseCount}</td>
                      <td className="num">{w.totalVolumeKg} kg</td>
                      <td className="num">{orDash(w.caloriesBurned)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )
        )}

        {tab === 'Measurements' && (
          history.measurements.length === 0 ? (
            <EmptyState icon={<IconCheckSquare size={40} />} title="No measurements recorded" />
          ) : (
            <div className="table-wrap">
              <table className="table">
                <thead>
                  <tr>
                    <th className="idx">#</th>
                    <th className="fit">Measured on</th>
                    <th className="num">Weight</th>
                    <th className="num">Height</th>
                    <th className="num">Body fat</th>
                    <th className="num">Muscle</th>
                    <th className="num">Chest</th>
                    <th className="num">Waist</th>
                    <th className="num">BMI</th>
                    <th className="wide">Recorded by</th>
                  </tr>
                </thead>
                <tbody>
                  {history.measurements.map((m, i) => (
                    <tr key={m.id}>
                      <td className="idx">{i + 1}</td>
                      <td className="fit"><span className="cell-icon"><IconCalendar size={13} />{date(m.measuredOn)}</span></td>
                      <td className="num">{m.weightKg ? `${m.weightKg} kg` : orDash(null)}</td>
                      <td className="num">{m.heightCm ? `${m.heightCm} cm` : orDash(null)}</td>
                      <td className="num">{m.bodyFatPercent ? `${m.bodyFatPercent} %` : orDash(null)}</td>
                      <td className="num">{m.muscleMassKg ? `${m.muscleMassKg} kg` : orDash(null)}</td>
                      <td className="num">{m.chestCm ? `${m.chestCm} cm` : orDash(null)}</td>
                      <td className="num">{m.waistCm ? `${m.waistCm} cm` : orDash(null)}</td>
                      <td className="num">{orDash(m.bmi)}</td>
                      <td>{orDash(m.recordedByTrainerName)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )
        )}

        {tab === 'Documents' && (
          history.documents.length === 0 ? (
            <EmptyState icon={<IconFile size={40} />} title="No documents uploaded" />
          ) : (
            <div className="table-wrap">
              <table className="table">
                <thead>
                  <tr>
                    <th className="idx">#</th>
                    <th className="wide">File</th>
                    <th className="fit">Type</th>
                    <th className="num">Size</th>
                    <th className="fit">Uploaded</th>
                    <th>Notes</th>
                    <th className="actions">Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {history.documents.map((d, i) => (
                    <tr key={d.id}>
                      <td className="idx">{i + 1}</td>
                      <td>
                        <div className="cell-icon">
                          <IconFile size={14} />
                          <span className="cell-main">{d.fileName}</span>
                        </div>
                      </td>
                      <td className="fit"><Pill tone="neutral">{d.documentType}</Pill></td>
                      <td className="num">{formatSize(d.fileSizeBytes)}</td>
                      <td className="fit"><span className="cell-icon"><IconCalendar size={13} />{date(d.createdAt)}</span></td>
                      <td>{orDash(d.notes)}</td>
                      <td className="actions">
                        <button
                          className="btn btn-outline btn-sm"
                          disabled={documentBusy === d.id}
                          onClick={() => downloadDocument(d.id, d.fileName)}
                        >
                          <IconDownload size={13} /> {documentBusy === d.id ? 'Preparing...' : 'Download'}
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )
        )}

        {tab === 'Communications' && (
          commLoading ? (
            <Loading message="Loading messages..." />
          ) : commError ? (
            <div style={{ padding: 'var(--sp-5)' }}><ErrorAlert error={commError} /></div>
          ) : !commLogs || commLogs.length === 0 ? (
            <EmptyState
              icon={<IconMessage size={40} />}
              title="No messages yet"
              message="Receipts and wishes will appear here."
            />
          ) : (
            <div className="page-card-body">
              <ul className="comm-timeline">
                {commLogs.map((log) => (
                  <li key={log.id}>
                    <span className="comm-when"><IconCalendar size={13} />{date(log.sentOnDate)}</span>
                    <KindPill kind={log.kind} />
                    <span className="comm-what">{log.detail || <span className="muted">&mdash;</span>}</span>
                    <ChannelTicks emailSent={log.emailSent} whatsAppSent={log.whatsAppSent} />
                  </li>
                ))}
              </ul>
            </div>
          )
        )}
      </PageCard>

      {statusOpen && (
        <Modal
          title="Change member status"
          icon={<IconCheckSquare size={18} />}
          onClose={() => setStatusOpen(false)}
          width={460}
          footer={
            <>
              <button className="btn btn-outline" onClick={() => setStatusOpen(false)} disabled={statusBusy}>
                Cancel
              </button>
              <button className="btn btn-dark" onClick={applyStatus} disabled={statusBusy}>
                {statusBusy ? 'Saving...' : 'Apply'}
              </button>
            </>
          }
        >
          <Field label="Status" required>
            <select className="select" value={nextStatus} onChange={(e) => setNextStatus(e.target.value)}>
              {MEMBER_STATUS_OPTIONS.map((s) => (
                <option key={s} value={s}>{memberStatusLabel(s)}</option>
              ))}
            </select>
          </Field>
        </Modal>
      )}
    </div>
  );
}
