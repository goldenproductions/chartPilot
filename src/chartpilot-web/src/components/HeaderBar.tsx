import type { ChartDto, ProfileDto, ScoreDto, ValuesFileDto } from '../api/types';
import { scoreClass } from './common';

export interface HeaderBarProps {
  chart: ChartDto | undefined;
  profiles: ProfileDto[];
  activeProfileId: string | null;
  onProfileChange: (profileId: string) => void;
  valuesFiles: ValuesFileDto[];
  activeEnvironment: string | null;
  onEnvironmentChange: (fileName: string) => void;
  score: ScoreDto | undefined;
  scoreStale: boolean;
  mainView: 'review' | 'diff';
  onMainViewChange: (view: 'review' | 'diff') => void;
  onOpenChart: () => void;
  onReport: () => void;
  onWorkflow: () => void;
  onExportValues: () => void;
  actionsEnabled: boolean;
  helmVersion?: string | null;
}

export function HeaderBar(props: HeaderBarProps) {
  const {
    chart,
    profiles,
    activeProfileId,
    onProfileChange,
    valuesFiles,
    activeEnvironment,
    onEnvironmentChange,
    score,
    scoreStale,
    mainView,
    onMainViewChange,
    onOpenChart,
    onReport,
    onWorkflow,
    onExportValues,
    actionsEnabled,
    helmVersion,
  } = props;

  const activeProfile = profiles.find((profile) => profile.id === activeProfileId);

  return (
    <header className="header">
      <span className="brand">
        <span className="brand-mark" aria-hidden="true" />
        ChartPilot
      </span>

      {chart ? (
        <span className="chart-title" title={chart.chartPath}>
          <strong>{chart.name}</strong>
          <span style={{ color: 'var(--text-dim)' }}>{chart.version}</span>
          {chart.appVersion ? (
            <span style={{ color: 'var(--text-faint)', fontSize: 'var(--fs-sm)' }}>
              app {chart.appVersion}
            </span>
          ) : null}
        </span>
      ) : (
        <span style={{ color: 'var(--text-faint)' }}>No chart opened</span>
      )}

      <button type="button" className="btn" onClick={onOpenChart}>
        Open chart&hellip;
      </button>

      <span className="header-spacer" />

      <label className="field">
        Profile
        <select
          value={activeProfileId ?? ''}
          onChange={(event) => onProfileChange(event.target.value)}
          disabled={profiles.length === 0}
          title={activeProfile?.description ?? 'Golden path profile'}
        >
          {profiles.length === 0 ? <option value="">(none)</option> : null}
          {profiles.map((profile) => (
            <option key={profile.id} value={profile.id}>
              {profile.name}
            </option>
          ))}
        </select>
      </label>

      <label className="field">
        Environment
        <select
          value={activeEnvironment ?? ''}
          onChange={(event) => onEnvironmentChange(event.target.value)}
          disabled={valuesFiles.length === 0}
        >
          {valuesFiles.length === 0 ? <option value="">(none)</option> : null}
          {valuesFiles.map((file) => (
            <option key={file.fileName} value={file.fileName}>
              {file.fileName}
              {file.isDefault ? ' (default)' : ''}
            </option>
          ))}
        </select>
      </label>

      <div className="toggle-group" role="group" aria-label="Main view">
        <button
          type="button"
          aria-pressed={mainView === 'review'}
          onClick={() => onMainViewChange('review')}
        >
          Review
        </button>
        <button
          type="button"
          aria-pressed={mainView === 'diff'}
          onClick={() => onMainViewChange('diff')}
        >
          Env diff
        </button>
      </div>

      <button
        type="button"
        className="btn"
        onClick={onExportValues}
        disabled={!actionsEnabled}
        title="Download the edited values.yaml"
      >
        Export values
      </button>
      <button type="button" className="btn" onClick={onReport} disabled={!actionsEnabled}>
        Report
      </button>
      <button type="button" className="btn" onClick={onWorkflow} disabled={!actionsEnabled}>
        Workflow
      </button>

      {score ? (
        <span
          className={`score-pill ${scoreClass(score.overall)}`}
          style={scoreStale ? { opacity: 0.5 } : undefined}
          title={helmVersion ? `Rendered with Helm ${helmVersion}` : undefined}
        >
          {score.overall}
          <span>/100</span>
        </span>
      ) : (
        <span className="score-pill" style={{ color: 'var(--text-faint)' }}>
          &mdash;<span>/100</span>
        </span>
      )}
    </header>
  );
}
