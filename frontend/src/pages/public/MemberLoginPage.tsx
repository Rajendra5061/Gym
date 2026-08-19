import { useState, type FormEvent } from 'react';
import { Link, Navigate, useNavigate } from 'react-router-dom';
import { ApiError } from '@/api/client';
import { useAuth } from '@/auth/AuthContext';
import { Alert, Field } from '@/components/ui';
import {
  IconArrowLeft, IconCheck, IconDumbbell, IconEye, IconEyeOff,
  IconInfo, IconLock, IconUser,
} from '@/components/icons';
import { PublicNav, dashboardPathFor, useGymSettings } from '@/components/PublicNav';
import './public.css';

export default function MemberLoginPage() {
  const { user, signIn } = useAuth();
  const { gymName } = useGymSettings();
  const navigate = useNavigate();

  const [userName, setUserName] = useState('');
  const [password, setPassword] = useState('');
  const [showPassword, setShowPassword] = useState(false);
  const [userError, setUserError] = useState<string | null>(null);
  const [passwordError, setPasswordError] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    const name = userName.trim();

    setUserError(name ? null : 'Username is required.');
    setPasswordError(password ? null : 'Password is required.');
    setError(null);
    if (!name || !password) return;

    setBusy(true);
    try {
      // Staff accounts land here too; the server's roles decide where they go next.
      const signedIn = await signIn(name, password);
      navigate(dashboardPathFor(signedIn), { replace: true });
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Sign-in failed. Please try again.');
      setBusy(false);
    }
  }

  if (user) return <Navigate to={dashboardPathFor(user)} replace />;

  return (
    <div className="pub-shell">
      <PublicNav gymName={gymName} />

      <main className="pub-auth-wrap">
        <div className="pub-auth-card pub-auth-card-reverse">
          <aside className="pub-auth-side pub-auth-side-member">
            <div className="pub-auth-side-brand">
              <IconDumbbell size={20} /> {gymName}
            </div>
            <ul className="pub-auth-side-list">
              <li><IconCheck size={15} /> Your membership and expiry date</li>
              <li><IconCheck size={15} /> Attendance history and check-ins</li>
              <li><IconCheck size={15} /> Payments, receipts and dues</li>
              <li><IconCheck size={15} /> Workout plans from your trainer</li>
            </ul>
            <div className="pub-auth-side-caption">
              <strong>Member Portal</strong>
              Track your plan, attendance and payments in one place.
            </div>
          </aside>

          <section className="pub-auth-form">
            <div className="pub-auth-head">
              <span className="pub-auth-badge pub-badge-lavender"><IconUser size={24} /></span>
              <div className="grow">
                <div className="pub-auth-title">Member Login</div>
                <div className="pub-auth-sub">Sign in to your dashboard and details.</div>
              </div>
              <Link to="/" className="btn btn-outline btn-sm"><IconArrowLeft size={13} /> Home</Link>
            </div>

            <form onSubmit={onSubmit} noValidate>
              <div className="pub-auth-fields">
                {error ? <Alert tone="error">{error}</Alert> : null}

                <Field label="Username" required error={userError ?? undefined}>
                  <div className="input-group">
                    <span className="input-icon"><IconUser size={15} /></span>
                    <input
                      className={`input ${userError ? 'input-invalid' : ''}`}
                      value={userName}
                      onChange={(e) => setUserName(e.target.value)}
                      placeholder="Username or email"
                      autoComplete="username"
                      autoFocus
                    />
                  </div>
                </Field>

                <Field label="Password" required error={passwordError ?? undefined}>
                  <div className="input-group">
                    <span className="input-icon"><IconLock size={15} /></span>
                    <input
                      className={`input pub-input-has-suffix ${passwordError ? 'input-invalid' : ''}`}
                      type={showPassword ? 'text' : 'password'}
                      value={password}
                      onChange={(e) => setPassword(e.target.value)}
                      placeholder="Password"
                      autoComplete="current-password"
                    />
                    <span className="input-suffix">
                      <button
                        type="button"
                        className="btn btn-ghost btn-icon btn-sm"
                        onClick={() => setShowPassword((visible) => !visible)}
                        aria-label={showPassword ? 'Hide password' : 'Show password'}
                      >
                        {showPassword ? <IconEyeOff size={15} /> : <IconEye size={15} />}
                      </button>
                    </span>
                  </div>
                </Field>

                <button type="submit" className="btn pub-btn-indigo btn-block" disabled={busy}>
                  {busy ? 'Logging in…' : 'Login'}
                </button>
              </div>
            </form>

            <Link to="/forgot-password" className="pub-auth-link">Forgot password?</Link>

            <div className="pub-auth-foot">
              <IconInfo size={14} />
              If you face issues, contact the gym admin to reset your password.
            </div>
          </section>
        </div>
      </main>
    </div>
  );
}
