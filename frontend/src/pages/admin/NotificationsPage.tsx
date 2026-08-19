import { useEffect, useState } from 'react';
import { useAuth } from '@/auth/AuthContext';
import {
  Alert, ConfirmModal, EmptyState, ErrorAlert, FilterField, FilterMenu, FilterStrip, Loading,
  PageCard, PageCardHeader, Pager, Pill, SearchField,
} from '@/components/ui';
import {
  IconBell, IconCheck, IconRefresh,
} from '@/components/icons';
import {
  NOTIFICATION_TYPES, SEVERITY_OPTIONS, deleteNotification, generateSystemAlerts,
  listNotifications, markAllNotificationsRead, markNotificationsRead,
  notificationTypeLabel, severityKey, severityLabel,
} from '@/api/endpoints/notifications';
import type { NotificationDto, PagedResult } from '@/api/types';
import { NotificationSeverity } from '@/api/types';
import { dateTime, relativeTime } from '@/lib/format';
import './ops.css';

interface Filters {
  search: string;
  severity: NotificationSeverity | '';
  isRead: boolean | '';
  type: number | '';
}

const EMPTY_FILTERS: Filters = { search: '', severity: '', isRead: '', type: '' };

const SEVERITY_TONE: Record<string, 'info' | 'success' | 'warning' | 'danger'> = {
  info: 'info',
  success: 'success',
  warning: 'warning',
  critical: 'danger',
};

