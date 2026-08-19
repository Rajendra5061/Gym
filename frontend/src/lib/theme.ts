/**
 * Day / dark mode.
 *
 * The choice is stored per browser and stamped on <html data-theme>. `tokens.css` redefines its
 * colour tokens under that attribute, so switching is a single DOM write — no component re-render
 * is required and nothing needs to know which theme is active.
 *
 * "system" is a real third state, not a synonym for light: it leaves the attribute off so the
 * `prefers-color-scheme` block in tokens.css applies and the app follows the OS.
 */
export type ThemeChoice = 'light' | 'dark' | 'system';

const STORAGE_KEY = 'gym.theme';

/** Fires on change so several toggles (app header, public nav) stay in step. */
const listeners = new Set<(choice: ThemeChoice) => void>();

export function readThemeChoice(): ThemeChoice {
  const stored = localStorage.getItem(STORAGE_KEY);
  return stored === 'light' || stored === 'dark' || stored === 'system' ? stored : 'system';
}

/** What the user actually sees right now, with "system" resolved against the OS setting. */
export function resolveTheme(choice: ThemeChoice = readThemeChoice()): 'light' | 'dark' {
  if (choice !== 'system') return choice;
  return window.matchMedia?.('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
}

export function applyThemeChoice(choice: ThemeChoice): void {
  const root = document.documentElement;
  if (choice === 'system') root.removeAttribute('data-theme');
  else root.setAttribute('data-theme', choice);

  // Lets the browser paint form controls and scrollbars to match.
  root.style.colorScheme = resolveTheme(choice);
}

export function setThemeChoice(choice: ThemeChoice): void {
  localStorage.setItem(STORAGE_KEY, choice);
  applyThemeChoice(choice);
  listeners.forEach((listener) => listener(choice));
}

export function subscribeToTheme(listener: (choice: ThemeChoice) => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

/**
 * Called once before React mounts. `index.html` also stamps the attribute inline so the first
 * paint is already correct — without that a dark-mode user gets a white flash on every load.
 */
export function initTheme(): void {
  applyThemeChoice(readThemeChoice());

  // Follow the OS while the choice is "system".
  window.matchMedia?.('(prefers-color-scheme: dark)').addEventListener('change', () => {
    if (readThemeChoice() === 'system') applyThemeChoice('system');
  });
}
