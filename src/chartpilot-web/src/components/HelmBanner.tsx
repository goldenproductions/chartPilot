import type { EnvironmentDto } from '../api/types';
import { ApiError } from '../api/client';

const WINGET = 'winget install Helm.Helm';
const BREW = 'brew install helm';

/**
 * Helm is the one external dependency ChartPilot cannot work without.
 * When it is missing we say so up front, with the install command, instead of
 * letting the first render fail with a cryptic error.
 */
export function HelmBanner({
  environment,
  error,
  onRetry,
}: {
  environment: EnvironmentDto | undefined;
  error: unknown;
  onRetry: () => void;
}) {
  if (error) {
    const api = error instanceof ApiError ? error : null;
    return (
      <div className="banner banner-critical" role="alert">
        <div>
          <strong>The ChartPilot API is not responding.</strong>
          <div>
            {api?.detail ??
              'Start it with `dotnet run --project src/ChartPilot.Api` and reload this page.'}
          </div>
        </div>
        <button type="button" className="btn" onClick={onRetry} style={{ marginLeft: 'auto' }}>
          Retry
        </button>
      </div>
    );
  }

  if (!environment || environment.helmAvailable) {
    return null;
  }

  return (
    <div className="banner" role="alert">
      <div>
        <strong>Helm was not found on this machine.</strong>
        <div>
          ChartPilot renders charts with <code>helm template</code>. Install Helm, then reload:
        </div>
        <pre>
          {WINGET}
          {'\n'}
          {BREW}
        </pre>
        {environment.helmError ? (
          <div style={{ marginTop: 4 }}>
            Resolver said: <code>{environment.helmError}</code>
          </div>
        ) : null}
        <div style={{ marginTop: 4, opacity: 0.8 }}>
          You can also point ChartPilot at an existing binary with the{' '}
          <code>ChartPilot:HelmPath</code> setting.
        </div>
      </div>
      <button type="button" className="btn" onClick={onRetry} style={{ marginLeft: 'auto' }}>
        Check again
      </button>
    </div>
  );
}
