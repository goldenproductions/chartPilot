# legacy-importer (deliberately bad)

This chart exists to be reviewed, not to be deployed. It is the demo subject for ChartPilot and the
fixture behind the end-to-end score test, so **its problems are intentional and must not be "fixed"**.

What is wrong with it, on purpose:

| Area | Problem |
|---|---|
| Reliability | no readiness or liveness probe, no `resources` block at all, `replicaCount: 1`, no PodDisruptionBudget, `Recreate` update strategy |
| Security | image tag `latest`, `privileged: true`, `runAsUser: 0`, no `readOnlyRootFilesystem`, no `runAsNonRoot`, `allowPrivilegeEscalation: true` |
| Security | a plaintext database password in a `Secret`'s `stringData`, mounted as an environment variable |
| Security | ServiceAccount with `automountServiceAccountToken: true`, bound to a ClusterRole granting `*` on `*` in all API groups |
| Security | no NetworkPolicy |
| Networking | a VirtualService on a **public** Gateway (`hosts: ["*"]`, plain HTTP) with no AuthorizationPolicy, no PeerAuthentication, no DestinationRule, and no timeout or retries |
| Certificates | Certificate with `duration: 8760h` (a year), no `renewBefore`, and an `issuerRef` pointing at an issuer this chart never renders |
| Observability | no ServiceMonitor, no Prometheus annotations, none of the `app.kubernetes.io/*` standard labels |
| Governance | no `values.schema.json`, an unpinned dependency (`version: "^1.0.0"`), no maintainers, no data classification |

Render it with:

```bash
helm template legacy-importer ./samples/charts/legacy-importer
helm template legacy-importer ./samples/charts/legacy-importer -f samples/charts/legacy-importer/values-prod.yaml
```
