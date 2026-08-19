# insecure-member-api

A deliberately non-compliant sample chart. It exists so ChartPilot has something to have an
opinion about, and so the end-to-end test has a chart with a known finding set.

Every one of the following is intentional:

| Problem | Where |
|---|---|
| Container runs as root (`runAsUser: 0`, no `runAsNonRoot`) | `templates/deployment.yaml` |
| `privileged: true`, writable root filesystem, privilege escalation allowed | `templates/deployment.yaml` |
| Image tag is `latest` | `values.yaml` |
| No readiness or liveness probe | `templates/deployment.yaml` |
| No resource requests or limits | `templates/deployment.yaml` |
| Single replica, no PodDisruptionBudget | `templates/deployment.yaml` |
| ServiceAccount token automount enabled | `templates/serviceaccount.yaml` |
| Cluster-wide wildcard RBAC | `templates/rbac.yaml` |
| Database password inline in a Secret | `templates/secret.yaml`, `values.yaml` |
| Public Gateway (host `*`) with no AuthorizationPolicy | `templates/gateway.yaml`, `templates/virtualservice.yaml` |
| VirtualService with no timeout and no retry policy | `templates/virtualservice.yaml` |
| No PeerAuthentication (no strict mTLS) and no DestinationRule | - |
| No NetworkPolicy | - |
| Certificate with a one-year duration and no `renewBefore` | `templates/certificate.yaml` |
| Certificate references an Issuer the chart does not ship | `templates/certificate.yaml` |
| No standard `app.kubernetes.io/*` labels, no owner label | every template |
| No ServiceMonitor and no Prometheus annotations | - |
| No `values.schema.json` | - |
| Unpinned dependency (`^18.0.0`) | `Chart.yaml` |
| `platform.dataClassification` declares sensitive personal data | `values.yaml` |
| An expired suppression | `.chartpilot.yaml` |

Render it with:

```bash
helm template test samples/charts/insecure-member-api
helm template test samples/charts/insecure-member-api -f samples/charts/insecure-member-api/values-prod.yaml
```
