/**
 * The member's current membership: plan, dates, money and — where the account is allowed to
 * read it — the full subscription history.
 */

import { useEffect, useState } from 'react';
import {
  EmptyState, ErrorAlert, Loading, PageCard, PageCardHeader, Pill, StatusPill,
} from '@/components/ui';
import { IconCalendar, IconClock, IconCrown, IconMoney, IconUser } from '@/components/icons';
import { memberApi, optional } from '@/api/endpoints/member';
import type { MemberDashboardDto } from '@/api/endpoints/member';
import type { SubscriptionDto } from '@/api/types';
import { useAuth } from '@/auth/AuthContext';
import { date, money } from '@/lib/format';
import './member.css';

/** Whole days covered by the subscription, inclusive of both end points. */
function durationDays(subscription: SubscriptionDto): number {
  const start = new Date(subscription.startDate).getTime();
  const end = new Date(subscription.endDate).getTime();
  if (Number.isNaN(start) || Number.isNaN(end)) return 0;
  return Math.max(0, Math.round((end - start) / 86_400_000) + 1);
}

function Row({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div className="m-detail-row">
      <div className="label">{label}</div>
      <div className="value">{value === null || value === undefined || value === '' ? '—' : value}</div>
    </div>
  );
}

export default function MyMembershipPage() {
  const { user, currency } = useAuth();
  const memberId = user?.memberId ?? null;

  const [data, setData] = useState<MemberDashboardDto | null>(null);
  const [history, setHistory] = useState<SubscriptionDto[] | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<unknown>(null);

  useEffect(() => {
    if (memberId === null) { setLoading(false); return; }
    const ctrl = new AbortController();
    setLoading(true);
    setError(null);

    memberApi.dashboard(memberId, ctrl.signal)
      .then(async (result) => {
        if (ctrl.signal.aborted) return;
        setData(result);
        const subscriptions = await optional(() => memberApi.subscriptions(memberId, ctrl.signal));
        if (!ctrl.signal.aborted) setHistory(subscriptions);
      })
      .catch((e) => { if (!ctrl.signal.aborted) setError(e); })
      .finally(() => { if (!ctrl.signal.aborted) setLoading(false); });

    return () => ctrl.abort();
  }, [memberId]);

  if (memberId === null) {
    return (
      <div className="page">
        <PageCard>
          <EmptyState icon={<IconUser size={34} />} title="No member record linked" message="This login is not attached to a member profile." />
        </PageCard>
      </div>
    );
  }

  if (loading) return <div className="page"><PageCard><Loading message="Loading your membership…" /></PageCard></div>;
  if (error) return <div className="page"><PageCard><div style={{ padding: 20 }}><ErrorAlert error={error} /></div></PageCard></div>;

  const subscription = data?.activeSubscription ?? null;
  const daysRemaining = data?.daysRemaining ?? 0;
  const outstanding = data?.outstandingAmount ?? 0;
  const active = (subscription?.statusText ?? '').toLowerCase() === 'active';

  return (
    <div className="page">
      <div className="member-tiles" style={{ marginTop: 0 }}>
        <div className="m-tile m-tile-blue">
          <div className="m-tile-icon"><IconCrown size={22} /></div>
          <div className="grow">
            <div className="m-tile-title">Plan</div>
            <div className="m-tile-value" style={{ fontSize: 21 }}>{subscription?.planName ?? 'No active plan'}</div>
            <div className="m-tile-caption">{subscription ? subscription.subscriptionCode : 'Speak to the front desk to join a plan'}</div>
          </div>
        </div>
        <div className="m-tile m-tile-green">
          <div className="m-tile-icon"><IconClock size={22} /></div>
          <div className="grow">
            <div className="m-tile-title">Days Remaining</div>
            <div className="m-tile-value">
              {daysRemaining} {active ? <Pill tone="success">Active</Pill> : null}
            </div>
            <div className="m-tile-caption">{subscription ? `Ends ${date(subscription.endDate)}` : '—'}</div>
          </div>
        </div>
        <div className="m-tile m-tile-orange">
          <div className="m-tile-icon"><IconMoney size={22} /></div>
          <div className="grow">
            <div className="m-tile-title">Remaining Amount</div>
            <div className="m-tile-value">{money(outstanding, currency)}</div>
            <div className="m-tile-caption">{outstanding > 0 ? 'Payable at the front desk' : 'Nothing outstanding'}</div>
          </div>
        </div>
      </div>

      <PageCard>
        <PageCardHeader
          icon={<IconCrown size={20} />}
          title="My Membership"
          subtitle="The plan you are on and how it is paid for."
        />
        {!subscription ? (
          <EmptyState
            icon={<IconCrown size={34} />}
            title="No active membership"
            message="You do not have a running subscription at the moment. The gym team can set one up for you."
          />
        ) : (
          <>
            <div className="caption-bar"><IconCrown size={16} /> Membership Details</div>
            <div className="m-detail-rows">
              <Row label="Plan name" value={subscription.planName} />
              <Row label="Duration" value={`${durationDays(subscription)} days`} />
              <Row label="Plan fee" value={money(subscription.finalAmount, currency)} />
              <Row label="Total paid" value={money(subscription.paidAmount, currency)} />
              <Row label="Remaining amount" value={money(subscription.outstandingAmount, currency)} />
              <Row label="Start date" value={date(subscription.startDate)} />
              <Row label="End date" value={date(subscription.endDate)} />
              <Row
                label="Days remaining"
                value={<>{subscription.daysRemaining} {subscription.isExpiringSoon ? <Pill tone="warning">Expiring soon</Pill> : null}</>}
              />
              <Row label="Status" value={<StatusPill status={subscription.statusText} />} />
            </div>
          </>
        )}
      </PageCard>

      {history && history.length > 0 && (
        <PageCard>
          <PageCardHeader
            icon={<IconCalendar size={20} />}
            title="Membership History"
            subtitle="Every plan you have held at this gym."
          />
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th className="idx">#</th>
                  <th className="wide">Plan</th>
                  <th className="fit">Start date</th>
                  <th className="fit">End date</th>
                  <th className="fit">Final amount</th>
                  <th className="fit">Paid</th>
                  <th className="fit">Outstanding</th>
                  <th className="fit">Status</th>
                </tr>
              </thead>
              <tbody>
                {history.map((row, index) => (
                  <tr key={row.id}>
                    <td className="idx">{index + 1}</td>
                    <td>
                      <div className="cell-main">{row.planName}</div>
                      <div className="cell-sub">{row.subscriptionCode}</div>
                    </td>
                    <td className="fit"><span className="cell-icon"><IconCalendar size={14} />{date(row.startDate)}</span></td>
                    <td className="fit"><span className="cell-icon"><IconCalendar size={14} />{date(row.endDate)}</span></td>
                    <td className="fit"><span className="cell-icon"><IconMoney size={14} />{money(row.finalAmount, currency)}</span></td>
                    <td className="fit">{money(row.paidAmount, currency)}</td>
                    <td className="fit">{money(row.outstandingAmount, currency)}</td>
                    <td className="fit"><StatusPill status={row.statusText} /></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </PageCard>
      )}
    </div>
  );
}
