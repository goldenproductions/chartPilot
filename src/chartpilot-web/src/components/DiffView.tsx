import type { DiffDto } from '../api/types';
import { InlineError, Skeleton } from './common';

/**
 * Feature 7 — the N-way values comparison: one column per values file, with
 * the differing rows highlighted. This is the view that answers "is prod
 * actually more robust than dev?".
 */
export function DiffView({
  diff,
  isLoading,
  isFetching,
  error,
  differencesOnly,
  onDifferencesOnlyChange,
  onRetry,
}: {
  diff: DiffDto | undefined;
  isLoading: boolean;
  isFetching: boolean;
  error: unknown;
  differencesOnly: boolean;
  onDifferencesOnlyChange: (value: boolean) => void;
  onRetry: () => void;
}) {
  return (
    <div className="centre">
      <div className="pane-toolbar">
        <strong style={{ fontSize: 'var(--fs-sm)' }}>Environment diff</strong>
        <label className="field">
          <input
            type="checkbox"
            checked={differencesOnly}
            onChange={(event) => onDifferencesOnlyChange(event.target.checked)}
          />
          Differences only
        </label>
        <span className="pane-status">
          {diff ? `${diff.rows.length} paths across ${diff.sources.length} values files` : ''}
        </span>
        <span style={{ marginLeft: 'auto' }} />
        {isFetching ? <span className="pane-status">loading&hellip;</span> : null}
      </div>

      {error ? (
        <InlineError error={error} retry={onRetry} />
      ) : isLoading ? (
        <div style={{ padding: 12 }}>
          <Skeleton lines={12} />
        </div>
      ) : !diff || diff.sources.length === 0 ? (
        <div className="empty">
          <h2>Nothing to compare</h2>
          <p>
            This chart ships a single values file. Add <code>values-dev.yaml</code> /{' '}
            <code>values-prod.yaml</code> siblings to compare environments.
          </p>
        </div>
      ) : diff.rows.length === 0 ? (
        <div className="empty">
          <h2>No differences</h2>
          <p>
            Every value resolves identically across {diff.sources.join(', ')}. Turn off
            &ldquo;differences only&rdquo; to see the full value set.
          </p>
        </div>
      ) : (
        <div className="diff-view" style={isFetching ? { opacity: 0.55 } : undefined}>
          <table className="diff-table">
            <thead>
              <tr>
                <th scope="col">Path</th>
                {diff.sources.map((source) => (
                  <th key={source} scope="col">
                    {source}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {diff.rows.map((row) => (
                <tr key={row.path} className={row.isDifferent ? 'different' : undefined}>
                  <td className="path">{row.path}</td>
                  {diff.sources.map((source) => {
                    const cell = row.cells.find((candidate) => candidate.source === source);
                    if (!cell || !cell.present) {
                      return (
                        <td key={source} className="value absent">
                          not set
                        </td>
                      );
                    }

                    return (
                      <td key={source} className="value">
                        {cell.value === null || cell.value === undefined ? (
                          <span className="absent">null</span>
                        ) : (
                          cell.value
                        )}
                      </td>
                    );
                  })}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
