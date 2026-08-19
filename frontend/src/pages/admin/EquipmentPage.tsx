import { useEffect, useState } from 'react';
import { useAuth } from '@/auth/AuthContext';
import {
  Alert, ConfirmModal, EmptyState, ErrorAlert, Field, FilterField, FilterMenu, FilterStrip, Loading,
  Modal, PageCard, PageCardHeader, Pager, Pill,
} from '@/components/ui';
import {
  IconBox, IconCalendar, IconPlus, IconSettings,
} from '@/components/icons';
import {
  EQUIPMENT_CONDITIONS, EquipmentCondition, conditionLabel, deleteEquipment, equipmentCategories,
  getEquipment, isModuleMissing, listEquipment, saveEquipment, type EquipmentDto,
} from '@/api/endpoints/operations';
import type { PillTone } from '@/components/ui';
import type { PagedResult } from '@/api/types';
import { date as fmtDate, isoDate, money } from '@/lib/format';
import './ops.css';

interface Filters {
  search: string;
  category: string;
  condition: EquipmentCondition | '';
}

const EMPTY_FILTERS: Filters = { search: '', category: '', condition: '' };

function conditionTone(condition: EquipmentCondition): PillTone {
  switch (condition) {
    case EquipmentCondition.New:
    case EquipmentCondition.Good:
      return 'success';
    case EquipmentCondition.NeedsService:
      return 'warning';
    case EquipmentCondition.UnderRepair:
    case EquipmentCondition.Retired:
      return 'danger';
    default:
      return 'neutral';
  }
}

function blankEquipment(): Partial<EquipmentDto> {
  return {
    id: 0,
    name: '',
    code: '',
    category: '',
    serialNumber: '',
    manufacturer: '',
    purchaseDate: '',
    purchaseCost: null,
    quantity: 1,
    condition: EquipmentCondition.New,
    location: '',
    warrantyExpiry: '',
    lastServicedOn: '',
    nextServiceDue: '',
    notes: '',
    isActive: true,
  };
}

