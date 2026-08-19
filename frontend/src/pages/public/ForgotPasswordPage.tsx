import { useEffect, useRef, useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { api } from '@/api/client';
import { Alert, ErrorAlert, Field } from '@/components/ui';
import {
  IconArrowLeft, IconCheck, IconEye, IconEyeOff, IconInfo, IconLock, IconUser,
} from '@/components/icons';
import { PublicNav, useGymSettings } from '@/components/PublicNav';
import { dateTime } from '@/lib/format';
import './public.css';

/**
 * `POST /api/auth/forgot-password` hands the reset token straight back because this build
 * has no mail provider wired up — an administrator has to pass it to the account holder.
 */
interface ForgotPasswordResult {
  message: string;
  resetToken?: string | null;
  expiresUtc?: string | null;
}

export default function ForgotPasswordPage() {
  const { gymName } = useGymSettings();

  const [stage, setStage] = useState<1 | 2>(1);
  const [userNameOrEmail, setUserNameOrEmail] = useState('');
  const [issued, setIssued] = useState<ForgotPasswordResult | null>(null);

  const [resetToken, setResetToken] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [showPassword, setShowPassword] = useState(false);
  const [done, setDone] = useState(false);

  const [fieldError, setFieldError] = useState<Record<string, string>>({});
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);
  const [copied, setCopied] = useState(false);
  const tokenRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (!copied) return;
    const timer = window.setTimeout(() => setCopied(false), 2000);
    return () => window.clearTimeout(timer);
  }, [copied]);

  async function requestToken(event: FormEvent) {
    event.preventDefault();
    const account = userNameOrEmail.trim();
    setError(null);
    setFieldError(account ? {} : { account: 'Username or email is required.' });
    if (!account) return;

    setBusy(true);
    try {
      const result = await api.post<ForgotPasswordResult | undefined>(
        '/api/auth/forgot-password',
        { userNameOrEmail: account },
      );
      setIssued(result ?? { message: 'If the account exists, a reset code has been issued.' });
      setResetToken(result?.resetToken ?? '');
      setStage(2);
    } catch (err) {
      setError(err);
    } finally {
      setBusy(false);
    }
  }

  async function resetPassword(event: FormEvent) {
    event.preventDefault();
    const account = userNameOrEmail.trim();
    const errors: Record<string, string> = {};
    if (!resetToken.trim()) errors.token = 'Reset code is required.';
    if (!newPassword) errors.password = 'New password is required.';
    if (!confirmPassword) errors.confirm = 'Confirm the new password.';
    else if (newPassword !== confirmPassword) errors.confirm = 'The two passwords do not match.';

    setError(null);
    setFieldError(errors);
    if (Object.keys(errors).length > 0) return;

    setBusy(true);
    try {
      await api.post<void>('/api/auth/reset-password', {
        userNameOrEmail: account,
        resetToken: resetToken.trim(),
        newPassword,
        confirmPassword,
      });
      setDone(true);
      setNewPassword('');
      setConfirmPassword('');
    } catch (err) {
      setError(err);
    } finally {
      setBusy(false);
    }
  }

  async function copyToken() {
    const token = issued?.resetToken;
    if (!token) return;
    try {
      await navigator.clipboard.writeText(token);
      setCopied(true);
    } catch {
      // Clipboard access can be denied; leave the code selected so it can be copied by hand.
      tokenRef.current?.select();
    }
  }

  return (
    <div className="pub-shell">
      <PublicNav gymName={gymName} />

      <main className="pub-auth-wrap">
        <div className="pub-auth-single">
          <div className="pub-auth-head">
            <span className="pub-auth-badge pub-badge-lavender"><IconLock size={22} /></span>
            <div className="grow">
              <div className="pub-auth-title">Forgot password</div>
              <div className="pub-auth-sub">Get a reset code, then choose a new password.</div>
            </div>
            <Link to="/login" className="btn btn-outline btn-sm"><IconArrowLeft size={13} /> Sign in</Link>
          </div>

          <div className="pub-steps">
            <span className={`pub-step ${stage === 1 ? 'pub-step-active' : 'pub-step-done'}`}>
              <span className="pub-step-num">{stage === 1 ? '1' : <IconCheck size={13} />}</span>
              Request code
            </span>
            <span className="pub-step-line" />
            <span className={`pub-step ${stage === 2 ? (done ? 'pub-step-done' : 'pub-step-active') : ''}`}>
              <span className="pub-step-num">{done ? <IconCheck size={13} /> : '2'}</span>
              New password
            </span>
          </div>

          {stage === 1 ? (
            <form onSubmit={requestToken} noValidate>
              <div className="pub-auth-fields">
                <ErrorAlert error={error} />

                <Field
                  label="Username or email"
                  required
                  error={fieldError.account}
                  help="The account you sign in with."
                >
                  <div className="input-group">
                    <span className="input-icon"><IconUser size={15} /></span>
                    <input
                      className={`input ${fieldError.account ? 'input-invalid' : ''}`}
                      value={userNameOrEmail}
                      onChange={(e) => setUserNameOrEmail(e.target.value)}
                      placeholder="Username or email"
                      autoComplete="username"
                      autoFocus
                    />
                  </div>
                </Field>

                <button type="submit" className="btn pub-btn-indigo btn-block" disabled={busy}>
                  {busy ? 'Requesting…' : 'Request reset code'}
                </button>
              </div>
            </form>
          ) : done ? (
            <div className="pub-auth-fields">
              <Alert tone="success">
                Your password has been reset. You can sign in with the new password now.
              </Alert>
              <Link to="/login" className="btn pub-btn-indigo btn-block">Go to member sign-in</Link>
              <Link to="/admin-login" className="btn btn-outline btn-block">Go to admin sign-in</Link>
            </div>
          ) : (
            <form onSubmit={resetPassword} noValidate>
              <div className="pub-auth-fields">
                <ErrorAlert error={error} />

                {issued ? <Alert tone="info"><div>{issued.message}</div></Alert> : null}

                {issued?.resetToken ? (
                  <Field
                    label="Reset code issued by the server"
                    help={
                      issued.expiresUtc
                        ? `This code expires on ${dateTime(issued.expiresUtc)}.`
                        : 'Keep this code safe; it can only be used once.'
                    }
                  >
                    <div className="pub-token-row">
                      <input className="input" ref={tokenRef} value={issued.resetToken} readOnly />
                      <button type="button" className="btn btn-outline" onClick={copyToken}>
                        {copied ? 'Copied' : 'Copy'}
                      </button>
                    </div>
                  </Field>
                ) : null}

                <Alert tone="warning">
                  <div>
                    <IconInfo size={14} /> No mail provider is configured in this build, so the
                    code is shown here instead of being emailed. An administrator must hand the
                    code over to the account holder in person.
                  </div>
                </Alert>

                <Field label="Reset code" required error={fieldError.token}>
                  <input
                    className={`input ${fieldError.token ? 'input-invalid' : ''}`}
                    value={resetToken}
                    onChange={(e) => setResetToken(e.target.value)}
                    placeholder="Paste the reset code"
                  />
                </Field>

                <Field
                  label="New password"
                  required
                  error={fieldError.password}
                  help="At least 8 characters, including a letter and a digit."
                >
                  <div className="input-group">
                    <span className="input-icon"><IconLock size={15} /></span>
                    <input
                      className={`input pub-input-has-suffix ${fieldError.password ? 'input-invalid' : ''}`}
                      type={showPassword ? 'text' : 'password'}
                      value={newPassword}
                      onChange={(e) => setNewPassword(e.target.value)}
                      autoComplete="new-password"
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

                <Field label="Confirm new password" required error={fieldError.confirm}>
                  <div className="input-group">
                    <span className="input-icon"><IconLock size={15} /></span>
                    <input
                      className={`input ${fieldError.confirm ? 'input-invalid' : ''}`}
                      type={showPassword ? 'text' : 'password'}
                      value={confirmPassword}
                      onChange={(e) => setConfirmPassword(e.target.value)}
                      autoComplete="new-password"
                    />
                  </div>
                </Field>

                <button type="submit" className="btn pub-btn-indigo btn-block" disabled={busy}>
                  {busy ? 'Saving…' : 'Set new password'}
                </button>

                <button
                  type="button"
                  className="btn btn-outline btn-block"
                  onClick={() => { setStage(1); setIssued(null); setError(null); setFieldError({}); }}
                  disabled={busy}
                >
                  Start again
                </button>
              </div>
            </form>
          )}

          <div className="pub-auth-foot">
            <IconInfo size={14} /> Reset codes are issued by the server and expire; ask an
            administrator if you cannot complete this yourself.
          </div>
        </div>
      </main>
    </div>
  );
}
