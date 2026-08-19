import { useMemo, useState } from 'react';
import {
  SEVERITY_ORDER,
  type CheckDto,
  type FindingDto,
  type FixOptionDto,
  type PassedCheckDto,
  type Severity,
  type SuppressedFindingDto,
} from '../api/types';
import type { FindingsFilter } from '../store/uiStore';
import { Skeleton, handleListKeyDown } from './common';

export interface FindingsPanelProps {
  findings: FindingDto[];
  passed: PassedCheckDto[];
  suppressed: SuppressedFindingDto[];
  checks: CheckDto[];
  filter: FindingsFilter;
  onToggleSeverity: (severity: Severity) => void;
  onShowPassed: (value: boolean) => void;
  onQueryChange: (query: string) => void;
  onSelectFinding: (finding: FindingDto) => void;
  isLoading: boolean;
  isStale: boolean;
  hasReview: boolean;
}

function matchesQuery(finding: FindingDto, query: string): boolean {
  if (query.length === 0) {
    return true;
  }

  const needle = query.toLowerCase();
  return (
    finding.checkId.toLowerCase().includes(needle) ||
    finding.message.toLowerCase().includes(needle) ||
    (finding.resource ? finding.resource.toLowerCase().includes(needle) : false)
  );
}

/** Features 5 and 6 — the findings list, grouped by severity and navigable. */
export function FindingsPanel(props: FindingsPanelProps) {
  // Which findings have their guidance open. Keyed per finding rather than per rule, because the
  // same rule can fire on several resources and they are read one at a time.
  const [expanded, setExpanded] = useState<ReadonlySet<string>>(() => new Set());

  const toggleExplain = (key: string) =>
    setExpanded((current) => {
      const next = new Set(current);
      if (!next.delete(key)) {
        next.add(key);
      }
      return next;
    });

  const {
    findings,
    passed,
    suppressed,
    checks,
    filter,
    onToggleSeverity,
    onShowPassed,
    onQueryChange,
    onSelectFinding,
    isLoading,
    isStale,
    hasReview,
  } = props;

  const titleById = useMemo(() => {
    const map = new Map<string, string>();
    for (const check of checks) {
      map.set(check.id, check.title);
    }
    return map;
  }, [checks]);

  const grouped = useMemo(() => {
    const map: Record<Severity, FindingDto[]> = { Critical: [], Warning: [], Info: [] };
    for (const finding of findings) {
      if (map[finding.severity]) {
        map[finding.severity].push(finding);
      }
    }
    return map;
  }, [findings]);

  const visiblePassed = useMemo(
    () =>
      passed.filter(
        (item) =>
          filter.query.length === 0 ||
          item.checkId.toLowerCase().includes(filter.query.toLowerCase()) ||
          item.title.toLowerCase().includes(filter.query.toLowerCase()),
      ),
    [passed, filter.query],
  );

  if (isLoading) {
    return (
      <section className="card">
        <h2>Findings</h2>
        <Skeleton lines={10} />
      </section>
    );
  }

  if (!hasReview) {
    return (
      <section className="card">
        <h2>Findings</h2>
        <div style={{ color: 'var(--text-faint)', fontSize: 'var(--fs-sm)' }}>
          The checks run as soon as the chart renders.
        </div>
      </section>
    );
  }

  const total = findings.length;

  return (
    <div style={isStale ? { opacity: 0.55 } : undefined}>
      <div className="findings-toolbar">
        {SEVERITY_ORDER.map((severity) => (
          <button
            key={severity}
            type="button"
            className={`chip chip-${severity.toLowerCase()}`}
            aria-pressed={filter.severities[severity]}
            onClick={() => onToggleSeverity(severity)}
          >
            {severity} ({grouped[severity].length})
          </button>
        ))}
        <button
          type="button"
          className="chip chip-passed"
          aria-pressed={filter.showPassed}
          onClick={() => onShowPassed(!filter.showPassed)}
        >
          Passed ({passed.length})
        </button>
        <input
          type="text"
          value={filter.query}
          placeholder="Filter findings"
          aria-label="Filter findings"
          onChange={(event) => onQueryChange(event.target.value)}
          style={{ flex: 1, minWidth: 90 }}
        />
      </div>

      {total === 0 ? (
        <div className="empty">
          <h2>No findings</h2>
          <p>
            Every enabled check in this profile passed for this environment. That is the result, not
            an absence of data &mdash; {passed.length} checks were evaluated.
          </p>
        </div>
      ) : null}

      <div role="list" onKeyDown={handleListKeyDown}>
        {SEVERITY_ORDER.filter((severity) => filter.severities[severity]).map((severity) => {
          const items = grouped[severity].filter((finding) => matchesQuery(finding, filter.query));
          if (items.length === 0) {
            return null;
          }

          return (
            <div key={severity}>
              <div className="finding-group-title">
                {severity} ({items.length})
              </div>
              {items.map((finding, index) => {
                const key = `${finding.checkId}:${finding.resource ?? 'chart'}:${index}`;
                const canExplain = Boolean(finding.whatItMeans) || (finding.options?.length ?? 0) > 0;
                const isOpen = expanded.has(key);

                return (
                  <div key={key} role="listitem" className="finding-row">
                    <button
                      type="button"
                      data-nav-item=""
                      className={`finding sev-${finding.severity}`}
                      onClick={() => onSelectFinding(finding)}
                      title={
                        finding.resource
                          ? `Go to ${finding.resource}${
                              finding.yamlPath ? ` — ${finding.yamlPath}` : ''
                            }`
                          : 'Chart-level finding'
                      }
                    >
                      <span className="finding-head">
                        <span className="finding-id">{finding.checkId}</span>
                        <span style={{ fontWeight: 600 }}>
                          {finding.title ?? titleById.get(finding.checkId) ?? ''}
                        </span>
                        {finding.resource ? (
                          <span className="finding-resource">{finding.resource}</span>
                        ) : (
                          <span className="finding-resource">chart</span>
                        )}
                      </span>
                      <span className="finding-message">{finding.message}</span>
                      {finding.yamlPath ? (
                        <span className="finding-path">{finding.yamlPath}</span>
                      ) : null}
                    </button>

                    {canExplain ? (
                      <button
                        type="button"
                        className="finding-explain-toggle"
                        aria-expanded={isOpen}
                        onClick={() => toggleExplain(key)}
                      >
                        {isOpen ? 'Hide guidance' : 'What should I do?'}
                      </button>
                    ) : null}

                    {isOpen ? <FindingGuidance finding={finding} /> : null}
                  </div>
                );
              })}
            </div>
          );
        })}

        {filter.showPassed && visiblePassed.length > 0 ? (
          <div>
            <div className="finding-group-title">Passed ({visiblePassed.length})</div>
            {visiblePassed.map((item, index) => (
              <div key={`${item.checkId}:${index}`} className="finding passed">
                <span className="finding-head">
                  <span className="finding-id">{item.checkId}</span>
                  <span>{item.title}</span>
                  {item.resource ? (
                    <span className="finding-resource">{item.resource}</span>
                  ) : null}
                </span>
              </div>
            ))}
          </div>
        ) : null}

        {suppressed.length > 0 ? (
          <div>
            <div className="finding-group-title">Suppressed ({suppressed.length})</div>
            {suppressed.map((item, index) => (
              <div key={`${item.finding.checkId}:${index}`} className="finding">
                <span className="finding-head">
                  <span className="finding-id">{item.finding.checkId}</span>
                  <span>{item.finding.message}</span>
                </span>
                <span className="finding-remediation">
                  {item.reason}
                  {item.expires ? ` (expires ${item.expires})` : ''}
                </span>
              </div>
            ))}
          </div>
        ) : null}
      </div>
    </div>
  );
}