export default function NotificationsPage() {
  const { can } = useAuth();
  const manage = can('notifications.manage');

  const [draft, setDraft] = useState<Filters>(EMPTY_FILTERS);
  const [applied, setApplied] = useState<Filters>(EMPTY_FILTERS);
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [reloadKey, setReloadKey] = useState(0);

  const [data, setData] = useState<PagedResult<NotificationDto> | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<unknown>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [pendingDelete, setPendingDelete] = useState<NotificationDto | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    (async () => {
      setLoading(true);
      try {
        const result = await listNotifications(
          {
            search: applied.search || undefined,
            severity: applied.severity,
            isRead: applied.isRead,
            type: applied.type,
            onlyMine: false,
            pageNumber,
            pageSize,
          },
          controller.signal,
        );
        if (controller.signal.aborted) return;
        setData(result);
        setError(null);
      } catch (err) {
        if (controller.signal.aborted) return;
        setError(err);
        setData(null);
      } finally {
        if (!controller.signal.aborted) setLoading(false);
      }
    })();
    return () => controller.abort();
  }, [applied, pageNumber, pageSize, reloadKey]);


  const refresh = () => setReloadKey((k) => k + 1);

  const markRead = async (id: number) => {
    setBusy(true);
    try {
      await markNotificationsRead([id]);
      refresh();
    } catch (err) {
      setError(err);
    } finally {
      setBusy(false);
    }
  };

  const markAll = async () => {
    setBusy(true);
    try {
      await markAllNotificationsRead();
      setNotice('All notifications marked as read.');
      refresh();
    } catch (err) {
      setError(err);
    } finally {
      setBusy(false);
    }
  };

  const generate = async () => {
    setBusy(true);
    setNotice(null);
    try {
      const created = await generateSystemAlerts();
      setNotice(
        typeof created === 'number'
          ? `${created} alert${created === 1 ? '' : 's'} generated.`
          : 'System alerts generated.',
      );
      refresh();
    } catch (err) {
      setError(err);
    } finally {
      setBusy(false);
    }
  };

  const confirmDelete = async () => {
    if (!pendingDelete) return;
    setBusy(true);
    try {
      await deleteNotification(pendingDelete.id);
      setPendingDelete(null);
      refresh();
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
  const activeFilterCount = (['severity', 'type', 'isRead'] as const)
    .filter((key) => applied[key] !== EMPTY_FILTERS[key]).length;

  const items = data?.items ?? [];

  return (
    <div className="page">

      <PageCard>
        <PageCardHeader
          icon={<IconBell size={20} />}
          title="Notifications"
          subtitle="Expiry warnings, payment alerts and system messages for your team."
          actions={
            <>
              <FilterMenu
                activeCount={activeFilterCount}
                onApply={applyFilters}
                onReset={resetFilters}
              >
                <FilterField label="Severity">
                  <select
                    className="select"
                    value={draft.severity === '' ? '' : String(draft.severity)}
                    onChange={(e) => setDraft({
                      ...draft,
                      severity: e.target.value === '' ? '' : (Number(e.target.value) as NotificationSeverity),
                    })}
                  >
                    <option value="">All severities</option>
                    {SEVERITY_OPTIONS.map((s) => <option key={s.value} value={s.value}>{s.label}</option>)}
                  </select>
                </FilterField>

                <FilterField label="Type">
                  <select
                    className="select"
                    value={draft.type === '' ? '' : String(draft.type)}
                    onChange={(e) => setDraft({ ...draft, type: e.target.value === '' ? '' : Number(e.target.value) })}
                  >
                    <option value="">All types</option>
                    {NOTIFICATION_TYPES.map((t) => <option key={t.value} value={t.value}>{t.label}</option>)}
                  </select>
                </FilterField>

                <FilterField label="Read state">
                  <select
                    className="select"
                    value={draft.isRead === '' ? '' : String(draft.isRead)}
                    onChange={(e) => setDraft({
                      ...draft,
                      isRead: e.target.value === '' ? '' : e.target.value === 'true',
                    })}
                  >
                    <option value="">All</option>
                    <option value="false">Unread</option>
                    <option value="true">Read</option>
                  </select>
                </FilterField>
              </FilterMenu>

              <button className="btn btn-outline" onClick={() => void markAll()} disabled={busy}>
                <IconCheck size={15} /> Mark all read
              </button>
              {manage && (
                <button className="btn btn-dark" onClick={() => void generate()} disabled={busy}>
                  <IconRefresh size={15} /> Generate alerts
                </button>
              )}
            </>
          }
        />

        {/* Search and Reset stay in the open, since they are the two controls reached most often. */}
        <FilterStrip>
          <SearchField
            placeholder="Title or message"
            value={draft.search}
            onChange={(value) => setDraft({ ...draft, search: value })}
            onSearch={applyFilters}
          />
        </FilterStrip>

        {manage && (
          <div className="page-card-body" style={{ paddingTop: 10, paddingBottom: 0 }}>
            <div className="form-note">
              Expiry and payment reminders are also generated automatically every day — the hour is
              configurable in the server settings.
            </div>
          </div>
        )}

        {notice ? <div className="page-card-body" style={{ paddingBottom: 0 }}><Alert tone="success">{notice}</Alert></div> : null}

        {loading && <Loading message="Loading notifications…" />}
        {!loading && Boolean(error) && <div className="page-card-body"><ErrorAlert error={error} /></div>}

        {!loading && !error && items.length === 0 && (
          <EmptyState
            icon={<IconBell size={34} />}
            title="Nothing to report"
            message="When memberships near expiry or payments fall due, alerts appear here."
            action={manage
              ? <button className="btn btn-dark" onClick={() => void generate()} disabled={busy}>Generate alerts</button>
              : undefined}
          />
        )}

        {!loading && !error && items.length > 0 && (
          <div className="ops-notice-list">
            {items.map((item) => {
              const key = severityKey(item.severity);
              return (
                <article
                  className={`ops-notice-card ops-sev-${key} ${item.isRead ? '' : 'ops-unread'}`}
                  key={item.id}
                >
                  <div className="grow">
                    <div className="ops-notice-title">
                      {!item.isRead && <span className="ops-notice-dot" aria-label="Unread" />}
                      {item.title}
                      <Pill tone={SEVERITY_TONE[key]}>{severityLabel(item.severity, item.severityText)}</Pill>
                      {notificationTypeLabel(item.type, item.typeText)
                        ? <Pill tone="neutral">{notificationTypeLabel(item.type, item.typeText)}</Pill>
                        : null}
                    </div>
                    <div className="ops-notice-message">{item.message}</div>
                    {item.memberName ? <div className="cell-sub">Member: {item.memberName}</div> : null}
                  </div>
                  <div className="ops-notice-side">
                    <span className="ops-notice-time" title={dateTime(item.createdAtUtc)}>
                      {relativeTime(item.createdAtUtc)}
                    </span>
                    <div className="row" style={{ gap: 6 }}>
                      {!item.isRead && (
                        <button className="btn btn-edit" onClick={() => void markRead(item.id)} disabled={busy}>
                          Mark read
                        </button>
                      )}
                      {manage && (
                        <button className="btn btn-del" onClick={() => setPendingDelete(item)} disabled={busy}>
                          Delete
                        </button>
                      )}
                    </div>
                  </div>
                </article>
              );
            })}
          </div>
        )}

        {!loading && !error && data && data.totalCount > 0 && (
          <Pager
            pageNumber={pageNumber}
            pageSize={pageSize}
            totalCount={data.totalCount}
            onPage={setPageNumber}
            onPageSize={(size) => { setPageSize(size); setPageNumber(1); }}
          />
        )}
      </PageCard>

      {pendingDelete && (
        <ConfirmModal
          title="Delete notification"
          message={<>Delete <strong>{pendingDelete.title}</strong>? This cannot be undone.</>}
          confirmLabel="Delete"
          busy={busy}
          onConfirm={() => void confirmDelete()}
          onClose={() => setPendingDelete(null)}
        />
      )}
    </div>
  );
}
