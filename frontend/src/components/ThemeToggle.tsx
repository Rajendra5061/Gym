import { useEffect, useState } from 'react';
import {
  readThemeChoice, resolveTheme, setThemeChoice, subscribeToTheme, type ThemeChoice,
} from '@/lib/theme';
import { IconMoon, IconSun } from '@/components/icons';

/**
 * Day / dark switch. One click flips between light and dark; the three-way choice including
 * "system" is kept in the store so a user who never touches this still follows their OS.
 */
export function ThemeToggle({ onDark = false }: { onDark?: boolean }) {
  const [choice, setChoice] = useState<ThemeChoice>(readThemeChoice);

  useEffect(() => subscribeToTheme(setChoice), []);

  const current = resolveTheme(choice);
  const next = current === 'dark' ? 'light' : 'dark';

  return (
    <button
      type="button"
      className={`theme-toggle ${onDark ? 'on-dark' : ''}`}
      onClick={() => setThemeChoice(next)}
      aria-label={`Switch to ${next} mode`}
      title={`Switch to ${next} mode`}
    >
      {current === 'dark' ? <IconSun size={17} /> : <IconMoon size={17} />}
    </button>
  );
}
