# ChartPilot — Helm Chart Review & Golden Path GUI

ChartPilot er en lokal webbaseret GUI til at forstå, konfigurere og kvalitetssikre Helm charts før deployment til Kubernetes.

Formålet er ikke bare at gøre `values.yaml` mere læsbar, men at hjælpe både udviklere og platform-teams med at træffe bedre beslutninger omkring driftbarhed, sikkerhed, standardisering og release-kvalitet.

I mange organisationer bliver Helm charts brugt som deployment-kontrakt mellem udviklere og platform-teamet. Men charts kan hurtigt blive komplekse, og små ændringer i `values.yaml` kan have stor effekt på de Kubernetes-resources, der faktisk bliver deployet. ChartPilot gør denne effekt synlig, før ændringerne rammer et miljø.

Projektet er tænkt som et lokalt, gratis og cloud-uafhængigt værktøj, men med direkte relevans for platforme baseret på AKS, Kubernetes, Helm, Istio, cert-manager, GitHub Actions og moderne platform engineering-principper.

---

## Elevator pitch

> ChartPilot er en lokal GUI til Helm charts, der parser chart metadata og values, renderer Kubernetes manifests live og kører platform checks for sikkerhed, driftbarhed og governance. Målet er at gøre det let for udviklere at bruge sikre standarder og let for platform-teams at skalere golden paths uden at blive flaskehals.

---

## Problemet

Helm er kraftfuldt, men ofte svært at arbejde sikkert med:

- `values.yaml` kan være stor og uoverskuelig.
- Det er svært at se, hvilke Kubernetes-resources en ændring faktisk genererer.
- Udviklere kan nemt mangle vigtige platformkrav som probes, resource limits, NetworkPolicies eller sikre image tags.
- Forskelle mellem dev, test og prod values kan være svære at overskue.
- Sikkerheds- og driftskrav bliver ofte først opdaget sent i pipeline eller under review.
- Platform-teams ender ofte med at være manuelle reviewers af de samme fejl igen og igen.

ChartPilot forsøger at flytte feedback tidligere i processen.

---

## Løsningen

ChartPilot giver en visuel arbejdsgang omkring Helm charts:

1. Vælg et Helm chart.
2. Se chart metadata, dependencies og standard values.
3. Redigér `values.yaml` i en struktureret editor.
4. Render manifests live med `helm template`.
5. Se hvilke Kubernetes-resources der bliver genereret.
6. Kør automatiske platform checks.
7. Sammenlign values mellem miljøer.
8. Generér forslag til CI/CD workflow eller deploy-kommandoer.
9. Eksportér en valideret `values.yaml` og review-rapport.

---

## Primære features

### 1. Chart Overview

ChartPilot læser og viser grundlæggende information fra chartet:

- Chart name
- Chart version
- App version
- Description
- Dependencies
- Maintainers
- Tilgængelige values-filer
- Om chartet har `values.schema.json`
- Templates og Kubernetes resource-typer

Eksempel:

```text
Chart: member-api
Version: 0.3.1
App Version: 1.12.0
Dependencies: redis, mongodb
Schema: values.schema.json found
Resources: Deployment, Service, VirtualService, Certificate
```

Formålet er hurtigt at give både udviklere og platformfolk overblik over, hvad chartet indeholder.

---

### 2. Values Editor

ChartPilot indeholder en YAML-editor til Helm values. Editoren kan arbejde på to niveauer:

**Simpel YAML mode** — brugeren redigerer direkte i `values.yaml`, men får syntax highlighting og valideringsfejl.

**Schema-baseret mode** — hvis chartet har `values.schema.json`, kan ChartPilot bruge schemaet til at vise mere guidede felter:

- tekstfelter
- talfelter
- booleans
- dropdowns/enums
- required fields
- default values
- beskrivelser fra schemaet

Eksempel:

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

Målet er at gøre chart-konfiguration lettere at forstå uden at skjule YAML'en helt.

---

### 3. Live Helm Render

Når brugeren ændrer values, kan ChartPilot køre:

```bash
helm template release-name ./chart -f values.yaml
```

og vise de genererede Kubernetes manifests direkte i GUI'en. Det gør det tydeligt, hvordan en ændring i values påvirker outputtet.

Eksempel — brugeren ændrer `replicaCount: 1` til `replicaCount: 3`, og ChartPilot viser, at dette ændrer:

```yaml
spec:
  replicas: 3
```

i Deployment-manifestet.

---

### 4. Kubernetes Resource Explorer

De renderede manifests vises som en struktureret liste:

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

Når brugeren klikker på en resource, vises den fulde YAML.

---

### 5. Platform Readiness Checks

ChartPilot analyserer de renderede manifests og giver feedback på sikkerhed, stabilitet og driftbarhed.

**Reliability**

- Mangler `readinessProbe`
- Mangler `livenessProbe`
- Mangler resource requests
- Mangler resource limits
- Kun én replica i prod
- Mangler PodDisruptionBudget
- Rolling update strategy ikke sat
- Ingen health endpoint eksponeret

