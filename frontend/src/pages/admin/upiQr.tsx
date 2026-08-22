/**
 * Shared UPI collection UI: the "Collect by UPI" modal.
 *
 * Extracted from RecordPaymentPage so the subscription screens can offer the same QR without a
 * circular import — RecordPaymentPage already imports the member picker and quote panel from
 * SubscriptionsPage, so the dependency could not simply run the other way. The QR encoder and
 * its renderer moved out again to `@/lib/qr` and `@/components/QrCode` once the profile card
 * needed the same symbol.
 */
import { useState } from 'react';
import type { UpiPaymentIntentDto } from '@/api/endpoints/payments';
import { Alert, Field, Modal } from '@/components/ui';
import { QrCode } from '@/components/QrCode';
import { IconCard, IconCheck, IconQr, IconShield, IconWarning } from '@/components/icons';
import { encodeQr } from '@/lib/qr';
import { money } from '@/lib/format';

/* ============================================================================
 * The page
 * ========================================================================== */


export function UpiModal({ intent, onClose }: { intent: UpiPaymentIntentDto; onClose: () => void }) {
  const [copied, setCopied] = useState('');

  async function copy(label: string, value: string) {
    try {
      await navigator.clipboard.writeText(value);
      setCopied(label);
      setTimeout(() => setCopied(''), 1500);
    } catch {
      setCopied('');
    }
  }

  const link = intent.upiDeepLink ?? '';
  const drawable = link ? encodeQr(link) !== null : false;
  // Gateway ON means the payment is settled from the provider's callback: every manual
  // affordance disappears, because anything staff key in by hand would settle it twice.
  const manual = intent.requiresManualVerification;

  return (
    <Modal
      title="Collect by UPI"
      icon={<IconQr size={18} />}
      onClose={onClose}
      width={780}
      footer={<button className="btn btn-dark" onClick={onClose}>Done</button>}
    >
      <div className="stack">
        {/*
          Driven entirely by the server's flag. Told flatly that nothing is verified, staff learn
          to confirm by hand and keep doing it after a gateway is wired up — at which point the
          same payment is settled twice over. So the modal says only what is true right now.
        */}
        {manual ? (
          <Alert tone="warning">
            <IconWarning size={18} />
            <div>
              <div style={{ fontWeight: 600 }}>This QR gives no automatic verification.</div>
              <div>
                No payment gateway is configured, so nothing tells this app that the money arrived.
                Staff must read the transaction reference off the payer&apos;s app and confirm the
                payment before it counts as settled.
              </div>
            </div>
          </Alert>
        ) : (
          /*
           * The intent carries no payment id — no payment row exists until the gateway's
           * callback creates one — so there is nothing to poll here. A static banner states
           * the whole truth: the gateway records the payment on its own.
           */
          <Alert tone="info">
            <IconShield size={18} />
            <div>
              <div style={{ fontWeight: 600 }}>Nothing to confirm by hand.</div>
              <div>
                Payments on this QR are verified and recorded automatically by the gateway —
                this window can be closed.
              </div>
            </div>
          </Alert>
        )}

        <div className="upi-panel">
          {link && drawable && (
            <div className="qr-box">
              <QrCode text={link} title={`UPI payment QR for ${intent.memberName}`} />
            </div>
          )}

          <div className="grow stack" style={{ minWidth: 280 }}>
            <dl className="kv">
              <dt>Member</dt><dd>{intent.memberName}</dd>
              <dt>Amount</dt><dd>{money(intent.amount, intent.currencySymbol)}</dd>
              {intent.payeeName ? <><dt>Payee</dt><dd>{intent.payeeName}</dd></> : null}
              {intent.upiId ? <><dt>UPI ID</dt><dd>{intent.upiId}</dd></> : null}
              <dt>Reference</dt><dd>{intent.paymentReference}</dd>
            </dl>

            {/* Reconciliation copy-outs are manual-mode affordances: with a gateway on, the
                reference above is context, not a work item, so nothing invites keying it back. */}
            {manual && (
              <Field label="Payment reference — quote this when reconciling">
                <div className="copy-block">
                  <code>{intent.paymentReference}</code>
                  <button className="btn btn-outline btn-sm" onClick={() => copy('reference', intent.paymentReference)}>
                    {copied === 'reference' ? <><IconCheck size={13} /> Copied</> : 'Copy'}
                  </button>
                </div>
              </Field>
            )}

            {link ? (
              manual && (
                <Field label="UPI deep link" help={drawable ? 'The QR above encodes this exact link.' : 'Too long to render as a QR here — send the link instead.'}>
                  <div className="copy-block">
                    <code>{link}</code>
                    <button className="btn btn-outline btn-sm" onClick={() => copy('link', link)}>
                      {copied === 'link' ? <><IconCheck size={13} /> Copied</> : 'Copy'}
                    </button>
                  </div>
                </Field>
              )
            ) : (
              <Alert tone="info">
                No UPI deep link came back — set the gym's UPI ID under Settings before collecting by UPI.
              </Alert>
            )}
          </div>
        </div>

        {/* Server instructions describe the manual reconciliation steps; with a gateway on
            they would only re-teach the double-settling habit the banner just ruled out. */}
        {manual && intent.instructions ? (
          <div className="form-section">
            <div className="form-section-title"><IconCard size={16} /> Instructions from the server</div>
            <div style={{ marginTop: 8, whiteSpace: 'pre-wrap' }}>{intent.instructions}</div>
          </div>
        ) : null}

        {manual && (
          <div className="form-note">
            This screen never asks for a card number, CVV or UPI PIN — only the transaction
            reference and, optionally, the payer's VPA.
          </div>
        )}
      </div>
    </Modal>
  );
}
