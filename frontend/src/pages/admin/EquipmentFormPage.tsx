import { useEffect, useRef, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { ApiError } from '@/api/client';
import {
  EQUIPMENT_CONDITIONS, EquipmentCondition, getEquipment, saveEquipment, type EquipmentDto,
} from '@/api/endpoints/operations';
import { useAuth } from '@/auth/AuthContext';
import {
  Alert, ErrorAlert, Field, FormSection, Loading, PageCard, PageCardHeader,
} from '@/components/ui';
import {
  IconArrowLeft, IconBox, IconCalendar, IconSettings,
} from '@/components/icons';
import { money } from '@/lib/format';
import './ops.css';

/**
 * yyyy-MM-dd for a `<input type="date">`.
 *
 * The API sends a plain calendar date ("2026-08-18T00:00:00"), so the day is taken straight off
 * the string. `isoDate` in lib/format routes through `toISOString`, which converts local midnight
 * to the previous evening UTC and slips the date back a day in any zone ahead of UTC — enough to
 * walk a purchase or service date backwards once per edit-and-save.
 */
function toDateInput(value: string | null | undefined): string {
  return value ? value.slice(0, 10) : '';
}

interface FormState {
  name: string; code: string; category: string; serialNumber: string; manufacturer: string;
  quantity: string; condition: string; location: string; isActive: string;
  purchaseDate: string; purchaseCost: string; warrantyExpiry: string;
  lastServicedOn: string; nextServiceDue: string; notes: string;
}

const BLANK: FormState = {
  name: '', code: '', category: '', serialNumber: '', manufacturer: '',
  quantity: '1', condition: String(EquipmentCondition.New), location: '', isActive: 'true',
  purchaseDate: '', purchaseCost: '', warrantyExpiry: '',
  lastServicedOn: '', nextServiceDue: '', notes: '',
};

/** Server validation keys arrive in the C# PascalCase; match them case-insensitively. */
function fieldError(errors: Record<string, string[]>, name: string): string | undefined {
  const key = Object.keys(errors).find((k) => k.toLowerCase() === name.toLowerCase());
  return key ? errors[key][0] : undefined;
}

function numberOrNull(value: string): number | null {
  const trimmed = value.trim();
  if (!trimmed) return null;
  const parsed = Number(trimmed);
  return Number.isFinite(parsed) ? parsed : null;
}

export default function EquipmentFormPage() {
  const navigate = useNavigate();
  const { id } = useParams<{ id?: string }>();
  const equipmentId = id ? Number(id) : null;
  const isEdit = equipmentId !== null;
  const { can, currency } = useAuth();

  const [form, setForm] = useState<FormState>(BLANK);
  const [loading, setLoading] = useState(isEdit);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<unknown>(null);
  // Kept apart from `error`: a save that fails must leave the form on screen, but a record that
  // never loaded has nothing to save, so the form is replaced entirely.
  const [loadError, setLoadError] = useState<unknown>(null);
  const [errors, setErrors] = useState<Record<string, string[]>>({});

  const alive = useRef(true);
  useEffect(() => {
    alive.current = true;
    return () => { alive.current = false; };
  }, []);

  const allowed = can('equipment.manage');

  // The list projection carries no purchase, warranty or serial fields, so edit mode always
  // loads the full record — as the modal it replaced did via `openEdit`.
  useEffect(() => {
    if (equipmentId === null) return;
    const controller = new AbortController();
    let current = true;
    setLoading(true);
    getEquipment(equipmentId, controller.signal)
      .then((e) => {
        if (!current) return;
        setForm({
          name: e.name,
          code: e.code ?? '',
          category: e.category ?? '',
          serialNumber: e.serialNumber ?? '',
          manufacturer: e.manufacturer ?? '',
          quantity: String(e.quantity),
          condition: String(e.condition),
          location: e.location ?? '',
          isActive: String(e.isActive),
          purchaseDate: toDateInput(e.purchaseDate),
          purchaseCost: e.purchaseCost === null || e.purchaseCost === undefined
            ? '' : String(e.purchaseCost),
          warrantyExpiry: toDateInput(e.warrantyExpiry),
          lastServicedOn: toDateInput(e.lastServicedOn),
          nextServiceDue: toDateInput(e.nextServiceDue),
          notes: e.notes ?? '',
        });
        setError(null);
        setLoadError(null);
      })
      .catch((err) => { if (current) { setLoadError(err); setError(null); } })
      .finally(() => { if (current) setLoading(false); });
    return () => { current = false; controller.abort(); };
  }, [equipmentId]);

  function set<K extends keyof FormState>(key: K, value: FormState[K]) {
    setForm((f) => ({ ...f, [key]: value }));
  }

  async function save() {
    setSaving(true);
    setError(null);
    setErrors({});

    // `saveEquipment` routes on `id`: a real id updates, 0 creates.
    const body: Partial<EquipmentDto> = {
      id: isEdit && equipmentId !== null ? equipmentId : 0,
      name: form.name.trim(),
      code: form.code.trim() || null,
      category: form.category.trim() || null,
      serialNumber: form.serialNumber.trim() || null,
      manufacturer: form.manufacturer.trim() || null,
      quantity: numberOrNull(form.quantity) ?? 0,
      condition: Number(form.condition) as EquipmentCondition,
      location: form.location.trim() || null,
      isActive: form.isActive === 'true',
      purchaseDate: form.purchaseDate || null,
      purchaseCost: numberOrNull(form.purchaseCost),
      warrantyExpiry: form.warrantyExpiry || null,
      lastServicedOn: form.lastServicedOn || null,
      nextServiceDue: form.nextServiceDue || null,
      notes: form.notes.trim() || null,
    };

    try {
      await saveEquipment(body);
      if (!alive.current) return;
      navigate('/admin/equipment');
    } catch (err) {
      if (!alive.current) return;
      setError(err);
      if (err instanceof ApiError) setErrors(err.validationErrors);
      window.scrollTo({ top: 0, behavior: 'smooth' });
    } finally {
      if (alive.current) setSaving(false);
    }
  }

  // The record could not be read — the id is wrong, or someone else deleted the machine. An
  // editable form over nothing offers a Save that cannot mean anything, so the page shows the
  // failure and a way back instead.
  if (isEdit && !loading && loadError) {
    return (
      <div className="page">
        <PageCard>
          <PageCardHeader
            icon={<IconBox size={20} />}
            title="Edit Equipment"
            actions={
              <button className="btn btn-outline" onClick={() => navigate('/admin/equipment')}>
                <IconArrowLeft size={15} /> Back to Equipment
              </button>
            }
          />
          <div className="page-card-body">
            <ErrorAlert error={loadError} />
            <div className="form-note">
              Nothing can be edited until a piece of equipment that exists is opened.
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
          icon={<IconBox size={20} />}
          title={isEdit ? 'Edit Equipment' : 'Add Equipment'}
          subtitle={isEdit
            ? 'Update the machine, its condition and its service schedule.'
            : 'Add a treadmill, bench or machine to the inventory.'}
          actions={
            <button className="btn btn-outline" onClick={() => navigate('/admin/equipment')}>
              <IconArrowLeft size={15} /> Back to Equipment
            </button>
          }
        />

        <div className="page-card-body">
          {!allowed ? (
            <Alert tone="warning">
              You do not have permission to {isEdit ? 'edit' : 'add'} equipment.
            </Alert>
          ) : loading ? (
            <Loading message="Loading equipment..." />
          ) : (
            <>
              {error ? <div style={{ marginBottom: 18 }}><ErrorAlert error={error} /></div> : null}

              <FormSection
                title="Basic Information"
                caption="What the machine is, where it sits and what shape it is in."
                icon={<IconBox size={15} />}
              >
                <div className="form-grid">
                  <Field label="Name" required error={fieldError(errors, 'Name')}>
                    <input
                      className={`input ${fieldError(errors, 'Name') ? 'input-invalid' : ''}`}
                      placeholder="e.g. Treadmill T-500"
                      value={form.name}
                      onChange={(e) => set('name', e.target.value)}
                      autoFocus
                    />
                  </Field>
                  <Field
                    label="Code"
                    error={fieldError(errors, 'Code')}
                    help="Your own asset tag or sticker number."
                  >
                    <input
                      className={`input ${fieldError(errors, 'Code') ? 'input-invalid' : ''}`}
                      value={form.code}
                      onChange={(e) => set('code', e.target.value)}
                    />
                  </Field>
                  <Field
                    label="Category"
                    error={fieldError(errors, 'Category')}
                    help="Cardio, strength, free weights…"
                  >
                    <input
                      className={`input ${fieldError(errors, 'Category') ? 'input-invalid' : ''}`}
                      value={form.category}
                      onChange={(e) => set('category', e.target.value)}
                    />
                  </Field>
                  <Field label="Condition" required>
                    <select
                      className="select"
                      value={form.condition}
                      onChange={(e) => set('condition', e.target.value)}
                    >
                      {EQUIPMENT_CONDITIONS.map((c) => (
                        <option key={c.value} value={c.value}>{c.label}</option>
                      ))}
                    </select>
                  </Field>
                  <Field label="Quantity" required error={fieldError(errors, 'Quantity')}>
                    <input
                      className={`input ${fieldError(errors, 'Quantity') ? 'input-invalid' : ''}`}
                      type="number" min={1}
                      value={form.quantity}
                      onChange={(e) => set('quantity', e.target.value)}
                    />
                  </Field>
                  <Field label="Manufacturer" error={fieldError(errors, 'Manufacturer')}>
                    <input
                      className={`input ${fieldError(errors, 'Manufacturer') ? 'input-invalid' : ''}`}
                      value={form.manufacturer}
                      onChange={(e) => set('manufacturer', e.target.value)}
                    />
                  </Field>
                  <Field label="Serial number" error={fieldError(errors, 'SerialNumber')}>
                    <input
                      className={`input ${fieldError(errors, 'SerialNumber') ? 'input-invalid' : ''}`}
                      value={form.serialNumber}
                      onChange={(e) => set('serialNumber', e.target.value)}
                    />
                  </Field>
                  <Field
                    label="Location"
                    error={fieldError(errors, 'Location')}
                    help="Which floor or zone it sits in."
                  >
                    <input
                      className={`input ${fieldError(errors, 'Location') ? 'input-invalid' : ''}`}
                      value={form.location}
                      onChange={(e) => set('location', e.target.value)}
                    />
                  </Field>
                  <Field label="Status" help="Drives the Active pill on the equipment list.">
                    <select
                      className="select"
                      value={form.isActive}
                      onChange={(e) => set('isActive', e.target.value)}
                    >
                      <option value="true">Active</option>
                      <option value="false">Inactive</option>
                    </select>
                  </Field>
                </div>
              </FormSection>

              <FormSection
                title="Purchase & Warranty"
                caption="When it was bought, what it cost and how long it is covered."
                icon={<IconCalendar size={15} />}
                optional
              >
                <div className="form-grid">
                  <Field label="Purchase date" error={fieldError(errors, 'PurchaseDate')}>
                    <input
                      className={`input ${fieldError(errors, 'PurchaseDate') ? 'input-invalid' : ''}`}
                      type="date"
                      value={form.purchaseDate}
                      onChange={(e) => set('purchaseDate', e.target.value)}
                    />
                  </Field>
                  <Field
                    label="Purchase cost"
                    error={fieldError(errors, 'PurchaseCost')}
                    help={numberOrNull(form.purchaseCost) !== null
                      ? `Currently ${money(numberOrNull(form.purchaseCost), currency)}`
                      : undefined}
                  >
                    <input
                      className={`input ${fieldError(errors, 'PurchaseCost') ? 'input-invalid' : ''}`}
                      type="number" min={0} step="0.01"
                      value={form.purchaseCost}
                      onChange={(e) => set('purchaseCost', e.target.value)}
                    />
                  </Field>
                  <Field label="Warranty expiry" error={fieldError(errors, 'WarrantyExpiry')}>
                    <input
                      className={`input ${fieldError(errors, 'WarrantyExpiry') ? 'input-invalid' : ''}`}
                      type="date"
                      value={form.warrantyExpiry}
                      onChange={(e) => set('warrantyExpiry', e.target.value)}
                    />
                  </Field>
                </div>
              </FormSection>

              <FormSection
                title="Maintenance"
                caption="The service history and the reminder that keeps it running."
                icon={<IconSettings size={15} />}
                optional
              >
                <div className="form-grid">
                  <Field label="Last serviced on" error={fieldError(errors, 'LastServicedOn')}>
                    <input
                      className={`input ${fieldError(errors, 'LastServicedOn') ? 'input-invalid' : ''}`}
                      type="date"
                      value={form.lastServicedOn}
                      onChange={(e) => set('lastServicedOn', e.target.value)}
                    />
                  </Field>
                  <Field
                    label="Next service due"
                    error={fieldError(errors, 'NextServiceDue')}
                    help="Drives the service reminder on the equipment list."
                  >
                    <input
                      className={`input ${fieldError(errors, 'NextServiceDue') ? 'input-invalid' : ''}`}
                      type="date"
                      value={form.nextServiceDue}
                      onChange={(e) => set('nextServiceDue', e.target.value)}
                    />
                  </Field>
                </div>
                <div style={{ marginTop: 'var(--sp-4)' }}>
                  <Field label="Notes">
                    <textarea
                      className="textarea"
                      value={form.notes}
                      onChange={(e) => set('notes', e.target.value)}
                      placeholder="Service contracts, spare parts, remarks..."
                    />
                  </Field>
                </div>
              </FormSection>

              <div className="form-footer">
                <button className="btn btn-dark" onClick={save} disabled={saving}>
                  {saving ? 'Saving...' : 'Save Equipment'}
                </button>
                <button
                  className="btn btn-outline"
                  onClick={() => navigate('/admin/equipment')}
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
