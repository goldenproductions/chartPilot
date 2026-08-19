# ChartPilot

**Helm Chart Review & Golden Path GUI**

ChartPilot is a local, web-based GUI for understanding, configuring and quality-assuring Helm charts before they are deployed to Kubernetes.

> ChartPilot parses chart metadata and values, renders Kubernetes manifests live, and runs platform checks for security, operability and governance. The goal is to make it easy for developers to follow safe standards, and easy for platform teams to scale golden paths without becoming a bottleneck.

## What it does

1. Pick a Helm chart — see metadata, dependencies and values files
2. Edit `values.yaml` in a YAML editor (schema-guided when `values.schema.json` exists)
3. Render manifests live with `helm template`
4. Explore the generated Kubernetes resources in a tree
5. Run platform readiness checks (reliability, security, Istio, cert-manager, observability)
6. Get a platform score per category
7. Compare values across dev/test/prod
8. Generate a GitHub Actions workflow
9. Export a Markdown review report

## Documentation

| Document | What it covers |
|---|---|
| [`docs/chartpilot-spec.md`](docs/chartpilot-spec.md) | Full functional specification — all 13 features |
| [`docs/architecture.md`](docs/architecture.md) | Process shape, solution layout, core pipeline, check engine, API, security posture |
| [`docs/features.md`](docs/features.md) | Feature map and milestone delivery plan (M0–M5) |
| [`docs/build-report.md`](docs/build-report.md) | What was actually built, the final build/test results, decisions and known gaps |

## Status

**Implemented and working end to end.** M0–M4 are complete, M5 is complete apart from a demo GIF.

Verified on 2026-08-19 on Windows 11 with .NET SDK 10.0.300, Node 24.8.0 and Helm v4.2.4:

- `dotnet build` — **succeeded, 0 warnings, 0 errors**
- `dotnet test` — **453 passed, 0 failed, 0 skipped** (Core 356, Helm 66, Api 31)
- `npm run build` in `src/chartpilot-web` — succeeded (`tsc --noEmit` + `vite build`)

What exists:

- **`ChartPilot.Core`** — chart loading, values merge/validate/diff, multi-document manifest parser,
  resource graph with cross-resource edges, the check engine, profiles and severity resolution,
  scoring, Markdown report writer and GitHub Actions workflow generator.
- **`ChartPilot.Helm`** — Helm binary locator, `helm template` and `helm lint` execution with a
  wall-clock timeout, an output size cap and an allowlist-rooted chart path guard.
- **`ChartPilot.Api`** — Minimal API under `/api/v1`, loopback-only binding, serves the built SPA.
- **`ChartPilot.Cli`** — `chartpilot check`, `chartpilot profiles`, `chartpilot checks`.
- **`chartpilot-web`** — React 19 + Vite + Monaco three-pane GUI: chart overview, values editor with
  live debounced render, resource explorer, findings panel with click-to-navigate, score card,
  environment diff, report and workflow export.
- **51 checks** across six families: `CP-SEC-*` (14), `CP-REL-*` (10), `CP-NET-*` (8), `CP-GOV-*` (8),
  `CP-OBS-*` (6), `CP-CERT-*` (5). **7 built-in profiles.**
- **Five sample charts** in `samples/charts/`, including a golden-path reference chart and two
  deliberately bad ones that this repository's own CI gates on.

