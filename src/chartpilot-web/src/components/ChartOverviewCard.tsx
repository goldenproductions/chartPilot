import type { ChartDto } from '../api/types';
import { Skeleton } from './common';

/** Feature 1 — everything the chart declares about itself, before any render. */
export function ChartOverviewCard({
  chart,
  isLoading,
}: {
  chart: ChartDto | undefined;
  isLoading: boolean;
}) {
  if (isLoading) {
    return (
      <section className="card">
        <h2>Chart overview</h2>
        <Skeleton lines={6} />
      </section>
    );
  }

  if (!chart) {
    return null;
  }

  const maintainers = chart.maintainers ?? [];
  const dependencies = chart.dependencies ?? [];
  const valuesFiles = chart.valuesFiles ?? [];
  const detectedKinds = chart.detectedKinds ?? [];

  return (
    <section className="card">
      <h2>Chart overview</h2>
      <dl className="kv">
        <dt>Name</dt>
        <dd>{chart.name}</dd>
        <dt>Version</dt>
        <dd>{chart.version}</dd>
        {chart.appVersion ? (
          <>
            <dt>App version</dt>
            <dd>{chart.appVersion}</dd>
          </>
        ) : null}
        {chart.type ? (
          <>
            <dt>Type</dt>
            <dd>{chart.type}</dd>
          </>
        ) : null}
        {chart.kubeVersion ? (
          <>
            <dt>kubeVersion</dt>
            <dd>{chart.kubeVersion}</dd>
          </>
        ) : null}
        {chart.description ? (
          <>
            <dt>Description</dt>
            <dd>{chart.description}</dd>
          </>
        ) : null}
        <dt>Path</dt>
        <dd style={{ fontFamily: 'var(--font-mono)', fontSize: '10.5px' }}>{chart.chartPath}</dd>
      </dl>

      <div style={{ marginTop: 8 }}>
        <h3>Dependencies</h3>
        {dependencies.length === 0 ? (
          <div style={{ color: 'var(--text-faint)', fontSize: 'var(--fs-sm)' }}>None declared</div>
        ) : (
          <div className="tag-row">
            {dependencies.map((dependency) => (
              <span
                key={`${dependency.name}@${dependency.version ?? ''}`}
                className={`tag ${dependency.isVersionPinned === false ? 'tag-warn' : ''}`}
                title={
                  dependency.isVersionPinned === false
                    ? 'Version is a range, not a pinned version'
                    : (dependency.repository ?? undefined)
                }
              >
                {dependency.name} {dependency.version ?? '*'}
              </span>
            ))}
          </div>
        )}
      </div>

      <div style={{ marginTop: 8 }}>
        <h3>Maintainers</h3>
        {maintainers.length === 0 ? (
          <div style={{ color: 'var(--text-faint)', fontSize: 'var(--fs-sm)' }}>None declared</div>
        ) : (
          <div className="tag-row">
            {maintainers.map((maintainer) => (
              <span key={maintainer.name} className="tag" title={maintainer.email ?? undefined}>
                {maintainer.name}
              </span>
            ))}
          </div>
        )}
      </div>

      <div style={{ marginTop: 8 }}>
        <h3>Values files</h3>
        <div className="tag-row">
          {valuesFiles.map((file) => (
            <span key={file.fileName} className="tag">
              {file.fileName}
            </span>
          ))}
          <span className={`tag ${chart.hasValuesSchema ? 'tag-ok' : 'tag-warn'}`}>
            {chart.hasValuesSchema ? 'values.schema.json' : 'no values.schema.json'}
          </span>
          {chart.hasSuppressionsFile ? <span className="tag">.chartpilot.yaml</span> : null}
        </div>
      </div>

      <div style={{ marginTop: 8 }}>
        <h3>Kinds in templates</h3>
        {detectedKinds.length === 0 ? (
          <div style={{ color: 'var(--text-faint)', fontSize: 'var(--fs-sm)' }}>
            No kinds detected by the static scan
          </div>
        ) : (
          <div className="tag-row">
            {detectedKinds.map((kind) => (
              <span key={kind} className="tag">
                {kind}
              </span>
            ))}
          </div>
        )}
      </div>
    </section>
  );
}
