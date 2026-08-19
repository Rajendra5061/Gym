import { useState, type CSSProperties, type FormEvent } from 'react';
import { Link, Navigate, useNavigate } from 'react-router-dom';
import { ApiError } from '@/api/client';
import { useAuth } from '@/auth/AuthContext';
import { Alert, Field } from '@/components/ui';
import {
  IconArrowLeft, IconCheck, IconDumbbell, IconEye, IconEyeOff,
  IconInfo, IconLock, IconShield, IconUser,
} from '@/components/icons';
import { PublicNav, dashboardPathFor, useGymSettings } from '@/components/PublicNav';
import './public.css';

/** What signing in actually gets a member — the pitch promises only what the portal shows. */
const PITCH_POINTS = [
  'Membership and expiry at a glance',
  'Every check-in on record',
  'A receipt for every rupee paid',
];

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

      {/* Photo panel on the left carrying the pitch; the form on the right on a tokened surface.
          The form column is built so nobody can mistake the page: a badge saying exactly what it
          is, "Member Login" as the title, and the fields directly under it. */}
      <main className="pub-auth-wrap2">
        <div className="pub-auth-splitcard">
        <aside
          className="pub-auth-photoside"
          style={{ '--pub-photo': "url('/images/gal-latbar.jpg')" } as CSSProperties}
        >
          <div className="pub-auth-photocopy">
            <span className="pub-hero-eyebrow"><IconDumbbell size={13} /> {gymName}</span>
            <h1 className="pub-auth-pitch-title">
              You do the reps.
              <span>We keep the score.</span>
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
              <nav className="pub-auth-switch" aria-label="Sign-in type">
                <Link to="/login" className="active" aria-current="page"><IconUser size={14} /> Member</Link>
                <Link to="/admin-login"><IconShield size={14} /> Staff</Link>
              </nav>
              <Link to="/" className="btn btn-outline btn-sm"><IconArrowLeft size={13} /> Home</Link>
            </div>

            <div className="pub-auth-formmain">
              <div className="pub-auth-titlerow">
                <span className="pub-auth-mark pub-auth-mark-member"><IconUser size={24} /></span>
                <h2 className="pub-auth-bigtitle">Member Login</h2>
              </div>
              <span className="pub-auth-title-accent" aria-hidden="true" />
              <p className="pub-auth-bigsub">
                Enter your username and password to open your member dashboard.
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

                  <button type="submit" className="btn pub-btn-glow btn-block" disabled={busy}>
                    {busy ? 'Logging in…' : 'Login to my account'}
                  </button>
                </div>
              </form>

              <div className="pub-auth-underform">
                <Link to="/forgot-password" className="pub-auth-link">Forgot password?</Link>
                <span className="pub-auth-underform-alt">
                  Gym staff? <Link to="/admin-login">Sign in here</Link>
                </span>
              </div>
            </div>

            <div className="pub-auth-foot">
              <IconInfo size={14} />
              If you face issues, contact the gym admin to reset your password.
            </div>
          </div>
        </section>
        </div>
      </main>
    </div>
  );
}
