import { Component, type ErrorInfo, type ReactNode } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { Alert, PageCard, PageCardHeader } from './ui';
import { IconDashboard, IconRefresh, IconWarning } from './icons';

/**
 * The panel a caught render error is replaced with. Kept as a function component so it can
 * read the router location — it renders inside the route it replaced, so the layout, the
 * navbar and every other route are still live behind it.
 */
function RouteErrorPanel({ error, onRetry }: { error: Error; onRetry: () => void }) {
  const { pathname } = useLocation();
  const home = pathname.startsWith('/member')
    ? '/member/dashboard'
    : pathname.startsWith('/admin') ? '/admin/dashboard' : '/';

  return (
    <div className="page">
      <PageCard>
        <PageCardHeader
          icon={<IconWarning size={20} />}
          title="Something went wrong"
          subtitle="This screen could not be displayed. The rest of the application is unaffected."
        />
        <div className="page-card-body stack">
          <Alert tone="error">
            <div>
              <div style={{ fontWeight: 600 }}>{error.message || 'Unexpected error'}</div>
              <div className="muted" style={{ fontSize: 12, marginTop: 4 }}>Screen: {pathname}</div>
            </div>
          </Alert>

          {error.stack && (
            <details className="error-details">
              <summary>Technical details</summary>
              <pre>{error.stack}</pre>
            </details>
          )}

          <div className="row">
            <button className="btn btn-primary" onClick={onRetry}>
              <IconRefresh size={14} /> Try again
            </button>
            <button className="btn btn-outline" onClick={() => window.location.reload()}>
              Reload the page
            </button>
            <Link className="btn btn-outline" to={home}>
              <IconDashboard size={14} /> Back to dashboard
            </Link>
          </div>
        </div>
      </PageCard>
    </div>
  );
}

interface Props {
  children: ReactNode;
  /** Changing this clears a caught error — pass the route path so navigating away recovers. */
  resetKey?: string;
}

interface State { error: Error | null }

/**
 * Catches render/lifecycle errors below it. Placed around each route element so one broken
 * page module cannot blank the whole document, which is what a plain uncaught error does:
 * React unmounts the entire tree and leaves an empty <body>.
 */
export class ErrorBoundary extends Component<Props, State> {
  state: State = { error: null };

  static getDerivedStateFromError(error: unknown): State {
    return { error: error instanceof Error ? error : new Error(String(error)) };
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    console.error(`[ErrorBoundary] ${this.props.resetKey ?? 'app'} failed to render:`, error, info.componentStack);
  }

  componentDidUpdate(prev: Props) {
    // A new route gets a clean slate; without this the panel would follow the user around.
    if (this.state.error && prev.resetKey !== this.props.resetKey) this.setState({ error: null });
  }

  private retry = () => this.setState({ error: null });

  render() {
    const { error } = this.state;
    if (error) return <RouteErrorPanel error={error} onRetry={this.retry} />;
    return this.props.children;
  }
}
