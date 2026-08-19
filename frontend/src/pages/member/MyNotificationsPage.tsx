/**
 * The member's own notification feed.
 *
 * Uses the shared notifications endpoints; the server scopes a Member-role account to its own
 * rows, so no member id travels with these requests. Clicking an unread card marks it read.
 */

import { useEffect, useState } from 'react';
import { EmptyState, ErrorAlert, Loading, PageCard, PageCardHeader, Pager, Pill } from '@/components/ui';
import { IconBell, IconCheck } from '@/components/icons';
import {
  getNotificationCounts, listNotifications, markAllNotificationsRead, markNotificationsRead,
  notificationTypeLabel, severityKey, severityLabel,
} from '@/api/endpoints/notifications';
import type { NotificationCountsDto } from '@/api/endpoints/notifications';
import { isUnavailable } from '@/api/endpoints/member';
import type { NotificationDto, PagedResult } from '@/api/types';
import { dateTime, relativeTime } from '@/lib/format';
import './member.css';

const SEVERITY_TONE: Record<string, 'info' | 'success' | 'warning' | 'danger'> = {
  info: 'info',
  success: 'success',
  warning: 'warning',
  critical: 'danger',
};

export default function MyNotificationsPage() {
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [reloadKey, setReloadKey] = useState(0);

  const [data, setData] = useState<PagedResult<NotificationDto> | null>(null);
  const [counts, setCounts] = useState<NotificationCountsDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    const ctrl = new AbortController();
    setLoading(true);
    setError(null);

    Promise.all([
      listNotifications({ onlyMine: false, pageNumber, pageSize }, ctrl.signal),
      getNotificationCounts(ctrl.signal),
    ])
      .then(([list, countResult]) => {
        if (ctrl.signal.aborted) return;
        setData(list);
        setCounts(countResult);
      })
      .catch((e) => { if (!ctrl.signal.aborted) setError(e); })
      .finally(() => { if (!ctrl.signal.aborted) setLoading(false); });

    return () => ctrl.abort();
  }, [pageNumber, pageSize, reloadKey]);

  const refresh = () => setReloadKey((k) => k + 1);

  /** Marks one notification read, updating the card and the unread badge in place. */
  const markRead = async (item: NotificationDto) => {
    if (item.isRead || busy) return;
    setBusy(true);
    // Optimistic: the card dims immediately; a failure reloads the truth from the server.
    setData((prev) => prev && ({
      ...prev,
      items: prev.items.map((n) => (n.id === item.id ? { ...n, isRead: true } : n)),
    }));
    setCounts((prev) => prev && ({ ...prev, unread: Math.max(0, prev.unread - 1) }));
    try {
      await markNotificationsRead([item.id]);
    } catch (e) {
      setError(e);
      refresh();
    } finally {
      setBusy(false);
    }
  };

  const markAll = async () => {
    setBusy(true);
    try {
      await markAllNotificationsRead();
      refresh();
    } catch (e) {
      setError(e);
    } finally {
      setBusy(false);
    }
  };

  const items = data?.items ?? [];
  const unread = counts?.unread ?? 0;
  const blocked = Boolean(error) && isUnavailable(error);

  return (
    <div className="page">
      <PageCard>
        <PageCardHeader
          icon={<IconBell size={20} />}
          title="My Notifications"
          subtitle="Reminders and alerts about your membership, payments and plans."
          actions={
            <>
              <span className="btn btn-dark" style={{ cursor: 'default' }}>
                <IconBell size={15} /> {unread} Unread
              </span>
              <button
                className="btn btn-outline"
                onClick={() => void markAll()}
                disabled={busy || unread === 0}
              >
                <IconCheck size={15} /> Mark all read
              </button>
            </>
          }
        />

        {loading && <Loading message="Loading your notifications…" />}

        {!loading && blocked && (
          <EmptyState
            icon={<IconBell size={34} />}
            title="Notifications are not available"
            message="Your account is not set up to read notifications yet — please ask the front desk."
          />
        )}

        {!loading && Boolean(error) && !blocked && (
          <div className="page-card-body"><ErrorAlert error={error} /></div>
        )}

        {!loading && !error && items.length === 0 && (
          <EmptyState
            icon={<IconBell size={34} />}
            title="You're all caught up"
            message="Alerts about your membership, payments and plans appear here."
          />
        )}

        {!loading && !error && items.length > 0 && (
          <div className="m-notice-list">
            {items.map((item) => {
              const key = severityKey(item.severity);
              return (
                <article
                  className={`m-notice-card m-sev-${key} ${item.isRead ? '' : 'm-unread m-notice-clickable'}`}
                  key={item.id}
                  role={item.isRead ? undefined : 'button'}
                  tabIndex={item.isRead ? undefined : 0}
                  onClick={() => void markRead(item)}
                  onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); void markRead(item); } }}
                  aria-label={item.isRead ? undefined : `Mark "${item.title}" as read`}
                >
                  <div className="grow">
                    <div className="m-notice-title">
                      {!item.isRead && <span className="m-notice-dot" aria-label="Unread" />}
                      {item.title}
                      <Pill tone={SEVERITY_TONE[key]}>{severityLabel(item.severity, item.severityText)}</Pill>
                      {notificationTypeLabel(item.type, item.typeText)
                        ? <Pill tone="neutral">{notificationTypeLabel(item.type, item.typeText)}</Pill>
                        : null}
                    </div>
                    <div className="m-notice-message">{item.message}</div>
                  </div>
                  <div className="m-notice-side">
                    <span className="m-notice-time" title={dateTime(item.createdAtUtc)}>
                      {relativeTime(item.createdAtUtc)}
                    </span>
                    {!item.isRead && (
                      <button
                        className="btn btn-edit"
                        onClick={(e) => { e.stopPropagation(); void markRead(item); }}
                        disabled={busy}
                      >
                        Mark read
                      </button>
                    )}
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
    </div>
  );
}
