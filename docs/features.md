# ChartPilot — Feature Overview & Delivery Plan

The full functional description is in [`chartpilot-spec.md`](chartpilot-spec.md); how it is built is in [`architecture.md`](architecture.md). This document is the map between them: what each feature actually requires, and in what order it gets built.

---

## 1. Feature map

| # | Feature | What it needs | Where it lives | Milestone |
|---|---|---|---|---|
| 1 | Chart Overview | Read `Chart.yaml`, discover values files + schema, static template kind scan | `Core/Charts` | M0 |
| 2 | Values Editor | Monaco + YAML validation; schema-driven fields when `values.schema.json` exists | `web/`, `Core/Values` | M1 |
| 3 | Live Helm Render | `IHelmClient.TemplateAsync`, debounced re-render, stderr surfacing | `ChartPilot.Helm` | M0/M1 |
| 4 | Resource Explorer | Multi-doc YAML parser, `ResourceGraph`, grouped tree | `Core/Manifests` | M0 |
| 5 | Platform Readiness Checks | The rule catalog over the resource graph | `Core/Checks` | **M2** |
| 6 | Risk / Platform Score | Weighted deductions per category | `Core/Scoring` | M2 |
| 7 | Environment Diff | Structured N-way values comparison | `Core/Values` | M4 |
| 8 | GitHub Actions Generator | Templated workflow from chart + profile + environments | `Core/Reporting` | M4 |
| 9 | Golden Path Profiles | Profile files + severity resolution | `Core/Profiles` | M3 |
| 10 | Data Classification Awareness | Classification-driven severity promotion | `Core/Profiles` | M3 |
| 11 | cert-manager Support | `CP-CERT-*` rules + Certificate/Issuer/Secret graph edges | `Core/Checks` | M2/M3 |
| 12 | Istio Support | `CP-NET-*` rules + VirtualService/Gateway/Service graph edges | `Core/Checks` | M2/M3 |
| 13 | Exportable Review Report | Markdown writer over `ReviewResult` | `Core/Reporting` | M4 |
| — | CLI (`chartpilot check`) | Same Core, exit codes, `--fail-on` | `ChartPilot.Cli` | M5 |

Features 5 and 6 are the product. Everything before them exists to make them possible; everything after them exists to make them travel — into a pull request, into CI, into a conversation with a platform team.

---

## 2. Milestones

Each milestone is independently demonstrable. If work stops at the end of any one of them, what exists still shows something real.

### M0 — Walking skeleton

*Point it at a chart and see what comes out.*

- Solution scaffold: `Core`, `Helm`, `Api`, `web`, test projects
- `HelmLocator` + `/environment` endpoint, with the "helm is not installed" banner
- `POST /workspaces` → chart overview card
- `POST /render` → multi-doc parse → grouped resource tree, click for full YAML
- Sample charts in `samples/charts/`, including the deliberately bad one

**Done when:** opening a sample chart shows its metadata and its rendered resources, with no editing yet.

### M1 — Values editing and live render

*Change a value, watch the manifest change.*

- Monaco values editor, draft overlay held in the workspace
- Debounced re-render, in-flight cancellation, template errors shown against the failing line
- Values-file switcher (`values.yaml` / `values-prod.yaml` / …)
- Schema-driven hints when `values.schema.json` is present

**Done when:** `replicaCount: 1 → 3` visibly updates `spec.replicas` in the rendered Deployment.

### M2 — The check engine *(the core deliverable)*

*The tool starts having an opinion.*

- `IResourceCheck` contract, DI-scanned catalog, `CheckContext`
- First rule wave: `CP-REL-*` and `CP-SEC-*` (probes, requests/limits, replicas, root, `latest`, `privileged`, `runAsNonRoot`, automount, inline secrets)
- First `CP-NET-*` / `CP-CERT-*` rules that exercise graph edges
- Scorer + score breakdown card
- Findings panel: severity grouping, click-to-navigate to the offending resource and YAML path
- `GET /checks` rule catalog endpoint

**Done when:** the deliberately bad sample chart produces a known score and a known finding set, asserted by an end-to-end test.

### M3 — Profiles and governance

*The same catalog, tuned per service class.*

- Profile files + `GET /profiles`, profile picker in the header
- Severity resolution: default → profile promotion → classification promotion
- `platform.dataClassification` / `exposure` read from values
- `.chartpilot.yaml` suppressions with mandatory reason and expiry, expired suppressions becoming findings
- Full `CP-ISTIO-*`, `CP-CERT-*`, `CP-OBS-*`, `CP-GOV-*` waves

**Done when:** switching `sandbox-service` → `sensitive-member-data-service` on an unchanged chart materially changes the findings and the score.

### M4 — Making it travel

*Review output leaves the tool.*

- N-way environment diff view
- Markdown review report export (snapshot-tested)
- GitHub Actions workflow generator
- Export of the edited `values.yaml`

**Done when:** a review produces a Markdown report that is pasteable into a pull request as-is.

### M5 — CLI, CI and polish

- `chartpilot check` with `--profile`, `--report`, `--fail-on`, exit codes 0/1/2
- The generated workflow actually runs `chartpilot check` against the sample chart in this repo's own CI
- Empty/error/loading states, keyboard navigation, a README demo GIF

**Done when:** this repository's own CI gates on ChartPilot's own output.

---

## 3. Build order rationale

The render path comes first because **every** later feature consumes rendered resources — checks, score, diff, report and the CLI are all functions over the same `ReviewResult`. Building the resource graph early means the check engine in M2 is mostly rule-writing rather than plumbing.

Profiles (M3) come *after* the rules exist, not before. A profile is a set of promotions over a catalog; designing the promotion model before there is a catalog to promote is guesswork.

The CLI is last not because it is unimportant, but because it is cheap once Core is stable — it is a thin face over an engine that will already have been exercised through the API.

---

## 4. Scope guards

Things that will look tempting mid-build and are deliberately out of scope for now:

- Cluster connectivity of any kind (live diff, `helm get values`, dry-run against a real API server)
- Chart repositories and OCI registries — local directories only
- Rendering charts that require `--dependency-update` network access, unless the user explicitly opts in
- Persistence, multi-user state, authentication
- Auto-fixing findings by rewriting the user's `values.yaml` — ChartPilot recommends; the human edits
