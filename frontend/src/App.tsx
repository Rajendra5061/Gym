import { Suspense, lazy } from 'react';
import { Navigate, Route, Routes, useLocation } from 'react-router-dom';
import { AuthProvider, useAuth } from '@/auth/AuthContext';
import { AppLayout } from '@/layouts/AppLayout';
import { ErrorBoundary } from '@/components/ErrorBoundary';
import { Loading, PageCard } from '@/components/ui';
import type { ReactNode } from 'react';

/* Every page is loaded on demand. Two reasons: the browser no longer parses thirty-odd
   screens before the first one paints, and a module that throws while it evaluates rejects
   only its own import — the other routes keep working instead of the whole app blanking. */

/* Public */
const HomePage = lazy(() => import('@/pages/public/HomePage'));
const AdminLoginPage = lazy(() => import('@/pages/public/AdminLoginPage'));
const MemberLoginPage = lazy(() => import('@/pages/public/MemberLoginPage'));
const ForgotPasswordPage = lazy(() => import('@/pages/public/ForgotPasswordPage'));

/* Admin */
const DashboardPage = lazy(() => import('@/pages/admin/DashboardPage'));
const MembersPage = lazy(() => import('@/pages/admin/MembersPage'));
const MemberFormPage = lazy(() => import('@/pages/admin/MemberFormPage'));
const MemberDetailsPage = lazy(() => import('@/pages/admin/MemberDetailsPage'));
const PlansPage = lazy(() => import('@/pages/admin/PlansPage'));
const SubscriptionsPage = lazy(() => import('@/pages/admin/SubscriptionsPage'));
const TrainersPage = lazy(() => import('@/pages/admin/TrainersPage'));
const TrainerFormPage = lazy(() => import('@/pages/admin/TrainerFormPage'));
const PaymentsPage = lazy(() => import('@/pages/admin/PaymentsPage'));
const RecordPaymentPage = lazy(() => import('@/pages/admin/RecordPaymentPage'));
const AttendancePage = lazy(() => import('@/pages/admin/AttendancePage'));
const EquipmentPage = lazy(() => import('@/pages/admin/EquipmentPage'));
const EnquiriesPage = lazy(() => import('@/pages/admin/EnquiriesPage'));
const WorkoutPlansPage = lazy(() => import('@/pages/admin/WorkoutPlansPage'));
const WorkoutPlanFormPage = lazy(() => import('@/pages/admin/WorkoutPlanFormPage'));
const FeedbackPage = lazy(() => import('@/pages/admin/FeedbackPage'));
const ReportsPage = lazy(() => import('@/pages/admin/ReportsPage'));
const ExpensesPage = lazy(() => import('@/pages/admin/ExpensesPage'));
const NotificationsPage = lazy(() => import('@/pages/admin/NotificationsPage'));
const UsersPage = lazy(() => import('@/pages/admin/UsersPage'));
const RolesPage = lazy(() => import('@/pages/admin/RolesPage'));
const AuditPage = lazy(() => import('@/pages/admin/AuditPage'));
const RecycleBinPage = lazy(() => import('@/pages/admin/RecycleBinPage'));
const SettingsPage = lazy(() => import('@/pages/admin/SettingsPage'));
const MyAccountPage = lazy(() => import('@/pages/admin/MyAccountPage'));

/* Member */
const MemberDashboardPage = lazy(() => import('@/pages/member/MemberDashboardPage'));
const MyProfilePage = lazy(() => import('@/pages/member/MyProfilePage'));
const MyMembershipPage = lazy(() => import('@/pages/member/MyMembershipPage'));
const MyAttendancePage = lazy(() => import('@/pages/member/MyAttendancePage'));
const MyPaymentsPage = lazy(() => import('@/pages/member/MyPaymentsPage'));
const MyWorkoutPlansPage = lazy(() => import('@/pages/member/MyWorkoutPlansPage'));
const MyFeedbackPage = lazy(() => import('@/pages/member/MyFeedbackPage'));

/** Gate that waits for the session probe, then enforces sign-in and role. */
function Protected({ area, children }: { area: 'admin' | 'member'; children: ReactNode }) {
  const { ready, user, isInRole } = useAuth();

  if (!ready) return <Loading message="Starting…" />;
  if (!user) return <Navigate to={area === 'admin' ? '/admin-login' : '/login'} replace />;

  // A member account has no business in the admin area; send it to its own dashboard.
  if (area === 'admin' && !isInRole('Admin') && !isInRole('Staff') && !isInRole('Trainer')) {
    return <Navigate to="/member/dashboard" replace />;
  }
  return <>{children}</>;
}

