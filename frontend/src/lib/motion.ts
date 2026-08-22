/**
 * Whether the OS asks for reduced motion. CSS honours this by itself; recharts does not,
 * so chart series read this flag to skip their draw-in animation — the same courtesy the
 * rest of the UI already extends through the stylesheet's reduced-motion blocks.
 * Snapshot at module load: the preference effectively never flips mid-session, and chart
 * mounts are what matter.
 */
export const prefersReducedMotion =
  typeof window !== 'undefined' &&
  typeof window.matchMedia === 'function' &&
  window.matchMedia('(prefers-reduced-motion: reduce)').matches;
