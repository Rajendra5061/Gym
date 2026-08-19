import { useEffect, useState } from 'react';
import { useAuth } from '@/auth/AuthContext';
import {
  Alert, ConfirmModal, EmptyState, ErrorAlert, Field, FilterField, FilterMenu, FilterStrip,
  Loading, Modal, PageCard, PageCardHeader, Pager, Pill,
} from '@/components/ui';
import {
  IconBox, IconCalendar, IconCard, IconMoney, IconPlus, IconSettings,
} from '@/components/icons';
import {
  deleteExpense, listExpenseCategories, listExpenses, listPaymentMethods, saveExpense,
  saveExpenseCategory, type ExpenseCategoryDto, type ExpenseDto, type PaymentMethodDto,
  type SaveExpenseDto,
} from '@/api/endpoints/expenses';
import type { PagedResult } from '@/api/types';
import { date as fmtDate, isoDate, money } from '@/lib/format';
import './ops.css';

interface Filters {
  search: string;
  categoryId: string;
  from: string;
  to: string;
  minAmount: string;
  maxAmount: string;
}

const EMPTY_FILTERS: Filters = { search: '', categoryId: '', from: '', to: '', minAmount: '', maxAmount: '' };

function blankExpense(): SaveExpenseDto {
  return {
    id: 0,
    expenseCategoryId: 0,
    title: '',
    description: '',
    amount: 0,
    expenseDate: isoDate(new Date()),
    paymentMethodId: null,
    vendorName: '',
    referenceNumber: '',
  };
}