/**
 * Per-route containment: the chunk loads behind a spinner, and anything the page throws while
 * loading or rendering is caught here — inside the layout, so the navbar stays usable.
 */
function RouteBoundary({ children }: { children: ReactNode }) {
  const { pathname } = useLocation();
  return (
    <ErrorBoundary resetKey={pathname}>
      <Suspense fallback={<div className="page"><PageCard><Loading /></PageCard></div>}>
        {children}
      </Suspense>
    </ErrorBoundary>
  );
}

/** Shorthand so every route element below is wrapped the same way. */
const page = (element: ReactNode) => <RouteBoundary>{element}</RouteBoundary>;

function AppRoutes() {
  return (
    <Routes>
      <Route path="/" element={page(<HomePage />)} />
      <Route path="/admin-login" element={page(<AdminLoginPage />)} />
      <Route path="/login" element={page(<MemberLoginPage />)} />
      <Route path="/forgot-password" element={page(<ForgotPasswordPage />)} />

      <Route element={<Protected area="admin"><AppLayout area="admin" /></Protected>}>
        <Route path="/admin" element={<Navigate to="/admin/dashboard" replace />} />
        <Route path="/admin/dashboard" element={page(<DashboardPage />)} />
        <Route path="/admin/members" element={page(<MembersPage />)} />
        <Route path="/admin/members/new" element={page(<MemberFormPage />)} />
        <Route path="/admin/members/:id/edit" element={page(<MemberFormPage />)} />
        <Route path="/admin/members/:id" element={page(<MemberDetailsPage />)} />
        <Route path="/admin/plans" element={page(<PlansPage />)} />
        <Route path="/admin/subscriptions" element={page(<SubscriptionsPage />)} />
        <Route path="/admin/trainers" element={page(<TrainersPage />)} />
        <Route path="/admin/trainers/new" element={page(<TrainerFormPage />)} />
        <Route path="/admin/trainers/:id/edit" element={page(<TrainerFormPage />)} />
        <Route path="/admin/payments" element={page(<PaymentsPage />)} />
        <Route path="/admin/payments/new" element={page(<RecordPaymentPage />)} />
        <Route path="/admin/attendance" element={page(<AttendancePage />)} />
        <Route path="/admin/equipment" element={page(<EquipmentPage />)} />
        <Route path="/admin/enquiries" element={page(<EnquiriesPage />)} />
        <Route path="/admin/workout-plans" element={page(<WorkoutPlansPage />)} />
        <Route path="/admin/workout-plans/new" element={page(<WorkoutPlanFormPage />)} />
        <Route path="/admin/workout-plans/:id/edit" element={page(<WorkoutPlanFormPage />)} />
        <Route path="/admin/feedback" element={page(<FeedbackPage />)} />
        <Route path="/admin/reports" element={page(<ReportsPage />)} />
        <Route path="/admin/expenses" element={page(<ExpensesPage />)} />
        <Route path="/admin/notifications" element={page(<NotificationsPage />)} />
        <Route path="/admin/users" element={page(<UsersPage />)} />
        <Route path="/admin/roles" element={page(<RolesPage />)} />
        <Route path="/admin/audit" element={page(<AuditPage />)} />
        <Route path="/admin/recycle-bin" element={page(<RecycleBinPage />)} />
        <Route path="/admin/settings" element={page(<SettingsPage />)} />
        <Route path="/admin/profile" element={page(<MyAccountPage />)} />
      </Route>

      <Route element={<Protected area="member"><AppLayout area="member" /></Protected>}>
        <Route path="/member" element={<Navigate to="/member/dashboard" replace />} />
        <Route path="/member/dashboard" element={page(<MemberDashboardPage />)} />
        <Route path="/member/profile" element={page(<MyProfilePage />)} />
        <Route path="/member/membership" element={page(<MyMembershipPage />)} />
        <Route path="/member/attendance" element={page(<MyAttendancePage />)} />
        <Route path="/member/payments" element={page(<MyPaymentsPage />)} />
        <Route path="/member/workout-plans" element={page(<MyWorkoutPlansPage />)} />
        <Route path="/member/feedback" element={page(<MyFeedbackPage />)} />
      </Route>

      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}

export default function App() {
  return (
    // Outermost net: catches anything above the route elements — the auth probe, the layout —
    // so even those failures render a panel rather than an empty document.
    <ErrorBoundary>
      <AuthProvider>
        <AppRoutes />
      </AuthProvider>
    </ErrorBoundary>
  );
}
