import { useEffect, useState } from 'react';
import { InlineError } from './common';

const SUGGESTIONS = [
  'samples/charts/member-api',
  'samples/charts/legacy-importer',
  'samples/charts/batch-report',
];

/**
 * A browser cannot hand a server a real directory path, so the chart directory
 * is typed. The API resolves it under its configured allowlist root.
 */
export function OpenChartDialog({
  initialPath,
  isSubmitting,
  error,
  allowlistRoot,
  onSubmit,
  onClose,
}: {
  initialPath: string | null;
  isSubmitting: boolean;
  error: unknown;
  allowlistRoot?: string | null;
  onSubmit: (chartPath: string) => void;
  onClose: () => void;
}) {
  const [path, setPath] = useState(initialPath ?? '');

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        onClose();
      }
    };

    document.addEventListener('keydown', onKeyDown);
    return () => document.removeEventListener('keydown', onKeyDown);
  }, [onClose]);

  return (
    <div className="overlay" role="dialog" aria-modal="true" aria-label="Open a Helm chart">
      <div className="dialog" style={{ width: 'min(620px, 100%)' }}>
        <header>
          <h2>Open a Helm chart</h2>
        </header>

        <form
          onSubmit={(event) => {
            event.preventDefault();
            const trimmed = path.trim();
            if (trimmed.length > 0) {
              onSubmit(trimmed);
            }
          }}
        >
          <div className="content">
            <label className="field" style={{ display: 'block', marginBottom: 6 }}>
              Chart directory (the folder containing <code>Chart.yaml</code>)
            </label>
            <input
              type="text"
              value={path}
              autoFocus
              spellCheck={false}
              onChange={(event) => setPath(event.target.value)}
              style={{ width: '100%', maxWidth: 'none', fontFamily: 'var(--font-mono)' }}
              placeholder="C:\\Repos\\chartPilot\\samples\\charts\\member-api"
            />

            <div className="tag-row" style={{ marginTop: 8 }}>
              {SUGGESTIONS.map((suggestion) => (
                <button
                  key={suggestion}
                  type="button"
                  className="tag"
                  onClick={() => setPath(suggestion)}
                >
                  {suggestion}
                </button>
              ))}
            </div>

            {allowlistRoot ? (
              <div style={{ marginTop: 8, color: 'var(--text-faint)', fontSize: 'var(--fs-sm)' }}>
                Paths are resolved under <code>{allowlistRoot}</code>.
              </div>
            ) : null}

            {error ? <InlineError error={error} /> : null}
          </div>

          <footer>
            <button type="button" className="btn" onClick={onClose}>
              Cancel
            </button>
            <button
              type="submit"
              className="btn btn-primary"
              disabled={isSubmitting || path.trim().length === 0}
            >
              {isSubmitting ? 'Opening…' : 'Open chart'}
            </button>
          </footer>
        </form>
      </div>
    </div>
  );
}
