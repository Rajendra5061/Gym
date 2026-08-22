import { useEffect, useRef, useState } from 'react';
import { NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom';
import { useAuth } from '@/auth/AuthContext';
import { getNotificationCounts } from '@/api/endpoints/notifications';
import { initials } from '@/lib/format';
import { ThemeToggle } from '@/components/ThemeToggle';
import {
  IconDashboard, IconUsers, IconUser, IconCrown, IconCard, IconCalendar, IconDumbbell,
  IconChart, IconBell, IconShield, IconFile, IconTrash, IconSettings, IconLogout, IconMenu,
  IconMessage, IconBox, IconCheckSquare, IconMoney, IconChevronsLeft, IconPhone, IconMail,
} from '@/components/icons';
import type { ReactNode } from 'react';

/** Remembers the collapsed rail across visits. */
const SIDEBAR_KEY = 'gym.sidebarCollapsed';

interface NavItem { to: string; label: string; icon: ReactNode; permission?: string; }
/** A labelled block of destinations in the rail. A null label renders the items with no heading. */
interface NavSection { label: string | null; items: NavItem[]; }

/**
 * Admin/staff navigation. Every destination is visible in the rail — grouped under headings
 * rather than hidden behind menus — which is also how the Reports screen presents its own
 * sidebar, so the app has a single navigation idiom.
 */
const adminNav: NavSection[] = [
  {
    label: null, items: [
      { to: '/admin/dashboard', label: 'Dashboard', icon: <IconDashboard size={16} />, permission: 'dashboard.view' },
    ],
  },
  {
    label: 'Members', items: [
      { to: '/admin/members',  label: 'Members',  icon: <IconUsers size={16} />, permission: 'members.view' },
      { to: '/admin/trainers', label: 'Trainers', icon: <IconUser size={16} />,  permission: 'trainers.view' },
    ],
  },
  {
    label: 'Billing', items: [
      { to: '/admin/plans',         label: 'Membership Plans', icon: <IconCrown size={16} />,       permission: 'plans.view' },
      { to: '/admin/subscriptions', label: 'Subscriptions',    icon: <IconCheckSquare size={16} />, permission: 'subscriptions.view' },
      { to: '/admin/payments',      label: 'Payments',         icon: <IconCard size={16} />,        permission: 'payments.view' },
      { to: '/admin/expenses',      label: 'Expenses',         icon: <IconMoney size={16} />,       permission: 'expenses.view' },
      { to: '/admin/salaries',      label: 'Salaries',         icon: <IconMoney size={16} />,       permission: 'salary.view' },
    ],
  },
  {
    label: 'Activity', items: [
      { to: '/admin/attendance',    label: 'Attendance',    icon: <IconCalendar size={16} />, permission: 'attendance.view' },
      { to: '/admin/workout-plans', label: 'Workout Plans', icon: <IconDumbbell size={16} />, permission: 'workouts.view' },
      { to: '/admin/diet-plans',    label: 'Diet Plans',    icon: <IconFile size={16} />,     permission: 'diet.view' },
      { to: '/admin/equipment',     label: 'Equipment',     icon: <IconBox size={16} />,      permission: 'equipment.view' },
    ],
  },
  {
    label: 'Engagement', items: [
      { to: '/admin/enquiries',     label: 'Enquiries',     icon: <IconMessage size={16} />, permission: 'enquiries.view' },
      { to: '/admin/feedback',      label: 'Feedback',      icon: <IconMessage size={16} />, permission: 'feedback.view' },
      { to: '/admin/notifications', label: 'Notifications', icon: <IconBell size={16} />,    permission: 'notifications.view' },
      { to: '/admin/communications', label: 'Communications', icon: <IconPhone size={16} />, permission: 'notifications.view' },
    ],
  },
  {
    label: 'Insights', items: [
      { to: '/admin/reports', label: 'Reports', icon: <IconChart size={16} />, permission: 'reports.basic' },
    ],
  },
  {
    label: 'System', items: [
      { to: '/admin/users',       label: 'Users',       icon: <IconUser size={16} />,     permission: 'users.view' },
      { to: '/admin/audit',       label: 'Audit Logs',  icon: <IconFile size={16} />,     permission: 'audit.view' },
      { to: '/admin/recycle-bin', label: 'Recycle Bin', icon: <IconTrash size={16} />,    permission: 'recyclebin.view' },
      { to: '/admin/settings',    label: 'Settings',    icon: <IconSettings size={16} />, permission: 'settings.view' },
    ],
  },
];

/**
 * Trainer portal navigation. Trainers see only the screens their day runs on: their roster,
 * the plans they write, and the attendance desk. The list/form screens reuse the admin
 * components on /trainer paths, so permission codes gate them exactly as they do for staff.
 */
const trainerNav: NavSection[] = [
  {
    label: null, items: [
      { to: '/trainer/dashboard', label: 'Dashboard', icon: <IconDashboard size={16} /> },
    ],
  },
  {
    label: 'Members', items: [
      { to: '/trainer/members',    label: 'My Members', icon: <IconUsers size={16} /> },
      { to: '/trainer/attendance', label: 'Attendance', icon: <IconCalendar size={16} />, permission: 'attendance.view' },
    ],
  },
  {
    label: 'Coaching', items: [
      { to: '/trainer/workout-plans', label: 'Workout Plans', icon: <IconDumbbell size={16} />, permission: 'workouts.view' },
      { to: '/trainer/diet-plans',    label: 'Diet Plans',    icon: <IconFile size={16} />,     permission: 'diet.view' },
    ],
  },
  {
    label: 'Updates', items: [
      { to: '/trainer/notifications', label: 'Notifications', icon: <IconBell size={16} />, permission: 'notifications.view' },
    ],
  },
];

/**
 * Member self-service navigation. Grouped under the same heading idiom as the admin and trainer
 * rails: nine flat entries read as one undifferentiated list, and the headings say which of them
 * are about paying, which about training, and which about keeping in touch.
 */
const memberNav: NavSection[] = [
  {
    label: null, items: [
      { to: '/member/dashboard', label: 'Dashboard', icon: <IconDashboard size={16} /> },
    ],
  },
  {
    label: 'Membership', items: [
      // No profile entry here: the avatar menu in the header already has "View Profile", and two
      // routes to the same screen in two places invites them to drift apart.
      { to: '/member/membership', label: 'Membership', icon: <IconCrown size={16} /> },
      { to: '/member/payments',   label: 'Payments',   icon: <IconCard size={16} /> },
    ],
  },
  {
    label: 'Training', items: [
      { to: '/member/workout-plans', label: 'Workout Plans', icon: <IconDumbbell size={16} /> },
      { to: '/member/diet',          label: 'Diet',          icon: <IconFile size={16} /> },
    ],
  },
  {
    label: 'Activity', items: [
      { to: '/member/attendance', label: 'Attendance', icon: <IconCalendar size={16} /> },
      { to: '/member/progress',   label: 'Progress',   icon: <IconChart size={16} /> },
    ],
  },
  {
    label: 'Updates', items: [
      { to: '/member/notifications', label: 'Notifications', icon: <IconBell size={16} /> },
      { to: '/member/feedback',      label: 'Feedback',      icon: <IconMessage size={16} /> },
    ],
  },
];

const NAV_BY_AREA = { admin: adminNav, trainer: trainerNav, member: memberNav } as const;

/** Where the header bell lands: each area reads its alerts on its own notifications screen. */
const BELL_TARGET = {
  admin: '/admin/notifications',
  trainer: '/trainer/notifications',
  member: '/member/notifications',
} as const;

export function AppLayout({ area }: { area: 'admin' | 'trainer' | 'member' }) {
  const { gym, user, can, signOut } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const [menuOpen, setMenuOpen] = useState(false);
  const menuRef = useRef<HTMLDivElement>(null);
  const [unread, setUnread] = useState(0);

  // Remembered per browser: someone who works collapsed should not have to re-collapse every visit.
  const [collapsed, setCollapsed] = useState(
    () => localStorage.getItem(SIDEBAR_KEY) === '1',
  );
  useEffect(() => { localStorage.setItem(SIDEBAR_KEY, collapsed ? '1' : '0'); }, [collapsed]);

  // Warm the heavy lazy routes once the shell is idle, so the first click on Dashboard or
  // Members doesn't pay the chunk download (and, in dev, the compile) while the user watches
  // a spinner. Fire-and-forget: a failed prefetch just means the click loads it as before.
  useEffect(() => {
    const warm = () => {
      const routes: Array<() => Promise<unknown>> = [
        () => import('@/pages/admin/DashboardPage'),
        () => import('@/pages/admin/MembersPage'),
        () => import('@/pages/admin/SubscriptionsPage'),
        () => import('@/pages/admin/PaymentsPage'),
        () => import('@/pages/admin/CommunicationsPage'),
        () => import('@/pages/member/MemberDashboardPage'),
        () => import('@/pages/trainer/TrainerDashboardPage'),
      ];
      routes.forEach((load) => { load().catch(() => { /* loaded on demand instead */ }); });
    };
    const idle = (window as Window & { requestIdleCallback?: (cb: () => void) => number }).requestIdleCallback;
    const handle = idle ? idle(warm) : window.setTimeout(warm, 1500);
    return () => {
      if (!idle) window.clearTimeout(handle as number);
    };
  }, []);

  // Phone drawer: the rail slides in over the page and any navigation dismisses it.
  const [drawerOpen, setDrawerOpen] = useState(false);
  useEffect(() => setDrawerOpen(false), [location.pathname]);

  // Drop destinations the signed-in user may not reach, then drop any heading left with nothing
  // under it — an "Engagement" label above empty space reads as a broken menu.
  const sections = NAV_BY_AREA[area]
    .map((section) => ({
      ...section,
      items: section.items.filter((item) => !item.permission || can(item.permission)),
    }))
    .filter((section) => section.items.length > 0);

  const canSeeNotifications = can('notifications.view');

  // Name the current screen in the header. Longest matching path wins, so /admin/members/new is
  // still reported as "Members" rather than falling back to the area name.
  const current = sections
    .flatMap((section) => section.items)
    .filter((item) => location.pathname.startsWith(item.to))
    .sort((a, b) => b.to.length - a.to.length)[0];

  const profilePath = `/${area}/profile`;
  // Screens reachable from the account menu rather than the rail still deserve a header title.
  const title = current?.label
    ?? (location.pathname === profilePath ? 'My Profile' : null)
    ?? (area === 'admin' ? 'Administration' : area === 'trainer' ? 'Trainer Portal' : 'My Gym');

  // Unread badge. Refreshed on navigation rather than polled: the count only changes when
  // something happens in the app, and a timer would add background traffic on every screen.
  useEffect(() => {
    if (!canSeeNotifications) return;
    const controller = new AbortController();
    getNotificationCounts(controller.signal)
      .then((counts) => setUnread(counts.unread))
      .catch(() => { /* the badge is decoration; never break the shell over it */ });
    return () => controller.abort();
  }, [canSeeNotifications, location.pathname]);

  // A menu left open across a route change would hang over the new page.
  useEffect(() => setMenuOpen(false), [location.pathname]);

  useEffect(() => {
    if (!menuOpen) return;
    function onPointerDown(event: MouseEvent) {
      if (!menuRef.current?.contains(event.target as Node)) setMenuOpen(false);
    }
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') setMenuOpen(false);
    }
    document.addEventListener('mousedown', onPointerDown);
    document.addEventListener('keydown', onKeyDown);
    return () => {
      document.removeEventListener('mousedown', onPointerDown);
      document.removeEventListener('keydown', onKeyDown);
    };
  }, [menuOpen]);

  async function handleSignOut() {
    await signOut();
    navigate('/', { replace: true });
  }

  return (
    <div className="app">
      <aside className={`sidebar ${collapsed ? 'collapsed' : ''} ${drawerOpen ? 'mobile-open' : ''}`}>
        <a
          className="sidebar-brand"
          href={`/${area}/dashboard`}
          title={gym?.gymName ?? 'Gym Management'}
          onClick={(e) => { e.preventDefault(); navigate(`/${area}/dashboard`); }}
        >
          <IconDumbbell size={20} />
          <span>{gym?.gymName ?? 'Gym Management'}</span>
        </a>

        <button
          type="button"
          className="sidebar-collapse"
          aria-label={collapsed ? 'Expand the sidebar' : 'Collapse the sidebar'}
          aria-expanded={!collapsed}
          title={collapsed ? 'Expand' : 'Collapse'}
          onClick={() => setCollapsed((value) => !value)}
        >
          <IconChevronsLeft size={16} />
          <span>Collapse</span>
        </button>

        <nav className="sidebar-nav">
          {sections.map((section, index) => (
            <div className="sidebar-section" key={section.label ?? `group-${index}`}>
              {section.label && <div className="sidebar-section-label">{section.label}</div>}
              {section.items.map((item) => (
                <NavLink
                  key={item.to}
                  to={item.to}
                  // Collapsed, the icon is the only label there is, so give it a native tooltip.
                  title={collapsed ? item.label : undefined}
                  className={({ isActive }) => `sidebar-link ${isActive ? 'active' : ''}`}
                >
                  {item.icon}
                  <span>{item.label}</span>
                </NavLink>
              ))}
            </div>
          ))}
        </nav>

      </aside>

      {drawerOpen && (
        <div className="sidebar-backdrop" onClick={() => setDrawerOpen(false)} aria-hidden="true" />
      )}

      <div className="app-main">
        <header className="topbar">
          <button
            type="button"
            className="topbar-menu"
            onClick={() => setDrawerOpen(true)}
            aria-label="Open navigation"
          >
            <IconMenu size={19} />
          </button>
          <div className="topbar-title">{title}</div>

          <div className="topbar-actions">
            <ThemeToggle />

            {canSeeNotifications && (
              <NavLink
                className="topbar-bell"
                to={BELL_TARGET[area]}
                aria-label={unread > 0 ? `Notifications, ${unread} unread` : 'Notifications'}
                title={unread > 0 ? `${unread} unread` : 'Notifications'}
              >
                <IconBell size={18} />
                {unread > 0 && (
                  <span className="topbar-badge">{unread > 99 ? '99+' : unread}</span>
                )}
              </NavLink>
            )}

            <div className="account-menu" ref={menuRef}>
              <button
                type="button"
                className={`account-trigger ${menuOpen ? 'open' : ''}`}
                aria-haspopup="menu"
                aria-expanded={menuOpen}
                aria-label="Account menu"
                title={user?.fullName ?? 'Account'}
                onClick={() => setMenuOpen((open) => !open)}
              >
                {initials(user?.fullName)}
              </button>

              {menuOpen && (
                <div className="account-pop" role="menu">
                  <div className="account-pop-head">
                    <div className="account-pop-name">{user?.fullName}</div>
                    <div className="account-pop-sub">{user?.roles.join(', ')}</div>
                  </div>

                  <NavLink className="account-item" role="menuitem" to={profilePath}>
                    <IconUser size={15} /> View Profile
                  </NavLink>

                  <button
                    type="button"
                    className="account-item account-item-danger"
                    role="menuitem"
                    onClick={handleSignOut}
                  >
                    <IconLogout size={15} /> Logout
                  </button>
                </div>
              )}
            </div>
          </div>
        </header>

        <main className="app-body">
          <Outlet />
        </main>

        {/* `.app-body` takes the free space, so this stays on the bottom edge on a short page
            instead of riding up under the content. Mirrors the public shell's footer. */}
        {/* Contact details come from Settings → Gym, so changing them there changes them here.
            Each is rendered only when set, so a gym with no email never shows a stray separator. */}
        <footer className="app-footer">
          <span>{gym?.gymName ?? 'Gym Management'} · Gym Management System</span>
          {(gym?.phone || gym?.email) && (
            <span className="app-footer-contact">
              {gym?.phone && (
                <a href={`tel:${gym.phone.replace(/\s+/g, '')}`}>
                  <IconPhone size={13} /> {gym.phone}
                </a>
              )}
              {gym?.email && (
                <a href={`mailto:${gym.email}`}>
                  <IconMail size={13} /> {gym.email}
                </a>
              )}
            </span>
          )}
        </footer>
      </div>
    </div>
  );
}
