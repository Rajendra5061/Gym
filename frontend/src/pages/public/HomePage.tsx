import { useEffect, useState, type ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { api } from '@/api/client';
import { PublicNav, useGymSettings } from '@/components/PublicNav';
import {
  IconBox, IconCalendar, IconCard, IconDumbbell, IconUsers,
} from '@/components/icons';
import './public.css';

type ServerState = 'checking' | 'online' | 'offline';

const FEATURES: { icon: ReactNode; title: string; text: string; background: string }[] = [
  {
    icon: <IconUsers size={21} />, title: 'Member Management', background: 'var(--grad-blue)',
    text: 'Profiles, plans, trainers and dues for every member in one register.',
  },
  {
    icon: <IconCalendar size={21} />, title: 'Attendance Tracking', background: 'var(--grad-green)',
    text: 'Check members in and out and see who is in the gym right now.',
  },
  {
    icon: <IconCard size={21} />, title: 'Payments & Plans', background: 'var(--grad-orange)',
    text: 'Subscriptions, receipts, part-payments and refunds, all reconciled.',
  },
  {
    icon: <IconBox size={21} />, title: 'Equipment', background: 'var(--grad-cyan)',
    text: 'Inventory, servicing dates and condition for every machine on the floor.',
  },
];


export default function HomePage() {
  const { settings, gymName } = useGymSettings();
  const [server, setServer] = useState<ServerState>('checking');

  // A 200 from the API's health probe is all "connected" means here.
  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        await api.get<unknown>('/health');
        if (!cancelled) setServer('online');
      } catch {
        if (!cancelled) setServer('offline');
      }
    })();
    return () => { cancelled = true; };
  }, []);

  const serverText =
    server === 'checking' ? 'Checking server…'
    : server === 'online' ? 'Connected to the gym server'
    : 'Server not reachable — please try again shortly';

  return (
    <div className="pub-shell">
      <PublicNav gymName={gymName} />

      <main className="pub-main">
        <section className="pub-hero">
          <div>
            <span className="pub-hero-eyebrow"><IconDumbbell size={13} /> Gym Management System</span>
            <h1 className="pub-hero-title">Welcome to {gymName}</h1>
            <p className="pub-hero-text">
              Memberships, attendance, payments and equipment handled from a single console.
              Members sign in to follow their plan and dues; the front desk runs the day from
              the admin side.
            </p>
            <div className="pub-hero-actions">
              <Link to="/login" className="btn pub-btn-white">Member Login</Link>
              <Link to="/admin-login" className="btn pub-btn-on-dark">Admin Login</Link>
            </div>
            <div className="pub-status">
              <span
                className={`pub-dot ${server === 'online' ? 'pub-dot-ok' : server === 'offline' ? 'pub-dot-bad' : ''}`}
              />
              {serverText}
            </div>
          </div>

          <div className="pub-hero-panel">
            <div className="pub-hero-panel-mark"><IconDumbbell size={26} /></div>
            <div className="pub-hero-panel-title">{gymName}</div>
            <p className="pub-hero-panel-text">
              One record per member, from the day they join to today's check-in.
            </p>
            <div className="pub-hero-chips">
              <span className="pub-hero-chip">Members</span>
              <span className="pub-hero-chip">Plans</span>
              <span className="pub-hero-chip">Attendance</span>
              <span className="pub-hero-chip">Payments</span>
              <span className="pub-hero-chip">Reports</span>
            </div>
          </div>
        </section>

        <section className="pub-feature-grid">
          {FEATURES.map((feature) => (
            <article className="pub-feature-card" key={feature.title}>
              <div className="pub-feature-icon" style={{ background: feature.background }}>{feature.icon}</div>
              <div className="pub-feature-title">{feature.title}</div>
              <p className="pub-feature-text">{feature.text}</p>
            </article>
          ))}
        </section>

      </main>

      <footer className="pub-footer">
        <span>{gymName} · Gym Management System</span>
        {(settings?.phone || settings?.email) && (
          <span className="pub-footer-contact">
            {settings?.phone && <a href={`tel:${settings.phone.replace(/\s+/g, '')}`}>{settings.phone}</a>}
            {settings?.email && <a href={`mailto:${settings.email}`}>{settings.email}</a>}
          </span>
        )}
      </footer>
    </div>
  );
}
