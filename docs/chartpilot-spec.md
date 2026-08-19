# ChartPilot — Helm Chart Review & Golden Path GUI

ChartPilot is a local, web-based GUI for understanding, configuring and quality-assuring Helm charts before they are deployed to Kubernetes.

The goal is not merely to make `values.yaml` more readable, but to help both developers and platform teams make better decisions about operability, security, standardization and release quality.

In many organizations, Helm charts act as the deployment contract between developers and the platform team. But charts get complex quickly, and small changes in `values.yaml` can have a large effect on the Kubernetes resources that actually get deployed. ChartPilot makes that effect visible before the changes reach an environment.

The project is intended as a local, free and cloud-independent tool, but with direct relevance to platforms built on AKS, Kubernetes, Helm, Istio, cert-manager, GitHub Actions and modern platform engineering principles.

---

## Elevator pitch

> ChartPilot is a local GUI for Helm charts that parses chart metadata and values, renders Kubernetes manifests live, and runs platform checks for security, operability and governance. The goal is to make it easy for developers to follow safe standards, and easy for platform teams to scale golden paths without becoming a bottleneck.

---

## The problem

Helm is powerful, but often hard to work with safely:

- `values.yaml` can be large and hard to navigate.
- It is hard to see which Kubernetes resources a change actually generates.
- Developers can easily miss important platform requirements such as probes, resource limits, NetworkPolicies or safe image tags.
- Differences between dev, test and prod values are hard to keep track of.
- Security and operational requirements are often discovered late in the pipeline or during review.
- Platform teams end up manually reviewing the same mistakes over and over.

ChartPilot aims to move that feedback earlier in the process.

---

## The solution

ChartPilot provides a visual workflow around Helm charts:

1. Pick a Helm chart.
2. Inspect chart metadata, dependencies and default values.
3. Edit `values.yaml` in a structured editor.
4. Render manifests live with `helm template`.
5. See which Kubernetes resources get generated.
6. Run automated platform checks.
7. Compare values across environments.
8. Generate a suggested CI/CD workflow or deploy commands.
9. Export a validated `values.yaml` and a review report.

---

## Core features

### 1. Chart Overview

ChartPilot reads and displays the basic information from the chart:

- Chart name
- Chart version
- App version
- Description
- Dependencies
- Maintainers
- Available values files
- Whether the chart has a `values.schema.json`
- Templates and Kubernetes resource types

Example:

```text
Chart: member-api
Version: 0.3.1
App Version: 1.12.0
Dependencies: redis, mongodb
Schema: values.schema.json found
Resources: Deployment, Service, VirtualService, Certificate
```

The point is to quickly give both developers and platform engineers an overview of what the chart contains.

---

### 2. Values Editor

ChartPilot includes a YAML editor for Helm values. The editor works at two levels:

**Plain YAML mode** — the user edits `values.yaml` directly, but gets syntax highlighting and validation errors.

**Schema-driven mode** — if the chart has a `values.schema.json`, ChartPilot can use the schema to present more guided fields:

- text fields
- number fields
- booleans
- dropdowns/enums
- required fields
- default values
- descriptions from the schema

Example:

```yaml
replicaCount: 3
image:
  repository: ghcr.io/example/member-api
  tag: "1.4.2"
resources:
  requests:
    cpu: 100m
    memory: 128Mi
  limits:
    cpu: 500m
    memory: 512Mi
```

The aim is to make chart configuration easier to understand without hiding the YAML entirely.

---

### 3. Live Helm Render

When the user changes values, ChartPilot can run:

```bash
helm template release-name ./chart -f values.yaml
```

and show the generated Kubernetes manifests directly in the GUI. This makes it obvious how a change in values affects the output.

Example — the user changes `replicaCount: 1` to `replicaCount: 3`, and ChartPilot shows that this changes:

```yaml
spec:
  replicas: 3
```

in the Deployment manifest.

---

### 4. Kubernetes Resource Explorer

The rendered manifests are shown as a structured list:

```text
Workloads
  - Deployment/member-api
  - Job/db-migration
Networking
  - Service/member-api
  - VirtualService/member-api
  - Gateway/public-gateway
Security
  - ServiceAccount/member-api
Certificates
  - Certificate/member-api-tls
```

Clicking a resource shows its full YAML.

---

### 5. Platform Readiness Checks

ChartPilot analyzes the rendered manifests and gives feedback on security, stability and operability.

**Reliability**

- Missing `readinessProbe`
- Missing `livenessProbe`
- Missing resource requests
- Missing resource limits
- Only one replica in prod
- Missing PodDisruptionBudget
- Rolling update strategy not set
- No health endpoint exposed

**Security**

- Container runs as root
- `privileged: true`
- Missing `runAsNonRoot`
- Missing `readOnlyRootFilesystem`
- Image tag is `latest`
- Secrets stored as plaintext in manifests
- ServiceAccount token automount is enabled
- Missing NetworkPolicy
- Overly broad RBAC

**Service mesh / Istio**

- VirtualService without a matching Gateway
- Public route without an AuthorizationPolicy
- Namespace missing strict mTLS
- Missing DestinationRule
- No timeout/retry policy

**cert-manager**

- Certificate missing `renewBefore`
- Certificate duration is very long
- Invalid or unknown issuer
- TLS secret reference missing

**Observability**

- Missing Prometheus annotations or ServiceMonitor
- Missing standard labels
- Missing correlation ID configuration
- Missing logging configuration

The output could look like this:

```text
Platform Readiness: 78/100

Passed:
  [+] readinessProbe configured
  [+] resource requests set
  [+] image tag is pinned
  [+] NetworkPolicy exists

Warnings:
  [!] no PodDisruptionBudget configured
  [!] livenessProbe missing
  [!] ServiceAccount token automount not disabled

Critical:
  [x] container runs as root
  [x] public VirtualService without AuthorizationPolicy
```

---

### 6. Risk Score / Platform Score

ChartPilot can give the chart an aggregate score based on the checks:

```text
Overall:     78/100
Security:    65/100
Reliability: 80/100
Operability: 85/100
Governance:  70/100
```

The score makes it quick to judge whether a chart is ready for a given environment. For example:

- Dev may deploy above a score of 60
- Test requires a score above 75
- Prod requires a score above 90 and no critical findings

It is not meant as absolute truth, but as a review tool that makes the conversation more concrete.

---

### 7. Environment Diff

ChartPilot can compare multiple values files (`values-dev.yaml`, `values-test.yaml`, `values-prod.yaml`) and show the differences in a structured way:

```diff
  replicaCount
- dev:  1
- test: 2
+ prod: 3

  image.tag
- dev:  latest
- test: 1.4.2-rc1
+ prod: 1.4.2

  resources.limits.cpu
- dev:  none
- test: 500m
+ prod: 1000m

  ingress.enabled
- dev:  true
- test: true
+ prod: false
```

This makes it easier to see whether prod is actually more robust and secure than dev.

---

### 8. GitHub Actions Generator

Once the chart is validated, ChartPilot can generate a GitHub Actions workflow template:

```yaml
name: Helm Deploy
on:
  workflow_dispatch:
    inputs:
      environment:
        type: choice
        options:
          - dev
          - test
          - prod

jobs:
  validate:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - name: Helm lint
        run: helm lint ./chart
      - name: Render manifests
        run: helm template member-api ./chart -f values-${{ inputs.environment }}.yaml
      - name: Run platform checks
        run: chartpilot check ./chart -f values-${{ inputs.environment }}.yaml

  deploy:
    needs: validate
    runs-on: ubuntu-latest
    environment: ${{ inputs.environment }}
    steps:
      - uses: actions/checkout@v4
      - name: Deploy with Helm
        run: |
          helm upgrade --install member-api ./chart \
            -f values-${{ inputs.environment }}.yaml \
            --namespace member-platform \
            --create-namespace
```

This demonstrates the link between GUI review and CI/CD.

---

### 9. Golden Path Profiles

