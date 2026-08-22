import { useMemo } from 'react';
import { encodeQr } from '@/lib/qr';

/**
 * Draws a QR matrix as one crisp SVG path with the mandatory 4-module quiet zone.
 *
 * The quiet zone is not decoration — scanners need that clear margin to find the symbol at all,
 * which is why it is baked into the viewBox rather than left to the caller's padding.
 *
 * Renders nothing when the text will not fit the encoder's version range; callers that must say
 * something in that case should test with `encodeQr` first.
 */
export function QrCode(
  { text, title, dark = '#14161f', light = '#ffffff' }:
  {
    text: string;
    /** Accessible name — a QR is an image, and a screen reader cannot scan it. */
    title: string;
    dark?: string;
    light?: string;
  },
) {
  const encoded = useMemo(() => encodeQr(text), [text]);
  if (!encoded) return null;

  const { size, modules } = encoded;
  const quiet = 4;
  const span = size + quiet * 2;
  const parts: string[] = [];
  for (let r = 0; r < size; r++) {
    for (let c = 0; c < size; c++) {
      if (modules[r][c]) parts.push(`M${c + quiet} ${r + quiet}h1v1h-1z`);
    }
  }

  return (
    <svg viewBox={`0 0 ${span} ${span}`} role="img" aria-label={title} shapeRendering="crispEdges">
      {/* The light square is painted, never left transparent: a dark theme showing through the
          gaps would invert the symbol and stop it scanning. */}
      <rect width={span} height={span} fill={light} />
      <path d={parts.join('')} fill={dark} />
    </svg>
  );
}
