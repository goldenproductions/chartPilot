# ChartPilot — Build Report

**Date:** 2026-08-19
**Branch:** `main`
**Platform the numbers below were produced on:** Windows 11, .NET SDK 10.0.300, Node 24.8.0, npm 10.9.1, Helm v4.2.4

This report records what was actually built, what the final build and test runs reported, the
decisions taken while building that are not already visible in `architecture.md`, and the gaps that
remain. It is deliberately written after the fact and against verified output, not against the plan.

---

## 1. Final build and test results

| Command | Result |
|---|---|
| `dotnet build ChartPilot.sln` | **Build succeeded — 0 warnings, 0 errors** |
| `dotnet test ChartPilot.sln` | **453 passed, 0 failed, 0 skipped** |
| `npm run build` (`src/chartpilot-web`) | **Succeeded** — `tsc --noEmit` clean, Vite build clean |

Test breakdown:

| Test project | Passed | Failed | Skipped |
|---|---:|---:|---:|
| `ChartPilot.Core.Tests` | 356 | 0 | 0 |
| `ChartPilot.Helm.Tests` | 66 | 0 | 0 |
| `ChartPilot.Api.Tests` | 31 | 0 | 0 |
| **Total** | **453** | **0** | **0** |

The whole test suite runs without Helm installed: every check is tested against fixture manifests,
and `ChartPilot.Helm.Tests` drives a fake process runner. Only the end-to-end sample-chart tests
touch the real binary.

### Runtime verification (not just tests)

- `dotnet run --project src/ChartPilot.Api` starts on `http://127.0.0.1:5173`;
  `GET /api/v1/environment` returns `helmAvailable: true`, Helm `4.2.4`, `resolutionSource: "path"`.
- `POST /api/v1/workspaces` against `samples/charts/member-api` returns `201` with the full chart
  overview: metadata, 2 pinned dependencies, 4 values files, the schema, 14 templates, 13 detected kinds.
- `POST /api/v1/workspaces/{id}/review` with `values-prod.yaml` and the
  `sensitive-member-data-service` profile returns score **100** with no findings.
- The built SPA is served from `/` by the same host (`200 text/html`).
- CLI: `check samples/charts/member-api --values values-prod.yaml --profile sensitive-member-data-service --fail-on critical`
  → **100/100, 48 passed, exit 0**.
- CLI: `check samples/charts/insecure-member-api --profile sensitive-member-data-service --fail-on warning`
  → **4/100, 25 critical, 17 warnings, 10 passed, exit 1**.
- CLI: `check samples/charts/legacy-importer --profile legacy-integration-service --fail-on critical --report … --workflow …`
  → **20/100, exit 1**, and both the Markdown report and the GitHub Actions workflow were written and
  are well formed.

---

## 2. What was built, by work item

Work was parallelised across agents, each owning a disjoint set of files. The contracts under
`src/ChartPilot.Core/Contracts/` were written first and frozen so the areas below could be built
simultaneously without touching each other.

