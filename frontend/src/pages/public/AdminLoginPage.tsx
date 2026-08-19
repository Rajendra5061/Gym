import { useState, type CSSProperties, type FormEvent } from 'react';
import { Link, Navigate, useNavigate } from 'react-router-dom';
import { ApiError } from '@/api/client';
import { useAuth } from '@/auth/AuthContext';
import { Alert, Field } from '@/components/ui';
import {
  IconArrowLeft, IconCheck, IconDumbbell, IconEye, IconEyeOff,
  IconLock, IconShield, IconUser,
} from '@/components/icons';
import { PublicNav, dashboardPathFor, useGymSettings } from '@/components/PublicNav';
import './public.css';

/** The day's work, in the order the desk does it. Nothing aspirational — these are the modules. */
const PITCH_POINTS = [
  'Enrol and manage members',
  'Collect and reconcile payments',
  'Reports, audit trail and backups',
];

export default function AdminLoginPage() {
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
      // The password only ever reaches signIn; it is never stored or logged.
      const signedIn = await signIn(name, password);
      navigate(dashboardPathFor(signedIn), { replace: true });
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Sign-in failed. Please try again.');
      setBusy(false);
    }
  }

  // Someone already signed in has no use for this form.
  if (user) return <Navigate to={dashboardPathFor(user)} replace />;

  return (
    <div className="pub-shell">
      <PublicNav gymName={gymName} />

      {/* Same split as the member page: staff photograph and pitch on the left, and a form
          column that says "Admin Login" in so many words on the right. */}
      <main className="pub-auth-wrap2">
        <div className="pub-auth-splitcard">
        <aside
          className="pub-auth-photoside"
          style={{ '--pub-photo': "url('/images/gal-machine.jpg')" } as CSSProperties}
        >
          <div className="pub-auth-photocopy">
            <span className="pub-hero-eyebrow"><IconDumbbell size={13} /> {gymName}</span>
            <h1 className="pub-auth-pitch-title">
              Run the floor.
              <span>Own the numbers.</span>
            </h1>
            <ul className="pub-auth-pitch-list">
              {PITCH_POINTS.map((point) => (
                <li key={point}><IconCheck size={15} /> {point}</li>
              ))}
            </ul>
          </div>
        </aside>

        <section className="pub-auth-formside">
          <div className="pub-auth-formwrap">
            <div className="pub-auth-topbar">
              <nav className="pub-auth-switch pub-auth-switch-green" aria-label="Sign-in type">
                <Link to="/login"><IconUser size={14} /> Member</Link>
                <Link to="/admin-login" className="active" aria-current="page"><IconShield size={14} /> Staff</Link>
              </nav>
              <Link to="/" className="btn btn-outline btn-sm"><IconArrowLeft size={13} /> Home</Link>
            </div>

            <div className="pub-auth-formmain">
              <div className="pub-auth-titlerow">
                <span className="pub-auth-mark pub-auth-mark-staff"><IconShield size={24} /></span>
                <h2 className="pub-auth-bigtitle">Admin Login</h2>
              </div>
              <span className="pub-auth-title-accent pub-auth-title-accent-green" aria-hidden="true" />
              <p className="pub-auth-bigsub">
                Enter your staff credentials to open the management console.
              </p>

              <form onSubmit={onSubmit} noValidate>
                <div className="pub-auth-fields">
                  {error ? <Alert tone="error">{error}</Alert> : null}

                  <Field label="Username" required error={userError ?? undefined}>
                    <div className="input-group">
                      <span className="input-icon"><IconUser size={16} /></span>
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
                      <span className="input-icon"><IconLock size={16} /></span>
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

                  <button type="submit" className="btn pub-btn-glow-green btn-block" disabled={busy}>
                    {busy ? 'Logging in…' : 'Login to the console'}
                  </button>
                </div>
              </form>

              <div className="pub-auth-underform">
                <Link to="/forgot-password" className="pub-auth-link">Forgot password?</Link>
                <span className="pub-auth-underform-alt">
                  A member? <Link to="/login">Sign in here</Link>
                </span>
              </div>
            </div>

            <div className="pub-auth-foot"><IconShield size={14} /> Admin access only</div>
          </div>
        </section>
        </div>
      </main>
    </div>
  );
}
