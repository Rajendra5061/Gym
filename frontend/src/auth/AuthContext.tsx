import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { api, ApiError, onSessionExpired, tokenStore } from '@/api/client';
import type { CurrentUser, GymBranding, LoginResponse } from '@/api/types';

interface AuthState {
  user: CurrentUser | null;
  gym: GymBranding | null;
  ready: boolean;
  mustChangePassword: boolean;
  signIn: (userNameOrEmail: string, password: string) => Promise<CurrentUser>;
  signOut: () => Promise<void>;
  refreshUser: () => Promise<void>;
  can: (permission: string) => boolean;
  canAny: (...permissions: string[]) => boolean;
  isInRole: (role: string) => boolean;
  currency: string;
}

const AuthContext = createContext<AuthState | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<CurrentUser | null>(null);
  const [gym, setGym] = useState<GymBranding | null>(null);
  const [ready, setReady] = useState(false);
  const [mustChangePassword, setMustChange] = useState(false);

  // `/api/settings/branding`, not `/api/settings/gym`: the full profile needs `settings.view`,
  // which members do not hold, so asking for it left every member-portal page with no gym name
  // and the hardcoded currency fallback below. Branding is readable by any signed-in account.
  const loadGym = useCallback(async () => {
    try { setGym(await api.get<GymBranding>('/api/settings/branding')); } catch { /* branding is optional */ }
  }, []);

  // Restore a session from a stored token on first paint.
  useEffect(() => {
    let cancelled = false;
    (async () => {
      if (tokenStore.access) {
        try {
          const me = await api.get<CurrentUser>('/api/auth/me');
          if (!cancelled) { setUser(me); await loadGym(); }
        } catch (error) {
          // Only a genuine 401 means the stored token is no longer good. Discarding tokens on
          // *any* failure signed people out over a server restart, a dropped connection or a
          // rate-limit reply — losing their session for something that fixes itself in seconds.
          // Keeping them lets the next attempt restore the session. This mirrors refreshUser().
          if (error instanceof ApiError && error.isUnauthorized) tokenStore.clear();
        }
      }
      if (!cancelled) setReady(true);
    })();
    return () => { cancelled = true; };
  }, [loadGym]);

  // A failed refresh anywhere in the app drops the session here.
  useEffect(() => {
    onSessionExpired.handler = () => { setUser(null); setGym(null); };
    return () => { onSessionExpired.handler = null; };
  }, []);

  const signIn = useCallback(async (userNameOrEmail: string, password: string) => {
    const res = await api.post<LoginResponse>('/api/auth/login', { userNameOrEmail, password });
    tokenStore.set(res.accessToken, res.refreshToken);
    setUser(res.user);
    setMustChange(res.mustChangePassword);
    await loadGym();
    return res.user;
  }, [loadGym]);

  const signOut = useCallback(async () => {
    try { await api.post('/api/auth/logout', { refreshToken: tokenStore.refresh }); }
    catch { /* signing out locally matters more than the server round trip */ }
    tokenStore.clear();
    setUser(null);
    setGym(null);
    setMustChange(false);
  }, []);

  const refreshUser = useCallback(async () => {
    try { setUser(await api.get<CurrentUser>('/api/auth/me')); }
    catch (error) { if (error instanceof ApiError && error.isUnauthorized) { tokenStore.clear(); setUser(null); } }
  }, []);

  const value = useMemo<AuthState>(() => {
    const isAdmin = user?.roles.includes('Admin') ?? false;
    return {
      user, gym, ready, mustChangePassword, signIn, signOut, refreshUser,
      // Admins implicitly hold every permission, mirroring the server-side rule.
      can: (permission) => isAdmin || (user?.permissions.includes(permission) ?? false),
      canAny: (...permissions) => isAdmin || permissions.some((p) => user?.permissions.includes(p)),
      isInRole: (role) => user?.roles.includes(role) ?? false,
      currency: gym?.currencySymbol ?? '₹',
    };
  }, [user, gym, ready, mustChangePassword, signIn, signOut, refreshUser]);

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthState {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used inside <AuthProvider>.');
  return ctx;
}
