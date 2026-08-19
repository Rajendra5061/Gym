/**
 * Shared UPI collection UI: the QR encoder and the "Collect by UPI" modal.
 *
 * Extracted from RecordPaymentPage so the subscription screens can offer the same QR without a
 * circular import — RecordPaymentPage already imports the member picker and quote panel from
 * SubscriptionsPage, so the dependency could not simply run the other way.
 */
import { useMemo, useState } from 'react';
import type { UpiPaymentIntentDto } from '@/api/endpoints/payments';
import { Alert, Field, Modal } from '@/components/ui';
import { IconCard, IconCheck, IconQr, IconShield, IconWarning } from '@/components/icons';
import { money } from '@/lib/format';


const GF_EXP = new Uint8Array(512);
const GF_LOG = new Uint8Array(256);
(() => {
  let x = 1;
  for (let i = 0; i < 255; i++) { GF_EXP[i] = x; GF_LOG[x] = i; x <<= 1; if (x & 0x100) x ^= 0x11d; }
  for (let i = 255; i < 512; i++) GF_EXP[i] = GF_EXP[i - 255];
})();

const gfMul = (a: number, b: number) => (a === 0 || b === 0 ? 0 : GF_EXP[GF_LOG[a] + GF_LOG[b]]);

/** Generator polynomial for `degree` error-correction codewords, highest power first. */
function generatorPoly(degree: number): number[] {
  let poly = [1];
  for (let i = 0; i < degree; i++) {
    const next = new Array<number>(poly.length + 1).fill(0);
    for (let j = 0; j < poly.length; j++) {
      next[j] ^= poly[j];
      next[j + 1] ^= gfMul(poly[j], GF_EXP[i]);
    }
    poly = next;
  }
  return poly;
}

function reedSolomon(data: number[], ecLength: number): number[] {
  const gen = generatorPoly(ecLength);
  const buffer = new Array<number>(data.length + ecLength).fill(0);
  for (let i = 0; i < data.length; i++) buffer[i] = data[i];
  for (let i = 0; i < data.length; i++) {
    const factor = buffer[i];
    if (factor === 0) continue;
    for (let j = 0; j < gen.length; j++) buffer[i + j] ^= gfMul(gen[j], factor);
  }
  return buffer.slice(data.length);
}

/** [ec codewords per block, [blockCount, dataCodewords] …] for error-correction level M. */
const ECC_M: { ec: number; blocks: [number, number][] }[] = [
  { ec: 10, blocks: [[1, 16]] },                 // v1
  { ec: 16, blocks: [[1, 28]] },                 // v2
  { ec: 26, blocks: [[1, 44]] },                 // v3
  { ec: 18, blocks: [[2, 32]] },                 // v4
  { ec: 24, blocks: [[2, 43]] },                 // v5
  { ec: 16, blocks: [[4, 27]] },                 // v6
  { ec: 18, blocks: [[4, 31]] },                 // v7
  { ec: 22, blocks: [[2, 38], [2, 39]] },        // v8
  { ec: 22, blocks: [[3, 36], [2, 37]] },        // v9
  { ec: 26, blocks: [[4, 43], [1, 44]] },        // v10
];

const ALIGNMENT: number[][] = [
  [], [6, 18], [6, 22], [6, 26], [6, 30], [6, 34], [6, 22, 38], [6, 24, 42], [6, 26, 46], [6, 28, 50],
];

const dataCodewordCount = (version: number) =>
  ECC_M[version - 1].blocks.reduce((sum, [count, size]) => sum + count * size, 0);

function formatBits(mask: number): number {
  const data = (0 /* level M */ << 3) | mask;
  let rem = data;
  for (let i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >>> 9) * 0x537);
  return ((data << 10) | rem) ^ 0x5412;
}

function versionBits(version: number): number {
  let rem = version;
  for (let i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >>> 11) * 0x1f25);
  return (version << 12) | rem;
}

function encodeSegment(bytes: Uint8Array, version: number): number[] {
  const bits: number[] = [];
  const push = (value: number, length: number) => {
    for (let i = length - 1; i >= 0; i--) bits.push((value >>> i) & 1);
  };
  push(0b0100, 4);                                  // byte mode
  push(bytes.length, version <= 9 ? 8 : 16);
  for (const byte of bytes) push(byte, 8);

  const total = dataCodewordCount(version);
  const capacity = total * 8;
  for (let i = 0; i < 4 && bits.length < capacity; i++) bits.push(0);
  while (bits.length % 8 !== 0) bits.push(0);

  const codewords: number[] = [];
  for (let i = 0; i < bits.length; i += 8) {
    let byte = 0;
    for (let j = 0; j < 8; j++) byte = (byte << 1) | bits[i + j];
    codewords.push(byte);
  }
  for (let pad = 0xec; codewords.length < total; pad ^= 0xec ^ 0x11) codewords.push(pad);
  return codewords;
}

