import { useMemo } from 'react';
import { ApiError } from '../api/client';
import { resourceKey, type ResourceDto } from '../api/types';
import { MANIFEST_MODEL_URI, VALUES_MODEL_URI } from '../lib/monacoSetup';
import { resolveYamlPath } from '../lib/yamlPath';
import type { CenterView, RevealRequest } from '../store/uiStore';
import { Skeleton } from './common';
import { StderrPanel } from './StderrPanel';
import { YamlEditor } from './YamlEditor';

export interface CentrePaneProps {
  centerView: CenterView;
  onCenterViewChange: (view: CenterView) => void;
  environmentName: string | null;
  draft: string;
  onDraftChange: (value: string) => void;
  valuesLoading: boolean;
  resources: ResourceDto[];
  selectedResourceKey: string | null;
  onSelectResource: (key: string) => void;
  isFetching: boolean;
  isStale: boolean;
  error: unknown;
  reveal: RevealRequest | null;
  onRevealLine: (target: CenterView, line: number) => void;
  hasWorkspace: boolean;
  helmAvailable: boolean;
}

function joinManifests(resources: ResourceDto[]): string {
  return resources
    .map((resource) => {
      const header = resource.sourceTemplate ? `# Source: ${resource.sourceTemplate}\n` : '';
      return `---\n${header}${resource.yaml.trimEnd()}\n`;
    })
    .join('');
}

/**
 * The centre of the wireframe: one pane, one toggle. The values that caused the
 * manifest and the manifest they caused stay on the same screen.
 */
export function CentrePane(props: CentrePaneProps) {
  const {
    centerView,
    onCenterViewChange,
    environmentName,
    draft,
    onDraftChange,
    valuesLoading,
    resources,
    selectedResourceKey,
    onSelectResource,
    isFetching,
    isStale,
    error,
    reveal,
    onRevealLine,
    hasWorkspace,
    helmAvailable,
  } = props;

  const selected = useMemo(
    () => resources.find((resource) => resourceKey(resource) === selectedResourceKey) ?? null,
    [resources, selectedResourceKey],
  );

  const manifestText = useMemo(
    () => (selected ? selected.yaml : joinManifests(resources)),
    [selected, resources],
  );

  const activeText = centerView === 'values' ? draft : manifestText;

  const revealLine = useMemo(() => {
    if (!reveal || reveal.target !== centerView) {
      return null;
    }

    if (reveal.line !== null) {
      return reveal.line;
    }

    if (!reveal.yamlPath) {
      return null;
    }

    if (reveal.target === 'manifest' && reveal.resourceKey !== selectedResourceKey) {
      return null;
    }

    return resolveYamlPath(activeText, reveal.yamlPath)?.line ?? null;
  }, [reveal, centerView, selectedResourceKey, activeText]);

  const apiError = error instanceof ApiError ? error : null;
  const stderr = apiError?.helmStderr?.trim();

  const canNavigateTo = (file: string): boolean => {
    if (environmentName && file.endsWith(environmentName)) {
      return true;
    }

    return resources.some((resource) => resource.sourceTemplate?.endsWith(file));
  };

  const navigateTo = (file: string, line: number): void => {
    if (environmentName && file.endsWith(environmentName)) {
      onRevealLine('values', line);
      return;
    }

    const match = resources.find((resource) => resource.sourceTemplate?.endsWith(file));
    if (match) {
      onSelectResource(resourceKey(match));
      onCenterViewChange('manifest');
    }
  };

  if (!hasWorkspace) {
    return (
      <div className="centre">
        <div className="empty">
          <h2>No chart opened</h2>
          <p>
            Open a chart directory to read its metadata, edit its values and see the Kubernetes
            resources it renders. ChartPilot never contacts a cluster.
          </p>
          <p style={{ fontSize: 'var(--fs-sm)' }}>
            Try <code>samples/charts/member-api</code> from this repository.
          </p>
        </div>
      </div>
    );
  }

  return (
    <div className="centre">
      <div className="pane-toolbar">
        <div className="toggle-group" role="group" aria-label="Centre pane content">
          <button
            type="button"
            aria-pressed={centerView === 'values'}
            onClick={() => onCenterViewChange('values')}
          >
            values
          </button>
          <button
            type="button"
            aria-pressed={centerView === 'manifest'}
            onClick={() => onCenterViewChange('manifest')}
          >
            rendered manifest
          </button>
        </div>

        <span className="pane-status">
          {centerView === 'values'
            ? (environmentName ?? 'values.yaml')
            : selected
              ? `${selected.kind}/${selected.name}${
                  selected.sourceTemplate ? ` — ${selected.sourceTemplate}` : ''
                }`
              : `${resources.length} resources`}
        </span>

        <span style={{ marginLeft: 'auto' }} />

        {isFetching ? (
          <span className="pane-status" aria-live="polite">
            rendering&hellip;
          </span>
        ) : isStale ? (
          <span className="pane-status">stale</span>
        ) : null}

        {centerView === 'manifest' && selected ? (
          <button type="button" className="btn" onClick={() => onSelectResource('')}>
            Show all
          </button>
        ) : null}
      </div>

      <div className={`editor-host${isStale ? ' stale' : ''}`}>
        {isFetching ? <div className="rendering-strip" aria-hidden="true" /> : null}

        {centerView === 'values' && valuesLoading ? (
          <div style={{ padding: 12 }}>
            <Skeleton lines={12} />
          </div>
        ) : centerView === 'manifest' && resources.length === 0 ? (
          <div className="empty">
            <h2>Nothing rendered</h2>
            <p>
              {helmAvailable
                ? 'Helm produced no resources for these values. Check the error panel or enable the resources you expect in the values editor.'
                : 'Helm is not available, so the chart cannot be rendered.'}
            </p>
          </div>
        ) : (
          <YamlEditor
            value={activeText}
            path={centerView === 'values' ? VALUES_MODEL_URI : MANIFEST_MODEL_URI}
            readOnly={centerView === 'manifest'}
            onChange={centerView === 'values' ? onDraftChange : undefined}
            revealLine={revealLine}
            revealNonce={reveal?.nonce ?? 0}
          />
        )}
      </div>

      {stderr ? (
        <StderrPanel
          title={apiError?.title ?? 'Helm failed'}
          stderr={stderr}
          canNavigate={canNavigateTo}
          onNavigate={navigateTo}
        />
      ) : apiError ? (
        <div className="error-panel" role="alert">
          <h3>{apiError.title}</h3>
          <pre className="stderr">{apiError.detail ?? apiError.message}</pre>
        </div>
      ) : null}
    </div>
  );
}