| # | Work item | Delivered |
|---|---|---|
| W0 | Contracts and scaffold | `ChartPilot.sln`, `Directory.Build.props` (net10.0, nullable, implicit usings), and every shared record, enum and interface under `Core/Contracts/` — `RenderedResource`, `ResourceRef`, `ResourceCategory`, `GraphEdge`, `IResourceGraph`, `ManifestNavigator`, `Finding`, `Severity`, `CheckDescriptor`, `CheckContext`, `IResourceCheck`, `Profile`, `ReviewResult`, `ScoreReport`, `ChartModel`, `ValuesDocument`, `IHelmClient`, `IFileSystem` |
| W1 | Helm integration | `HelmLocator` (config → PATH → well-known locations), `HelmClient` (`template` and `lint`), `ProcessRunner`, `PathGuard`, `HelmLintParser`, `ChartPilotHelmOptions`, `ClusterFreeEnvironment` |
| W2 | Charts and values | `ChartLoader`, `TemplateKindScanner`, `ValuesMerger`, `ValuesValidator` (JsonSchema.Net), `ValuesDiffService`, `YamlJsonConverter` |
| W3 | Manifests and graph | `ManifestParser` (multi-doc stream, `# Source:` attribution, List unwrapping, malformed-document isolation), `ResourceGraph`, `ResourceGraphBuilder` (selector, Istio, cert-manager, secret and service-account edges) |
| W4 | Check engine and rules | `CheckEngine`, `CheckCatalog`, `CheckBase`, `CheckHelpers`, `IConditionalCheck`, and **51 rules**: `CP-SEC-*` (14), `CP-REL-*` (10), `CP-NET-*` (8), `CP-GOV-*` (8), `CP-OBS-*` (6), `CP-CERT-*` (5) — each with a violating and a compliant fixture |
| W5 | Profiles, severity and scoring | `BuiltInProfiles` (7 profiles), `ProfileStore`, `SeverityResolver`, `PlatformMetadataReader`, `SuppressionLoader` (`.chartpilot.yaml`, mandatory reason and expiry, expired suppressions re-raised as `CP-GOV-005`), `Scorer` |
| W6 | Review pipeline | `RenderService`, `ReviewPipeline`, `ValuesFileResolver`, `HelmErrorSource` (maps Helm's `file:line:col` back to something the GUI can open) |
| W7 | Reporting | `MarkdownReportWriter` (snapshot-tested), `GitHubActionsWorkflowGenerator` (snapshot-tested) |
| W8 | HTTP API | Minimal API under `/api/v1`, `WorkspaceStore`, the DTO layer, a ProblemDetails exception handler carrying the `helmStderr` extension, loopback-only binding, allowlist-root resolution, SPA hosting and fallback |
| W9 | Web frontend | React 19 + TypeScript + Vite + Monaco: header with profile and environment pickers, chart overview, values editor with debounced live render, resource explorer, findings panel with click-to-navigate, score card, Helm stderr panel, environment diff, and the report / workflow / values export dialogs |
| W10 | Samples and CI | 5 sample charts (`member-api` golden path, `sample-service`, `batch-report`, `insecure-member-api`, `legacy-importer`) and `.github/workflows/ci.yml`, which gates the repo on ChartPilot's own output |

---

## 3. Decisions taken during the build

**Shared types live in `Contracts/`, but keep their architectural namespace.** `architecture.md`
assigns types to feature folders. Physically collecting the shared declarations under
`Core/Contracts/<Area>/` while keeping the namespace the document specifies — `RenderedResource` is
in `Contracts/Manifests/` and is still `ChartPilot.Core.Manifests` — let several agents work inside
Core at once without editing the same files. The namespace is the contract; the folder is only a
collision-avoidance device.

**Profiles are data, not code.** Deduction weights, category weights, mandatory requirements and
severity overrides all come from the profile record, never from constants in a rule. That is what
makes `sandbox-service` and `sensitive-member-data-service` run the identical 51-rule catalog and
still produce materially different findings and scores.

**Rules query the graph, not raw YAML.** "A public VirtualService with no AuthorizationPolicy" and
"a Certificate whose issuer this chart does not render" are graph questions. Building
`ResourceGraphBuilder` before the rule waves meant M2 was rule-writing rather than plumbing, and it
is the reason the cross-resource findings exist at all.

**The GUI pipeline uses `review`, not `render`.** `ReviewDto` already carries the rendered
resources, so a keystroke costs one Helm execution rather than two. Renders are debounced 400 ms and
carry an `AbortSignal`, so a superseded render is cancelled instead of raced.

**The allowlist root defaults instead of being required.** An unconfigured root would have rejected
every real chart at render time, several requests after the mistake was made. The API falls back to
the checkout root, the CLI uses the chart's parent directory, `GET /environment` reports the
effective value, and opening a chart outside it fails as a `400` at open time.

**Loopback is enforced, not merely defaulted.** ChartPilot executes arbitrary Go templates from
charts the user points it at. The API refuses to start when configured with a non-loopback URL,
rather than trusting the default.

**`--dependency-update` is off and there is no kubeconfig path at all.** Rendering never hits the
network by default and cannot reach a cluster even if a chart tries. `--include-crds` is on, because
a CRD the chart ships is a resource the reviewer needs to see.

**Hand-rolled CLI argument parsing.** Three commands and nine flags do not justify a parsing
dependency, and not taking one keeps an offline restore reproducible.

**Full Monaco bundle (~3.7 MB, ~1 MB gzipped).** It is served from loopback; trimming Monaco's
language contributions is a known source of subtle breakage and would buy nothing here.

**Plain xunit asserts, no FluentAssertions** — licensing.

**Five sample charts, not two.** The catalog needed a chart that passes cleanly (`member-api`), a
chart that fails on nearly everything (`insecure-member-api`), a chart that exercises the
legacy-integration profile's demotions (`legacy-importer`), and a non-serving workload that must
*not* be judged by serving-path rules (`batch-report`). CI gates on the first three.

---

## 4. Known gaps

- **No schema-driven form editor.** `values.schema.json` drives completion, hover documentation and
  inline validation through `monaco-yaml`, but the spec's "schema-driven mode" — generated text,
  number, boolean and enum **controls** — was not built. The editor is plain YAML.
- **The CI workflow has never executed.** `.github/workflows/ci.yml` is complete and every step has
  been run by hand locally, but it has not yet run on GitHub Actions.
- **No frontend test suite.** `chartpilot-web` has no vitest or Playwright tests. Its correctness
  rests on `tsc --noEmit` and on the API contract tests behind it.
- **No README demo GIF** (an M5 polish item).
- **Deliberately out of scope**, per `features.md` §4: cluster connectivity of any kind, chart
  repositories and OCI registries, persistence, authentication, and auto-rewriting the user's
  `values.yaml`.