function interleave(codewords: number[], version: number): number[] {
  const { ec, blocks } = ECC_M[version - 1];
  const dataBlocks: number[][] = [];
  const ecBlocks: number[][] = [];
  let offset = 0;
  for (const [count, size] of blocks) {
    for (let i = 0; i < count; i++) {
      const block = codewords.slice(offset, offset + size);
      offset += size;
      dataBlocks.push(block);
      ecBlocks.push(reedSolomon(block, ec));
    }
  }
  const out: number[] = [];
  const longest = Math.max(...dataBlocks.map((b) => b.length));
  for (let i = 0; i < longest; i++) for (const block of dataBlocks) if (i < block.length) out.push(block[i]);
  for (let i = 0; i < ec; i++) for (const block of ecBlocks) out.push(block[i]);
  return out;
}

function maskAt(mask: number, row: number, col: number): boolean {
  switch (mask) {
    case 0: return (col + row) % 2 === 0;
    case 1: return row % 2 === 0;
    case 2: return col % 3 === 0;
    case 3: return (col + row) % 3 === 0;
    case 4: return (Math.floor(col / 3) + Math.floor(row / 2)) % 2 === 0;
    case 5: return ((col * row) % 2) + ((col * row) % 3) === 0;
    case 6: return (((col * row) % 2) + ((col * row) % 3)) % 2 === 0;
    default: return (((col + row) % 2) + ((col * row) % 3)) % 2 === 0;
  }
}

/** The four ISO penalty rules; the lowest-scoring mask is the one we emit. */
function penalty(grid: Uint8Array[], size: number): number {
  let score = 0;

  const runScore = (line: number[]) => {
    let total = 0;
    let run = 1;
    for (let i = 1; i < line.length; i++) {
      if (line[i] === line[i - 1]) { run++; } else { if (run >= 5) total += 3 + (run - 5); run = 1; }
    }
    if (run >= 5) total += 3 + (run - 5);
    return total;
  };

  const FINDER = [1, 0, 1, 1, 1, 0, 1, 0, 0, 0, 0];
  const hasFinder = (line: number[], at: number) => {
    for (let i = 0; i < 11; i++) if (line[at + i] !== FINDER[i]) return false;
    return true;
  };
  const hasFinderRev = (line: number[], at: number) => {
    for (let i = 0; i < 11; i++) if (line[at + i] !== FINDER[10 - i]) return false;
    return true;
  };

  for (let i = 0; i < size; i++) {
    const row: number[] = [];
    const col: number[] = [];
    for (let j = 0; j < size; j++) { row.push(grid[i][j]); col.push(grid[j][i]); }
    score += runScore(row) + runScore(col);
    for (let j = 0; j + 11 <= size; j++) {
      if (hasFinder(row, j) || hasFinderRev(row, j)) score += 40;
      if (hasFinder(col, j) || hasFinderRev(col, j)) score += 40;
    }
  }

  for (let r = 0; r + 1 < size; r++) {
    for (let c = 0; c + 1 < size; c++) {
      const v = grid[r][c];
      if (v === grid[r][c + 1] && v === grid[r + 1][c] && v === grid[r + 1][c + 1]) score += 3;
    }
  }

  let dark = 0;
  for (let r = 0; r < size; r++) for (let c = 0; c < size; c++) dark += grid[r][c];
  const percent = (dark * 100) / (size * size);
  score += Math.floor(Math.abs(percent - 50) / 5) * 10;

  return score;
}