**Security**

- Container kører som root
- `privileged: true`
- Mangler `runAsNonRoot`
- Mangler `readOnlyRootFilesystem`
- Image tag er `latest`
- Secrets ligger som klartekst i manifests
- ServiceAccount token automount er aktiveret
- Mangler NetworkPolicy
- For bred RBAC

**Service mesh / Istio**

- VirtualService uden tilhørende Gateway
- Public route uden AuthorizationPolicy
- Namespace mangler strict mTLS
- Mangler DestinationRule
- Ingen timeout/retry-policy

**cert-manager**

- Certificate mangler `renewBefore`
- Certificate duration er meget lang
- Ugyldig eller ukendt issuer
- TLS secret mangler reference

**Observability**

- Mangler Prometheus annotations eller ServiceMonitor
- Mangler standard labels
- Mangler correlation ID-konfiguration
- Mangler logging configuration

Output kunne se sådan ud:

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

ChartPilot kan give chartet en samlet score baseret på checks:

```text
Overall:     78/100
Security:    65/100
Reliability: 80/100
Operability: 85/100
Governance:  70/100
```

Scoren gør det hurtigt at vurdere, om et chart er klar til et miljø. Eksempel:

- Dev må deploye ved score over 60
- Test kræver score over 75
- Prod kræver score over 90 og ingen critical findings

Det er ikke tænkt som en absolut sandhed, men som et review-værktøj der gør samtalen mere konkret.

---

### 7. Environment Diff

ChartPilot kan sammenligne flere values-filer (`values-dev.yaml`, `values-test.yaml`, `values-prod.yaml`) og vise forskelle struktureret:

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

Det gør det lettere at se, om prod faktisk er mere robust og sikkert end dev.

---

### 8. GitHub Actions Generator

Når chartet er valideret, kan ChartPilot generere en GitHub Actions workflow-skabelon:

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

Dette viser koblingen mellem GUI-review og CI/CD.

---

### 9. Golden Path Profiles

ChartPilot kan have forskellige platform-profiler:

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

Mulige profiler:

- `public-web-service`
- `internal-api`
- `sensitive-member-data-service`
- `batch-job`
- `legacy-integration-service`
- `sandbox-service`

Det gør værktøjet mere platform-orienteret end en generisk Helm GUI.

---

### 10. Data Classification Awareness

En service kan markeres med dataklasse:

```yaml
platform:
  dataClassification: sensitive-personal-data
  exposure: internal
```

Hvis en service håndterer følsomme persondata, skærper ChartPilot checks:

- ingen public ingress
- NetworkPolicy kræves
- mTLS kræves
- AuthorizationPolicy kræves
- secrets må ikke være inline
- audit/logging labels kræves
- resource limits kræves
- image scanning bør være aktiv
- CI/CD skal have ekstra gates

Dette gør værktøjet relevant for organisationer med medlemsdata, persondata eller andre følsomme domæner.

---

### 11. cert-manager Support

ChartPilot kan genkende og analysere cert-manager resources: `Certificate`, `Issuer`, `ClusterIssuer`, TLS secret references.

Eksempel på feedback:

```text
[+] Certificate member-api-tls found
[+] renewBefore configured
[!] Certificate duration is 2160h, consider shorter lifetime
[x] Certificate references unknown issuer letsencrypt-prod
```

---

### 12. Istio Support

ChartPilot kan analysere Istio-konfiguration: `Gateway`, `VirtualService`, `DestinationRule`, `AuthorizationPolicy`, `PeerAuthentication`.

Eksempel på feedback:

```text
[+] VirtualService routes to Service/member-api
[+] PeerAuthentication strict mTLS enabled
[!] No timeout configured on route
[!] No retry policy configured
[x] Public route has no AuthorizationPolicy
```

Det gør projektet meget relevant for organisationer, der bruger service mesh.

---

### 13. Exportable Review Report

Efter review kan ChartPilot eksportere en Markdown-rapport:

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

Rapporten kan bruges i pull requests eller som dokumentation til release reviews.

---

## Mulig teknisk arkitektur

### Frontend

- React
- Vite
- Monaco Editor til YAML
- Resource tree-view
- Diff viewer
- Score/check cards

### Backend

**Option A: .NET Minimal API**

- Matcher IDA's backend-stack
- God samtalevinkel
- Let at lave API'er og process execution

**Option B: Go**

- Naturligt til Kubernetes tooling
- God performance
- Single binary
- Mange Kubernetes libraries

Anbefaling til jobsamtalen:

> **.NET Minimal API + React**, fordi det både viser platform engineering og kobler til deres C#/.NET-miljø.

### Backend ansvar

- Køre `helm template`
- Køre `helm lint`
- Parse YAML manifests
- Køre policy checks
- Returnere resources, warnings og score til frontend
- Generere GitHub Actions snippets
- Eksportere Markdown review report

---

## CLI som bonus

Selvom hovedideen er GUI, kan der også være en CLI:

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

Det gør projektet brugbart i CI/CD, ikke kun manuelt.
