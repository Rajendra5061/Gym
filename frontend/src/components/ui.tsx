import { useEffect, useRef, useState, type ReactNode } from 'react';
import { words } from '@/lib/format';
import { IconFilter, IconSearch } from '@/components/icons';

/* ------------------------------------------------------------------ page card */

export function PageCard({ children, className = '' }: { children: ReactNode; className?: string }) {
  return <section className={`page-card ${className}`}>{children}</section>;
}

export function PageCardHeader(
  { icon, title, subtitle, actions }:
  { icon?: ReactNode; title: ReactNode; subtitle?: ReactNode; actions?: ReactNode },
) {
  return (
    <header className="page-card-header">
      <div className="grow">
        <h2 className="page-card-title">{icon}{title}</h2>
        {subtitle ? <div className="page-card-subtitle">{subtitle}</div> : null}
      </div>
      {actions ? <div className="row">{actions}</div> : null}
    </header>
  );
}

export function FilterStrip({ children }: { children: ReactNode }) {
  return <div className="filter-strip">{children}</div>;
}

/**
 * Secondary filters folded behind one button, so a screen shows a search box and little else
 * until someone asks for more. `activeCount` puts a badge on the trigger — without it a filter
 * applied earlier is invisible once the panel closes, and an unexplained empty table follows.
 */
export function FilterMenu(
  { children, activeCount = 0, onApply, onReset, label = 'Filters' }:
  {
    children: ReactNode; activeCount?: number;
    onApply: () => void; onReset?: () => void; label?: string;
  },
) {
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    function onPointerDown(event: MouseEvent) {
      if (!ref.current?.contains(event.target as Node)) setOpen(false);
    }
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') setOpen(false);
    }
    document.addEventListener('mousedown', onPointerDown);
    document.addEventListener('keydown', onKeyDown);
    return () => {
      document.removeEventListener('mousedown', onPointerDown);
      document.removeEventListener('keydown', onKeyDown);
    };
  }, [open]);

  return (
    <div className="filter-menu" ref={ref}>
      <button
        type="button"
        className={`btn btn-outline filter-menu-trigger ${open ? 'open' : ''}`}
        aria-haspopup="dialog"
        aria-expanded={open}
        title={label}
        onClick={() => setOpen((value) => !value)}
      >
        <IconFilter size={15} />
        <span>{label}</span>
        {activeCount > 0 && <span className="filter-menu-badge">{activeCount}</span>}
      </button>

      {open && (
        <div className="filter-menu-pop" role="dialog" aria-label={label}>
          <div className="filter-menu-grid">{children}</div>
          <div className="filter-menu-foot">
            {onReset && (
              <button
                type="button"
                className="btn btn-ghost btn-sm"
                onClick={() => { onReset(); setOpen(false); }}
              >
                Clear all
              </button>
            )}
            <button
              type="button"
              className="btn btn-primary btn-sm"
              onClick={() => { onApply(); setOpen(false); }}
            >
              Apply filters
            </button>
          </div>
        </div>
      )}
    </div>
  );
}

export function FilterField({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="filter-field">
      <span className="field-label">{label}</span>
      {children}
    </div>
  );
}

/**
 * The search box every list screen puts in its filter strip. It exists so the magnifier sits
 * inside the field on all of them rather than each page hand-rolling the input-group wrapper
 * and half of them forgetting it.
 */
export function SearchField(
  { placeholder, value, onChange, onSearch, label = 'Search' }:
  {
    placeholder: string;
    value: string;
    onChange: (value: string) => void;
    /** Run the search. Wired to Enter; pages that filter as you type may leave it out. */
    onSearch?: () => void;
    label?: string;
  },
) {
  return (
    <FilterField label={label}>
      <div className="input-group">
        <span className="input-icon"><IconSearch size={14} /></span>
        <input
          className="input"
          type="search"
          placeholder={placeholder}
          value={value}
          onChange={(event) => onChange(event.target.value)}
          onKeyDown={(event) => { if (event.key === 'Enter') onSearch?.(); }}
        />
      </div>
    </FilterField>
  );
}

