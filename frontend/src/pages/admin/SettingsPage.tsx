/**
 * System settings: the gym profile, mail delivery, the payment gateway, the raw key/value
 * settings table, licensing and backups.
 *
 * Restoring a backup overwrites the live database, so it demands the database name typed
 * exactly — the same phrase the API insists on — and the server checks it again regardless.
 *
 * The Email and Payment Gateway tabs exist to answer one question an operator otherwise cannot
 * answer at all: is anything automatic actually happening? Both read status the server resolved
 * at start-up, both state the current reality in a sentence before showing any detail, and
 * neither renders a secret — no SMTP password, no webhook signing key. Those live in server
 * configuration and the API does not return them.
 */

import { useCallback, useEffect, useMemo, useState } from 'react';
import type { ReactNode } from 'react';
import { useSearchParams } from 'react-router-dom';
import {
  Alert, ConfirmModal, EmptyState, ErrorAlert, Field, FormSection, Loading, Modal, PageCard,
  PageCardHeader, Pill, StatusPill,
} from '@/components/ui';
import {
  IconBox, IconCard, IconCheck, IconClock, IconDumbbell, IconInfo, IconLock, IconMail, IconMoney,
  IconPhone, IconPlus, IconQr, IconRefresh, IconSettings, IconShield, IconTrash, IconUser,
  IconWarning,
} from '@/components/icons';
import { settingsApi } from '@/api/endpoints/system';
import type {
  BackupRecordDto, EmailStatusDto, EmailTestResultDto, GymSettingsFull, LicenseStatusDto,
  PaymentGatewayStatusDto, SystemSettingDto,
} from '@/api/endpoints/system';
import { useAuth } from '@/auth/AuthContext';
import { date, dateTime } from '@/lib/format';
import './admin.css';

type Tab = 'gym' | 'email' | 'gateway' | 'system' | 'license' | 'backup';

const TABS: { key: Tab; label: string }[] = [
  { key: 'gym', label: 'Gym Information' },
  { key: 'email', label: 'Email' },
  { key: 'gateway', label: 'Payment Gateway' },
  { key: 'system', label: 'System Settings' },
  { key: 'license', label: 'License' },
  { key: 'backup', label: 'Backup' },
];

/** The tab lives in the query string so a screen can be linked to, reloaded and captured. */
function readTab(value: string | null): Tab {
  const match = TABS.find((t) => t.key === value);
  return match ? match.key : 'gym';
}

/**
 * Everything in the API that sends a message, and what makes it do so. Read off
 * PaymentReceiptMailer and PaymentReceiptFactory: one receipt per settled payment, worded by
 * what the receipt covers. Listed because "no email arrived" is otherwise indistinguishable
 * from "nothing ever asked for one".
 */
const EMAIL_TRIGGERS: { icon: ReactNode; title: string; detail: string }[] = [
  {
    icon: <IconMoney size={14} />,
    title: 'Payment receipt',
    detail: 'A payment that reaches Paid or Partially Refunded emails that member their receipt with the PDF attached. Sent at most once per payment — a retried request, a create-then-confirm sequence or a restore cannot email it twice.',
  },
  {
    icon: <IconUser size={14} />,
    title: 'New membership',
    detail: 'The receipt covering the first payment on a subscription is worded as a welcome rather than as a bare receipt.',
  },
  {
    icon: <IconRefresh size={14} />,
    title: 'Renewal',
    detail: 'The receipt covering a payment that continues an existing membership is worded as a renewal, and names the new end date.',
  },
];

/** One label/value pair in the status grids. */
function Fact(
  { label, value, note, mono }:
  { label: string; value: ReactNode; note?: string; mono?: boolean },
) {
  return (
    <div className="set-fact">
      <div className="set-fact-label">{label}</div>
      <div className={`set-fact-value${mono ? ' set-mono' : ''}`}>{value}</div>
      {note ? <div className="set-fact-note">{note}</div> : null}
    </div>
  );
}

/** "HH:mm:ss" → "HH:mm" for <input type="time">, and back again. */
function toTimeInput(value: string | undefined): string {
  return (value ?? '').slice(0, 5);
}
function fromTimeInput(value: string): string {
  return value ? `${value}:00` : '00:00:00';
}

/**
 * Backups are written as `{database}_{yyyyMMdd_HHmmss}.bak`, so the database name the restore
 * confirmation requires can be read straight off the file name.
 */
function databaseNameOf(fileName: string): string {
  const match = /^(.*)_\d{8}_\d{6}\.bak$/i.exec(fileName);
  return match ? match[1] : fileName.replace(/\.bak$/i, '');
}

