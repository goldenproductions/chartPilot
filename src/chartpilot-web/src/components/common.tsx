import type { KeyboardEvent as ReactKeyboardEvent, ReactNode } from 'react';
import { ApiError } from '../api/client';

export function Skeleton({ lines = 3, width = '100%' }: { lines?: number; width?: string }) {
  return (
    <div aria-hidden="true">
      {Array.from({ length: lines }, (_, index) => (
        <div
          key={index}
          className="skeleton"
          style={{ width: index === lines - 1 ? '65%' : width }}
        />
      ))}
    </div>
  );
}

export function EmptyState({
  title,
  children,
  action,
}: {
  title: string;
  children?: ReactNode;
  action?: ReactNode;
}) {
  return (
    <div className="empty">
      <h2>{title}</h2>
      {children ? <p style={{ margin: '0 0 8px' }}>{children}</p> : null}
      {action}
    </div>
  );
}

export function InlineError({ error, retry }: { error: unknown; retry?: () => void }) {
  const api = error instanceof ApiError ? error : null;
  const title = api?.title ?? (error instanceof Error ? error.message : 'Something went wrong');

  return (
    <div className="error-inline" role="alert">
      <strong>{title}</strong>
      {api?.detail ? <div>{api.detail}</div> : null}
      {api?.helmStderr ? <pre className="stderr">{api.helmStderr}</pre> : null}
      {retry ? (
        <div style={{ marginTop: 6 }}>
          <button type="button" className="btn" onClick={retry}>
            Retry
          </button>
        </div>
      ) : null}
    </div>
  );
}

export function scoreClass(score: number): string {
  if (score >= 85) {
    return 'score-good';
  }

  if (score >= 65) {
    return 'score-mid';
  }

  return 'score-bad';
}

/**
 * Arrow-key navigation for the resource tree and the findings list.
 * Items opt in with a `data-nav-item` attribute.
 */
export function handleListKeyDown(event: ReactKeyboardEvent<HTMLElement>): void {
  const keys = ['ArrowDown', 'ArrowUp', 'Home', 'End'];
  if (!keys.includes(event.key)) {
    return;
  }

  const items = Array.from(
    event.currentTarget.querySelectorAll<HTMLElement>('[data-nav-item]:not([disabled])'),
  );

  if (items.length === 0) {
    return;
  }

  const active = document.activeElement as HTMLElement | null;
  const current = active ? items.indexOf(active) : -1;

  let next: number;
  switch (event.key) {
    case 'ArrowDown':
      next = current < 0 ? 0 : Math.min(items.length - 1, current + 1);
      break;
    case 'ArrowUp':
      next = current <= 0 ? 0 : current - 1;
      break;
    case 'Home':
      next = 0;
      break;
    default:
      next = items.length - 1;
      break;
  }

  event.preventDefault();
  items[next].focus();
}