/* ---------------------------------------------------------------------- form */

export function Field(
  { label, required, help, error, children }:
  { label: string; required?: boolean; help?: string; error?: string; children: ReactNode },
) {
  return (
    <div className="field">
      <label className="field-label">{label}{required ? <span className="req">*</span> : null}</label>
      {children}
      {error ? <span className="field-error">{error}</span> : help ? <span className="field-help">{help}</span> : null}
    </div>
  );
}

export function FormSection(
  { title, caption, icon, optional, children }:
  { title: string; caption?: string; icon?: ReactNode; optional?: boolean; children: ReactNode },
) {
  return (
    <div className={`form-section ${optional ? 'form-section-optional' : ''}`}>
      <div className="row" style={{ marginBottom: 14 }}>
        <div className="grow">
          <div className="form-section-title">{icon}{title}</div>
          {caption ? <div className="form-section-caption">{caption}</div> : null}
        </div>
        {optional ? <span className="pill pill-neutral">Optional</span> : null}
      </div>
      {children}
    </div>
  );
}

/* --------------------------------------------------------------------- pills */

export type PillTone = 'success' | 'warning' | 'danger' | 'info' | 'neutral' | 'primary' | 'dark';

export function Pill({ tone = 'neutral', children }: { tone?: PillTone; children: ReactNode }) {
  return <span className={`pill pill-${tone}`}>{children}</span>;
}

/** Maps a status word to a tone so every table colours the same way. */
export function toneFor(status: string | null | undefined): PillTone {
  const s = (status ?? '').toLowerCase().replace(/\s+/g, '');
  if (['active', 'paid', 'success', 'checkedin', 'approved', 'completed', 'present'].includes(s)) return 'success';
  if (['pending', 'partiallypaid', 'frozen', 'awaitingconfirmation', 'onleave', 'expiringsoon'].includes(s)) return 'warning';
  if (['expired', 'cancelled', 'canceled', 'failed', 'locked', 'inactive', 'rejected', 'refunded', 'overdue', 'resigned'].includes(s)) return 'danger';
  if (['checkedout', 'upgraded', 'downgraded', 'info', 'new'].includes(s)) return 'info';
  return 'neutral';
}

/**
 * The status pill every table uses. The value arrives as the raw C# enum name
 * (`PartiallyRefunded`, `AwaitingConfirmation`), so it is split into words for display while the
 * tone is still matched on the original, unspaced value.
 */
export function StatusPill({ status }: { status: string | null | undefined }) {
  return <Pill tone={toneFor(status)}>{words(status) || '—'}</Pill>;
}

/* -------------------------------------------------------------------- states */

export function Spinner() { return <div className="spinner" aria-label="Loading" />; }

export function Loading({ message = 'Loading…' }: { message?: string }) {
  return <div className="loading-block"><Spinner /><span>{message}</span></div>;
}

export function EmptyState(
  { icon, title, message, action }:
  { icon?: ReactNode; title: string; message?: string; action?: ReactNode },
) {
  return (
    <div className="empty">
      {icon}
      <div className="empty-title">{title}</div>
      {message ? <div className="empty-msg">{message}</div> : null}
      {action ? <div style={{ marginTop: 16 }}>{action}</div> : null}
    </div>
  );
}

export function Alert(
  { tone = 'info', children }:
  { tone?: 'error' | 'success' | 'warning' | 'info'; children: ReactNode },
) {
  return <div className={`alert alert-${tone}`}>{children}</div>;
}

/** Renders an ApiError's message plus any field-level messages it carried. */
export function ErrorAlert({ error }: { error: unknown }) {
  if (!error) return null;
  const err = error as { message?: string; validationErrors?: Record<string, string[]> };
  const fieldErrors = Object.entries(err.validationErrors ?? {});
  return (
    <Alert tone="error">
      <div>
        <div style={{ fontWeight: 600 }}>{err.message ?? 'Something went wrong.'}</div>
        {fieldErrors.length > 0 && (
          <ul style={{ margin: '6px 0 0', paddingLeft: 18 }}>
            {fieldErrors.map(([field, messages]) =>
              messages.map((m, i) => <li key={`${field}-${i}`}>{m}</li>))}
          </ul>
        )}
      </div>
    </Alert>
  );
}

