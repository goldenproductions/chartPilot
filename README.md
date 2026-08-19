# ChartPilot

**Review a Helm chart before it reaches a cluster.**

[![CI](https://github.com/goldenproductions/chartPilot/actions/workflows/ci.yml/badge.svg)](https://github.com/goldenproductions/chartPilot/actions/workflows/ci.yml)
[![Licence: Apache 2.0](https://img.shields.io/badge/licence-Apache%202.0-blue.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)
[![Helm 3 or 4](https://img.shields.io/badge/Helm-3%20%7C%204-0F1689.svg)](https://helm.sh/)

ChartPilot renders a Helm chart with `helm template`, builds a graph of the Kubernetes resources it
produces, and tells you what is wrong with them — with the reasoning, the severity in the context of
*your* platform's rules, and concrete options for fixing it.

It runs entirely on your machine. It never needs a kubeconfig and never contacts a cluster.

![The ChartPilot window: chart overview and resource tree on the left, the rendered manifest in the centre with the offending line highlighted, findings and score on the right](docs/images/04-finding-navigation.jpg)

## Try it in a minute

```bash
git clone https://github.com/goldenproductions/chartPilot.git
cd chartPilot

# The CLI, against a deliberately bad sample chart that ships with the repo
dotnet run --project src/ChartPilot.Cli -- check samples/charts/legacy-importer --explain
```

```
ChartPilot score: 32/100
Critical: 8
Warnings: 25

Critical:
  [x] CP-SEC-002 Deployment/legacy-importer: Container 'importer' sets privileged: true.

      privileged: true switches off container isolation. The process gets every Linux
      capability and raw access to the node's devices, so anything that compromises this
      container has effectively compromised the node it runs on.

      Your options:

        1. Turn it off  (recommended)
           Almost no application workload needs privileged mode; it is usually copied from
           an example.
             securityContext:
               privileged: false
               allowPrivilegeEscalation: false
               capabilities:
                 drop: [ALL]
           Try this first. If the container still works, it never needed privilege - which
           is the common case.
        ...
```

For the GUI, build the frontend once and run the API:

```bash
cd src/chartpilot-web && npm install && npm run build && cd ../..
dotnet run --project src/ChartPilot.Api      # then open http://127.0.0.1:5080
```

The [tutorial](docs/tutorial.md) walks through the whole interface with screenshots.

## Why it exists

Helm charts are the deployment contract between developers and a platform team, and small changes to
`values.yaml` have large effects on what actually gets deployed. The feedback usually arrives late —
in a pull request review, in a pipeline, or in production — and the platform team ends up manually
catching the same handful of mistakes over and over.

ChartPilot moves that feedback to before the change is committed, and encodes the platform team's
standards as **golden path profiles** so the same chart can be judged as a sandbox service or as one
holding personal data.

What it does that a per-file linter cannot: it reasons over the **whole rendered chart**. "This
VirtualService is exposed through a Gateway and no AuthorizationPolicy covers its destination
workload" is a question about three resources at once.

## What it does

1. Pick a Helm chart — see metadata, dependencies and values files
2. Edit `values.yaml` in a YAML editor (schema-guided when `values.schema.json` exists)
3. Render manifests live with `helm template`
4. Explore the generated Kubernetes resources in a tree
5. Run platform readiness checks (reliability, security, Istio, cert-manager, observability)
   — every finding explains itself in plain language and offers concrete options to choose from
6. Get a platform score per category
7. Compare values across dev/test/prod
8. Generate a GitHub Actions workflow
9. Export a Markdown review report

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

## Documentation

| Document | What it covers |
|---|---|
| [`docs/chartpilot-spec.md`](docs/chartpilot-spec.md) | Full functional specification — all 13 features |
| [`docs/architecture.md`](docs/architecture.md) | Process shape, solution layout, core pipeline, check engine, API, security posture |
| [`docs/features.md`](docs/features.md) | Feature map and milestone delivery plan (M0–M5) |
| [`docs/tutorial.md`](docs/tutorial.md) | **Walkthrough of the web UI**, with screenshots |
| [`docs/tutorial.html`](docs/tutorial.html) | The same walkthrough as a styled page — open it in a browser |
| [`docs/build-report.md`](docs/build-report.md) | What was actually built, the final build/test results, decisions and known gaps |
| [`CONTRIBUTING.md`](CONTRIBUTING.md) | How to build it, and how to add a check |
| [`SECURITY.md`](SECURITY.md) | The threat model, and what counts as a vulnerability |

## Status

**Implemented and working end to end.** M0–M4 are complete, M5 is complete apart from a demo GIF.
CI runs on every push and is green.

Verified on 2026-08-19 on Windows 11 with .NET SDK 10.0.300, Node 24.8.0 and Helm v4.2.4:

- `dotnet build` — **succeeded, 0 warnings, 0 errors**
- `dotnet test` — **472 passed, 0 failed, 0 skipped** (Core 363, Helm 66, Api 43)
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

## How this was built

Worth stating up front, because the commit history says it on every commit and you would find out
anyway.

**The implementation was written with [Claude](https://claude.com/claude-code).** The specification,
the architecture, the design decisions and the review are mine; the code was produced against that
architecture and then verified — every claim in this README was checked by running the thing, not by
asking the model whether it worked.

One part deserves a specific warning. The per-rule advice in
[`src/ChartPilot.Core/Checks/Guidance/`](src/ChartPilot.Core/Checks/Guidance) — the "what should I
do?" text behind every finding — is **machine-authored and has not been reviewed by a domain
expert.** It is written in standard Kubernetes terms and, as far as I can tell, it is sound. But
treat it as a well-informed starting point rather than an authority, especially the security and
Istio families. Corrections are the most welcome kind of issue this project can get; there is
[a template for exactly that](.github/ISSUE_TEMPLATE/guidance_feedback.yml).

The parts that are *not* machine-authored judgement, and can be trusted differently:

- **Findings** are computed from your rendered manifests by deterministic rules, each with a
  violating and a compliant fixture test.
- **Severity reasons** are derived from the profile table that made the decision, so they cannot
  disagree with it.

## Contributing

Contributions are welcome — [`CONTRIBUTING.md`](CONTRIBUTING.md) covers the layout, the house style,
and how to add a check (a rule, its guidance, and two fixtures).

The most valuable contributions are the ones that need real-world experience:

- A rule that fires when it should not, or stays silent when it should not.
- Advice that is wrong for how you actually run Kubernetes.
- A chart shape that trips the parser.

By participating you agree to the [Code of Conduct](CODE_OF_CONDUCT.md). Security issues go through
[`SECURITY.md`](SECURITY.md), not public issues.

## Licence

[Apache 2.0](LICENSE) — the same licence Helm, Kubernetes, Istio and cert-manager use.
