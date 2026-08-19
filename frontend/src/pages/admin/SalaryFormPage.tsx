import { useEffect, useMemo, useRef, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { ApiError } from '@/api/client';
import {
  salariesApi, type SalaryPaymentDto, type SaveSalaryPaymentDto,
} from '@/api/endpoints/salaries';
import { trainersApi } from '@/api/endpoints/trainers';
import { paymentsApi, type PaymentMethodDto } from '@/api/endpoints/payments';
import type { Lookup } from '@/api/types';
import { useAuth } from '@/auth/AuthContext';
import {
  Alert, ErrorAlert, Field, FormSection, Loading, PageCard, PageCardHeader,
} from '@/components/ui';
import { IconArrowLeft, IconCard, IconInfo, IconMoney } from '@/components/icons';
import { money } from '@/lib/format';
import './ops.css';
import './admin.css';

const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

const CURRENT_YEAR = new Date().getFullYear();

const period = (month: number, year: number) => `${String(month).padStart(2, '0')}/${year}`;

/**
 * yyyy-MM-dd for a `<input type="date">`.
 *
 * The API sends a plain calendar date ("2026-08-18T00:00:00"), so the day is taken straight off
 * the string rather than round-tripped through `Date`, which would slip the date back a day in
 * any zone behind UTC.
 */
function toDateInput(value: string | null | undefined): string {
  return value ? value.slice(0, 10) : '';
}

/** Today as a local yyyy-MM-dd, for the same reason. */
function todayInput(): string {
  const d = new Date();
  return `${d.getFullYear()}-${`${d.getMonth() + 1}`.padStart(2, '0')}-${`${d.getDate()}`.padStart(2, '0')}`;
}

interface FormState {
  trainerId: string; periodMonth: string; periodYear: string;
  baseAmount: string; bonus: string; deduction: string;
  paymentDate: string; paymentMethodId: string; transactionReference: string; notes: string;
}

const BLANK: FormState = {
  trainerId: '',
  periodMonth: String(new Date().getMonth() + 1),
  periodYear: String(CURRENT_YEAR),
  baseAmount: '',
  bonus: '0',
  deduction: '0',
  paymentDate: todayInput(),
  paymentMethodId: '',
  transactionReference: '',
  notes: '',
};

/** Server validation keys arrive in the C# PascalCase; match them case-insensitively. */
function fieldError(errors: Record<string, string[]>, name: string): string | undefined {
  const key = Object.keys(errors).find((k) => k.toLowerCase() === name.toLowerCase());
  return key ? errors[key][0] : undefined;
}

function amountOf(value: string): number {
  const parsed = Number(value.trim());
  return Number.isFinite(parsed) ? parsed : 0;
}

/**
 * Resolves one payment by id.
 *
 * SalariesController exposes no `GET /api/salaries/{id}` — only the paged list, the yearly
 * summary, create and delete — so the record behind an `/edit` URL is found by walking the list.
 * Pages are the server maximum (200) and the walk stops as soon as the total is covered.
 */
async function findPayment(id: number, signal?: AbortSignal): Promise<SalaryPaymentDto | null> {
  const pageSize = 200;
  for (let pageNumber = 1; pageNumber <= 25; pageNumber++) {
    const page = await salariesApi.list(
      { year: '', month: '', trainerId: '', pageNumber, pageSize }, signal,
    );
    const hit = page.items.find((row) => row.id === id);
    if (hit) return hit;
    if (pageNumber * pageSize >= page.totalCount) return null;
  }
  return null;
}

/**
 * Records a trainer's monthly salary.
 *
 * The `/:id/edit` route lands here too, but as a read-only review: the API is create-and-delete
 * only, because recording a payment also books an expense, and quietly rewriting money already
 * posted to the ledger is what the audit trail exists to prevent.
 */
export default function SalaryFormPage() {
  const navigate = useNavigate();
  const { id } = useParams<{ id?: string }>();
  const paymentId = id ? Number(id) : null;
  const isEdit = paymentId !== null;
  const { can, currency } = useAuth();

  const [form, setForm] = useState<FormState>(BLANK);
  const [loading, setLoading] = useState(isEdit);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<unknown>(null);
  // Kept apart from `error`: a save that fails must leave the form on screen, but a record that
  // never loaded has nothing to save, so the form is replaced entirely.
  const [loadError, setLoadError] = useState<unknown>(null);
  const [errors, setErrors] = useState<Record<string, string[]>>({});

  const [trainers, setTrainers] = useState<Lookup[]>([]);
  const [methods, setMethods] = useState<PaymentMethodDto[]>([]);

  const alive = useRef(true);
  useEffect(() => {
    alive.current = true;
    return () => { alive.current = false; };
  }, []);

  // An existing payment can only be read back — the API has no amend operation for it — so the
  // edit URL is a review screen and the lower "view" permission is enough to open it.
  const allowed = can(isEdit ? 'salary.view' : 'salary.manage');

  useEffect(() => {
    const controller = new AbortController();
    (async () => {
      try {
        const rows = await trainersApi.lookup(true, controller.signal);
        if (!controller.signal.aborted) setTrainers(rows);
      } catch {
        if (!controller.signal.aborted) setTrainers([]);
      }
      try {
        const rows = await paymentsApi.methods(controller.signal);
        if (!controller.signal.aborted) setMethods(rows);
      } catch {
        if (!controller.signal.aborted) setMethods([]);
      }
    })();
    return () => controller.abort();
  }, []);

  useEffect(() => {
    if (paymentId === null) return;
    const controller = new AbortController();
    let current = true;
    setLoading(true);
    findPayment(paymentId, controller.signal)
      .then((payment) => {
        if (!current) return;
        if (!payment) {
          setLoadError(new Error(`Salary payment #${paymentId} could not be found.`));
          setError(null);
          return;
        }
        setForm({
          trainerId: String(payment.trainerId),
          periodMonth: String(payment.periodMonth),
          periodYear: String(payment.periodYear),
          baseAmount: String(payment.baseAmount),
          bonus: String(payment.bonus),
          deduction: String(payment.deduction),
          paymentDate: toDateInput(payment.paymentDate),
          paymentMethodId: payment.paymentMethodId ? String(payment.paymentMethodId) : '',
          transactionReference: payment.transactionReference ?? '',
          notes: payment.notes ?? '',
        });
        setError(null);
        setLoadError(null);
      })
      .catch((err) => { if (current) { setLoadError(err); setError(null); } })
      .finally(() => { if (current) setLoading(false); });
    return () => { current = false; controller.abort(); };
  }, [paymentId]);

  function set<K extends keyof FormState>(key: K, value: FormState[K]) {
    setForm((f) => ({ ...f, [key]: value }));
  }

  /** Picking a trainer suggests their contracted salary as the base, when the gym recorded one. */
  async function pickTrainer(value: string) {
    set('trainerId', value);
    if (!value) return;
    try {
      const trainer = await trainersApi.get(Number(value));
      const contracted = trainer.monthlySalary;
      if (!alive.current || contracted === null || contracted === undefined) return;
      // Never stomp an amount already typed, or a later choice of a different trainer.
      setForm((f) => (f.trainerId === value && !amountOf(f.baseAmount)
        ? { ...f, baseAmount: String(contracted) } : f));
    } catch {
      /* the suggestion is a convenience; the amount can always be typed by hand */
    }
  }

  const netPreview = amountOf(form.baseAmount) + amountOf(form.bonus) - amountOf(form.deduction);

  // An old record can sit outside the eight-year window the picker offers; keep its year listed
  // so the select shows the period the payment actually covers.
  const yearOptions = useMemo(() => {
    const base = Array.from({ length: 8 }, (_, i) => CURRENT_YEAR + 1 - i);
    const chosen = Number(form.periodYear);
    return Number.isFinite(chosen) && chosen > 0 && !base.includes(chosen)
      ? [...base, chosen].sort((a, b) => b - a)
      : base;
  }, [form.periodYear]);

  async function save() {
    setError(null);
    setErrors({});

    // The server answers a missing trainer with a bare 404; naming the field reads better.
    if (!form.trainerId) {
      setErrors({ TrainerId: ['Choose the trainer being paid.'] });
      return;
    }

    setSaving(true);
    const body: SaveSalaryPaymentDto = {
      trainerId: Number(form.trainerId),
      periodYear: Number(form.periodYear),
      periodMonth: Number(form.periodMonth),
      baseAmount: amountOf(form.baseAmount),
      bonus: amountOf(form.bonus),
      deduction: amountOf(form.deduction),
      paymentDate: form.paymentDate,
      paymentMethodId: form.paymentMethodId ? Number(form.paymentMethodId) : null,
      transactionReference: form.transactionReference.trim() || null,
      notes: form.notes.trim() || null,
    };

    try {
      await salariesApi.create(body);
      if (!alive.current) return;
      navigate('/admin/salaries');
    } catch (err) {
      if (!alive.current) return;
      setError(err);
      if (err instanceof ApiError) setErrors(err.validationErrors);
      window.scrollTo({ top: 0, behavior: 'smooth' });
    } finally {
      if (alive.current) setSaving(false);
    }
  }

  // The record could not be read — the id is wrong, or someone else deleted the payment. A form
  // over nothing offers actions that cannot mean anything, so the page shows the failure and a
  // way back instead.
  if (isEdit && !loading && loadError) {
    return (
      <div className="page">
        <PageCard>
          <PageCardHeader
            icon={<IconMoney size={20} />}
            title="Salary Payment"
            actions={
              <button className="btn btn-outline" onClick={() => navigate('/admin/salaries')}>
                <IconArrowLeft size={15} /> Back to Salaries
              </button>
            }
          />
          <div className="page-card-body">
            <ErrorAlert error={loadError} />
            <div className="form-note">
              Nothing can be shown until a salary payment that exists is opened.
            </div>
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
          title={isEdit ? 'Salary Payment' : 'Record Salary Payment'}
          subtitle={isEdit
            ? 'A recorded payment is read-only — its matching expense is already in the ledger.'
            : 'Pay a trainer for one month; the matching expense is booked under Salaries.'}
          actions={
            <button className="btn btn-outline" onClick={() => navigate('/admin/salaries')}>
              <IconArrowLeft size={15} /> Back to Salaries
            </button>
          }
        />

        <div className="page-card-body">
          {!allowed ? (
            <Alert tone="warning">
              You do not have permission to {isEdit ? 'view' : 'record'} salary payments.
            </Alert>
          ) : loading ? (
            <Loading message="Loading salary payment..." />
          ) : (
            <>
              {error ? <div style={{ marginBottom: 18 }}><ErrorAlert error={error} /></div> : null}

              {isEdit ? (
                <div style={{ marginBottom: 18 }}>
                  <Alert tone="warning">
                    <IconInfo size={16} />
                    <span>
                      Salary payments cannot be amended. Correcting one means deleting it from the
                      list and recording it again — the expense already booked for it stays put.
                    </span>
                  </Alert>
                </div>
              ) : null}

              <FormSection
                title="Salary Payment"
                caption="Who is being paid, for which month, and what the pay adds up to."
                icon={<IconMoney size={15} />}
              >
                <div className="form-grid">
                  <Field label="Trainer" required error={fieldError(errors, 'TrainerId')}>
                    <select
                      className={`select ${fieldError(errors, 'TrainerId') ? 'input-invalid' : ''}`}
                      value={form.trainerId}
                      onChange={(e) => void pickTrainer(e.target.value)}
                      disabled={isEdit}
                    >
                      <option value="">Select a trainer…</option>
                      {trainers.map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}
                    </select>
                  </Field>
                  <Field
                    label="Salary month"
                    required
                    help="One payment per trainer per month."
                    error={fieldError(errors, 'PeriodMonth')}
                  >
                    <select
                      className="select"
                      value={form.periodMonth}
                      onChange={(e) => set('periodMonth', e.target.value)}
                      disabled={isEdit}
                    >
                      {MONTHS.map((label, index) => (
                        <option key={label} value={index + 1}>{label}</option>
                      ))}
                    </select>
                  </Field>
                  <Field label="Salary year" required error={fieldError(errors, 'PeriodYear')}>
                    <select
                      className="select"
                      value={form.periodYear}
                      onChange={(e) => set('periodYear', e.target.value)}
                      disabled={isEdit}
                    >
                      {yearOptions.map((y) => <option key={y} value={y}>{y}</option>)}
                    </select>
                  </Field>
                  <Field
                    label="Base amount"
                    required
                    help="The agreed monthly salary."
                    error={fieldError(errors, 'BaseAmount')}
                  >
                    <input
                      className={`input ${fieldError(errors, 'BaseAmount') ? 'input-invalid' : ''}`}
                      type="number" min={0} step="0.01"
                      value={form.baseAmount}
                      onChange={(e) => set('baseAmount', e.target.value)}
                      disabled={isEdit}
                    />
                  </Field>
                  <Field label="Bonus" error={fieldError(errors, 'Bonus')}>
                    <input
                      className={`input ${fieldError(errors, 'Bonus') ? 'input-invalid' : ''}`}
                      type="number" min={0} step="0.01"
                      value={form.bonus}
                      onChange={(e) => set('bonus', e.target.value)}
                      disabled={isEdit}
                    />
                  </Field>
                  <Field
                    label="Deduction"
                    help="Advances or unpaid leave taken off the pay."
                    error={fieldError(errors, 'Deduction')}
                  >
                    <input
                      className={`input ${fieldError(errors, 'Deduction') ? 'input-invalid' : ''}`}
                      type="number" min={0} step="0.01"
                      value={form.deduction}
                      onChange={(e) => set('deduction', e.target.value)}
                      disabled={isEdit}
                    />
                  </Field>
                  <Field label="Net payable" help="Base plus bonus, less the deduction.">
                    <input className="input" value={money(netPreview, currency)} disabled readOnly />
                  </Field>
                </div>
              </FormSection>

              <FormSection
                title="Payment Details"
                caption="When the money left the gym and how it was sent."
                icon={<IconCard size={15} />}
              >
                <div className="form-grid">
                  <Field label="Payment date" required error={fieldError(errors, 'PaymentDate')}>
                    <input
                      className={`input ${fieldError(errors, 'PaymentDate') ? 'input-invalid' : ''}`}
                      type="date"
                      value={form.paymentDate}
                      onChange={(e) => set('paymentDate', e.target.value)}
                      disabled={isEdit}
                    />
                  </Field>
                  <Field label="Payment method" error={fieldError(errors, 'PaymentMethodId')}>
                    <select
                      className="select"
                      value={form.paymentMethodId}
                      onChange={(e) => set('paymentMethodId', e.target.value)}
                      disabled={isEdit}
                    >
                      <option value="">Not recorded</option>
                      {methods.map((m) => <option key={m.id} value={m.id}>{m.name}</option>)}
                    </select>
                  </Field>
                  <Field
                    label="Transaction reference"
                    help="Bank or UPI reference, if any."
                    error={fieldError(errors, 'TransactionReference')}
                  >
                    <input
                      className={`input ${fieldError(errors, 'TransactionReference') ? 'input-invalid' : ''}`}
                      maxLength={128}
                      value={form.transactionReference}
                      onChange={(e) => set('transactionReference', e.target.value)}
                      disabled={isEdit}
                    />
                  </Field>
                </div>
              </FormSection>

              <FormSection
                title="Notes"
                caption="Anything the amounts alone do not explain."
                icon={<IconInfo size={15} />}
              >
                <Field label="Notes" error={fieldError(errors, 'Notes')}>
                  <textarea
                    className={`textarea ${fieldError(errors, 'Notes') ? 'input-invalid' : ''}`}
                    maxLength={512}
                    value={form.notes}
                    onChange={(e) => set('notes', e.target.value)}
                    placeholder="Arrears, part payment, remarks..."
                    disabled={isEdit}
                  />
                </Field>
              </FormSection>

              {isEdit ? (
                <>
                  <div className="form-footer">
                    <button className="btn btn-outline" onClick={() => navigate('/admin/salaries')}>
                      Back to Salaries
                    </button>
                  </div>
                  <div className="form-note">
                    This payment covers {period(Number(form.periodMonth), Number(form.periodYear))}.
                  </div>
                </>
              ) : (
                <>
                  <div className="form-footer">
                    <button className="btn btn-dark" onClick={() => void save()} disabled={saving}>
                      {saving ? 'Saving...' : 'Save Payment'}
                    </button>
                    <button
                      className="btn btn-outline"
                      onClick={() => navigate('/admin/salaries')}
                      disabled={saving}
                    >
                      Cancel
                    </button>
                  </div>
                  <div className="form-note">
                    Fields marked with * are mandatory. Recording a salary also books a matching
                    expense under the Salaries category.
                  </div>
                </>
              )}
            </>
          )}
        </div>
      </PageCard>
    </div>
  );
}