/* --------------------------------------------------------------------- pager */

export function Pager(
  { pageNumber, pageSize, totalCount, onPage, onPageSize }:
  {
    pageNumber: number; pageSize: number; totalCount: number;
    onPage: (page: number) => void; onPageSize?: (size: number) => void;
  },
) {
  const totalPages = Math.max(1, Math.ceil(totalCount / Math.max(1, pageSize)));
  const from = totalCount === 0 ? 0 : (pageNumber - 1) * pageSize + 1;
  const to = Math.min(totalCount, pageNumber * pageSize);

  return (
    <div className="pager">
      <span className="grow">Showing {from}–{to} of {totalCount}</span>
      {onPageSize && (
        <>
          <span className="muted" style={{ fontSize: 12 }}>Rows</span>
          <select
            className="select"
            style={{ width: 80, height: 32 }}
            value={pageSize}
            onChange={(e) => onPageSize(Number(e.target.value))}
          >
            {[10, 25, 50, 100].map((n) => <option key={n} value={n}>{n}</option>)}
          </select>
        </>
      )}
      <button className="pager-btn" disabled={pageNumber <= 1} onClick={() => onPage(1)} aria-label="First page">«</button>
      <button className="pager-btn" disabled={pageNumber <= 1} onClick={() => onPage(pageNumber - 1)} aria-label="Previous page">‹</button>
      <span style={{ fontSize: 13, fontWeight: 600 }}>Page {pageNumber} of {totalPages}</span>
      <button className="pager-btn" disabled={pageNumber >= totalPages} onClick={() => onPage(pageNumber + 1)} aria-label="Next page">›</button>
      <button className="pager-btn" disabled={pageNumber >= totalPages} onClick={() => onPage(totalPages)} aria-label="Last page">»</button>
    </div>
  );
}

/* --------------------------------------------------------------------- modal */

export function Modal(
  { title, icon, onClose, footer, children, width = 720 }:
  { title: ReactNode; icon?: ReactNode; onClose: () => void; footer?: ReactNode; children: ReactNode; width?: number },
) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);

  return (
    <div className="modal-backdrop" onMouseDown={(e) => { if (e.target === e.currentTarget) onClose(); }}>
      <div className="modal" style={{ maxWidth: width }} role="dialog" aria-modal="true">
        <div className="modal-header">
          {icon}
          <span className="modal-title">{title}</span>
          <button className="btn btn-ghost btn-icon btn-sm" onClick={onClose} aria-label="Close">✕</button>
        </div>
        <div className="modal-body">{children}</div>
        {footer ? <div className="modal-footer">{footer}</div> : null}
      </div>
    </div>
  );
}

/** Confirmation that can require the operator to type an exact phrase first. */
export function ConfirmModal(
  { title, message, confirmText, confirmLabel = 'Confirm', tone = 'danger', busy, onConfirm, onClose }:
  {
    title: string; message: ReactNode; confirmText?: string; confirmLabel?: string;
    tone?: 'danger' | 'primary'; busy?: boolean;
    onConfirm: () => void; onClose: () => void;
  },
) {
  const [typed, setTyped] = useState('');
  const ready = !confirmText || typed.trim() === confirmText;

  return (
    <Modal
      title={title}
      onClose={onClose}
      width={520}
      footer={
        <>
          <button className="btn btn-outline" onClick={onClose} disabled={busy}>Cancel</button>
          <button
            className={`btn btn-${tone}`}
            onClick={onConfirm}
            disabled={!ready || busy}
          >
            {busy ? 'Working…' : confirmLabel}
          </button>
        </>
      }
    >
      <div className="stack">
        <div>{message}</div>
        {confirmText && (
          <Field label={`Type "${confirmText}" to continue`} required>
            <input className="input" value={typed} onChange={(e) => setTyped(e.target.value)} autoFocus />
          </Field>
        )}
      </div>
    </Modal>
  );
}

