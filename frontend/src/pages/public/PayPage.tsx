/**
 * The page a texted pay link opens — on the member's phone, with no session.
 *
 * Rebuilt around a gateway-driven flow: one QR (a hosted image or a locally drawn payload),
 * one PAY NOW intent, and a live status strip that polls the server every few seconds until
 * the gateway settles the payment. Nothing here is manual — no UTR field, no "I have paid" —
 * because the status comes only from the backend's verified callback. The "pay manually to
 * UPI ID" row survives solely as the legacy fallback when no gateway is configured.
 */

import { useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { paymentsApi } from '@/api/endpoints/payments';
import type {
  PaymentRequestStatusDto, PublicPaymentRequestDto, PublicPaymentStatus,
} from '@/api/endpoints/payments';
import { QrCode } from '@/components/QrCode';
import { encodeQr } from '@/lib/qr';
import { date, dateTime } from '@/lib/format';
import { IconCheck, IconClock, IconDumbbell, IconWarning } from '@/components/icons';
import './public.css';

const POLL_MS = 3000;

const STATUS_LINE: Record<PublicPaymentStatus, string> = {
  pending: 'Waiting for payment…',
  paid: 'Payment successful',
  failed: 'Payment failed',
  expired: 'Payment request expired',
};

export default function PayPage() {
  const { token = '' } = useParams();
  const [data, setData] = useState<PublicPaymentRequestDto | null>(null);
  const [failed, setFailed] = useState(false); // the request fetch itself failed (bad link)
  const [status, setStatus] = useState<PublicPaymentStatus | null>(null);
  const [statusInfo, setStatusInfo] = useState<PaymentRequestStatusDto | null>(null);
  const [copied, setCopied] = useState(false);
  const [attempt, setAttempt] = useState(0); // bumped by "Try again" to reload the request

  /* Load the request — and reload it after a failed payment's "Try again". */
  useEffect(() => {
    let cancelled = false;
    setData(null);
    setFailed(false);
    setStatus(null);
    setStatusInfo(null);
    paymentsApi.paymentRequest(token)
      .then((result) => {
        if (cancelled) return;
        setData(result);
        // A stale-but-pending request is expired for our purposes; terminal states win as-is.
        setStatus(result.status === 'pending' && result.expired ? 'expired' : result.status);
      })
      .catch(() => { if (!cancelled) setFailed(true); });
    return () => { cancelled = true; };
  }, [token, attempt]);

  /*
   * Poll the status every 3s while pending — but only while the tab is actually visible, so a
   * backgrounded phone browser is not hammered. Becoming visible again polls immediately rather
   * than waiting out the interval. A terminal status flips `status`, which tears this effect
   * down; unmount clears the interval and the visibility listener.
   */
  useEffect(() => {
    if (status !== 'pending') return;
    let disposed = false;
    let inFlight = false;

    const tick = async () => {
      if (disposed || inFlight || document.visibilityState !== 'visible') return;
      inFlight = true;
      try {
        const result = await paymentsApi.paymentRequestStatus(token);
        if (disposed) return;
        setStatusInfo(result);
        if (result.status !== 'pending') setStatus(result.status);
      } catch {
        /* transient — keep polling */
      } finally {
        inFlight = false;
      }
    };

    const interval = window.setInterval(() => { void tick(); }, POLL_MS);
    const onVisibility = () => { void tick(); };
    document.addEventListener('visibilitychange', onVisibility);
    return () => {
      disposed = true;
      window.clearInterval(interval);
      document.removeEventListener('visibilitychange', onVisibility);
    };
  }, [status, token]);

  /* A request already paid on first load still needs the receipt details for the paid screen. */
  useEffect(() => {
    if (status !== 'paid' || statusInfo) return;
    let cancelled = false;
    paymentsApi.paymentRequestStatus(token)
      .then((result) => { if (!cancelled) setStatusInfo(result); })
      .catch(() => { /* the paid screen renders with em dashes */ });
    return () => { cancelled = true; };
  }, [status, statusInfo, token]);

  const copyUpiId = async () => {
    if (!data) return;
    try {
      await navigator.clipboard.writeText(data.upiId);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch { /* clipboard can be unavailable; the id stays visible to copy by hand */ }
  };

  const shell = (content: React.ReactNode) => (
    <div className="pub-pay-shell">
      <div className="pub-pay-card">{content}</div>
      <div className="pub-pay-foot">Powered by Gym Management System</div>
    </div>
  );

  if (failed) {
    return shell(
      <div className="pub-pay-state">
        <span className="pub-pay-state-icon"><IconClock size={30} /></span>
        <h1>This payment link isn't valid</h1>
        <p>It may have been mistyped or withdrawn. Ask the gym to send a fresh one,
        or pay at the front desk.</p>
        <Link className="btn pub-btn-on-dark" to="/">Go to the gym site</Link>
      </div>,
    );
  }

  if (!data || !status) {
    return shell(<div className="pub-pay-state"><p>Loading your payment…</p></div>);
  }

  const amount = `${data.currencySymbol}${data.amount.toLocaleString('en-IN', { minimumFractionDigits: 2 })}`;
  const purpose = data.note || (data.planName ? `${data.planName} membership` : 'Membership payment');

  /* ----------------------------------------------------- terminal screens */

  if (status === 'expired') {
    return shell(
      <div className="pub-pay-state">
        <span className="pub-pay-state-icon"><IconClock size={30} /></span>
        <h1>This payment request has expired</h1>
        <p>For your safety, payment links stop working after a while.
        Ask {data.gymName} to send a fresh link.</p>
      </div>,
    );
  }

  if (status === 'paid') {
    return shell(
      <div className="pub-pay-state">
        <span className="pub-pay-state-icon pub-pay-state-icon--ok"><IconCheck size={30} /></span>
        <h1 className="pub-pay-done-title">Payment successful</h1>
        <div className="pub-pay-done-amount">{amount}</div>
        <p>{data.gymName} · {purpose}</p>
        <div className="pub-pay-rows">
          <div className="pub-pay-row">
            <span>Payment ID</span><span>{data.paymentCode}</span>
          </div>
          <div className="pub-pay-row">
            <span>Transaction ID</span><span>{statusInfo?.gatewayTransactionId ?? '—'}</span>
          </div>
          <div className="pub-pay-row">
            <span>Paid at</span><span>{dateTime(statusInfo?.paidAtUtc)}</span>
          </div>
          {statusInfo?.validUntil ? (
            <div className="pub-pay-row">
              <span>Membership valid until</span><span>{date(statusInfo.validUntil)}</span>
            </div>
          ) : null}
        </div>
        <Link className="btn pub-btn-on-dark" to="/member/membership">Go to my membership</Link>
        <p className="pub-pay-close">You can close this page — your receipt has been emailed.</p>
      </div>,
    );
  }

  if (status === 'failed') {
    return shell(
      <div className="pub-pay-state">
        <span className="pub-pay-state-icon pub-pay-state-icon--fail"><IconWarning size={30} /></span>
        <h1>Payment failed</h1>
        <p>Your payment could not be completed. Your membership has NOT been renewed.</p>
        <button type="button" className="btn pub-btn-on-dark" onClick={() => setAttempt((a) => a + 1)}>
          Try again
        </button>
      </div>,
    );
  }

  /* ------------------------------------------------------- pending screen */

  // `image:` prefix → hosted QR image; any other value → raw payload; null → the UPI deep link.
  const qrImage = data.qrData?.startsWith('image:') ? data.qrData.slice('image:'.length) : null;
  const qrPayload = qrImage ? null : (data.qrData ?? data.upiDeepLink);
  const qrDrawable = qrPayload ? encodeQr(qrPayload) !== null : false;
  const showSimulator = data.gatewayEnabled && !data.paymentUrl
    && (data.orderId ?? '').startsWith('order_sim');

  return shell(
    <>
      <header className="pub-pay-head">
        <span className="pub-pay-mark"><IconDumbbell size={20} /></span>
        <div>
          <div className="pub-pay-gym">{data.gymName}</div>
          <div className="pub-pay-sub">Payment request</div>
        </div>
      </header>

      <div className="pub-pay-amount">
        <span className="pub-pay-amount-label">Hi {data.memberFirstName}, please pay</span>
        <span className="pub-pay-amount-value">{amount}</span>
        <span className="pub-pay-amount-note">{purpose}</span>
        <span className="pub-pay-code">Payment ID {data.paymentCode}</span>
      </div>

      <div className="pub-pay-qrpanel">
        <div className="pub-pay-qrhead">Scan QR to pay</div>
        {qrImage ? (
          <div className="pub-pay-qrbox">
            <img src={qrImage} alt={`Payment QR for ${data.gymName}`} />
          </div>
        ) : qrDrawable && qrPayload ? (
          <div className="pub-pay-qrbox">
            <QrCode text={qrPayload} title={`Payment QR for ${data.gymName}`} />
          </div>
        ) : null}
        <p className="pub-pay-qrline">
          Scan with any UPI app — Google Pay, PhonePe, Paytm, BHIM and every other UPI app work.
        </p>
      </div>

      <div className="pub-pay-or"><span>or</span></div>

      {data.paymentUrl ? (
        <button
          type="button"
          className="pub-pay-now"
          onClick={() => { window.location.href = data.paymentUrl as string; }}
        >
          Pay now
        </button>
      ) : (
        <a className="pub-pay-now" href={data.upiDeepLink}>Pay now</a>
      )}

      <div className={`pub-pay-status pub-pay-status--${status}`} role="status">
        <span className="pub-pay-status-dot" />
        {STATUS_LINE[status]}
      </div>

      {!data.gatewayEnabled && (
        <div className="pub-pay-vpa">
          <div>
            <span className="pub-pay-vpa-label">Or pay manually to UPI ID</span>
            <span className="pub-pay-vpa-id">{data.upiId}</span>
          </div>
          <button type="button" className="btn btn-outline btn-sm" onClick={copyUpiId}>
            {copied ? <><IconCheck size={14} /> Copied</> : 'Copy'}
          </button>
        </div>
      )}

      {showSimulator && (
        <div className="pub-pay-dev">
          Simulator checkout — <Link to={`/pay/${token}/simulate`}>open the simulator</Link>
        </div>
      )}
    </>,
  );
}