export default function EquipmentPage() {
  const { can, currency } = useAuth();
  const manage = can('equipment.manage');

  const [draft, setDraft] = useState<Filters>(EMPTY_FILTERS);
  const [applied, setApplied] = useState<Filters>(EMPTY_FILTERS);
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [reloadKey, setReloadKey] = useState(0);

  const [data, setData] = useState<PagedResult<EquipmentDto> | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<unknown>(null);
  const [missing, setMissing] = useState(false);

  const [categories, setCategories] = useState<string[]>([]);
  const [editing, setEditing] = useState<Partial<EquipmentDto> | null>(null);
  const [editLoading, setEditLoading] = useState(false);
  const [formError, setFormError] = useState<unknown>(null);
  const [saving, setSaving] = useState(false);
  const [pendingDelete, setPendingDelete] = useState<EquipmentDto | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    (async () => {
      setLoading(true);
      try {
        const result = await listEquipment(
          {
            search: applied.search || undefined,
            category: applied.category || undefined,
            condition: applied.condition,
            pageNumber,
            pageSize,
          },
          controller.signal,
        );
        if (controller.signal.aborted) return;
        setData(result);
        setMissing(false);
        setError(null);
      } catch (err) {
        if (controller.signal.aborted) return;
        setData(null);
        if (isModuleMissing(err)) { setMissing(true); setError(null); }
        else { setMissing(false); setError(err); }
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
        const rows = await equipmentCategories(controller.signal);
        if (!controller.signal.aborted) setCategories(rows);
      } catch {
        if (!controller.signal.aborted) setCategories([]);
      }
    })();
    return () => controller.abort();
  }, [reloadKey]);

  /** Opens the editor on the full record, since the list row omits half the fields. */
  const openEdit = async (row: EquipmentDto) => {
    setFormError(null);
    setEditing({ ...row });
    setEditLoading(true);
    try {
      const full = await getEquipment(row.id);
      setEditing({
        ...full,
        purchaseDate: isoDate(full.purchaseDate),
        warrantyExpiry: isoDate(full.warrantyExpiry),
        lastServicedOn: isoDate(full.lastServicedOn),
        nextServiceDue: isoDate(full.nextServiceDue),
      });
    } catch (err) {
      setFormError(err);
    } finally {
      setEditLoading(false);
    }
  };

  const submit = async () => {
    if (!editing) return;
    setSaving(true);
    setFormError(null);
    try {
      await saveEquipment({
        ...editing,
        purchaseDate: editing.purchaseDate || null,
        warrantyExpiry: editing.warrantyExpiry || null,
        lastServicedOn: editing.lastServicedOn || null,
        nextServiceDue: editing.nextServiceDue || null,
      });
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
      await deleteEquipment(pendingDelete.id);
      setPendingDelete(null);
      setReloadKey((k) => k + 1);
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
  const activeFilterCount = (['category', 'condition'] as const)
    .filter((key) => applied[key] !== EMPTY_FILTERS[key]).length;

  const items = data?.items ?? [];
  const firstIndex = (pageNumber - 1) * pageSize;

  return (
    <div className="page">
      <PageCard>
        <PageCardHeader
          icon={<IconBox size={20} />}
          title="Equipment"
          subtitle="Your inventory, its condition and when each machine is next due for service."
          actions={(
            <>
              <FilterMenu
                activeCount={activeFilterCount}
                onApply={applyFilters}
                onReset={resetFilters}
              >
                <FilterField label="Category">
                  <select
                    className="select"
                    value={draft.category}
                    onChange={(e) => setDraft({ ...draft, category: e.target.value })}
                  >
                    <option value="">All categories</option>
                    {categories.map((c) => <option key={c} value={c}>{c}</option>)}
                  </select>
                </FilterField>

                <FilterField label="Condition">
                  <select
                    className="select"
                    value={draft.condition === '' ? '' : String(draft.condition)}
                    onChange={(e) => setDraft({
                      ...draft,
                      condition: e.target.value === '' ? '' : (Number(e.target.value) as EquipmentCondition),
                    })}
                  >
                    <option value="">All conditions</option>
                    {EQUIPMENT_CONDITIONS.map((c) => <option key={c.value} value={c.value}>{c.label}</option>)}
                  </select>
                </FilterField>
              </FilterMenu>

              {manage ? (
                <button className="btn btn-dark" onClick={() => { setEditing(blankEquipment()); setFormError(null); }}>
                  <IconPlus size={15} /> Add Equipment
                </button>
              ) : null}
            </>
          )}
        />

        {/* Search and Reset stay in the open, since they are the two controls reached most often. */}
        <FilterStrip>
          <FilterField label="Search">
            <input
              className="input"
              placeholder="Name, code or serial"
              value={draft.search}
              onChange={(e) => setDraft({ ...draft, search: e.target.value })}
              onKeyDown={(e) => { if (e.key === 'Enter') applyFilters(); }}
            />
          </FilterField>
        </FilterStrip>

        {loading && <Loading message="Loading equipment…" />}
        {!loading && Boolean(error) && <div className="page-card-body"><ErrorAlert error={error} /></div>}

        {!loading && missing && (
          <EmptyState
            icon={<IconSettings size={34} />}
            title="Equipment module is not available yet"
            message="The equipment endpoints are still being added to the API. This screen will fill in as soon as they ship."
          />
        )}

        {!loading && !error && !missing && items.length === 0 && (
          <EmptyState
            icon={<IconBox size={34} />}
            title="No equipment on record"
            message="Add treadmills, benches and machines to track their condition and service dates."
            action={manage
              ? <button className="btn btn-dark" onClick={() => setEditing(blankEquipment())}><IconPlus size={15} /> Add Equipment</button>
              : undefined}
          />
        )}

        {!loading && !error && !missing && items.length > 0 && (
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th className="idx">#</th>
                  <th>Code</th>
                  <th>Name</th>
                  <th>Category</th>
                  <th className="num">Quantity</th>
                  <th>Condition</th>
                  <th>Location</th>
                  <th className="center">Next service</th>
                  <th>Status</th>
                  <th className="actions">Actions</th>
                </tr>
              </thead>
              <tbody>
                {items.map((row, index) => (
                  <tr key={row.id}>
                    <td className="idx">{firstIndex + index + 1}</td>
                    <td className="cell-main">{row.code || '—'}</td>
                    <td>
                      <div className="cell-main">{row.name}</div>
                      <div className="cell-sub">{row.manufacturer || row.location || '—'}</div>
                    </td>
                    <td>{row.category ? <Pill tone="primary">{row.category}</Pill> : <span className="muted">—</span>}</td>
                    <td className="num">{row.quantity}</td>
                    <td>
                      <Pill tone={conditionTone(row.condition)}>
                        {conditionLabel(row.condition, row.conditionText)}
                      </Pill>
                    </td>
                    <td>{row.location || <span className="muted">—</span>}</td>
                    <td className="center">
                      <span className="cell-icon"><IconCalendar size={13} />{fmtDate(row.nextServiceDue)}</span>
                    </td>
                    <td>
                      <Pill tone={row.isActive ? 'success' : 'neutral'}>
                        {row.isActive ? 'Active' : 'Retired'}
                      </Pill>
                    </td>
                    <td className="actions">
                      {manage && (
                        <>
                          <button className="btn btn-edit" onClick={() => void openEdit(row)}>
                            Edit
                          </button>
                          <button className="btn btn-del" onClick={() => setPendingDelete(row)}>Delete</button>
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

        {!loading && !error && !missing && data && data.totalCount > 0 && (
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
          title={editing.id ? 'Edit equipment' : 'Add equipment'}
          icon={<IconBox size={18} />}
          onClose={() => setEditing(null)}
          width={820}
          footer={
            <>
              <button className="btn btn-outline" onClick={() => setEditing(null)} disabled={saving}>Cancel</button>
              <button className="btn btn-dark" onClick={() => void submit()} disabled={saving}>
                {saving ? 'Saving…' : 'Save equipment'}
              </button>
            </>
          }
        >
          <div className="stack">
            {formError ? <ErrorAlert error={formError} /> : null}
            {editLoading ? <Alert tone="info">Loading the full record…</Alert> : null}
            <div className="form-grid">
              <Field label="Name" required>
                <input
                  className="input"
                  placeholder="e.g. Treadmill T-500"
                  value={editing.name ?? ''}
                  onChange={(e) => setEditing({ ...editing, name: e.target.value })}
                />
              </Field>
              <Field label="Code" help="Your own asset tag or sticker number.">
                <input
                  className="input"
                  value={editing.code ?? ''}
                  onChange={(e) => setEditing({ ...editing, code: e.target.value })}
                />
              </Field>
              <Field label="Category" help="Cardio, strength, free weights…">
                <input
                  className="input"
                  value={editing.category ?? ''}
                  onChange={(e) => setEditing({ ...editing, category: e.target.value })}
                />
              </Field>
              <Field label="Condition" required>
                <select
                  className="select"
                  value={editing.condition ?? EquipmentCondition.New}
                  onChange={(e) => setEditing({ ...editing, condition: Number(e.target.value) as EquipmentCondition })}
                >
                  {EQUIPMENT_CONDITIONS.map((c) => <option key={c.value} value={c.value}>{c.label}</option>)}
                </select>
              </Field>
              <Field label="Quantity" required>
                <input
                  className="input"
                  type="number"
                  min={1}
                  value={editing.quantity ?? 1}
                  onChange={(e) => setEditing({ ...editing, quantity: Number(e.target.value) })}
                />
              </Field>
              <Field label="Manufacturer">
                <input
                  className="input"
                  value={editing.manufacturer ?? ''}
                  onChange={(e) => setEditing({ ...editing, manufacturer: e.target.value })}
                />
              </Field>
              <Field label="Serial number">
                <input
                  className="input"
                  value={editing.serialNumber ?? ''}
                  onChange={(e) => setEditing({ ...editing, serialNumber: e.target.value })}
                />
              </Field>
              <Field label="Location" help="Which floor or zone it sits in.">
                <input
                  className="input"
                  value={editing.location ?? ''}
                  onChange={(e) => setEditing({ ...editing, location: e.target.value })}
                />
              </Field>
              <Field label="Purchase date">
                <input
                  className="input"
                  type="date"
                  value={editing.purchaseDate ?? ''}
                  onChange={(e) => setEditing({ ...editing, purchaseDate: e.target.value })}
                />
              </Field>
              <Field
                label="Purchase cost"
                help={editing.purchaseCost ? `Currently ${money(editing.purchaseCost, currency)}` : undefined}
              >
                <input
                  className="input"
                  type="number"
                  min={0}
                  step="0.01"
                  value={editing.purchaseCost ?? ''}
                  onChange={(e) => setEditing({
                    ...editing,
                    purchaseCost: e.target.value === '' ? null : Number(e.target.value),
                  })}
                />
              </Field>
              <Field label="Warranty expiry">
                <input
                  className="input"
                  type="date"
                  value={editing.warrantyExpiry ?? ''}
                  onChange={(e) => setEditing({ ...editing, warrantyExpiry: e.target.value })}
                />
              </Field>
              <Field label="Last serviced on">
                <input
                  className="input"
                  type="date"
                  value={editing.lastServicedOn ?? ''}
                  onChange={(e) => setEditing({ ...editing, lastServicedOn: e.target.value })}
                />
              </Field>
              <Field label="Next service due" help="Drives the service reminder on this list.">
                <input
                  className="input"
                  type="date"
                  value={editing.nextServiceDue ?? ''}
                  onChange={(e) => setEditing({ ...editing, nextServiceDue: e.target.value })}
                />
              </Field>
              <Field label="Status">
                <select
                  className="select"
                  value={String(editing.isActive ?? true)}
                  onChange={(e) => setEditing({ ...editing, isActive: e.target.value === 'true' })}
                >
                  <option value="true">Active</option>
                  <option value="false">Inactive</option>
                </select>
              </Field>
            </div>
            <Field label="Notes">
              <textarea
                className="textarea"
                value={editing.notes ?? ''}
                onChange={(e) => setEditing({ ...editing, notes: e.target.value })}
              />
            </Field>
            <div className="form-note">Fields marked with * are mandatory.</div>
          </div>
        </Modal>
      )}

      {pendingDelete && (
        <ConfirmModal
          title="Delete equipment"
          message={<>Remove <strong>{pendingDelete.name}</strong> from the inventory? It moves to the recycle bin.</>}
          confirmLabel="Delete"
          busy={busy}
          onConfirm={() => void confirmDelete()}
          onClose={() => setPendingDelete(null)}
        />
      )}
    </div>
  );
}
