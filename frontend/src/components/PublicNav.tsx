import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { api } from '@/api/client';
import { useAuth } from '@/auth/AuthContext';
import { IconDumbbell } from '@/components/icons';
import { ThemeToggle } from '@/components/ThemeToggle';
import type { CurrentUser, GymBranding } from '@/api/types';
import '@/pages/public/public.css';

/**
 * Where a freshly signed-in account belongs. Driven purely by the roles the server put on
 * the token, never by which login card the visitor happened to click. Trainers get their
 * own portal; office roles get the admin area; everyone else is a member.
 */
export function dashboardPathFor(user: CurrentUser): string {
  if (user.roles.includes('Trainer')) return '/trainer/dashboard';
  if (user.roles.includes('Admin') || user.roles.includes('Staff')) return '/admin/dashboard';
  return '/member/dashboard';
}

export const DEFAULT_GYM_NAME = 'Gym Management';

/**
 * Branding for the signed-out screens. `/api/settings/branding` is anonymous — it carries only
 * what the landing page already prints (name, contact, currency) — so visitors see the real gym
 * name; the fallback covers only an unreachable API.
 */
export function useGymSettings(): { settings: GymBranding | null; gymName: string } {
  const { gym } = useAuth();
  const [fetched, setFetched] = useState<GymBranding | null>(null);

  useEffect(() => {
    if (gym) return;
    let cancelled = false;
    (async () => {
      try {
        const settings = await api.get<GymBranding>('/api/settings/branding');
        if (!cancelled) setFetched(settings);
      } catch {
        /* anonymous visitors get the fallback branding */
      }
    })();
    return () => { cancelled = true; };
  }, [gym]);

  const settings = gym ?? fetched;
  return { settings, gymName: settings?.gymName?.trim() || DEFAULT_GYM_NAME };
}

/**
 * Near-black bar shared by every public screen: brand on the left, the day/dark switch and
 * both logins on the right. This is the only chrome a signed-out visitor sees, so it is also
 * the only place the theme can be changed before signing in — home, both login cards and the
 * password-reset card all render it, so all four screens get the switch from here.
 */
export function PublicNav({ gymName }: { gymName: string }) {
  return (
    <header className="pub-nav">
      <div className="pub-nav-inner">
        <Link to="/" className="pub-brand">
          <span className="pub-brand-mark"><IconDumbbell size={19} /></span>
          <span className="pub-brand-name">{gymName}</span>
        </Link>
        <nav className="pub-nav-actions">
          {/* The bar is near-black in both themes, so the toggle takes its on-dark skin. */}
          <ThemeToggle onDark />
          <Link to="/admin-login" className="btn btn-sm pub-btn-on-dark">Admin Login</Link>
          <Link to="/login" className="btn btn-sm pub-btn-white">Member Login</Link>
        </nav>
      </div>
    </header>
  );
}