/**
 * The "what should I do?" body: the finding in plain language, why it carries this severity for
 * this review, and the authored options. The remediation snippet the finding already carried is
 * the recommended option's yaml, so it is not repeated above.
 */
function FindingGuidance({ finding }: { finding: FindingDto }) {
  const options = finding.options ?? [];

  return (
    <div className="finding-guidance">
      {finding.whatItMeans ? <p className="fg-means">{finding.whatItMeans}</p> : null}

      {finding.severityReason ? (
        <p className="fg-severity">{finding.severityReason}</p>
      ) : null}

      {options.length > 0 ? (
        <>
          <div className="fg-options-title">Your options</div>
          <ol className="fg-options">
            {options.map((option) => (
              <FindingOption key={option.title} option={option} />
            ))}
          </ol>
        </>
      ) : (
        <pre className="fg-yaml">{finding.remediation}</pre>
      )}

      {finding.rationale ? <p className="fg-rationale">{finding.rationale}</p> : null}
    </div>
  );
}

function FindingOption({ option }: { option: FixOptionDto }) {
  return (
    <li className={option.isRecommended ? 'fg-option is-recommended' : 'fg-option'}>
      <div className="fg-option-head">
        <span className="fg-option-title">{option.title}</span>
        {option.isRecommended ? <span className="fg-badge">recommended</span> : null}
      </div>
      <p className="fg-option-summary">{option.summary}</p>
      <pre className="fg-yaml">{option.yaml}</pre>
      <p className="fg-option-tradeoff">{option.tradeoff}</p>
    </li>
  );
}
