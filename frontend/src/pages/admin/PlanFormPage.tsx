import { useEffect, useMemo, useRef, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { ApiError } from '@/api/client';
import { plansApi, type PlanInput } from '@/api/endpoints/plans';
import { PlanDurationType, PlanStatus } from '@/api/types';
import { useAuth } from '@/auth/AuthContext';
import {
  Alert, ErrorAlert, Field, FormSection, Loading, PageCard, PageCardHeader,
} from '@/components/ui';
import {
  IconArrowLeft, IconCheck, IconCrown, IconFile, IconMoney,
} from '@/components/icons';
import { words } from '@/lib/format';
import './billing.css';

interface FormState {
  name: string; status: string; durationType: string; durationValue: string;
  price: string; registrationFee: string; taxPercent: string; maxDiscountPercent: string;
  gracePeriodDays: string; maxFreezeDays: string; sessionLimit: string; displayOrder: string;
  trainerIncluded: boolean; description: string; features: string;
}

const BLANK: FormState = {
  name: '', status: String(PlanStatus.Active), durationType: String(PlanDurationType.Month),
  durationValue: '1', price: '0', registrationFee: '', taxPercent: '0', maxDiscountPercent: '100',
  gracePeriodDays: '0', maxFreezeDays: '0', sessionLimit: '', displayOrder: '0',
  trainerIncluded: false, description: '', features: '',
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

/** Blank or unparsable input goes to the server as 0 — its validators own the real rules. */
function numberOrZero(value: string): number {
  return numberOrNull(value) ?? 0;
}

/** `features` is stored as one newline-separated blob; the plan card renders it as a ticked list. */
function featureList(features: string): string[] {
  return features
    .split(/\r?\n/)
    .map((line) => line.replace(/^[-*•]\s*/, '').trim())
    .filter(Boolean);
}

const DURATION_TYPE_OPTIONS = Object.values(PlanDurationType)
  .filter((v): v is PlanDurationType => typeof v === 'number');

export default function PlanFormPage() {
  const navigate = useNavigate();
  const { id } = useParams<{ id?: string }>();
  const planId = id ? Number(id) : null;
  const isEdit = planId !== null;
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

  const allowed = can('plans.manage');

  useEffect(() => {
    if (planId === null) return;
    const controller = new AbortController();
    let current = true;
    setLoading(true);
    plansApi.byId(planId, controller.signal)
      .then((p) => {
        if (!current) return;
        setForm({
          name: p.name,
          status: String(p.status),
          durationType: String(p.durationType),
          durationValue: String(p.durationValue),
          price: String(p.price),
          registrationFee: p.registrationFee === null || p.registrationFee === undefined
            ? '' : String(p.registrationFee),
          taxPercent: String(p.taxPercent),
          maxDiscountPercent: String(p.maxDiscountPercent),
          gracePeriodDays: String(p.gracePeriodDays),
          maxFreezeDays: String(p.maxFreezeDays),
          sessionLimit: p.sessionLimit === null || p.sessionLimit === undefined
            ? '' : String(p.sessionLimit),
          displayOrder: String(p.displayOrder),
          trainerIncluded: p.trainerIncluded,
          description: p.description ?? '',
          features: p.features ?? '',
        });
        setError(null);
        setLoadError(null);
      })
      .catch((err) => { if (current) { setLoadError(err); setError(null); } })
      .finally(() => { if (current) setLoading(false); });
    return () => { current = false; controller.abort(); };
  }, [planId]);

  function set<K extends keyof FormState>(key: K, value: FormState[K]) {
    setForm((f) => ({ ...f, [key]: value }));
  }

  const previewFeatures = useMemo(() => featureList(form.features), [form.features]);

  async function save() {
    setSaving(true);
    setError(null);
    setErrors({});

    const body: PlanInput = {
      id: planId ?? 0,
      name: form.name.trim(),
      description: form.description.trim() || null,
      features: form.features.trim() || null,
      durationType: Number(form.durationType) as PlanDurationType,
      durationValue: numberOrZero(form.durationValue),
      price: numberOrZero(form.price),
      registrationFee: numberOrNull(form.registrationFee),
      taxPercent: numberOrZero(form.taxPercent),
      maxDiscountPercent: numberOrZero(form.maxDiscountPercent),
      gracePeriodDays: numberOrZero(form.gracePeriodDays),
      maxFreezeDays: numberOrZero(form.maxFreezeDays),
      sessionLimit: numberOrNull(form.sessionLimit),
      trainerIncluded: form.trainerIncluded,
      displayOrder: numberOrZero(form.displayOrder),
      status: Number(form.status) as PlanStatus,
    };

    try {
      if (isEdit && planId !== null) await plansApi.update(planId, body);
      else await plansApi.create(body);
      if (!alive.current) return;
      navigate('/admin/plans');
    } catch (err) {
      if (!alive.current) return;
      setError(err);
      if (err instanceof ApiError) setErrors(err.validationErrors);
      window.scrollTo({ top: 0, behavior: 'smooth' });
    } finally {
      if (alive.current) setSaving(false);
    }
  }

  // The record could not be read — the id is wrong, or someone else deleted the plan. An editable
  // form over nothing offers a Save that cannot mean anything, so the page shows the failure and
  // a way back instead.
  if (isEdit && !loading && loadError) {
    return (
      <div className="page">
        <PageCard>
          <PageCardHeader
            icon={<IconCrown size={20} />}
            title="Edit Membership Plan"
            actions={
              <button className="btn btn-outline" onClick={() => navigate('/admin/plans')}>
                <IconArrowLeft size={15} /> Back to Plans
              </button>
            }
          />
          <div className="page-card-body">
            <ErrorAlert error={loadError} />
            <div className="form-note">
              Nothing can be edited until a plan that exists is opened.
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
          icon={<IconCrown size={20} />}
          title={isEdit ? 'Edit Membership Plan' : 'Add Membership Plan'}
          subtitle={isEdit
            ? 'Update the price, duration and rules this plan is sold against.'
            : 'Define a plan so subscriptions have something to sell.'}
          actions={
            <button className="btn btn-outline" onClick={() => navigate('/admin/plans')}>
              <IconArrowLeft size={15} /> Back to Plans
            </button>
          }
        />

        <div className="page-card-body">
          {!allowed ? (
            <Alert tone="warning">
              You do not have permission to {isEdit ? 'edit' : 'add'} membership plans.
            </Alert>
          ) : loading ? (
            <Loading message="Loading plan..." />
          ) : (
            <>
              {error ? <div style={{ marginBottom: 18 }}><ErrorAlert error={error} /></div> : null}

              <FormSection
                title="Plan Details"
                caption="The name, term and headline price members see."
                icon={<IconCrown size={15} />}
              >
                <div className="form-grid">
                  <Field label="Plan name" required error={fieldError(errors, 'Name')}>
                    <input
                      className={`input ${fieldError(errors, 'Name') ? 'input-invalid' : ''}`}
                      value={form.name}
                      maxLength={120}
                      onChange={(e) => set('name', e.target.value)}
                      autoFocus
                    />
                  </Field>
                  <Field label="Status" help="Inactive plans stay in the catalogue but cannot be sold.">
                    <select className="select" value={form.status} onChange={(e) => set('status', e.target.value)}>
                      <option value={PlanStatus.Active}>Active</option>
                      <option value={PlanStatus.Inactive}>Inactive</option>
                    </select>
                  </Field>
                  <Field label="Duration type" required error={fieldError(errors, 'DurationType')}>
                    <select
                      className="select"
                      value={form.durationType}
                      onChange={(e) => set('durationType', e.target.value)}
                    >
                      {DURATION_TYPE_OPTIONS.map((v) => (
                        <option key={v} value={v}>{words(PlanDurationType[v])}</option>
                      ))}
                    </select>
                  </Field>
                  <Field
                    label="Duration value"
                    required
                    error={fieldError(errors, 'DurationValue')}
                    help="1 to 3650. The server derives the term length from this."
                  >
                    <input
                      className={`input ${fieldError(errors, 'DurationValue') ? 'input-invalid' : ''}`}
                      type="number" min={1} max={3650}
                      value={form.durationValue}
                      onChange={(e) => set('durationValue', e.target.value)}
                    />
                  </Field>
                  <Field
                    label="Price"
                    required
                    error={fieldError(errors, 'Price')}
                    help={`Charged per term, in ${currency}.`}
                  >
                    <div className="input-group">
                      <span className="input-icon">{currency}</span>
                      <input
                        className={`input ${fieldError(errors, 'Price') ? 'input-invalid' : ''}`}
                        type="number" min={0} step="0.01"
                        value={form.price}
                        onChange={(e) => set('price', e.target.value)}
                      />
                    </div>
                  </Field>
                  <Field
                    label="Registration fee"
                    error={fieldError(errors, 'RegistrationFee')}
                    help="Leave empty when the plan has no joining fee."
                  >
                    <div className="input-group">
                      <span className="input-icon">{currency}</span>
                      <input
                        className={`input ${fieldError(errors, 'RegistrationFee') ? 'input-invalid' : ''}`}
                        type="number" min={0} step="0.01"
                        value={form.registrationFee}
                        onChange={(e) => set('registrationFee', e.target.value)}
                      />
                    </div>
                  </Field>
                  <Field
                    label="Display order"
                    error={fieldError(errors, 'DisplayOrder')}
                    help="Lower numbers list first on the plan gallery."
                  >
                    <input
                      className={`input ${fieldError(errors, 'DisplayOrder') ? 'input-invalid' : ''}`}
                      type="number" min={0}
                      value={form.displayOrder}
                      onChange={(e) => set('displayOrder', e.target.value)}
                    />
                  </Field>
                </div>
              </FormSection>

              <FormSection
                title="Rules & Limits"
                caption="Tax, discount ceilings and the allowances every subscription inherits."
                icon={<IconMoney size={15} />}
              >
                <div className="form-grid">
                  <Field label="Tax percent" error={fieldError(errors, 'TaxPercent')}>
                    <input
                      className={`input ${fieldError(errors, 'TaxPercent') ? 'input-invalid' : ''}`}
                      type="number" min={0} max={100} step="0.01"
                      value={form.taxPercent}
                      onChange={(e) => set('taxPercent', e.target.value)}
                    />
                  </Field>
                  <Field
                    label="Max discount percent"
                    error={fieldError(errors, 'MaxDiscountPercent')}
                    help="Caps the discount an operator may key in."
                  >
                    <input
                      className={`input ${fieldError(errors, 'MaxDiscountPercent') ? 'input-invalid' : ''}`}
                      type="number" min={0} max={100} step="0.01"
                      value={form.maxDiscountPercent}
                      onChange={(e) => set('maxDiscountPercent', e.target.value)}
                    />
                  </Field>
                  <Field label="Grace period (days)" error={fieldError(errors, 'GracePeriodDays')}>
                    <input
                      className={`input ${fieldError(errors, 'GracePeriodDays') ? 'input-invalid' : ''}`}
                      type="number" min={0} max={90}
                      value={form.gracePeriodDays}
                      onChange={(e) => set('gracePeriodDays', e.target.value)}
                    />
                  </Field>
                  <Field label="Max freeze days" error={fieldError(errors, 'MaxFreezeDays')}>
                    <input
                      className={`input ${fieldError(errors, 'MaxFreezeDays') ? 'input-invalid' : ''}`}
                      type="number" min={0} max={365}
                      value={form.maxFreezeDays}
                      onChange={(e) => set('maxFreezeDays', e.target.value)}
                    />
                  </Field>
                  <Field
                    label="Session limit"
                    error={fieldError(errors, 'SessionLimit')}
                    help="Leave empty for unlimited sessions."
                  >
                    <input
                      className={`input ${fieldError(errors, 'SessionLimit') ? 'input-invalid' : ''}`}
                      type="number" min={1}
                      value={form.sessionLimit}
                      onChange={(e) => set('sessionLimit', e.target.value)}
                    />
                  </Field>
                </div>
                <div style={{ marginTop: 'var(--sp-4)' }}>
                  <label className="check-inline">
                    <input
                      type="checkbox"
                      checked={form.trainerIncluded}
                      onChange={(e) => set('trainerIncluded', e.target.checked)}
                    />
                    Personal trainer included in this plan
                  </label>
                </div>
              </FormSection>

              <FormSection
                title="Description & Features"
                caption="What the plan card says and the ticked list it shows."
                icon={<IconFile size={15} />}
              >
                <div className="form-grid" style={{ gridTemplateColumns: '1fr' }}>
                  <Field label="Description" error={fieldError(errors, 'Description')}>
                    <textarea
                      className={`textarea ${fieldError(errors, 'Description') ? 'input-invalid' : ''}`}
                      style={{ minHeight: 70 }}
                      value={form.description}
                      onChange={(e) => set('description', e.target.value)}
                    />
                  </Field>
                  <Field
                    label="Features"
                    error={fieldError(errors, 'Features')}
                    help="One feature per line. The plan card shows them as a ticked list."
                  >
                    <textarea
                      className={`textarea ${fieldError(errors, 'Features') ? 'input-invalid' : ''}`}
                      value={form.features}
                      placeholder={'Unlimited gym access\nLocker included\n2 guest passes'}
                      onChange={(e) => set('features', e.target.value)}
                    />
                  </Field>
                </div>
                {previewFeatures.length > 0 && (
                  <div style={{ marginTop: 'var(--sp-4)' }}>
                    <ul className="plan-features">
                      {previewFeatures.map((feature, i) => (
                        <li key={i}><IconCheck size={14} /><span>{feature}</span></li>
                      ))}
                    </ul>
                  </div>
                )}
              </FormSection>

              <div className="form-footer">
                <button className="btn btn-dark" onClick={save} disabled={saving || !form.name.trim()}>
                  {saving ? 'Saving…' : 'Save Plan'}
                </button>
                <button
                  className="btn btn-outline"
                  onClick={() => navigate('/admin/plans')}
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
