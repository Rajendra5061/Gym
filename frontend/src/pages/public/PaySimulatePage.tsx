/**
 * Dev-only simulator checkout for the gateway flow.
 *
 * Reached from the dashed "Simulator checkout" box on the pay page when the order came from
 * the simulated gateway (orderId starting `order_sim`). Two buttons post the outcome to the
 * dev-only endpoint and bounce back to the pay page, whose live status strip picks the result
 * up. Outside Development the endpoint answers 404 and this page says so instead of retrying.
 *
 * ROUTE (wired in App.tsx by the orchestrator, not here): /pay/:token/simulate
 */

import { useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { paymentsApi } from '@/api/endpoints/payments';
import type { PublicPaymentRequestDto } from '@/api/endpoints/payments';
import { ApiError } from '@/api/client';
import { IconDumbbell } from '@/components/icons';
import './public.css';

export default function PaySimulatePage() {
  const { token = '' } = useParams();
  const navigate = useNavigate();
  const [data, setData] = useState<PublicPaymentRequestDto | null>(null);
  const [failed, setFailed] = useState(false);
  const [busy, setBusy] = useState<'success' | 'failure' | null>(null);
  const [disabled, setDisabled] = useState(false); // the endpoint 404s outside Development
  const [error, setError] = useState('');

  useEffect(() => {
    let cancelled = false;
    paymentsApi.paymentRequest(token)
      .then((result) => { if (!cancelled) setData(result); })
      .catch(() => { if (!cancelled) setFailed(true); });
    return () => { cancelled = true; };
  }, [token]);

  async function simulate(outcome: 'success' | 'failure') {
    setBusy(outcome);
    setError('');
    try {
      await paymentsApi.simulatePayment(token, outcome);
      navigate(`/pay/${token}`);
    } catch (err) {
      if (err instanceof ApiError && err.status === 404) setDisabled(true);
      else setError(err instanceof Error ? err.message : 'The simulator call failed.');
    } finally {
      setBusy(null);
    }
  }

  const shell = (content: React.ReactNode) => (
    <div className="pub-pay-shell">
      <div className="pub-pay-card">{content}</div>
      <div className="pub-pay-foot">Powered by Gym Management System</div>
    </div>
  );

  if (failed) {
    return shell(
      <div className="pub-pay-state">
        <h1>This payment link isn't valid</h1>
        <p>The simulator needs a live payment request. Create a fresh one and try again.</p>
      </div>,
    );
  }

  if (!data) {
    return shell(<div className="pub-pay-state"><p>Loading the payment…</p></div>);
  }

  const amount = `${data.currencySymbol}${data.amount.toLocaleString('en-IN', { minimumFractionDigits: 2 })}`;

  return shell(
    <>
      <header className="pub-pay-head">
        <span className="pub-pay-mark"><IconDumbbell size={20} /></span>
        <div>
          <div className="pub-pay-gym">{data.gymName}</div>
          <div className="pub-pay-sub">Simulator checkout — Development only</div>
        </div>
      </header>

      <div className="pub-pay-amount">
        <span className="pub-pay-amount-label">Simulating a gateway payment of</span>
        <span className="pub-pay-amount-value">{amount}</span>
        <span className="pub-pay-amount-note">Payment ID {data.paymentCode}</span>
      </div>

      {disabled ? (
        <p className="pub-pay-sim-err">Simulator is disabled outside Development.</p>
      ) : (
        <div className="pub-pay-sim-actions">
          <button
            type="button"
            className="pub-pay-sim-ok"
            disabled={busy !== null}
            onClick={() => { void simulate('success'); }}
          >
            {busy === 'success' ? 'Simulating…' : 'Simulate successful payment'}
          </button>
          <button
            type="button"
            className="pub-pay-sim-fail"
            disabled={busy !== null}
            onClick={() => { void simulate('failure'); }}
          >
            {busy === 'failure' ? 'Simulating…' : 'Simulate failed payment'}
          </button>
          {error ? <p className="pub-pay-sim-err">{error}</p> : null}
        </div>
      )}
    </>,
  );
}