Known gaps are listed in [`docs/build-report.md`](docs/build-report.md#known-gaps).

## Prerequisites

- **.NET 10 SDK** (developed against 10.0.300) — required to build and to run the tests.
- **Node.js 20+** (developed against 24.8.0) — required only to build or develop the web frontend.
- **Helm 3 or 4** (`winget install Helm.Helm`; developed against v4.2.4) — required at *runtime* to
  render charts, but **not** to build or to run the tests: every check is tested against fixture
  manifests. ChartPilot resolves the binary from configuration → `PATH` → well-known install
  locations, and reports what it found on `GET /api/v1/environment`.

ChartPilot never needs a kubeconfig and never contacts a cluster.

## Running the API

```bash
dotnet run --project src/ChartPilot.Api
# now listening on http://127.0.0.1:5080
```

The API listens on **5080 in every environment**. Port 5173 belongs to the Vite dev server, so the
two never contend for it.

The API binds loopback only and refuses to start if it is configured with a non-loopback URL. If
`src/chartpilot-web/dist` has been built, the same host serves the GUI at `http://127.0.0.1:5080/`;
otherwise only the API responds.

Check that Helm was found:

```bash
curl http://127.0.0.1:5080/api/v1/environment
```

```json
{"helmAvailable":true,"helmPath":"C:\...\windows-amd64\helm.exe","helmVersion":"4.2.4",
 "helmError":null,"resolutionSource":"path","allowlistRoot":"C:\Repos\chartPilot",
 "chartPilotVersion":"1.0.0+9a0fc12"}
```

Charts must live under the allowlist root, which defaults to the checkout the API runs from and can
be overridden with `ChartPilot__AllowlistRoot` or `CHARTPILOT_ALLOWLIST_ROOT`.

## Running the web frontend

```bash
cd src/chartpilot-web
npm install

# Dev loop: Vite on http://127.0.0.1:5173, proxying /api to the API on 127.0.0.1:5080.
# Start the API first, in either profile — both listen on 5080:
#   dotnet run --project ../ChartPilot.Api
npm run dev

# Then open http://127.0.0.1:5173/ (Vite), not 5080 — you want hot reload.

# Production build into ./dist, which ChartPilot.Api then serves
npm run build
```

More detail in [`src/chartpilot-web/README.md`](src/chartpilot-web/README.md).

## Running the CLI

```bash
dotnet run --project src/ChartPilot.Cli -- check <chartPath> [options]
```

Options: `-f/--values` (repeatable), `--profile`, `--environment`, `--release`, `--report <path>`,
`--workflow <path>`, `--fail-on info|warning|critical`, `--json`.
`chartpilot profiles` and `chartpilot checks` print the built-in profile list and the rule catalog.

Exit codes: **0** nothing reached the `--fail-on` level, **1** the gate tripped, **2** execution error.

### A real example

The golden-path reference chart, reviewed as a sensitive-data service:

```bash
dotnet run --project src/ChartPilot.Cli -- check samples/charts/member-api \
  --values samples/charts/member-api/values-prod.yaml \
  --profile sensitive-member-data-service --fail-on critical
```

```
ChartPilot score: 100/100
Critical: 0
Warnings: 0

  Security     100/100
  Reliability  100/100
  Operability  100/100
  Governance   100/100

Passed: 48

Run with --report report.md to export full review.
```

Exit code `0`. The deliberately bad chart, on the same profile:

```bash
dotnet run --project src/ChartPilot.Cli -- check samples/charts/insecure-member-api \
  --profile sensitive-member-data-service --fail-on warning
```

```
ChartPilot score: 4/100
Critical: 25
Warnings: 17

  Security       0/100
  Reliability    0/100
  Operability    0/100
  Governance    26/100

Critical:
  [x] CP-GOV-001 chart: Chart 'insecure-member-api' does not ship a values.schema.json.
  [x] CP-GOV-002 chart: Dependency 'redis' has version '^18.0.0', which is not an exact version.
  [x] CP-NET-002 VirtualService/insecure-member-api-insecure-member-api: VirtualService/insecure-member-api-insecure-member-api is exposed through a Gateway but no AuthorizationPolicy covers its destination workload.
  ... 22 more, including CP-SEC-002 (privileged: true), CP-SEC-007 (inline secret data) and CP-SEC-010 (wildcard RBAC)

Warnings:
  [!] CP-CERT-001 Certificate/insecure-member-api-insecure-member-api-tls: Certificate/insecure-member-api-insecure-member-api-tls does not set spec.renewBefore.
  ... 16 more, including CP-GOV-005 (a suppression in .chartpilot.yaml expired on 2020-01-01, so the finding it waived was re-raised)

Passed: 10
```

Exit code `1`. Findings are printed in rule-id order; the excerpt above is the head of each list.

## Building and testing everything

```bash
dotnet build ChartPilot.sln
dotnet test  ChartPilot.sln
```

`.github/workflows/ci.yml` runs the same build and tests, builds the web client, and then gates the
repository on ChartPilot's own output: the reference chart must stay clean and the bad sample charts
must keep failing the critical gate.
