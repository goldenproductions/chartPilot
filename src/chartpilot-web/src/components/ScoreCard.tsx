import { CHECK_CATEGORIES, type ScoreDto } from '../api/types';
import { Skeleton, scoreClass } from './common';

/** Feature 6 — overall plus the four weighted category scores. */
export function ScoreCard({
  score,
  isLoading,
  isStale,
  profileName,
  classification,
  environment,
}: {
  score: ScoreDto | undefined;
  isLoading: boolean;
  isStale: boolean;
  profileName?: string;
  classification?: string;
  environment?: string;
}) {
  if (isLoading) {
    return (
      <section className="card">
        <h2>Score breakdown</h2>
        <Skeleton lines={5} />
      </section>
    );
  }

  if (!score) {
    return null;
  }

  const byCategory = new Map(score.categories.map((category) => [category.category, category]));

  return (
    <section className="card" style={isStale ? { opacity: 0.55 } : undefined}>
      <h2>Score breakdown</h2>

      <div className="score-row">
        <span style={{ fontWeight: 600 }}>Overall</span>
        <div className={`meter ${scoreClass(score.overall)}`}>
          <div style={{ width: `${Math.max(0, Math.min(100, score.overall))}%` }} />
        </div>
        <span className={scoreClass(score.overall)} style={{ textAlign: 'right', fontWeight: 700 }}>
          {score.overall}
        </span>
      </div>

      {CHECK_CATEGORIES.map((category) => {
        const entry = byCategory.get(category);
        if (!entry) {
          return null;
        }

        return (
          <div key={category}>
            <div className="score-row">
              <span>{category}</span>
              <div className={`meter ${scoreClass(entry.score)}`}>
                <div style={{ width: `${Math.max(0, Math.min(100, entry.score))}%` }} />
              </div>
              <span className={scoreClass(entry.score)} style={{ textAlign: 'right' }}>
                {entry.score}
              </span>
            </div>
            <div className="score-counts">
              {entry.criticalCount} critical &middot; {entry.warningCount} warning &middot;{' '}
              {entry.infoCount} info &middot; {entry.passedCount} passed
            </div>
          </div>
        );
      })}

      <div style={{ marginTop: 8, color: 'var(--text-faint)', fontSize: '10.5px' }}>
        {[
          environment ? `env ${environment}` : null,
          profileName ? `profile ${profileName}` : null,
          classification ? `classification ${classification}` : null,
        ]
          .filter(Boolean)
          .join(' · ')}
      </div>
    </section>
  );
}
