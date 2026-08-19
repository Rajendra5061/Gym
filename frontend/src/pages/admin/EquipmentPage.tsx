import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '@/auth/AuthContext';
import {
  ConfirmModal, EmptyState, ErrorAlert, FilterField, FilterMenu, FilterStrip, Loading,
  PageCard, PageCardHeader, Pager, Pill, SearchField,
} from '@/components/ui';
import {
  IconBox, IconCalendar, IconPlus, IconSettings,
} from '@/components/icons';
import {
  EQUIPMENT_CONDITIONS, EquipmentCondition, conditionLabel, deleteEquipment, equipmentCategories,
  isModuleMissing, listEquipment, type EquipmentDto,
} from '@/api/endpoints/operations';
import type { PillTone } from '@/components/ui';
import type { PagedResult } from '@/api/types';
import { date as fmtDate } from '@/lib/format';
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

export default function EquipmentPage() {
  const navigate = useNavigate();
  const { can } = useAuth();
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
                <button className="btn btn-dark" onClick={() => navigate('/admin/equipment/new')}>
                  <IconPlus size={15} /> Add Equipment
                </button>
              ) : null}
            </>
          )}
        />

        {/* Search and Reset stay in the open, since they are the two controls reached most often. */}
        <FilterStrip>
          <SearchField
            placeholder="Name, code or serial"
            value={draft.search}
            onChange={(value) => setDraft({ ...draft, search: value })}
            onSearch={applyFilters}
          />
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
              ? <button className="btn btn-dark" onClick={() => navigate('/admin/equipment/new')}><IconPlus size={15} /> Add Equipment</button>
              : undefined}
          />
        )}

        {!loading && !error && !missing && items.length > 0 && (
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th className="idx">#</th>
                  <th className="fit">Code</th>
                  <th className="wide">Name</th>
                  <th className="fit">Category</th>
                  <th className="num">Quantity</th>
                  <th className="fit">Condition</th>
                  <th>Location</th>
                  <th className="center fit">Next service</th>
                  <th className="fit">Status</th>
                  <th className="actions">Actions</th>
                </tr>
              </thead>
              <tbody>
                {items.map((row, index) => (
                  <tr key={row.id}>
                    <td className="idx">{firstIndex + index + 1}</td>
                    <td className="cell-main fit">{row.code || '—'}</td>
                    <td className="wide">
                      <div className="cell-main">{row.name}</div>
                      <div className="cell-sub">{row.manufacturer || row.location || '—'}</div>
                    </td>
                    <td className="fit">{row.category ? <Pill tone="primary">{row.category}</Pill> : <span className="muted">—</span>}</td>
                    <td className="num">{row.quantity}</td>
                    <td className="fit">
                      <Pill tone={conditionTone(row.condition)}>
                        {conditionLabel(row.condition, row.conditionText)}
                      </Pill>
                    </td>
                    <td>{row.location || <span className="muted">—</span>}</td>
                    <td className="center fit">
                      <span className="cell-icon"><IconCalendar size={13} />{fmtDate(row.nextServiceDue)}</span>
                    </td>
                    <td className="fit">
                      <Pill tone={row.isActive ? 'success' : 'neutral'}>
                        {row.isActive ? 'Active' : 'Retired'}
                      </Pill>
                    </td>
                    <td className="actions">
                      {manage && (
                        <>
                          <button className="btn btn-edit" onClick={() => navigate(`/admin/equipment/${row.id}/edit`)}>
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
