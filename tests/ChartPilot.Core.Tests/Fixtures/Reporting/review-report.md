# ChartPilot Review: member-api

## Summary

Overall score: **78/100**

| Category | Score | Critical | Warning | Info | Passed |
| --- | ---: | ---: | ---: | ---: | ---: |
| Security | 65 | 2 | 0 | 0 | 1 |
| Reliability | 80 | 0 | 2 | 0 | 1 |
| Operability | 85 | 0 | 0 | 0 | 0 |
| Governance | 70 | 0 | 0 | 1 | 0 |

- Environment: `test`
- Profile: `sensitive-internal-service`
- Data classification: `sensitive-personal-data`
- Chart version: `0.3.1`
- App version: `1.12.0`
- Helm version: `v4.2.4`
- Generated at: 2026-08-19T10:15:00Z

## Rendered resources

### Workloads

- Deployment/member-api

### Networking

- Service/member-api
- VirtualService/member-api

### Certificates

- Certificate/member-api-tls

## Critical findings

- **CP-NET-003** VirtualService/member-api — Public route has no AuthorizationPolicy.
  - *templates/virtualservice.yaml*
  - Remediation: Add an AuthorizationPolicy for the public route.
- **CP-SEC-001** Deployment/member-api — Container runs as root.
  - *templates/deployment.yaml* `spec.template.spec.containers[0]`
  - Remediation: Set securityContext.runAsNonRoot: true.

## Warnings

- **CP-REL-002** Deployment/member-api — livenessProbe missing.
  - *templates/deployment.yaml* `spec.template.spec.containers[0].livenessProbe`
  - Remediation: Add a livenessProbe to the container.
- **CP-REL-004** Deployment/member-api — No PodDisruptionBudget configured.
  - Remediation: Add a PodDisruptionBudget with minAvailable: 1.

## Info

- **CP-GOV-001** Chart ships no values.schema.json.
  - Remediation: Add a values.schema.json.

## Passed checks

- **CP-REL-001** Readiness probe configured — Deployment/member-api
- **CP-SEC-005** Image tag is pinned — Deployment/member-api

## Suppressed

| Check | Resource | Reason | Expires |
| --- | --- | --- | --- |
| CP-SEC-004 | Deployment/legacy-importer | Vendor image requires a writable root filesystem; tracked in PLAT-412 | 2026-12-01 |

## Recommended actions

1. Add an AuthorizationPolicy for the public route.
2. Set securityContext.runAsNonRoot: true.
3. Add a livenessProbe to the container.
4. Add a PodDisruptionBudget with minAvailable: 1.
5. Add a values.schema.json.