export default function SettingsPage() {
  const { can, user } = useAuth();
  const mayManage = can('settings.manage');
  const mayLicense = can('license.manage');
  const mayBackup = can('backup.manage');

  const [params, setParams] = useSearchParams();
  const tab = readTab(params.get('tab'));
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<unknown>(null);

  const selectTab = useCallback((next: Tab) => {
    setNotice(null);
    setError(null);
    setParams(next === 'gym' ? {} : { tab: next }, { replace: true });
  }, [setParams]);

  /* ------------------------------------------------------------------ gym */
  const [gym, setGym] = useState<GymSettingsFull | null>(null);
  const [gymLoading, setGymLoading] = useState(true);
  const [savingGym, setSavingGym] = useState(false);

  useEffect(() => {
    const ctrl = new AbortController();
    setGymLoading(true);
    settingsApi.gym(ctrl.signal)
      .then((result) => { if (!ctrl.signal.aborted) setGym(result); })
      .catch((e) => { if (!ctrl.signal.aborted) setError(e); })
      .finally(() => { if (!ctrl.signal.aborted) setGymLoading(false); });
    return () => ctrl.abort();
  }, []);

  const patchGym = useCallback((patch: Partial<GymSettingsFull>) => {
    setGym((current) => (current ? { ...current, ...patch } : current));
  }, []);

  async function saveGym() {
    if (!gym) return;
    setSavingGym(true);
    setError(null);
    setNotice(null);
    try {
      setGym(await settingsApi.saveGym(gym));
      setNotice('Gym settings saved.');
    } catch (e) {
      setError(e);
    } finally {
      setSavingGym(false);
    }
  }

  /* ---------------------------------------------------------------- email */
  /**
   * `null` after a load means the API build has no email-status route — a state to describe, not
   * a failure — so `emailLoaded` is what separates "nothing there" from "not asked yet".
   */
  const [email, setEmail] = useState<EmailStatusDto | null>(null);
  const [emailLoaded, setEmailLoaded] = useState(false);
  const [emailLoading, setEmailLoading] = useState(false);
  const [emailReload, setEmailReload] = useState(0);

  const [testTo, setTestTo] = useState('');
  const [testBusy, setTestBusy] = useState(false);
  const [testResult, setTestResult] = useState<EmailTestResultDto | 'unavailable' | null>(null);
  const [testError, setTestError] = useState<unknown>(null);

  useEffect(() => {
    if (tab !== 'email') return;
    const ctrl = new AbortController();
    setEmailLoading(true);
    settingsApi.emailStatus(ctrl.signal)
      .then((result) => { if (!ctrl.signal.aborted) { setEmail(result); setEmailLoaded(true); } })
      .catch((e) => { if (!ctrl.signal.aborted) setError(e); })
      .finally(() => { if (!ctrl.signal.aborted) setEmailLoading(false); });
    return () => ctrl.abort();
  }, [tab, emailReload]);

  // Seeded with the signed-in operator's own address: the useful default for a test is the
  // mailbox the person clicking the button can actually open.
  useEffect(() => {
    setTestTo((current) => current || user?.email || '');
  }, [user?.email]);

  /** The one sentence the whole tab exists to say. */
  const emailState = useMemo(() => {
    if (!email) return null;
    const provider = (email.provider ?? '').trim().toLowerCase();

    if (provider === 'smtp' && email.isEnabled) {
      const relay = email.smtpHost
        ? `${email.smtpHost}${email.smtpPort ? `:${email.smtpPort}` : ''}`
        : 'the configured relay';
      return {
        tone: 'live' as const,
        title: 'Sending is on — messages are delivered to real inboxes',
        detail: `Mail leaves this server through ${relay}. Anything the app sends reaches the address it is addressed to.`,
      };
    }

    if (provider === 'file') {
      return {
        tone: 'off' as const,
        title: 'Sending is off — messages are written to a local folder and are not delivered',
        detail: 'Every message is saved to the mail drop below as a .eml plus a readable .txt, exactly as SMTP would have transmitted it. Nothing leaves this machine, and no member ever receives one.',
      };
    }

    return {
      tone: 'off' as const,
      title: 'Sending is off — messages are discarded',
      detail: email.reason?.trim()
        || 'No mail provider is configured, so every message the app produces is accepted and thrown away. Nothing is delivered and nothing is kept.',
    };
  }, [email]);

  const emailProviderTone = useMemo(() => {
    const provider = (email?.provider ?? '').trim().toLowerCase();
    if (provider === 'smtp') return 'success' as const;
    if (provider === 'file') return 'warning' as const;
    return 'neutral' as const;
  }, [email?.provider]);

  async function sendTest() {
    const to = testTo.trim();
    if (!to) return;
    setTestBusy(true);
    setTestError(null);
    setTestResult(null);
    try {
      const result = await settingsApi.sendTestEmail(to);
      setTestResult(result ?? 'unavailable');
    } catch (e) {
      setTestError(e);
    } finally {
      setTestBusy(false);
    }
  }

  /* -------------------------------------------------------- payment gateway */
  const [gateway, setGateway] = useState<PaymentGatewayStatusDto | null>(null);
  const [gatewayLoaded, setGatewayLoaded] = useState(false);
  const [gatewayLoading, setGatewayLoading] = useState(false);
  const [gatewayReload, setGatewayReload] = useState(0);
  const [copiedWebhook, setCopiedWebhook] = useState(false);

  useEffect(() => {
    if (tab !== 'gateway') return;
    const ctrl = new AbortController();
    setGatewayLoading(true);
    settingsApi.paymentGateway(ctrl.signal)
      .then((result) => { if (!ctrl.signal.aborted) { setGateway(result); setGatewayLoaded(true); } })
      .catch((e) => { if (!ctrl.signal.aborted) setError(e); })
      .finally(() => { if (!ctrl.signal.aborted) setGatewayLoading(false); });
    return () => ctrl.abort();
  }, [tab, gatewayReload]);

  /**
   * The address the provider has to reach. A server behind a proxy often cannot know its own
   * public origin, so a relative path is resolved against the origin this page was served from —
   * which is right for a same-origin deployment and clearly wrong-looking (localhost) otherwise,
   * rather than silently plausible.
   */
  const webhookUrl = useMemo(() => {
    const absolute = gateway?.webhookUrl?.trim();
    if (absolute) return absolute;
    const path = gateway?.webhookPath?.trim();
    if (!path) return null;
    try {
      return new URL(path, window.location.origin).toString();
    } catch {
      return path;
    }
  }, [gateway?.webhookUrl, gateway?.webhookPath]);

  async function copyWebhook() {
    if (!webhookUrl) return;
    try {
      await navigator.clipboard.writeText(webhookUrl);
      setCopiedWebhook(true);
      setTimeout(() => setCopiedWebhook(false), 1500);
    } catch {
      setCopiedWebhook(false);
    }
  }

  /* --------------------------------------------------------------- system */
  const [system, setSystem] = useState<SystemSettingDto[]>([]);
  const [systemLoading, setSystemLoading] = useState(false);
  const [systemDraft, setSystemDraft] = useState<Record<number, string>>({});
  const [savingKey, setSavingKey] = useState<number | null>(null);

  useEffect(() => {
    if (tab !== 'system') return;
    const ctrl = new AbortController();
    setSystemLoading(true);
    settingsApi.system(undefined, ctrl.signal)
      .then((result) => {
        if (ctrl.signal.aborted) return;
        setSystem(result);
        setSystemDraft(Object.fromEntries(result.map((s) => [s.id, s.value ?? ''])));
      })
      .catch((e) => { if (!ctrl.signal.aborted) setError(e); })
      .finally(() => { if (!ctrl.signal.aborted) setSystemLoading(false); });
    return () => ctrl.abort();
  }, [tab]);

  const systemByCategory = useMemo(() => {
    const map = new Map<string, SystemSettingDto[]>();
    for (const setting of system) {
      const list = map.get(setting.category) ?? [];
      list.push(setting);
      map.set(setting.category, list);
    }
    return [...map.entries()].sort((a, b) => a[0].localeCompare(b[0]));
  }, [system]);

  async function saveSetting(setting: SystemSettingDto) {
    setSavingKey(setting.id);
    setError(null);
    setNotice(null);
    try {
      const saved = await settingsApi.saveSystem({ ...setting, value: systemDraft[setting.id] ?? '' });
      setSystem((current) => current.map((s) => (s.id === saved.id ? saved : s)));
      setNotice(`${setting.key} saved.`);
    } catch (e) {
      setError(e);
    } finally {
      setSavingKey(null);
    }
  }

  /* -------------------------------------------------------------- license */
  const [license, setLicense] = useState<LicenseStatusDto | null>(null);
  const [licenseLoading, setLicenseLoading] = useState(false);
  const [licenseForm, setLicenseForm] = useState<'trial' | 'activate' | null>(null);
  const [licenseKey, setLicenseKey] = useState('');
  const [customerName, setCustomerName] = useState('');
  const [licenseBusy, setLicenseBusy] = useState(false);
  const [licenseError, setLicenseError] = useState<unknown>(null);

  useEffect(() => {
    if (tab !== 'license' || !mayLicense) return;
    const ctrl = new AbortController();
    setLicenseLoading(true);
    settingsApi.license(ctrl.signal)
      .then((result) => { if (!ctrl.signal.aborted) setLicense(result); })
      .catch((e) => { if (!ctrl.signal.aborted) setError(e); })
      .finally(() => { if (!ctrl.signal.aborted) setLicenseLoading(false); });
    return () => ctrl.abort();
  }, [tab, mayLicense]);

  async function submitLicense() {
    setLicenseBusy(true);
    setLicenseError(null);
    try {
      const result = licenseForm === 'trial'
        ? await settingsApi.startTrial(customerName.trim())
        : await settingsApi.activateLicense(licenseKey.trim(), customerName.trim() || undefined);
      setLicense(result);
      setLicenseForm(null);
      setNotice(licenseForm === 'trial' ? 'Trial started.' : 'Licence activated.');
    } catch (e) {
      setLicenseError(e);
    } finally {
      setLicenseBusy(false);
    }
  }

  /* --------------------------------------------------------------- backup */
  const [backups, setBackups] = useState<BackupRecordDto[]>([]);
  const [backupLoading, setBackupLoading] = useState(false);
  const [backupBusy, setBackupBusy] = useState(false);
  const [backupReload, setBackupReload] = useState(0);
  const [confirmBackup, setConfirmBackup] = useState<
    { kind: 'restore' | 'delete'; row: BackupRecordDto } | null
  >(null);

  useEffect(() => {
    if (tab !== 'backup' || !mayBackup) return;
    const ctrl = new AbortController();
    setBackupLoading(true);
    settingsApi.backups(ctrl.signal)
      .then((result) => { if (!ctrl.signal.aborted) setBackups(result); })
      .catch((e) => { if (!ctrl.signal.aborted) setError(e); })
      .finally(() => { if (!ctrl.signal.aborted) setBackupLoading(false); });
    return () => ctrl.abort();
  }, [tab, mayBackup, backupReload]);

  async function createBackup() {
    setBackupBusy(true);
    setError(null);
    setNotice(null);
    try {
      const record = await settingsApi.createBackup();
      setNotice(`Backup ${record.fileName} created.`);
      setBackupReload((k) => k + 1);
    } catch (e) {
      setError(e);
    } finally {
      setBackupBusy(false);
    }
  }

  async function runBackupAction() {
    if (!confirmBackup) return;
    setBackupBusy(true);
    setError(null);
    setNotice(null);
    try {
      if (confirmBackup.kind === 'restore') {
        await settingsApi.restoreBackup(confirmBackup.row.id, databaseNameOf(confirmBackup.row.fileName));
        setNotice(`Database restored from ${confirmBackup.row.fileName}. Restart the API so it drops stale state.`);
      } else {
        await settingsApi.deleteBackup(confirmBackup.row.id);
        setNotice(`Backup record ${confirmBackup.row.fileName} deleted.`);
      }
      setConfirmBackup(null);
      setBackupReload((k) => k + 1);
    } catch (e) {
      setError(e);
      setConfirmBackup(null);
    } finally {
      setBackupBusy(false);
    }
  }

  /* ---------------------------------------------------------------- header */
  const headerAction = (() => {
    if (tab === 'gym' && mayManage) {
      return (
        <button className="btn btn-dark" onClick={() => void saveGym()} disabled={savingGym || !gym}>
          <IconCheck size={15} /> {savingGym ? 'Saving…' : 'Save Changes'}
        </button>
      );
    }
    if (tab === 'email') {
      return (
        <button className="btn btn-outline" onClick={() => setEmailReload((k) => k + 1)} disabled={emailLoading}>
          <IconRefresh size={15} /> {emailLoading ? 'Reading…' : 'Re-read status'}
        </button>
      );
    }
    if (tab === 'gateway') {
      return (
        <button className="btn btn-outline" onClick={() => setGatewayReload((k) => k + 1)} disabled={gatewayLoading}>
          <IconRefresh size={15} /> {gatewayLoading ? 'Reading…' : 'Re-read status'}
        </button>
      );
    }
    if (tab === 'license' && mayLicense) {
      return (
        <button
          className="btn btn-dark"
          onClick={() => { setLicenseError(null); setLicenseKey(''); setCustomerName(license?.customerName ?? ''); setLicenseForm('activate'); }}
        >
          <IconShield size={15} /> Activate Key
        </button>
      );
    }
    if (tab === 'backup' && mayBackup) {
      return (
        <button className="btn btn-dark" onClick={() => void createBackup()} disabled={backupBusy}>
          <IconPlus size={15} /> {backupBusy ? 'Working…' : 'Create Backup'}
        </button>
      );
    }
    return null;
  })();

  return (
    <div className="page">
      <PageCard>
        <PageCardHeader
          icon={<IconSettings size={20} />}
          title="Settings"
          subtitle="Gym profile, mail delivery, payment gateway, system behaviour, licensing and backups."
          actions={headerAction}
        />

        <div className="row" style={{ gap: 4, padding: '0 20px', borderBottom: '1px solid var(--divider)', flexWrap: 'wrap' }}>
          {TABS.map(({ key, label }) => (
            <button
              key={key}
              onClick={() => selectTab(key)}
              style={{
                border: 0, background: 'transparent', cursor: 'pointer', padding: '12px 14px',
                fontSize: 13, fontWeight: 700,
                color: tab === key ? 'var(--primary)' : 'var(--text-2)',
                borderBottom: `2px solid ${tab === key ? 'var(--primary)' : 'transparent'}`,
                marginBottom: -1,
              }}
            >
              {label}
            </button>
          ))}
        </div>

        <div className="page-card-body">
          {notice && <div style={{ marginBottom: 16 }}><Alert tone="success">{notice}</Alert></div>}
          {error ? <div style={{ marginBottom: 16 }}><ErrorAlert error={error} /></div> : null}

          {/* --------------------------------------------------- gym profile */}
          {tab === 'gym' && (gymLoading ? <Loading message="Loading gym settings…" /> : !gym ? (
            <EmptyState icon={<IconSettings size={34} />} title="Gym settings unavailable" />
          ) : (
            <>
              {!mayManage && <div style={{ marginBottom: 16 }}><Alert tone="info">You may view these settings but not change them.</Alert></div>}

              <FormSection title="Identity" icon={<IconDumbbell size={15} />} caption="How the gym names itself on screens, receipts and reports.">
                <div className="form-grid">
                  <Field label="Gym name" required>
                    <input className="input" value={gym.gymName} disabled={!mayManage} onChange={(e) => patchGym({ gymName: e.target.value })} />
                  </Field>
                  <Field label="Legal name">
                    <input className="input" value={gym.legalName ?? ''} disabled={!mayManage} onChange={(e) => patchGym({ legalName: e.target.value })} />
                  </Field>
                  <Field label="Tax number">
                    <input className="input" value={gym.taxNumber ?? ''} disabled={!mayManage} onChange={(e) => patchGym({ taxNumber: e.target.value })} />
                  </Field>
                  <Field label="Logo path" help="Server-side path to the logo printed on receipts.">
                    <input className="input" value={gym.logoPath ?? ''} disabled={!mayManage} onChange={(e) => patchGym({ logoPath: e.target.value })} />
                  </Field>
                </div>
              </FormSection>

              <FormSection title="Contact" icon={<IconPhone size={15} />} caption="Shown to members and printed on receipts.">
                <div className="form-grid">
                  <Field label="Phone"><input className="input" value={gym.phone ?? ''} disabled={!mayManage} onChange={(e) => patchGym({ phone: e.target.value })} /></Field>
                  <Field label="E-mail"><input className="input" type="email" value={gym.email ?? ''} disabled={!mayManage} onChange={(e) => patchGym({ email: e.target.value })} /></Field>
                  <Field label="Website"><input className="input" value={gym.website ?? ''} disabled={!mayManage} onChange={(e) => patchGym({ website: e.target.value })} /></Field>
                  <Field label="Address"><input className="input" value={gym.address ?? ''} disabled={!mayManage} onChange={(e) => patchGym({ address: e.target.value })} /></Field>
                  <Field label="City"><input className="input" value={gym.city ?? ''} disabled={!mayManage} onChange={(e) => patchGym({ city: e.target.value })} /></Field>
                  <Field label="State"><input className="input" value={gym.state ?? ''} disabled={!mayManage} onChange={(e) => patchGym({ state: e.target.value })} /></Field>
                  <Field label="Postal code"><input className="input" value={gym.postalCode ?? ''} disabled={!mayManage} onChange={(e) => patchGym({ postalCode: e.target.value })} /></Field>
                  <Field label="Country"><input className="input" value={gym.country ?? ''} disabled={!mayManage} onChange={(e) => patchGym({ country: e.target.value })} /></Field>
                </div>
              </FormSection>

              <FormSection title="Currency" icon={<IconMoney size={15} />} caption="Every amount in the app is rendered with this symbol.">
                <div className="form-grid">
                  <Field label="Currency code" help="ISO code, for example INR."><input className="input" value={gym.currencyCode} disabled={!mayManage} onChange={(e) => patchGym({ currencyCode: e.target.value })} /></Field>
                  <Field label="Currency symbol"><input className="input" value={gym.currencySymbol} disabled={!mayManage} onChange={(e) => patchGym({ currencySymbol: e.target.value })} /></Field>
                </div>
              </FormSection>

              <FormSection title="UPI collection" icon={<IconQr size={15} />} caption="Used to build the payment deep link and QR code." optional>
                <div className="form-grid">
                  <Field label="UPI id"><input className="input" value={gym.upiId ?? ''} disabled={!mayManage} onChange={(e) => patchGym({ upiId: e.target.value })} /></Field>
                  <Field label="Payee name"><input className="input" value={gym.upiPayeeName ?? ''} disabled={!mayManage} onChange={(e) => patchGym({ upiPayeeName: e.target.value })} /></Field>
                  <Field label="QR image path"><input className="input" value={gym.upiQrImagePath ?? ''} disabled={!mayManage} onChange={(e) => patchGym({ upiQrImagePath: e.target.value })} /></Field>
                </div>
              </FormSection>

              <FormSection title="Receipts & codes" icon={<IconMail size={15} />} caption="Prefixes for generated receipt and member numbers.">
                <div className="form-grid">
                  <Field label="Receipt prefix"><input className="input" value={gym.receiptPrefix ?? ''} disabled={!mayManage} onChange={(e) => patchGym({ receiptPrefix: e.target.value })} /></Field>
                  <Field label="Member code prefix"><input className="input" value={gym.memberCodePrefix ?? ''} disabled={!mayManage} onChange={(e) => patchGym({ memberCodePrefix: e.target.value })} /></Field>
                  <Field label="Receipt footer text"><input className="input" value={gym.receiptFooterText ?? ''} disabled={!mayManage} onChange={(e) => patchGym({ receiptFooterText: e.target.value })} /></Field>
                </div>
              </FormSection>

              <FormSection title="Rules & security" icon={<IconLock size={15} />} caption="Opening hours, reminders, grace periods and sign-in lockout.">
                <div className="form-grid">
                  <Field label="Opening time">
                    <input className="input" type="time" value={toTimeInput(gym.openingTime)} disabled={!mayManage} onChange={(e) => patchGym({ openingTime: fromTimeInput(e.target.value) })} />
                  </Field>
                  <Field label="Closing time">
                    <input className="input" type="time" value={toTimeInput(gym.closingTime)} disabled={!mayManage} onChange={(e) => patchGym({ closingTime: fromTimeInput(e.target.value) })} />
                  </Field>
                  <Field label="Expiry reminder (days)">
                    <input className="input" type="number" min={0} value={gym.expiryReminderDays} disabled={!mayManage} onChange={(e) => patchGym({ expiryReminderDays: Number(e.target.value) })} />
                  </Field>
                  <Field label="Default grace period (days)">
                    <input className="input" type="number" min={0} value={gym.defaultGracePeriodDays} disabled={!mayManage} onChange={(e) => patchGym({ defaultGracePeriodDays: Number(e.target.value) })} />
                  </Field>
                  <Field label="Max failed sign-ins">
                    <input className="input" type="number" min={1} value={gym.maxFailedLoginAttempts} disabled={!mayManage} onChange={(e) => patchGym({ maxFailedLoginAttempts: Number(e.target.value) })} />
                  </Field>
                  <Field label="Lockout (minutes)">
                    <input className="input" type="number" min={1} value={gym.lockoutMinutes} disabled={!mayManage} onChange={(e) => patchGym({ lockoutMinutes: Number(e.target.value) })} />
                  </Field>
                  <Field label="Expired members" help="Allow a check-in after the subscription has ended.">
                    <label className="row" style={{ height: 38, gap: 8 }}>
                      <input
                        type="checkbox"
                        checked={gym.allowExpiredMemberCheckIn}
                        disabled={!mayManage}
                        onChange={(e) => patchGym({ allowExpiredMemberCheckIn: e.target.checked })}
                      />
                      <span style={{ fontSize: 13 }}>Allow expired member check-in</span>
                    </label>
                  </Field>
                </div>
              </FormSection>
            </>
          ))}

          {/* ----------------------------------------------------------- email */}
          {tab === 'email' && (emailLoading && !emailLoaded ? <Loading message="Reading the mail configuration…" /> : (
            <div className="stack">
              {!email ? (
                <Alert tone="info">
                  <IconInfo size={18} />
                  <div>
                    <div style={{ fontWeight: 600 }}>Mail status is not available on this server.</div>
                    <div>
                      This API build answers no <code>GET /api/settings/email</code>, so the provider
                      actually in force cannot be read from here. Nothing is broken by that: mail is
                      configured entirely in the server&apos;s <code>Email</code> section — appsettings,
                      user secrets or environment variables — and the choice is made once, when the API
                      starts. Until the endpoint exists, the start-up log line beginning
                      &ldquo;Email provider:&rdquo; is the authoritative answer.
                    </div>
                  </div>
                </Alert>
              ) : (
                <div className={`set-state set-state-${emailState?.tone ?? 'off'}`}>
                  <div className="set-state-icon">
                    {emailState?.tone === 'live' ? <IconCheck size={17} /> : <IconWarning size={17} />}
                  </div>
                  <div className="grow">
                    <div className="set-state-title">{emailState?.title}</div>
                    <div className="set-state-detail">{emailState?.detail}</div>
                  </div>
                </div>
              )}

              {email && (
                <FormSection
                  title="Effective configuration"
                  icon={<IconMail size={15} />}
                  caption="Resolved once at start-up from the server's Email configuration. Changing it takes an API restart — it is not stored in the database and cannot be edited here."
                >
                  <div className="set-facts">
                    <Fact
                      label="Provider"
                      value={<Pill tone={emailProviderTone}>{email.provider || 'None'}</Pill>}
                      note={email.isEnabled ? 'A sender is built and accepting messages.' : 'The null sender: messages are accepted and discarded.'}
                    />
                    <Fact label="From address" value={email.fromAddress || 'Not set'} mono />
                    <Fact label="From name" value={email.fromName || 'Not set'} />
                    {email.replyToAddress ? <Fact label="Reply-to" value={email.replyToAddress} mono /> : null}

                    {(email.provider ?? '').toLowerCase() === 'file' && (
                      <Fact
                        label="Mail drop folder"
                        value={email.fileSinkDirectory || 'logs/mail-drop'}
                        mono
                        note="Two files per message: the complete .eml and a readable .txt sidecar."
                      />
                    )}

                    {(email.provider ?? '').toLowerCase() === 'smtp' && (
                      <>
                        <Fact
                          label="SMTP host"
                          value={`${email.smtpHost || 'Not set'}${email.smtpPort ? `:${email.smtpPort}` : ''}`}
                          mono
                        />
                        <Fact
                          label="Transport security"
                          value={email.smtpUseStartTls === false ? 'Off' : 'STARTTLS / TLS'}
                          note={email.smtpUseStartTls === false ? 'Only safe against a relay on loopback.' : undefined}
                        />
                        <Fact label="SMTP user" value={email.smtpUserName || 'Not set — anonymous relay'} mono />
                      </>
                    )}

                    <Fact
                      label="SMTP credentials"
                      value={email.smtpCredentialsConfigured
                        ? <Pill tone="success">Configured</Pill>
                        : <Pill tone="neutral">Not configured</Pill>}
                      note="Supplied through the Email__Smtp__Password environment variable, user secrets or a secret store."
                    />
                  </div>

                  <div className="form-note">
                    There is no password box on this screen, and there will not be one. The SMTP
                    password never touches the database and the API never returns it, so the only
                    thing that can be shown here is whether one resolved.
                  </div>
                </FormSection>
              )}

              <FormSection
                title="What sends an email"
                icon={<IconCard size={15} />}
                caption="Every message this application produces, and the event that produces it."
              >
                <ul className="set-triggers">
                  {EMAIL_TRIGGERS.map((trigger) => (
                    <li key={trigger.title} className="set-trigger">
                      <span className="set-trigger-dot">{trigger.icon}</span>
                      <span>
                        <span className="set-trigger-main">{trigger.title}</span>
                        <span className="set-trigger-sub" style={{ display: 'block' }}>{trigger.detail}</span>
                      </span>
                    </li>
                  ))}
                </ul>
                <div className="form-note">
                  Nothing else sends mail. A member never receives an expiry reminder, an attendance
                  note or a marketing message from this server — those are in-app notifications only.
                </div>
              </FormSection>

              <FormSection
                title="Send a test message"
                icon={<IconMail size={15} />}
                caption="Puts one message through whichever provider is live, so you can see exactly where it lands."
              >
                {!mayManage ? (
                  <Alert tone="info">Sending a test needs the settings management permission.</Alert>
                ) : (
                  <>
                    <div className="set-test-row">
                      <Field
                        label="Send to"
                        help="With the file provider nothing is transmitted — the message is written to the mail drop under this address."
                      >
                        <input
                          className="input"
                          type="email"
                          value={testTo}
                          placeholder="you@example.com"
                          onChange={(e) => setTestTo(e.target.value)}
                        />
                      </Field>
                      <button
                        className="btn btn-dark"
                        disabled={testBusy || testTo.trim().length === 0}
                        onClick={() => void sendTest()}
                      >
                        <IconMail size={15} /> {testBusy ? 'Sending…' : 'Send test email'}
                      </button>
                    </div>

                    {testError ? <div style={{ marginTop: 14 }}><ErrorAlert error={testError} /></div> : null}

                    {testResult === 'unavailable' && (
                      <div style={{ marginTop: 14 }}>
                        <Alert tone="info">
                          <IconInfo size={18} />
                          <div>
                            <div style={{ fontWeight: 600 }}>This server has no send-test endpoint.</div>
                            <div>
                              Nothing was sent and nothing was written. <code>POST /api/settings/email/test</code>
                              {' '}is not part of this API build.
                            </div>
                          </div>
                        </Alert>
                      </div>
                    )}

                    {testResult && testResult !== 'unavailable' && (
                      <div style={{ marginTop: 14 }}>
                        <Alert tone={testResult.sent ? 'success' : 'warning'}>
                          {testResult.sent ? <IconCheck size={18} /> : <IconWarning size={18} />}
                          <div>
                            <div style={{ fontWeight: 600 }}>
                              {testResult.sent
                                ? `Accepted by the ${testResult.provider || 'configured'} provider.`
                                : 'Nothing was sent.'}
                            </div>
                            <div>
                              {testResult.sent
                                ? <>Addressed to <strong>{testResult.toAddress || testTo.trim()}</strong>
                                    {testResult.destination ? <> and delivered to <code>{testResult.destination}</code></> : null}.</>
                                : (testResult.reason || 'The server gave no reason.')}
                            </div>
                          </div>
                        </Alert>
                      </div>
                    )}
                  </>
                )}
              </FormSection>
            </div>
          ))}

          {/* -------------------------------------------------- payment gateway */}
          {tab === 'gateway' && (gatewayLoading && !gatewayLoaded ? <Loading message="Reading the gateway configuration…" /> : (
            <div className="stack">
              {!gateway ? (
                <Alert tone="info">
                  <IconInfo size={18} />
                  <div>
                    <div style={{ fontWeight: 600 }}>Gateway status is not available on this server.</div>
                    <div>
                      This API build answers no <code>GET /api/payments/gateway</code>, so this screen
                      cannot report a gateway either way. Treat every UPI and bank transfer as
                      hand-verified until it can — which is what the collection screen already tells
                      the person taking the money.
                    </div>
                  </div>
                </Alert>
              ) : gateway.isConfigured ? (
                <div className="set-state set-state-live">
                  <div className="set-state-icon"><IconShield size={17} /></div>
                  <div className="grow">
                    <div className="set-state-title">
                      {gateway.provider ? `${gateway.provider} is configured` : 'A payment gateway is configured'}
                      {gateway.mode ? <> <Pill tone={gateway.mode.toLowerCase() === 'live' ? 'success' : 'warning'}>{gateway.mode}</Pill></> : null}
                    </div>
                    <div className="set-state-detail">
                      {gateway.message?.trim()
                        || 'The provider calls this server when a transfer completes, and the payment is settled from that verified callback rather than by hand.'}
                    </div>
                  </div>
                </div>
              ) : (
                <div className="set-state set-state-off">
                  <div className="set-state-icon"><IconWarning size={17} /></div>
                  <div className="grow">
                    <div className="set-state-title">No payment gateway is configured — transfers are confirmed by hand</div>
                    <div className="set-state-detail">
                      {gateway.message?.trim()
                        || 'Nothing calls this server when a member pays, so a UPI or bank transfer is only settled once a member of staff reads the transaction reference off the payer’s app and confirms it.'}
                    </div>
                  </div>
                </div>
              )}

              {gateway?.isConfigured && (
                <FormSection
                  title="Webhook"
                  icon={<IconShield size={15} />}
                  caption="The address the provider must call. It has to be reachable from the provider's servers, not just from this browser."
                >
                  <Field
                    label="Endpoint URL"
                    help="Paste this into the provider's dashboard. Every call to it is signature-verified before a payment is touched."
                  >
                    <div className="set-url">
                      <code>{webhookUrl || 'The server did not report a webhook address.'}</code>
                      {webhookUrl && (
                        <button className="btn btn-outline btn-sm" onClick={() => void copyWebhook()}>
                          {copiedWebhook ? <><IconCheck size={13} /> Copied</> : 'Copy'}
                        </button>
                      )}
                    </div>
                  </Field>

                  <div className="set-facts" style={{ marginTop: 18 }}>
                    <Fact label="Provider" value={gateway.provider || 'Not reported'} />
                    <Fact label="Mode" value={gateway.mode || 'Not reported'} />
                    <Fact
                      label="Signing secret"
                      value={gateway.signingSecretConfigured
                        ? <Pill tone="success">Configured</Pill>
                        : <Pill tone="danger">Not configured</Pill>}
                      note={gateway.signingSecretConfigured
                        ? 'Held in server configuration. The value is never returned by the API, so it cannot be shown here.'
                        : 'Without it no callback can be verified, and the endpoint will reject everything the provider sends.'}
                    />
                    <Fact
                      label="Last verified callback"
                      value={gateway.lastEventAtUtc ? dateTime(gateway.lastEventAtUtc) : 'None yet'}
                    />
                  </div>

                  <div className="form-note">
                    <span className="set-secret-note"><IconLock size={12} /> The signing secret is deliberately absent from this page and from every API response. If you need to rotate it, change it in the server configuration and restart the API.</span>
                  </div>
                </FormSection>
              )}

              <FormSection
                title="What this changes when collecting money"
                icon={<IconQr size={15} />}
                caption="The UPI collection screen reads the same flag and words itself to match."
              >
                <ul className="set-triggers">
                  <li className="set-trigger">
                    <span className="set-trigger-dot"><IconQr size={14} /></span>
                    <span>
                      <span className="set-trigger-main">
                        {gateway && gateway.isConfigured && gateway.requiresManualVerification === false
                          ? 'Collect by UPI settles itself'
                          : 'Collect by UPI needs a human'}
                      </span>
                      <span className="set-trigger-sub" style={{ display: 'block' }}>
                        {gateway && gateway.isConfigured && gateway.requiresManualVerification === false
                          ? 'The QR modal tells staff the gateway will confirm the transfer, and the payment settles from the verified callback.'
                          : 'The QR modal tells staff that nothing confirms the transfer automatically, and that they must record the transaction reference and confirm the payment themselves.'}
                      </span>
                    </span>
                  </li>
                  <li className="set-trigger">
                    <span className="set-trigger-dot"><IconMail size={14} /></span>
                    <span>
                      <span className="set-trigger-main">Receipts follow settlement, not collection</span>
                      <span className="set-trigger-sub" style={{ display: 'block' }}>
                        A receipt is emailed when the payment reaches a settled status — whether that
                        happened through a gateway callback or through a member of staff confirming it.
                        With mail off, nothing is delivered either way.
                      </span>
                    </span>
                  </li>
                </ul>
              </FormSection>
            </div>
          ))}

          {/* ------------------------------------------------ system settings */}
          {tab === 'system' && (systemLoading ? <Loading message="Loading system settings…" /> : system.length === 0 ? (
            <EmptyState icon={<IconSettings size={34} />} title="No system settings" message="Nothing has been stored in the settings table yet." />
          ) : (
            <>
              {systemByCategory.map(([category, settings]) => (
                <FormSection key={category} title={category} caption={`${settings.length} setting${settings.length === 1 ? '' : 's'}`}>
                  <div className="table-wrap">
                    <table className="table">
                      <thead>
                        <tr>
                          <th className="idx">#</th>
                          <th>Key</th>
                          <th>Value</th>
                          <th>Description</th>
                          <th className="actions">Actions</th>
                        </tr>
                      </thead>
                      <tbody>
                        {settings.map((setting, index) => (
                          <tr key={setting.id}>
                            <td className="idx">{index + 1}</td>
                            <td>
                              <div className="cell-main">{setting.key}</div>
                              <div className="cell-sub">{setting.dataType}{setting.isReadOnly ? ' · read only' : ''}</div>
                            </td>
                            <td style={{ minWidth: 220 }}>
                              {setting.dataType.toLowerCase() === 'bool' || setting.dataType.toLowerCase() === 'boolean' ? (
                                <label className="row" style={{ gap: 8 }}>
                                  <input
                                    type="checkbox"
                                    disabled={setting.isReadOnly || !mayManage}
                                    checked={(systemDraft[setting.id] ?? '').toLowerCase() === 'true'}
                                    onChange={(e) => setSystemDraft((d) => ({ ...d, [setting.id]: e.target.checked ? 'true' : 'false' }))}
                                  />
                                  <span style={{ fontSize: 13 }}>{(systemDraft[setting.id] ?? '').toLowerCase() === 'true' ? 'Enabled' : 'Disabled'}</span>
                                </label>
                              ) : (
                                <input
                                  className="input"
                                  type={['int', 'integer', 'decimal', 'number'].includes(setting.dataType.toLowerCase()) ? 'number' : 'text'}
                                  disabled={setting.isReadOnly || !mayManage}
                                  value={systemDraft[setting.id] ?? ''}
                                  onChange={(e) => setSystemDraft((d) => ({ ...d, [setting.id]: e.target.value }))}
                                />
                              )}
                            </td>
                            <td className="muted" style={{ maxWidth: 320 }}>{setting.description || '—'}</td>
                            <td className="actions">
                              {mayManage && !setting.isReadOnly ? (
                                <button
                                  className="btn btn-edit"
                                  disabled={savingKey === setting.id || (systemDraft[setting.id] ?? '') === (setting.value ?? '')}
                                  onClick={() => void saveSetting(setting)}
                                >
                                  {savingKey === setting.id ? 'Saving…' : 'Save'}
                                </button>
                              ) : <span className="muted">Locked</span>}
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                </FormSection>
              ))}
            </>
          ))}

          {/* ------------------------------------------------------- licence */}
          {tab === 'license' && (!mayLicense ? (
            <Alert tone="info">Licence details need the licence management permission.</Alert>
          ) : licenseLoading ? <Loading message="Loading licence…" /> : !license ? (
            <EmptyState icon={<IconShield size={34} />} title="No licence information" />
          ) : (
            <>
              {license.clockTamperDetected && (
                <div style={{ marginBottom: 16 }}>
                  <Alert tone="error"><IconWarning size={16} /> The system clock appears to have been moved backwards. Licence checks are running in a restricted mode.</Alert>
                </div>
              )}

              <div className="page-card" style={{ boxShadow: 'none' }}>
                <div className="page-card-header">
                  <div className="grow">
                    <div className="row" style={{ gap: 10 }}>
                      <h3 style={{ fontSize: 18 }}>{license.customerName || 'This installation'}</h3>
                      <StatusPill status={license.statusText} />
                      {license.isTrial ? <Pill tone="warning">Trial</Pill> : null}
                    </div>
                    <div className="page-card-subtitle">{license.message || `Installation ${license.gymIdentifier || '—'}`}</div>
                  </div>
                  <button
                    className="btn btn-outline"
                    onClick={() => { setLicenseError(null); setCustomerName(license.customerName ?? ''); setLicenseForm('trial'); }}
                  >
                    <IconClock size={14} /> Start trial
                  </button>
                </div>

                <div className="page-card-body">
                  <div className="form-grid">
                    <div>
                      <div className="field-label">Licence key</div>
                      <div className="cell-main" style={{ marginTop: 4 }}>{license.maskedLicenseKey || license.licenseKey || '—'}</div>
                    </div>
                    <div>
                      <div className="field-label">Started</div>
                      <div className="cell-main" style={{ marginTop: 4 }}>{date(license.startDateUtc)}</div>
                    </div>
                    <div>
                      <div className="field-label">Expires</div>
                      <div className="cell-main" style={{ marginTop: 4 }}>{date(license.expiryDateUtc)}</div>
                    </div>
                    <div>
                      <div className="field-label">Days remaining</div>
                      <div className="cell-main" style={{ marginTop: 4 }}>
                        {license.daysRemaining}
                        {license.isExpiringSoon ? <> <Pill tone="warning">Expiring soon</Pill></> : null}
                      </div>
                    </div>
                    <div>
                      <div className="field-label">Members</div>
                      <div className="cell-main" style={{ marginTop: 4 }}>
                        {license.currentMembers}{license.maxMembers ? ` of ${license.maxMembers}` : ' (unlimited)'}
                      </div>
                    </div>
                    <div>
                      <div className="field-label">User accounts</div>
                      <div className="cell-main" style={{ marginTop: 4 }}>
                        {license.currentUsers}{license.maxUsers ? ` of ${license.maxUsers}` : ' (unlimited)'}
                      </div>
                    </div>
                  </div>

                  {license.enabledFeatures.length > 0 && (
                    <div style={{ marginTop: 18 }}>
                      <div className="field-label" style={{ marginBottom: 8 }}>Enabled features</div>
                      <div className="row" style={{ gap: 6, flexWrap: 'wrap' }}>
                        {license.enabledFeatures.map((feature) => <Pill key={feature} tone="primary">{feature}</Pill>)}
                      </div>
                    </div>
                  )}
                </div>
              </div>
            </>
          ))}

          {/* -------------------------------------------------------- backup */}
          {tab === 'backup' && (!mayBackup ? (
            <Alert tone="info">Backups need the backup management permission.</Alert>
          ) : backupLoading ? <Loading message="Loading backup history…" /> : backups.length === 0 ? (
            <EmptyState icon={<IconBox size={34} />} title="No backups yet" message="Create the first backup with the button above." />
          ) : (
            <div className="table-wrap">
              <table className="table">
                <thead>
                  <tr>
                    <th className="idx">#</th>
                    <th>File</th>
                    <th>Size</th>
                    <th>Created</th>
                    <th>By</th>
                    <th>Status</th>
                    <th className="actions">Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {backups.map((row, index) => (
                    <tr key={row.id}>
                      <td className="idx">{index + 1}</td>
                      <td>
                        <div className="cell-main">{row.fileName}</div>
                        <div className="cell-sub">{row.filePath}</div>
                      </td>
                      <td>{row.fileSizeText}</td>
                      <td><span className="cell-icon"><IconClock size={14} />{dateTime(row.createdAtUtc)}</span></td>
                      <td>{row.createdByName || '—'}</td>
                      <td>
                        {row.isSuccess ? <Pill tone="success">{row.backupType}</Pill> : <Pill tone="danger">Failed</Pill>}
                        {row.restoredAtUtc ? <div className="cell-sub">Restored {dateTime(row.restoredAtUtc)}</div> : null}
                        {row.errorMessage ? <div className="cell-sub">{row.errorMessage}</div> : null}
                      </td>
                      <td className="actions">
                        <button
                          className="btn btn-edit"
                          disabled={!row.isSuccess || backupBusy}
                          onClick={() => setConfirmBackup({ kind: 'restore', row })}
                        >
                          Restore
                        </button>
                        <button className="btn btn-del" disabled={backupBusy} onClick={() => setConfirmBackup({ kind: 'delete', row })}>
                          <IconTrash size={12} /> Delete
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          ))}
        </div>
      </PageCard>

      {/* --------------------------------------------------------- licence modal */}
      {licenseForm && (
        <Modal
          title={licenseForm === 'trial' ? 'Start evaluation trial' : 'Activate licence key'}
          icon={<IconShield size={18} />}
          width={520}
          onClose={() => setLicenseForm(null)}
          footer={
            <>
              <button className="btn btn-outline" onClick={() => setLicenseForm(null)} disabled={licenseBusy}>Cancel</button>
              <button className="btn btn-dark" onClick={() => void submitLicense()} disabled={licenseBusy}>
                {licenseBusy ? 'Working…' : licenseForm === 'trial' ? 'Start trial' : 'Activate'}
              </button>
            </>
          }
        >
          <div className="stack">
            <ErrorAlert error={licenseError} />
            {licenseForm === 'activate' && (
              <Field label="Licence key" required help="Paste the key exactly as it was issued.">
                <input className="input" value={licenseKey} onChange={(e) => setLicenseKey(e.target.value)} autoFocus />
              </Field>
            )}
            <Field label="Customer name" required={licenseForm === 'trial'}>
              <input className="input" value={customerName} onChange={(e) => setCustomerName(e.target.value)} />
            </Field>
          </div>
        </Modal>
      )}

      {/* --------------------------------------------------------- backup modals */}
      {confirmBackup?.kind === 'restore' && (
        <ConfirmModal
          title="Restore database"
          confirmLabel="Restore database"
          confirmText={databaseNameOf(confirmBackup.row.fileName)}
          busy={backupBusy}
          message={
            <>
              Restoring <strong>{confirmBackup.row.fileName}</strong> overwrites every record currently in the
              database — members, payments and settings created since {dateTime(confirmBackup.row.createdAtUtc)} will be
              lost. Type the database name to confirm.
            </>
          }
          onConfirm={() => void runBackupAction()}
          onClose={() => setConfirmBackup(null)}
        />
      )}

      {confirmBackup?.kind === 'delete' && (
        <ConfirmModal
          title="Delete backup record"
          confirmLabel="Delete record"
          busy={backupBusy}
          message={
            <>Remove <strong>{confirmBackup.row.fileName}</strong> from the backup history? The file on disk is
              left in place.</>
          }
          onConfirm={() => void runBackupAction()}
          onClose={() => setConfirmBackup(null)}
        />
      )}
    </div>
  );
}
