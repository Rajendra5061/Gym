import { useEffect, useState, type CSSProperties } from 'react';
import { Link } from 'react-router-dom';
import { api } from '@/api/client';
import { PublicNav, useGymSettings } from '@/components/PublicNav';
import { IconDumbbell, IconMail, IconMapPin, IconPhone } from '@/components/icons';
import './public.css';

type ServerState = 'checking' | 'online' | 'offline';

/**
 * The training-floor gallery. Every photograph ships with the app (frontend/public/images) and
 * is licensed for this use, so the tiles never depend on a network beyond the app's own origin.
 */
const GALLERY: { src: string; label: string; className: string }[] = [
  { src: '/images/gal-latbar.jpg', label: 'Pull day', className: 'pub-gal-a' },
  { src: '/images/gal-machine.jpg', label: 'Machines', className: 'pub-gal-b' },
  { src: '/images/gal-rack.jpg', label: 'Free weights', className: 'pub-gal-c' },
  { src: '/images/gal-stretch.jpg', label: 'Mobility', className: 'pub-gal-d' },
  { src: '/images/gal-bench.jpg', label: 'Strength', className: 'pub-gal-e' },
];

/** One line, repeated to fill the marquee. Words, not claims — this strip is pure energy. */
const MARQUEE_WORDS = ['Strength', 'Cardio', 'Mobility', 'Discipline', 'Consistency', 'Recovery'];

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

  /* The address renders only when the gym has one on file; the two lines collapse into
     whatever exists. An anonymous visitor before sign-in may get neither — the column then
     leans on the standing invitation line, which is always true. */
  const addressLine = [settings?.address, settings?.city].filter(Boolean).join(', ');

  return (
    <div className="pub-shell">
      <PublicNav gymName={gymName} />

      <main className="pub-main">
        {/* ---------------------------------------------------- photo hero */}
        <section
          className="pub-xhero"
          style={{ '--pub-photo': "url('/images/hero-dark.jpg')" } as CSSProperties}
        >
          <div className="pub-xhero-inner">
            <span className="pub-hero-eyebrow"><IconDumbbell size={13} /> {gymName}</span>
            <h1 className="pub-xhero-title">
              Train hard.
              <span className="pub-xhero-accent">Track everything.</span>
            </h1>
            <p className="pub-xhero-text">
              Every rep is yours. Every membership, payment, check-in and plan is ours to keep
              straight — one console for the front desk, one portal for every member.
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
        </section>

        {/* -------------------------------------------------------- marquee */}
        <div className="pub-marquee" aria-hidden="true">
          <div className="pub-marquee-track">
            {[0, 1].map((copy) => (
              <span className="pub-marquee-run" key={copy}>
                {MARQUEE_WORDS.map((word) => (
                  <span className="pub-marquee-word" key={word}>{word}<i>·</i></span>
                ))}
              </span>
            ))}
          </div>
        </div>

        {/* -------------------------------------------------------- gallery */}
        <section>
          <div className="pub-section-head">
            <span className="pub-eyebrow">The floor</span>
            <h2 className="pub-section-title">Made of work, not promises</h2>
            <p className="pub-section-text">
              Pull days, press days, mobility and everything between — the register keeps up
              with all of it.
            </p>
          </div>
          <div className="pub-gallery">
            {GALLERY.map((tile) => (
              <figure className={`pub-gal-tile ${tile.className}`} key={tile.src}>
                <img src={tile.src} alt="" loading="lazy" />
                <figcaption>{tile.label}</figcaption>
              </figure>
            ))}
          </div>
        </section>

        {/* ----------------------------------------------------- motivation */}
        <section
          className="pub-moto"
          style={{ '--pub-photo': "url('/images/moto-curls.jpg')" } as CSSProperties}
        >
          <div className="pub-moto-photo" />
          <div className="pub-moto-body">
            <span className="pub-eyebrow pub-eyebrow-on-dark">No shortcuts</span>
            <h2 className="pub-moto-title">
              The only bad workout is the one that didn&apos;t happen.
            </h2>
            <p className="pub-moto-text">
              Show up, do the work, and let the numbers take care of themselves — this system
              keeps every one of them, from today&apos;s check-in to the receipt for the month.
            </p>
            <div className="pub-moto-stats">
              <div><strong>6</strong><span>modules on one register</span></div>
              <div><strong>14</strong><span>report types, Excel &amp; PDF</span></div>
              <div><strong>1</strong><span>record per member</span></div>
            </div>
            <div className="pub-hero-actions">
              <Link to="/login" className="btn pub-btn-white">Start your session</Link>
            </div>
          </div>
        </section>
      </main>

      {/* Near-black footer matching the nav bar, so the page opens and closes on the same
          chrome. Contact details render only when the gym has them on file. */}
      <footer className="pub-footer3">
        <div className="pub-footer3-grid">
          <div className="pub-footer3-about">
            <div className="pub-footer3-brand"><IconDumbbell size={19} /> {gymName}</div>
            <p className="pub-footer3-text">
              Every member, plan, payment and check-in in one register — run from the front
              desk, visible to every member.
            </p>
          </div>

          <div>
            <div className="pub-footer3-head">Visit us</div>
            {addressLine ? (
              <span className="pub-footer3-line"><IconMapPin size={15} /> {addressLine}</span>
            ) : null}
            <span className="pub-footer3-note">
              Walk in any time the floor is open — the front desk can sign you up in minutes.
            </span>
          </div>

          <div>
            <div className="pub-footer3-head">Contact</div>
            {settings?.phone && (
              <a className="pub-footer3-line" href={`tel:${settings.phone.replace(/\s+/g, '')}`}>
                <IconPhone size={15} /> {settings.phone}
              </a>
            )}
            {settings?.email && (
              <a className="pub-footer3-line" href={`mailto:${settings.email}`}>
                <IconMail size={15} /> {settings.email}
              </a>
            )}
          </div>

          <div>
            <div className="pub-footer3-head">Sign in</div>
            <Link to="/login" className="pub-footer3-link">Member Login</Link>
            <Link to="/admin-login" className="pub-footer3-link">Staff Login</Link>
            <Link to="/forgot-password" className="pub-footer3-link">Forgot password</Link>
          </div>
        </div>

        <div className="pub-footer3-base">
          <span>© {new Date().getFullYear()} {gymName}. All rights reserved.</span>
          <span>Gym Management System</span>
        </div>
      </footer>
    </div>
  );
}
