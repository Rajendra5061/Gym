import { useEffect, useRef, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { ApiError } from '@/api/client';
import {
  getExpense, listExpenseCategories, listPaymentMethods, saveExpense,
  type ExpenseCategoryDto, type PaymentMethodDto, type SaveExpenseDto,
} from '@/api/endpoints/expenses';
import { useAuth } from '@/auth/AuthContext';
import {
  Alert, ErrorAlert, Field, FormSection, Loading, PageCard, PageCardHeader,
} from '@/components/ui';
import { IconArrowLeft, IconInfo, IconMoney } from '@/components/icons';
import './ops.css';

/**
 * yyyy-MM-dd for a `<input type="date">`.
 *
 * The API sends a plain calendar date ("2026-08-18T00:00:00"), so the day is taken straight off
 * the string. `isoDate` in lib/format routes through `toISOString`, which converts local midnight
 * to the previous evening UTC and slips the date back a day in any zone ahead of UTC — enough to
 * walk an expense date backwards once per edit-and-save.
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
  expenseCategoryId: string; title: string; amount: string; expenseDate: string;
  paymentMethodId: string; vendorName: string; referenceNumber: string; description: string;
}

const BLANK: FormState = {
  expenseCategoryId: '0', title: '', amount: '', expenseDate: todayInput(),
  paymentMethodId: '', vendorName: '', referenceNumber: '', description: '',
};

/** Server validation keys arrive in the C# PascalCase; match them case-insensitively. */
function fieldError(errors: Record<string, string[]>, name: string): string | undefined {
  const key = Object.keys(errors).find((k) => k.toLowerCase() === name.toLowerCase());
  return key ? errors[key][0] : undefined;
}

export default function ExpenseFormPage() {
  const navigate = useNavigate();
  const { id } = useParams<{ id?: string }>();
  const expenseId = id ? Number(id) : null;
  const isEdit = expenseId !== null;
  const { can } = useAuth();

  const [form, setForm] = useState<FormState>(BLANK);
  const [loading, setLoading] = useState(isEdit);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<unknown>(null);
  // Kept apart from `error`: a save that fails must leave the form on screen, but a record that
  // never loaded has nothing to save, so the form is replaced entirely.
  const [loadError, setLoadError] = useState<unknown>(null);
  const [errors, setErrors] = useState<Record<string, string[]>>({});

  const [categories, setCategories] = useState<ExpenseCategoryDto[]>([]);
  const [methods, setMethods] = useState<PaymentMethodDto[]>([]);

  const alive = useRef(true);
  useEffect(() => {
    alive.current = true;
    return () => { alive.current = false; };
  }, []);

  const allowed = can('expenses.manage');

  useEffect(() => {
    const controller = new AbortController();
    (async () => {
      try {
        const rows = await listExpenseCategories(controller.signal);
        if (controller.signal.aborted) return;
        setCategories(rows);
        // The modal seeded a new expense with the first category; keep that default here, but
        // never stomp a choice already made or a category loaded off an existing record.
        if (!isEdit && rows.length > 0) {
          setForm((f) => (f.expenseCategoryId === '0'
            ? { ...f, expenseCategoryId: String(rows[0].id) } : f));
        }
      } catch {
        if (!controller.signal.aborted) setCategories([]);
      }
      try {
        const rows = await listPaymentMethods(controller.signal);
        if (!controller.signal.aborted) setMethods(rows);
      } catch {
        if (!controller.signal.aborted) setMethods([]);
      }
    })();
    return () => controller.abort();
  }, [isEdit]);

  useEffect(() => {
    if (expenseId === null) return;
    const controller = new AbortController();
    let current = true;
    setLoading(true);
    getExpense(expenseId, controller.signal)
      .then((expense) => {
        if (!current) return;
        setForm({
          expenseCategoryId: String(expense.expenseCategoryId),
          title: expense.title,
          amount: String(expense.amount),
          expenseDate: toDateInput(expense.expenseDate),
          paymentMethodId: expense.paymentMethodId === null || expense.paymentMethodId === undefined
            ? '' : String(expense.paymentMethodId),
          vendorName: expense.vendorName ?? '',
          referenceNumber: expense.referenceNumber ?? '',
          description: expense.description ?? '',
        });
        setError(null);
        setLoadError(null);
      })
      .catch((err) => { if (current) { setLoadError(err); setError(null); } })
      .finally(() => { if (current) setLoading(false); });
    return () => { current = false; controller.abort(); };
  }, [expenseId]);

  function set<K extends keyof FormState>(key: K, value: FormState[K]) {
    setForm((f) => ({ ...f, [key]: value }));
  }

  async function save() {
    setSaving(true);
    setError(null);
    setErrors({});

    const amount = Number(form.amount.trim());
    const body: SaveExpenseDto = {
      id: isEdit && expenseId !== null ? expenseId : 0,
      expenseCategoryId: Number(form.expenseCategoryId),
      title: form.title.trim(),
      description: form.description.trim() || null,
      amount: Number.isFinite(amount) ? amount : 0,
      expenseDate: form.expenseDate,
      paymentMethodId: form.paymentMethodId ? Number(form.paymentMethodId) : null,
      vendorName: form.vendorName.trim() || null,
      referenceNumber: form.referenceNumber.trim() || null,
    };

    try {
      await saveExpense(body);
      if (!alive.current) return;
      navigate('/admin/expenses');
    } catch (err) {
      if (!alive.current) return;
      setError(err);
      if (err instanceof ApiError) setErrors(err.validationErrors);
      window.scrollTo({ top: 0, behavior: 'smooth' });
    } finally {
      if (alive.current) setSaving(false);
    }
  }

  // The record could not be read — the id is wrong, or someone else deleted the expense. An
  // editable form over nothing offers a Save that cannot mean anything, so the page shows the
  // failure and a way back instead.
  if (isEdit && !loading && loadError) {
    return (
      <div className="page">
        <PageCard>
          <PageCardHeader
            icon={<IconMoney size={20} />}
            title="Edit Expense"
            actions={
              <button className="btn btn-outline" onClick={() => navigate('/admin/expenses')}>
                <IconArrowLeft size={15} /> Back to Expenses
              </button>
            }
          />
          <div className="page-card-body">
            <ErrorAlert error={loadError} />
            <div className="form-note">
              Nothing can be edited until an expense that exists is opened.
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
          title={isEdit ? 'Edit Expense' : 'Record Expense'}
          subtitle={isEdit
            ? 'Correct the cost, its category or how it was paid.'
            : 'Book a cost against a category so reports can total it.'}
          actions={
            <button className="btn btn-outline" onClick={() => navigate('/admin/expenses')}>
              <IconArrowLeft size={15} /> Back to Expenses
            </button>
          }
        />

        <div className="page-card-body">
          {!allowed ? (
            <Alert tone="warning">
              You do not have permission to {isEdit ? 'edit' : 'record'} expenses.
            </Alert>
          ) : loading ? (
            <Loading message="Loading expense..." />
          ) : (
            <>
              {error ? <div style={{ marginBottom: 18 }}><ErrorAlert error={error} /></div> : null}

              <FormSection
                title="Expense Details"
                caption="What was paid for, how much, and how it was settled."
                icon={<IconMoney size={15} />}
              >
                <div className="form-grid">
                  <Field
                    label="Category"
                    required
                    help="Group the cost so reports can total it."
                    error={fieldError(errors, 'ExpenseCategoryId')}
                  >
                    <select
                      className="select"
                      value={form.expenseCategoryId}
                      onChange={(e) => set('expenseCategoryId', e.target.value)}
                    >
                      <option value="0">Select a category…</option>
                      {categories.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
                    </select>
                  </Field>
                  <Field label="Date" required error={fieldError(errors, 'ExpenseDate')}>
                    <input
                      className={`input ${fieldError(errors, 'ExpenseDate') ? 'input-invalid' : ''}`}
                      type="date"
                      value={form.expenseDate}
                      onChange={(e) => set('expenseDate', e.target.value)}
                    />
                  </Field>
                  <Field label="Title" required help="3 to 200 characters." error={fieldError(errors, 'Title')}>
                    <input
                      className={`input ${fieldError(errors, 'Title') ? 'input-invalid' : ''}`}
                      placeholder="e.g. October electricity bill"
                      value={form.title}
                      onChange={(e) => set('title', e.target.value)}
                      autoFocus
                    />
                  </Field>
                  <Field label="Amount" required error={fieldError(errors, 'Amount')}>
                    <input
                      className={`input ${fieldError(errors, 'Amount') ? 'input-invalid' : ''}`}
                      type="number" min={0} step="0.01"
                      value={form.amount}
                      onChange={(e) => set('amount', e.target.value)}
                    />
                  </Field>
                  <Field label="Vendor" error={fieldError(errors, 'VendorName')}>
                    <input
                      className={`input ${fieldError(errors, 'VendorName') ? 'input-invalid' : ''}`}
                      placeholder="Who was paid"
                      value={form.vendorName}
                      onChange={(e) => set('vendorName', e.target.value)}
                    />
                  </Field>
                  <Field label="Payment method" required error={fieldError(errors, 'PaymentMethodId')}>
                    <select
                      className="select"
                      value={form.paymentMethodId}
                      onChange={(e) => set('paymentMethodId', e.target.value)}
                    >
                      <option value="">Not recorded</option>
                      {methods.map((m) => <option key={m.id} value={m.id}>{m.name}</option>)}
                    </select>
                  </Field>
                  <Field
                    label="Reference number"
                    help="Invoice or transaction reference."
                    error={fieldError(errors, 'ReferenceNumber')}
                  >
                    <input
                      className={`input ${fieldError(errors, 'ReferenceNumber') ? 'input-invalid' : ''}`}
                      value={form.referenceNumber}
                      onChange={(e) => set('referenceNumber', e.target.value)}
                    />
                  </Field>
                </div>
              </FormSection>

              <FormSection
                title="Description"
                caption="Anything the title alone does not explain."
                icon={<IconInfo size={15} />}
              >
                <Field label="Description" error={fieldError(errors, 'Description')}>
                  <textarea
                    className={`textarea ${fieldError(errors, 'Description') ? 'input-invalid' : ''}`}
                    value={form.description}
                    onChange={(e) => set('description', e.target.value)}
                  />
                </Field>
              </FormSection>

              <div className="form-footer">
                <button className="btn btn-dark" onClick={() => void save()} disabled={saving}>
                  {saving ? 'Saving...' : 'Save Expense'}
                </button>
                <button
                  className="btn btn-outline"
                  onClick={() => navigate('/admin/expenses')}
                  disabled={saving}
                >
                  Cancel
                </button>
              </div>
              <div className="form-note">Fields marked with * are mandatory.</div>
            </>
          )}
        </div>
      </PageCard>
    </div>
  );
}
