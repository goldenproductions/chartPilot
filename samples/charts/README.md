# Sample charts

The chart corpus used by the demo, the manual walkthrough and the end-to-end test.

| Chart | What it is |
|---|---|
| `member-api/` | The **golden-path reference chart** — the spec's running example. Probes, requests *and* limits, non-root and read-only root filesystem, a pinned tag, standard labels, NetworkPolicy, PodDisruptionBudget, ServiceMonitor, the full Istio set (Gateway, VirtualService, DestinationRule, AuthorizationPolicy, strict PeerAuthentication), a short-lived cert-manager Certificate with `renewBefore` plus the Issuer it references, a `values.schema.json`, and pinned dependencies. Four values layers: default, `dev`, `test`, `prod`. |
| `legacy-importer/` | **Deliberately non-compliant** — the demo subject and the fixture behind the end-to-end score test. Exercises the security, reliability, Istio, cert-manager, observability and governance rule families at once. See its own README for the full list of intentional problems. **Do not "fix" it.** |
| `batch-report/` | A small CronJob-only chart, so the workload and pod-spec rules are exercised against `spec.jobTemplate.spec.template.spec` rather than `spec.template.spec`. |
| `sample-service/` | Untouched `helm create` scaffold (Helm v4.2.4). Useful as a baseline — note that even Helm's own scaffold trips several ChartPilot rules, which is the point. |
| `insecure-member-api/` | An earlier deliberately non-compliant chart, kept because it ships a `.chartpilot.yaml` with an already-expired suppression, which nothing else in the corpus exercises. |

## Environment layers

`member-api` and `legacy-importer` both ship overlays so the environment diff and per-environment
scoring have real material:

- `member-api`: `values.yaml` → `values-dev.yaml` / `values-test.yaml` / `values-prod.yaml`.
  Production raises `replicaCount` to 3 and declares `platform.dataClassification:
  sensitive-personal-data`, which is what promotes several checks to Critical.
- `legacy-importer`: `values.yaml` and `values-prod.yaml` only — production is no better than the
  default, which is itself part of the finding set.
- `insecure-member-api`: `values.yaml`, `values-dev.yaml`, `values-prod.yaml`, where prod
  materially fixes things.

## Rendering without a cluster

```bash
helm template test samples/charts/member-api
helm template test samples/charts/member-api -f samples/charts/member-api/values-prod.yaml
helm template test samples/charts/legacy-importer
helm template test samples/charts/legacy-importer -f samples/charts/legacy-importer/values-prod.yaml
helm template test samples/charts/batch-report
helm template test samples/charts/sample-service
helm template test samples/charts/insecure-member-api
```

Every chart above renders with exit code 0 and passes `helm lint`.

## Vendored stub subcharts

`member-api/charts/{redis,mongodb}/`, `legacy-importer/charts/legacy-cache/` and
`insecure-member-api/charts/redis/` are stub subcharts with no templates. They exist only so
`helm template` can resolve the declared dependencies — including the deliberately unpinned ones —
without network access.
