import { useEffect, useMemo, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { paymentsApi, type PaymentMethodDto, type UpiPaymentIntentDto } from '@/api/endpoints/payments';
import { plansApi } from '@/api/endpoints/plans';
import { subscriptionsApi, type QuoteRequest } from '@/api/endpoints/subscriptions';
import { useAuth } from '@/auth/AuthContext';
import {
  Alert, ErrorAlert, Field, FormSection, Loading, PageCard, PageCardHeader,
} from '@/components/ui';
import {
  IconArrowLeft, IconCrown, IconMoney, IconQr,
} from '@/components/icons';
import { PlanStatus, type Lookup, type MembershipPlanDto } from '@/api/types';
import { MemberPicker, QuotePanel, todayIso, useQuote } from './SubscriptionsPage';
import { UpiModal } from './upiQr';
import './billing.css';

export default function RecordPaymentPage() {
  const { can, currency } = useAuth();
  const navigate = useNavigate();
  const mayCollect = can('payments.collect');

  const [member, setMember] = useState<Lookup | null>(null);
  const [subscriptionId, setSubscriptionId] = useState<number | ''>('');
  const [amount, setAmount] = useState('');

  /**
   * One select covers two different things, so the value carries which kind it is:
   * `sub:<id>` settles an existing membership, `plan:<id>` prices a new sale.
   * Only a subscription is sent as `subscriptionId` — a payment attaches to a membership, not to
   * a bare plan, so choosing a plan here only seeds the amount.
   */
  const [planChoice, setPlanChoice] = useState('');
  const [catalogue, setCatalogue] = useState<MembershipPlanDto[]>([]);
  const [paymentDate, setPaymentDate] = useState('');
  const [paymentMethodId, setPaymentMethodId] = useState<number | ''>('');
  const [markConfirmed, setMarkConfirmed] = useState(true);
  const [notes, setNotes] = useState('');

  const [updateMembership, setUpdateMembership] = useState(false);
  const [newPlanId, setNewPlanId] = useState<number | ''>('');
  const [newStartDate, setNewStartDate] = useState(todayIso());
  const [chargeRegistrationFee, setChargeRegistrationFee] = useState(true);

  const [methods, setMethods] = useState<PaymentMethodDto[]>([]);
  const [transactionReference, setTransactionReference] = useState('');
  // The error only appears once the field has been visited, so the form does not shout at
  // someone who has simply not reached it yet.
  const [referenceTouched, setReferenceTouched] = useState(false);
  const [plans, setPlans] = useState<Lookup[]>([]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [saved, setSaved] = useState('');

  const [upi, setUpi] = useState<UpiPaymentIntentDto | null>(null);
  const [upiBusy, setUpiBusy] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    paymentsApi.methods(controller.signal).then(setMethods).catch(() => setMethods([]));
    plansApi.lookup(controller.signal).then(setPlans).catch(() => setPlans([]));
    // The lookup carries no price, so the sellable catalogue is fetched too — that is what
    // lets choosing a plan fill in the amount.
    plansApi.paged({ pageNumber: 1, pageSize: 100, status: PlanStatus.Active }, controller.signal)
      .then((res) => setCatalogue(res.items))
      .catch(() => setCatalogue([]));
    return () => controller.abort();
  }, []);

  // Changing member clears any plan already picked, so the amount cannot carry over to someone
  // else. The member's existing subscriptions are no longer fetched here: the Plan select lists
  // the catalogue only, so nothing on this screen consumed them.
  useEffect(() => {
    setPlanChoice('');
    setSubscriptionId('');
  }, [member]);

  const quoteRequest = useMemo<QuoteRequest | null>(() => (
    !updateMembership || newPlanId === '' ? null : {
      membershipPlanId: Number(newPlanId),
      startDate: newStartDate,
      discountAmount: 0,
      chargeRegistrationFee,
    }
  ), [updateMembership, newPlanId, newStartDate, chargeRegistrationFee]);

  const { quote, loading: quoteLoading, error: quoteError } = useQuote(quoteRequest, updateMembership);

  /**
   * Seeds the amount from whatever was picked, and it stays editable — the desk often takes a
   * part payment or applies a discount agreed at the counter. The figure typed here is what is
   * recorded; when a subscription is actually sold, the server recalculates and owns the total.
   */
  function applyPlanChoice(choice: string) {
    setPlanChoice(choice);

    setSubscriptionId('');

    if (choice.startsWith('plan:')) {
      const plan = catalogue.find((p) => p.id === Number(choice.slice(5)));
      if (plan) {
        const tax = plan.price * (plan.taxPercent / 100);
        const total = plan.price + (plan.registrationFee ?? 0) + tax;
        setAmount(total.toFixed(2));
      }
    }
  }

  /**
   * UPI, bank transfer and cheque carry `requiresReference`: without the UTR/cheque number the
   * payment cannot be reconciled against the bank later, so it is mandatory for those methods and
   * absent for cash. The flag comes from the payment method, so adding a method under
   * Payments → Payment Methods enforces this without touching the form.
   */
  const selectedMethod = methods.find((m) => m.id === Number(paymentMethodId));
  const referenceRequired = selectedMethod?.requiresReference ?? false;
  const referenceMissing = referenceRequired && !transactionReference.trim();

  const canSubmit = !!member && !!amount && Number(amount) > 0 && paymentMethodId !== ''
    && !referenceMissing
    && (!updateMembership || newPlanId !== '');

  async function save() {
    if (!member || paymentMethodId === '') return;
    setBusy(true);
    setError(null);
    try {
      if (updateMembership && newPlanId !== '') {
        // One transaction: the server sells the subscription and books the payment against it.
        await subscriptionsApi.create({
          memberId: member.id,
          membershipPlanId: Number(newPlanId),
          startDate: newStartDate,
          discountAmount: 0,
          chargeRegistrationFee,
          notes: notes.trim() || null,
          payment: {
            paymentMethodId: Number(paymentMethodId),
            amount: Number(amount),
            transactionReference: transactionReference.trim() || null,
            paymentDate: paymentDate || null,
            notes: notes.trim() || null,
            markConfirmed,
          },
        });
        setSaved('Membership created and the payment recorded against it.');
      } else {
        await paymentsApi.create({
          memberId: member.id,
          subscriptionId: subscriptionId === '' ? null : Number(subscriptionId),
          // What the desk picked in the Plan select. The payment is not tied to a subscription
          // here, so without this the Plan column on the payments list stayed blank.
          membershipPlanId: planChoice.startsWith('plan:') ? Number(planChoice.slice(5)) : null,
          amount: Number(amount),
          discountAmount: 0,
          taxAmount: 0,
          paymentMethodId: Number(paymentMethodId),
          transactionReference: transactionReference.trim() || null,
          paymentDate: paymentDate || null,
          notes: notes.trim() || null,
          markConfirmed,
        });
        setSaved('Payment recorded.');
      }
      setTimeout(() => navigate('/admin/payments'), 700);
    } catch (err) {
      setError(err);
    } finally {
      setBusy(false);
    }
  }

  async function payByUpi() {
    if (!member || !amount) return;
    setUpiBusy(true);
    setError(null);
    try {
      setUpi(await paymentsApi.upiIntent({
        memberId: member.id,
        subscriptionId: subscriptionId === '' ? null : Number(subscriptionId),
        amount: Number(amount),
        notes: notes.trim() || null,
      }));
    } catch (err) {
      setError(err);
    } finally {
      setUpiBusy(false);
    }
  }

  if (!mayCollect) {
    return (
      <div className="page">
        <PageCard>
          <PageCardHeader icon={<IconMoney size={20} />} title="Record Payment" />
          <div className="section-pad">
            <Alert tone="error">You do not have permission to collect payments.</Alert>
          </div>
        </PageCard>
      </div>
    );
  }

  return (
    <div className="page">
      <PageCard>
        <PageCardHeader
          icon={<IconMoney size={20} />}
          title="Record Payment"
          subtitle="Book a payment against a member and, optionally, the membership it pays for."
          actions={
            <Link className="btn btn-outline" to="/admin/payments">
              <IconArrowLeft size={15} /> Back to Payments
            </Link>
          }
        />

        <div className="section-pad">
          <div className="stack">
            {saved ? <Alert tone="success">{saved}</Alert> : null}
            {error ? <ErrorAlert error={error} /> : null}

            <div className="form-grid">
              <Field label="Member" required help="Search by name, member code or phone.">
                <MemberPicker value={member} onChange={setMember} autoFocus />
              </Field>

              <Field
                label="Plan"
                help={
                  planChoice.startsWith('plan:')
                    ? 'Amount filled from the plan price — edit it if the desk agreed something else.'
                    : 'Pick an unpaid membership to settle its balance, or a plan to price a new sale.'
                }
              >
                <select
                  className="select"
                  value={planChoice}
                  disabled={!member}
                  onChange={(e) => applyPlanChoice(e.target.value)}
                >
                  <option value="">{member ? 'Not tied to a plan' : 'Choose a member first'}</option>

                  {/* Plans only, by name. The price lands in the Amount field on selection. */}
                  {catalogue.map((p) => (
                    <option key={`plan:${p.id}`} value={`plan:${p.id}`}>{p.name}</option>
                  ))}
                </select>
              </Field>

              <Field label="Amount" required>
                <div className="input-group">
                  <span className="input-icon">{currency}</span>
                  <input
                    className="input" type="number" min={0} step="0.01" placeholder="0.00"
                    value={amount}
                    onChange={(e) => setAmount(e.target.value)}
                  />
                </div>
              </Field>

              <Field label="Payment Date" help="Leave empty if your backend sets today by default.">
                <input className="input" type="date" value={paymentDate} onChange={(e) => setPaymentDate(e.target.value)} />
              </Field>

              <Field label="Mode" required>
                <select
                  className="select"
                  value={paymentMethodId}
                  onChange={(e) => setPaymentMethodId(e.target.value === '' ? '' : Number(e.target.value))}
                >
                  <option value="">Select a payment method…</option>
                  {methods.map((m) => <option key={m.id} value={m.id}>{m.name}</option>)}
                </select>
              </Field>

              {/* Only for methods that carry a reference — cash has none to give. */}
              {referenceRequired && (
                <Field
                  label={`${selectedMethod?.name ?? 'Transaction'} reference`}
                  required
                  error={referenceMissing && referenceTouched
                    ? `A ${selectedMethod?.name ?? 'transaction'} reference is required — without it this payment cannot be matched to the bank later.`
                    : undefined}
                  help={referenceMissing && referenceTouched ? undefined : 'UTR / transaction id from the payer’s app, or the cheque number.'}
                >
                  <input
                    className="input"
                    value={transactionReference}
                    onChange={(e) => setTransactionReference(e.target.value)}
                    onBlur={() => setReferenceTouched(true)}
                    placeholder="e.g. 412345678901"
                  />
                </Field>
              )}

              <Field
                label="Status"
                help="Awaiting confirmation keeps the payment unsettled until someone verifies the reference."
              >
                <select
                  className="select"
                  value={markConfirmed ? 'paid' : 'awaiting'}
                  onChange={(e) => setMarkConfirmed(e.target.value === 'paid')}
                >
                  <option value="paid">Paid — verified</option>
                  <option value="awaiting">Awaiting confirmation</option>
                </select>
              </Field>
            </div>

            <Field label="Notes">
              <textarea
                className="textarea" maxLength={2000}
                placeholder="Anything the next person at the desk should know."
                value={notes}
                onChange={(e) => setNotes(e.target.value)}
              />
            </Field>

            <FormSection
              title="Update Membership (Optional)"
              icon={<IconCrown size={16} />}
              optional
              caption="Tick this to sell a membership and book this payment against it in one transaction."
            >
              <label className="check-inline" style={{ marginBottom: updateMembership ? 14 : 0 }}>
                <input
                  type="checkbox"
                  checked={updateMembership}
                  onChange={(e) => setUpdateMembership(e.target.checked)}
                />
                Create a new subscription for this member
              </label>

              {updateMembership && (
                <div className="stack">
                  <div className="form-grid">
                    <Field label="Membership plan" required>
                      <select
                        className="select" value={newPlanId}
                        onChange={(e) => setNewPlanId(e.target.value === '' ? '' : Number(e.target.value))}
                      >
                        <option value="">Select a plan…</option>
                        {plans.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
                      </select>
                    </Field>
                    <Field label="Start date" required>
                      <input className="input" type="date" value={newStartDate} onChange={(e) => setNewStartDate(e.target.value)} />
                    </Field>
                  </div>
                  <label className="check-inline">
                    <input
                      type="checkbox" checked={chargeRegistrationFee}
                      onChange={(e) => setChargeRegistrationFee(e.target.checked)}
                    />
                    Charge the registration fee
                  </label>
                  <QuotePanel quote={quote} loading={quoteLoading} error={quoteError} currency={currency} />
                </div>
              )}
            </FormSection>

            <div className="form-footer" style={{ justifyContent: 'flex-start' }}>
              <button className="btn btn-dark" onClick={save} disabled={busy || !canSubmit}>
                {busy ? 'Saving…' : 'Save Payment'}
              </button>
              <button className="btn btn-outline" onClick={() => navigate('/admin/payments')} disabled={busy}>
                Cancel
              </button>
              <button
                className="btn btn-outline"
                onClick={payByUpi}
                disabled={upiBusy || !member || !amount || Number(amount) <= 0}
                title="Build a UPI deep link and QR for this amount"
              >
                <IconQr size={15} /> {upiBusy ? 'Building…' : 'Pay by UPI'}
              </button>
            </div>
            <div className="form-note">Fields marked <span style={{ color: 'var(--danger)' }}>*</span> are mandatory.</div>
          </div>
        </div>
      </PageCard>

      {upiBusy && !upi && <Loading message="Requesting a UPI intent…" />}
      {upi && <UpiModal intent={upi} onClose={() => setUpi(null)} />}
    </div>
  );
}

/* ------------------------------------------------------------------ UPI modal */