export default function ExpensesPage() {
  const { can, currency } = useAuth();
  const manage = can('expenses.manage');

  const [draft, setDraft] = useState<Filters>(EMPTY_FILTERS);
  const [applied, setApplied] = useState<Filters>(EMPTY_FILTERS);
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [reloadKey, setReloadKey] = useState(0);

  const [data, setData] = useState<PagedResult<ExpenseDto> | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<unknown>(null);

  const [categories, setCategories] = useState<ExpenseCategoryDto[]>([]);
  const [methods, setMethods] = useState<PaymentMethodDto[]>([]);

  const [editing, setEditing] = useState<SaveExpenseDto | null>(null);
  const [formError, setFormError] = useState<unknown>(null);
  const [saving, setSaving] = useState(false);
  const [pendingDelete, setPendingDelete] = useState<ExpenseDto | null>(null);
  const [busy, setBusy] = useState(false);

  const [categoryManager, setCategoryManager] = useState(false);
  const [newCategory, setNewCategory] = useState({ name: '', description: '' });
  const [categoryError, setCategoryError] = useState<unknown>(null);
  const [categoryNotice, setCategoryNotice] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    (async () => {
      setLoading(true);
      try {
        const result = await listExpenses(
          {
            search: applied.search || undefined,
            expenseCategoryId: applied.categoryId ? Number(applied.categoryId) : '',
            fromDate: applied.from || undefined,
            toDate: applied.to || undefined,
            minAmount: applied.minAmount ? Number(applied.minAmount) : '',
            maxAmount: applied.maxAmount ? Number(applied.maxAmount) : '',
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

  useEffect(() => {
    const controller = new AbortController();
    (async () => {
      try {
        const rows = await listExpenseCategories(controller.signal);
        if (!controller.signal.aborted) setCategories(rows);
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
  }, [reloadKey]);

  // Badge on the trigger. Counts only the folded-away filters — search has its own visible box,
  // so including it would show a badge for something the user can already see.
  const activeFilterCount = (['categoryId', 'from', 'to', 'minAmount', 'maxAmount'] as const)
    .filter((key) => applied[key] !== EMPTY_FILTERS[key]).length;

  const applyFilters = () => { setApplied(draft); setPageNumber(1); };
  const resetFilters = () => { setDraft(EMPTY_FILTERS); setApplied(EMPTY_FILTERS); setPageNumber(1); };

  const openNew = () => {
    const seed = blankExpense();
    setEditing({ ...seed, expenseCategoryId: categories[0]?.id ?? 0 });
    setFormError(null);
  };

  const openEdit = (expense: ExpenseDto) => {
    setEditing({
      id: expense.id,
      expenseCategoryId: expense.expenseCategoryId,
      title: expense.title,
      description: expense.description ?? '',
      amount: expense.amount,
      expenseDate: isoDate(expense.expenseDate),
      paymentMethodId: expense.paymentMethodId ?? null,
      vendorName: expense.vendorName ?? '',
      referenceNumber: expense.referenceNumber ?? '',
    });
    setFormError(null);
  };

  const submit = async () => {
    if (!editing) return;
    setSaving(true);
    setFormError(null);
    try {
      await saveExpense(editing);
      setEditing(null);
      setReloadKey((k) => k + 1);
    } catch (err) {
      setFormError(err);
    } finally {
      setSaving(false);
    }
  };

  const confirmDelete = async () => {
    if (!pendingDelete) return;
    setBusy(true);
    try {
      await deleteExpense(pendingDelete.id);
      setPendingDelete(null);
      setReloadKey((k) => k + 1);
    } catch (err) {
      setError(err);
      setPendingDelete(null);
    } finally {
      setBusy(false);
    }
  };

  const addCategory = async () => {
    setCategoryError(null);
    setCategoryNotice(null);
    try {
      await saveExpenseCategory({ id: 0, name: newCategory.name.trim(), description: newCategory.description.trim(), isActive: true });
      setNewCategory({ name: '', description: '' });
      setCategoryNotice('Category saved.');
      setReloadKey((k) => k + 1);
    } catch (err) {
      setCategoryError(err);
    }
  };

  const items = data?.items ?? [];
  const firstIndex = (pageNumber - 1) * pageSize;

  return (
    <div className="page">
      <PageCard>
        <PageCardHeader
          icon={<IconMoney size={20} />}
          title="Expenses"
          subtitle="Every cost the gym books, with its category, vendor and payment method."
          actions={
            <>
              <FilterMenu
                activeCount={activeFilterCount}
                onApply={applyFilters}
                onReset={resetFilters}
              >
                <FilterField label="Category">
                  <select
                    className="select"
                    value={draft.categoryId}
                    onChange={(e) => setDraft({ ...draft, categoryId: e.target.value })}
                  >
                    <option value="">All categories</option>
                    {categories.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
                  </select>
                </FilterField>

                <FilterField label="From">
                  <input className="input" type="date" value={draft.from} onChange={(e) => setDraft({ ...draft, from: e.target.value })} />
                </FilterField>

                <FilterField label="To">
                  <input className="input" type="date" value={draft.to} onChange={(e) => setDraft({ ...draft, to: e.target.value })} />
                </FilterField>

                <FilterField label="Min amount">
                  <input className="input" type="number" min={0} value={draft.minAmount} onChange={(e) => setDraft({ ...draft, minAmount: e.target.value })} />
                </FilterField>

                <FilterField label="Max amount">
                  <input className="input" type="number" min={0} value={draft.maxAmount} onChange={(e) => setDraft({ ...draft, maxAmount: e.target.value })} />
                </FilterField>
              </FilterMenu>

              <button className="btn btn-outline" onClick={() => { setCategoryManager(true); setCategoryNotice(null); setCategoryError(null); }}>
                <IconSettings size={15} /> Categories
              </button>
              {manage && (
                <button className="btn btn-dark" onClick={openNew}>
                  <IconPlus size={15} /> Record Expense
                </button>
              )}
            </>
          }
        />

        {/* Search and Reset stay in the open, since they are the two controls reached most often. */}
        <FilterStrip>
          <FilterField label="Search">
            <input
              className="input"
              placeholder="Title, vendor or number"
              value={draft.search}
              onChange={(e) => setDraft({ ...draft, search: e.target.value })}
              onKeyDown={(e) => { if (e.key === 'Enter') applyFilters(); }}
            />
          </FilterField>
        </FilterStrip>

        {loading && <Loading message="Loading expenses…" />}
        {!loading && Boolean(error) && <div className="page-card-body"><ErrorAlert error={error} /></div>}

        {!loading && !error && items.length === 0 && (
          <EmptyState
            icon={<IconMoney size={34} />}
            title="No expenses recorded"
            message="Record rent, salaries, utilities and equipment costs to see them here."
            action={manage ? <button className="btn btn-dark" onClick={openNew}><IconPlus size={15} /> Record Expense</button> : undefined}
          />
        )}

        {!loading && !error && items.length > 0 && (
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th className="idx">#</th>
                  <th>Number</th>
                  <th className="center">Date</th>
                  <th>Category</th>
                  <th>Title</th>
                  <th>Vendor</th>
                  <th className="num">Amount</th>
                  <th>Method</th>
                  <th className="actions">Actions</th>
                </tr>
              </thead>
              <tbody>
                {items.map((expense, index) => (
                  <tr key={expense.id}>
                    <td className="idx">{firstIndex + index + 1}</td>
                    <td className="cell-main">{expense.expenseNumber}</td>
                    <td className="center">
                      <span className="cell-icon"><IconCalendar size={13} />{fmtDate(expense.expenseDate)}</span>
                    </td>
                    <td><Pill tone="primary">{expense.categoryName}</Pill></td>
                    <td>
                      <div className="cell-main">{expense.title}</div>
                      <div className="cell-sub">{expense.recordedByName ? `by ${expense.recordedByName}` : '—'}</div>
                    </td>
                    <td>{expense.vendorName || '—'}</td>
                    <td className="num">{money(expense.amount, currency)}</td>
                    <td>
                      <span className="cell-icon"><IconCard size={13} />{expense.paymentMethodName || '—'}</span>
                    </td>
                    <td className="actions">
                      {manage && (
                        <>
                          <button className="btn btn-edit" onClick={() => openEdit(expense)}>Edit</button>
                          <button className="btn btn-del" onClick={() => setPendingDelete(expense)}>Delete</button>
                        </>
                      )}
                      {!manage && <span className="muted">—</span>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
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

      {editing && (
        <Modal
          title={editing.id ? 'Edit expense' : 'Record expense'}
          icon={<IconMoney size={18} />}
          onClose={() => setEditing(null)}
          footer={
            <>
              <button className="btn btn-outline" onClick={() => setEditing(null)} disabled={saving}>Cancel</button>
              <button className="btn btn-dark" onClick={() => void submit()} disabled={saving}>
                {saving ? 'Saving…' : 'Save expense'}
              </button>
            </>
          }
        >
          <div className="stack">
            {formError ? <ErrorAlert error={formError} /> : null}
            <div className="form-grid">
              <Field label="Category" required help="Group the cost so reports can total it.">
                <select
                  className="select"
                  value={editing.expenseCategoryId}
                  onChange={(e) => setEditing({ ...editing, expenseCategoryId: Number(e.target.value) })}
                >
                  <option value={0}>Select a category…</option>
                  {categories.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
                </select>
              </Field>
              <Field label="Date" required>
                <input
                  className="input"
                  type="date"
                  value={editing.expenseDate}
                  onChange={(e) => setEditing({ ...editing, expenseDate: e.target.value })}
                />
              </Field>
              <Field label="Title" required help="3 to 200 characters.">
                <input
                  className="input"
                  placeholder="e.g. October electricity bill"
                  value={editing.title}
                  onChange={(e) => setEditing({ ...editing, title: e.target.value })}
                />
              </Field>
              <Field label="Amount" required>
                <input
                  className="input"
                  type="number"
                  min={0}
                  step="0.01"
                  value={editing.amount}
                  onChange={(e) => setEditing({ ...editing, amount: Number(e.target.value) })}
                />
              </Field>
              <Field label="Vendor">
                <input
                  className="input"
                  placeholder="Who was paid"
                  value={editing.vendorName ?? ''}
                  onChange={(e) => setEditing({ ...editing, vendorName: e.target.value })}
                />
              </Field>
              <Field label="Payment method">
                <select
                  className="select"
                  value={editing.paymentMethodId ?? ''}
                  onChange={(e) => setEditing({ ...editing, paymentMethodId: e.target.value ? Number(e.target.value) : null })}
                >
                  <option value="">Not recorded</option>
                  {methods.map((m) => <option key={m.id} value={m.id}>{m.name}</option>)}
                </select>
              </Field>
              <Field label="Reference number" help="Invoice or transaction reference.">
                <input
                  className="input"
                  value={editing.referenceNumber ?? ''}
                  onChange={(e) => setEditing({ ...editing, referenceNumber: e.target.value })}
                />
              </Field>
            </div>
            <Field label="Description">
              <textarea
                className="textarea"
                value={editing.description ?? ''}
                onChange={(e) => setEditing({ ...editing, description: e.target.value })}
              />
            </Field>
            <div className="form-note">Fields marked with * are mandatory.</div>
          </div>
        </Modal>
      )}

      {categoryManager && (
        <Modal
          title="Expense categories"
          icon={<IconBox size={18} />}
          onClose={() => setCategoryManager(false)}
          footer={<button className="btn btn-outline" onClick={() => setCategoryManager(false)}>Close</button>}
        >
          <div className="stack">
            {categoryError ? <ErrorAlert error={categoryError} /> : null}
            {categoryNotice ? <Alert tone="success">{categoryNotice}</Alert> : null}

            <div className="table-wrap">
              <table className="table">
                <thead>
                  <tr>
                    <th className="idx">#</th>
                    <th>Category</th>
                    <th className="num">Expenses</th>
                    <th className="num">Total</th>
                    <th>Status</th>
                  </tr>
                </thead>
                <tbody>
                  {categories.map((category, index) => (
                    <tr key={category.id}>
                      <td className="idx">{index + 1}</td>
                      <td>
                        <div className="cell-main">{category.name}</div>
                        <div className="cell-sub">{category.description || '—'}</div>
                      </td>
                      <td className="num">{category.expenseCount}</td>
                      <td className="num">{money(category.totalAmount, currency)}</td>
                      <td><Pill tone={category.isActive ? 'success' : 'neutral'}>{category.isActive ? 'Active' : 'Inactive'}</Pill></td>
                    </tr>
                  ))}
                  {categories.length === 0 && (
                    <tr><td colSpan={5} className="muted" style={{ textAlign: 'center' }}>No categories yet.</td></tr>
                  )}
                </tbody>
              </table>
            </div>

            {manage && (
              <div className="form-section">
                <div className="form-section-title">Add a category</div>
                <div className="form-grid" style={{ marginTop: 12 }}>
                  <Field label="Name" required help="2 to 120 characters.">
                    <input
                      className="input"
                      value={newCategory.name}
                      onChange={(e) => setNewCategory({ ...newCategory, name: e.target.value })}
                    />
                  </Field>
                  <Field label="Description">
                    <input
                      className="input"
                      value={newCategory.description}
                      onChange={(e) => setNewCategory({ ...newCategory, description: e.target.value })}
                    />
                  </Field>
                </div>
                <div className="form-footer">
                  <button className="btn btn-dark" onClick={() => void addCategory()} disabled={!newCategory.name.trim()}>
                    Add category
                  </button>
                </div>
              </div>
            )}
          </div>
        </Modal>
      )}

      {pendingDelete && (
        <ConfirmModal
          title="Delete expense"
          message={<>Delete <strong>{pendingDelete.expenseNumber}</strong> ({money(pendingDelete.amount, currency)})? It moves to the recycle bin.</>}
          confirmLabel="Delete expense"
          busy={busy}
          onConfirm={() => void confirmDelete()}
          onClose={() => setPendingDelete(null)}
        />
      )}
    </div>
  );
}