/** Returns a square matrix of 0/1 modules, or null when the text will not fit. */
export function encodeQr(text: string): { size: number; modules: Uint8Array[] } | null {
  const bytes = new TextEncoder().encode(text);

  let version = 0;
  for (let v = 1; v <= 10; v++) {
    const header = 4 + (v <= 9 ? 8 : 16);
    if (Math.ceil((header + bytes.length * 8) / 8) <= dataCodewordCount(v)) { version = v; break; }
  }
  if (version === 0) return null;

  const size = version * 4 + 17;
  const grid = Array.from({ length: size }, () => new Uint8Array(size));
  const fixed = Array.from({ length: size }, () => new Uint8Array(size));

  const setFixed = (row: number, col: number, dark: boolean) => {
    if (row < 0 || col < 0 || row >= size || col >= size) return;
    grid[row][col] = dark ? 1 : 0;
    fixed[row][col] = 1;
  };

  // Finder patterns plus their separators.
  for (const [top, left] of [[0, 0], [0, size - 7], [size - 7, 0]]) {
    for (let r = -1; r <= 7; r++) {
      for (let c = -1; c <= 7; c++) {
        const inner = r >= 0 && r <= 6 && c >= 0 && c <= 6;
        const dark = inner && (
          r === 0 || r === 6 || c === 0 || c === 6 || (r >= 2 && r <= 4 && c >= 2 && c <= 4)
        );
        setFixed(top + r, left + c, dark);
      }
    }
  }

  // Alignment patterns, skipping the three that would sit on a finder.
  const centres = ALIGNMENT[version - 1];
  for (let i = 0; i < centres.length; i++) {
    for (let j = 0; j < centres.length; j++) {
      const last = centres.length - 1;
      if ((i === 0 && j === 0) || (i === 0 && j === last) || (i === last && j === 0)) continue;
      const row = centres[i];
      const col = centres[j];
      for (let r = -2; r <= 2; r++) {
        for (let c = -2; c <= 2; c++) {
          setFixed(row + r, col + c, Math.max(Math.abs(r), Math.abs(c)) !== 1);
        }
      }
    }
  }

  // Timing patterns.
  for (let i = 8; i < size - 8; i++) {
    setFixed(6, i, i % 2 === 0);
    setFixed(i, 6, i % 2 === 0);
  }

  // Reserve the format strip (values written after masking) and the dark module.
  // Index 6 is skipped: the format strip steps over the two timing modules it crosses.
  for (let i = 0; i <= 8; i++) {
    if (i === 6) continue;
    setFixed(8, i, false);
    setFixed(i, 8, false);
  }
  for (let i = 0; i < 8; i++) { setFixed(8, size - 1 - i, false); setFixed(size - 1 - i, 8, false); }
  setFixed(size - 8, 8, true);

  if (version >= 7) {
    const bits = versionBits(version);
    for (let i = 0; i < 18; i++) {
      const bit = ((bits >>> i) & 1) === 1;
      const a = size - 11 + (i % 3);
      const b = Math.floor(i / 3);
      setFixed(b, a, bit);
      setFixed(a, b, bit);
    }
  }

  // Zig-zag the interleaved codewords into every module the patterns left free.
  const stream = interleave(encodeSegment(bytes, version), version);
  let bitIndex = 0;
  const totalBits = stream.length * 8;
  for (let right = size - 1; right >= 1; right -= 2) {
    if (right === 6) right = 5;
    for (let vert = 0; vert < size; vert++) {
      for (let j = 0; j < 2; j++) {
        const col = right - j;
        const upward = ((right + 1) & 2) === 0;
        const row = upward ? size - 1 - vert : vert;
        if (fixed[row][col] || bitIndex >= totalBits) continue;
        grid[row][col] = (stream[bitIndex >>> 3] >>> (7 - (bitIndex & 7))) & 1;
        bitIndex++;
      }
    }
  }

  // Try every mask, keep the least penalised.
  let best: { mask: number; grid: Uint8Array[]; score: number } | null = null;
  for (let mask = 0; mask < 8; mask++) {
    const candidate = grid.map((row) => Uint8Array.from(row));
    for (let r = 0; r < size; r++) {
      for (let c = 0; c < size; c++) {
        if (!fixed[r][c] && maskAt(mask, r, c)) candidate[r][c] ^= 1;
      }
    }
    const bits = formatBits(mask);
    const put = (row: number, col: number, i: number) => { candidate[row][col] = (bits >>> i) & 1; };
    for (let i = 0; i <= 5; i++) put(8, i, i);
    put(8, 7, 6); put(8, 8, 7); put(7, 8, 8);
    for (let i = 9; i < 15; i++) put(14 - i, 8, i);
    for (let i = 0; i < 8; i++) put(8, size - 1 - i, i);
    for (let i = 8; i < 15; i++) put(size - 15 + i, 8, i);
    candidate[size - 8][8] = 1;

    const score = penalty(candidate, size);
    if (!best || score < best.score) best = { mask, grid: candidate, score };
  }

  return { size, modules: best!.grid };
}