ChartPilot can carry different platform profiles:

```yaml
profile: sensitive-internal-service
requirements:
  requireNetworkPolicy: true
  requireReadinessProbe: true
  requireLivenessProbe: true
  requireResourceLimits: true
  requireMtls: true
  requireAuthorizationPolicy: true
  allowPublicIngress: false
  disallowLatestTag: true
```

Possible profiles:

- `public-web-service`
- `internal-api`
- `sensitive-member-data-service`
- `batch-job`
- `legacy-integration-service`
- `sandbox-service`

This makes the tool more platform-oriented than a generic Helm GUI.

---

### 10. Data Classification Awareness

A service can be marked with a data classification:

```yaml
platform:
  dataClassification: sensitive-personal-data
  exposure: internal
```

If a service handles sensitive personal data, ChartPilot tightens the checks:

- no public ingress
- NetworkPolicy required
- mTLS required
- AuthorizationPolicy required
- secrets must not be inline
- audit/logging labels required
- resource limits required
- image scanning should be enabled
- CI/CD must have extra gates

This makes the tool relevant for organizations handling member data, personal data or other sensitive domains.

---

### 11. cert-manager Support

ChartPilot can recognize and analyze cert-manager resources: `Certificate`, `Issuer`, `ClusterIssuer`, TLS secret references.

Example feedback:

```text
[+] Certificate member-api-tls found
[+] renewBefore configured
[!] Certificate duration is 2160h, consider shorter lifetime
[x] Certificate references unknown issuer letsencrypt-prod
```

---

### 12. Istio Support

ChartPilot can analyze Istio configuration: `Gateway`, `VirtualService`, `DestinationRule`, `AuthorizationPolicy`, `PeerAuthentication`.

Example feedback:

```text
[+] VirtualService routes to Service/member-api
[+] PeerAuthentication strict mTLS enabled
[!] No timeout configured on route
[!] No retry policy configured
[x] Public route has no AuthorizationPolicy
```

This makes the project highly relevant for organizations running a service mesh.

---

### 13. Exportable Review Report

After a review, ChartPilot can export a Markdown report:

> **ChartPilot Review: member-api**
>
> **Summary**
> Overall score: 78/100
> Environment: test
> Profile: sensitive-internal-service
>
> **Rendered resources**
> - Deployment/member-api
> - Service/member-api
> - VirtualService/member-api
> - Certificate/member-api-tls
>
> **Critical findings**
> - Container runs as root
> - Public VirtualService without AuthorizationPolicy
>
> **Warnings**
> - Missing PodDisruptionBudget
> - Missing livenessProbe
>
> **Recommended actions**
> 1. Set `securityContext.runAsNonRoot: true`
> 2. Add AuthorizationPolicy for public route
> 3. Add PodDisruptionBudget
> 4. Add livenessProbe

The report can be used in pull requests or as documentation for release reviews.

---

## Possible technical architecture

### Frontend

- React
- Vite
- Monaco Editor for YAML
- Resource tree view
- Diff viewer
- Score/check cards

### Backend

**Option A: .NET Minimal API**

- Matches IDA's backend stack
- Good conversation angle
- Easy to build APIs and run external processes

**Option B: Go**

- Natural fit for Kubernetes tooling
- Good performance
- Single binary
- Many Kubernetes libraries

Recommendation for the interview:

> **.NET Minimal API + React**, because it demonstrates platform engineering while connecting to their C#/.NET environment.

### Backend responsibilities

- Run `helm template`
- Run `helm lint`
- Parse YAML manifests
- Run policy checks
- Return resources, warnings and score to the frontend
- Generate GitHub Actions snippets
- Export the Markdown review report

---

## CLI as a bonus

Although the main idea is a GUI, there can also be a CLI:

```bash
chartpilot check ./chart -f values-prod.yaml --profile sensitive-internal-service
```

Output:

```text
ChartPilot score: 78/100
Critical: 2
Warnings: 4
Run with --report report.md to export full review.
```

This makes the project usable in CI/CD, not just manually.
