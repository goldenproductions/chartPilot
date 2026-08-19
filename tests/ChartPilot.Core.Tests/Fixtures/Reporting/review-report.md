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

## Your options for each critical finding

### CP-NET-003

No PeerAuthentication sets strict mTLS. Istio's default is PERMISSIVE, which accepts both encrypted and plaintext connections — so a client that is not in the mesh keeps working, and nobody discovers that this traffic is unencrypted.

1. **Require mTLS for the namespace** _(recommended)_

   Every workload in the namespace must be talked to over mTLS.

   ```yaml
   apiVersion: security.istio.io/v1
   kind: PeerAuthentication
   metadata:
     name: default
     namespace: member-platform
   spec:
     mtls:
       mode: STRICT
   ```

   The destination. Anything talking plaintext to this namespace breaks the moment it applies, so confirm every client has a sidecar first.

2. **Require it for this workload only**

   Narrow the change to the service you are reviewing.

   ```yaml
   spec:
     selector:
       matchLabels:
         app.kubernetes.io/name: member-api
     mtls:
       mode: STRICT
   ```

   A safer first step in a namespace with mixed workloads. Leaves everything else permissive, so it is a staging post rather than an answer.

3. **Strict, with an exception for one port**

   Keep strict overall while a legacy client is migrated.

   ```yaml
   spec:
     mtls:
       mode: STRICT
     portLevelMtls:
       8080:
         mode: PERMISSIVE
   ```

   Useful during a migration, and honest about where the gap is. Put an expiry on it in your own tracker — port-level exceptions outlive the migrations that justified them.

### CP-SEC-001

The container starts its process as uid 0 — root inside the container. Container root is not the same as node root, but it is one runtime or kernel bug away from it, and it is what almost every container escape needs as a starting point.

1. **Run as a fixed non-root user** _(recommended)_

   Pick a high uid and tell Kubernetes to refuse the image if it would run as root.

   ```yaml
   securityContext:
     runAsNonRoot: true
     runAsUser: 10001
     runAsGroup: 10001
     fsGroup: 10001
   ```

   The right answer for almost every application. If the image writes to paths owned by root at build time, you will need to chown them in the Dockerfile or mount them as volumes.

2. **Use the uid the image already declares**

   Keep the image's own USER and only assert that it is not root.

   ```yaml
   securityContext:
     runAsNonRoot: true
   ```

   Less to configure and it survives an image that changes its uid. But if a future base image drops back to root, the pod stops starting rather than starting insecurely — which is the correct failure, though it will surprise someone.

3. **The process genuinely needs a privileged port**

   Stay non-root and grant the one capability that binding below port 1024 requires.

   ```yaml
   securityContext:
     runAsNonRoot: true
     runAsUser: 10001
     capabilities:
       drop: [ALL]
       add: [NET_BIND_SERVICE]
   ```

   Only for a process that must listen on 80 or 443 inside the pod. Changing the container to listen on 8080 and letting the Service map the port is simpler and strictly safer.