/** Draws the matrix as one crisp SVG path with the mandatory 4-module quiet zone. */
function QrCode({ text, title }: { text: string; title: string }) {
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
      <rect width={span} height={span} fill="#ffffff" />
      <path d={parts.join('')} fill="#14161f" />
    </svg>
  );
}

/* ============================================================================
 * The page
 * ========================================================================== */


export function UpiModal({ intent, onClose }: { intent: UpiPaymentIntentDto; onClose: () => void }) {
  const [copied, setCopied] = useState('');

  async function copy(label: string, value: string) {
    try {
      await navigator.clipboard.writeText(value);
      setCopied(label);
      setTimeout(() => setCopied(''), 1500);
    } catch {
      setCopied('');
    }
  }

  const link = intent.upiDeepLink ?? '';
  const drawable = link ? encodeQr(link) !== null : false;

  return (
    <Modal
      title="Collect by UPI"
      icon={<IconQr size={18} />}
      onClose={onClose}
      width={780}
      footer={<button className="btn btn-dark" onClick={onClose}>Done</button>}
    >
      <div className="stack">
        {/*
          Driven entirely by the server's flag. Told flatly that nothing is verified, staff learn
          to confirm by hand and keep doing it after a gateway is wired up — at which point the
          same payment is settled twice over. So the modal says only what is true right now.
        */}
        {intent.requiresManualVerification ? (
          <Alert tone="warning">
            <IconWarning size={18} />
            <div>
              <div style={{ fontWeight: 600 }}>This QR gives no automatic verification.</div>
              <div>
                No payment gateway is configured, so nothing tells this app that the money arrived.
                Staff must read the transaction reference off the payer&apos;s app and confirm the
                payment before it counts as settled.
              </div>
            </div>
          </Alert>
        ) : (
          <Alert tone="info">
            <IconShield size={18} />
            <div>
              <div style={{ fontWeight: 600 }}>The payment gateway will confirm this transfer.</div>
              <div>
                The provider calls this server when the transfer completes, and the payment is
                settled from that verified callback — there is nothing to confirm by hand. Only
                chase the transaction reference if the callback has not arrived.
              </div>
            </div>
          </Alert>
        )}

        <div className="upi-panel">
          {link && drawable && (
            <div className="qr-box">
              <QrCode text={link} title={`UPI payment QR for ${intent.memberName}`} />
            </div>
          )}

          <div className="grow stack" style={{ minWidth: 280 }}>
            <dl className="kv">
              <dt>Member</dt><dd>{intent.memberName}</dd>
              <dt>Amount</dt><dd>{money(intent.amount, intent.currencySymbol)}</dd>
              {intent.payeeName ? <><dt>Payee</dt><dd>{intent.payeeName}</dd></> : null}
              {intent.upiId ? <><dt>UPI ID</dt><dd>{intent.upiId}</dd></> : null}
              <dt>Reference</dt><dd>{intent.paymentReference}</dd>
            </dl>

            <Field label="Payment reference — quote this when reconciling">
              <div className="copy-block">
                <code>{intent.paymentReference}</code>
                <button className="btn btn-outline btn-sm" onClick={() => copy('reference', intent.paymentReference)}>
                  {copied === 'reference' ? <><IconCheck size={13} /> Copied</> : 'Copy'}
                </button>
              </div>
            </Field>

            {link ? (
              <Field label="UPI deep link" help={drawable ? 'The QR above encodes this exact link.' : 'Too long to render as a QR here — send the link instead.'}>
                <div className="copy-block">
                  <code>{link}</code>
                  <button className="btn btn-outline btn-sm" onClick={() => copy('link', link)}>
                    {copied === 'link' ? <><IconCheck size={13} /> Copied</> : 'Copy'}
                  </button>
                </div>
              </Field>
            ) : (
              <Alert tone="info">
                No UPI deep link came back — set the gym's UPI ID under Settings before collecting by UPI.
              </Alert>
            )}
          </div>
        </div>

        {intent.instructions ? (
          <div className="form-section">
            <div className="form-section-title"><IconCard size={16} /> Instructions from the server</div>
            <div style={{ marginTop: 8, whiteSpace: 'pre-wrap' }}>{intent.instructions}</div>
          </div>
        ) : null}

        <div className="form-note">
          This screen never asks for a card number, CVV or UPI PIN — only the transaction
          reference and, optionally, the payer's VPA.
        </div>
      </div>
    </Modal>
  );
}
