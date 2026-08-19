/** Shared display formatting. Money is always rendered from the gym's configured symbol. */

export function money(value: number | null | undefined, symbol = '₹'): string {
  if (value === null || value === undefined) return '—';
  return `${symbol}${value.toLocaleString('en-IN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
}

export function date(value: string | Date | null | undefined): string {
  if (!value) return '—';
  const d = typeof value === 'string' ? new Date(value) : value;
  if (Number.isNaN(d.getTime())) return '—';
  return d.toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' });
}

export function dateTime(value: string | Date | null | undefined): string {
  if (!value) return '—';
  const d = typeof value === 'string' ? new Date(value) : value;
  if (Number.isNaN(d.getTime())) return '—';
  return `${date(d)} ${d.toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' })}`;
}

export function time(value: string | Date | null | undefined): string {
  if (!value) return '—';
  const d = typeof value === 'string' ? new Date(value) : value;
  if (Number.isNaN(d.getTime())) return '—';
  return d.toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' });
}

/**
 * yyyy-MM-dd, the shape <input type="date"> expects.
 *
 * Built from the LOCAL date parts, never `toISOString()`. The API sends date-only values as
 * local midnight (`2026-08-18T00:00:00`), and in any timezone ahead of UTC that instant is still
 * the previous day in UTC — so `toISOString()` returned 2026-08-17. The field then saved the
 * shifted value back, meaning every edit walked the date one day further into the past.
 */
export function isoDate(value: string | Date | null | undefined): string {
  if (!value) return '';
  const d = typeof value === 'string' ? new Date(value) : value;
  if (Number.isNaN(d.getTime())) return '';
  const month = `${d.getMonth() + 1}`.padStart(2, '0');
  const day = `${d.getDate()}`.padStart(2, '0');
  return `${d.getFullYear()}-${month}-${day}`;
}

/**
 * Turns a server enum name into something readable: `PartiallyRefunded` → "Partially Refunded",
 * `WalkIn` → "Walk In", `NEW_MEMBER` → "NEW MEMBER".
 *
 * The API sends every enum as its C# member name in a `*Text` field, so the set of values grows
 * whenever the backend adds one. Splitting on case boundaries — rather than keeping a lookup of
 * every known value — means a new server value reads correctly the day it appears.
 *
 * Runs of capitals are kept whole so acronyms survive: `UPI` stays "UPI", `QRCode` becomes
 * "QR Code" and `PDFExport` becomes "PDF Export". Text that is already spaced is left as it is.
 */
export function words(value: string | null | undefined): string {
  if (value === null || value === undefined) return '';
  return String(value)
    .replace(/_+/g, ' ')
    // walkIn → walk In, plan2Days → plan2 Days
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    // UPIPayment → UPI Payment (the last capital of a run starts the next word)
    .replace(/([A-Z]+)([A-Z][a-z])/g, '$1 $2')
    .replace(/\s+/g, ' ')
    .trim();
}

/** `words()` with an em dash for anything empty, for direct rendering. */
export function label(value: string | null | undefined): string {
  return words(value) || '—';
}

export function initials(fullName: string | null | undefined): string {
  if (!fullName) return '?';
  const parts = fullName.trim().split(/\s+/).filter(Boolean);
  if (!parts.length) return '?';
  return (parts[0][0] + (parts.length > 1 ? parts[parts.length - 1][0] : '')).toUpperCase();
}

export function relativeTime(value: string | Date | null | undefined): string {
  if (!value) return '';
  const d = typeof value === 'string' ? new Date(value) : value;
  const seconds = Math.floor((Date.now() - d.getTime()) / 1000);
  if (seconds < 60) return 'just now';
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `${minutes} minute${minutes === 1 ? '' : 's'} ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours} hour${hours === 1 ? '' : 's'} ago`;
  const days = Math.floor(hours / 24);
  if (days < 30) return `${days} day${days === 1 ? '' : 's'} ago`;
  return date(d);
}
