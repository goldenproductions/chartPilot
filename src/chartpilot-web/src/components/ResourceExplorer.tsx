import { useMemo, useState } from 'react';
import {
  RESOURCE_CATEGORY_ORDER,
  resourceKey,
  type FindingDto,
  type ResourceCategory,
  type ResourceDto,
} from '../api/types';
import { Skeleton, handleListKeyDown } from './common';

const ISTIO_KINDS = new Set([
  'Gateway',
  'VirtualService',
  'DestinationRule',
  'AuthorizationPolicy',
  'PeerAuthentication',
  'ServiceEntry',
  'Sidecar',
]);

interface ResourceCounts {
  critical: number;
  warning: number;
}

/** Feature 4 — the rendered resources, grouped by the category the API returns. */
export function ResourceExplorer({
  resources,
  findings,
  selectedKey,
  onSelect,
  isLoading,
  isStale,
}: {
  resources: ResourceDto[];
  findings: FindingDto[];
  selectedKey: string | null;
  onSelect: (key: string) => void;
  isLoading: boolean;
  isStale: boolean;
}) {
  const [collapsed, setCollapsed] = useState<Record<string, boolean>>({});

  const grouped = useMemo(() => {
    const map = new Map<ResourceCategory, ResourceDto[]>();
    for (const resource of resources) {
      const category = (resource.category ?? 'Other') as ResourceCategory;
      const bucket = map.get(category);
      if (bucket) {
        bucket.push(resource);
      } else {
        map.set(category, [resource]);
      }
    }

    for (const bucket of map.values()) {
      bucket.sort((a, b) => a.kind.localeCompare(b.kind) || a.name.localeCompare(b.name));
    }

    return map;
  }, [resources]);

  const counts = useMemo(() => {
    const map = new Map<string, ResourceCounts>();
    for (const finding of findings) {
      if (!finding.resource) {
        continue;
      }

      const key = finding.resource;
      const entry = map.get(key) ?? { critical: 0, warning: 0 };
      if (finding.severity === 'Critical') {
        entry.critical += 1;
      } else if (finding.severity === 'Warning') {
        entry.warning += 1;
      }

      map.set(key, entry);
    }

    return map;
  }, [findings]);

  const hasIstio = resources.some((resource) => ISTIO_KINDS.has(resource.kind));

  if (isLoading) {
    return (
      <section className="card">
        <h2>Resources</h2>
        <Skeleton lines={8} />
      </section>
    );
  }

  if (resources.length === 0) {
    return (
      <section className="card">
        <h2>Resources</h2>
        <div style={{ color: 'var(--text-faint)', fontSize: 'var(--fs-sm)' }}>
          Nothing rendered yet. Edit the values or fix the template error to see the resources this
          chart produces.
        </div>
      </section>
    );
  }

  return (
    <section className="card" style={isStale ? { opacity: 0.55 } : undefined}>
      <h2>
        Resources <span style={{ textTransform: 'none' }}>({resources.length})</span>
      </h2>

      <div role="tree" aria-label="Rendered Kubernetes resources" onKeyDown={handleListKeyDown}>
        {RESOURCE_CATEGORY_ORDER.filter((category) => grouped.has(category)).map((category) => {
          const items = grouped.get(category) ?? [];
          const isCollapsed = collapsed[category] === true;

          return (
            <div key={category} className="tree-group" role="group" aria-label={category}>
              <button
                type="button"
                className="tree-group-header"
                aria-expanded={!isCollapsed}
                onClick={() =>
                  setCollapsed((previous) => ({ ...previous, [category]: !isCollapsed }))
                }
              >
                <span aria-hidden="true">{isCollapsed ? '▸' : '▾'}</span>
                {category}
                <span className="badge">{items.length}</span>
              </button>

              {isCollapsed
                ? null
                : items.map((resource) => {
                    const key = resourceKey(resource);
                    const count = counts.get(key);
                    const selected = key === selectedKey;

                    return (
                      <button
                        key={key}
                        type="button"
                        role="treeitem"
                        aria-selected={selected}
                        data-nav-item=""
                        className="tree-item"
                        title={resource.sourceTemplate ?? undefined}
                        onClick={() => onSelect(key)}
                      >
                        <span className="kind">{resource.kind}</span>
                        <span className="name">{resource.name}</span>
                        {count && count.critical > 0 ? (
                          <span className="badge badge-critical" title="Critical findings">
                            {count.critical}
                          </span>
                        ) : count && count.warning > 0 ? (
                          <span className="badge badge-warning" title="Warnings">
                            {count.warning}
                          </span>
                        ) : null}
                      </button>
                    );
                  })}
            </div>
          );
        })}
      </div>

      {hasIstio ? null : (
        <div style={{ marginTop: 8, color: 'var(--text-faint)', fontSize: 'var(--fs-sm)' }}>
          No Istio resources rendered (Gateway, VirtualService, DestinationRule,
          AuthorizationPolicy) &mdash; the mesh checks have nothing to inspect.
        </div>
      )}
    </section>
  );
}
