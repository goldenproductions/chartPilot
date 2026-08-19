# ChartPilot — contributor orientation

ChartPilot is a local web GUI plus a CLI for reviewing Helm charts before they are deployed. It
renders a chart with `helm template`, parses the manifest stream into a resource graph, runs a
catalog of platform checks over that graph, and scores the result.

It never contacts a Kubernetes cluster. No kubeconfig, no `--dry-run`, no `helm get`. That is a
security property, not an omission — see `docs/architecture.md` §11.

The three documents in `docs/` are the specification and they win over anything written here:

- `docs/chartpilot-spec.md` — what the product does (all 13 features)
- `docs/architecture.md` — how it is put together (**the inter-project contract**)
- `docs/features.md` — the delivery order, milestones M0–M5

---

## Layout

```
ChartPilot.sln
Directory.Build.props           # net10.0, nullable, implicit usings — every project inherits this
src/
  ChartPilot.Core/              # the whole engine. Depends only on YamlDotNet + JsonSchema.Net.
    Contracts/                  # the shared types (frozen — see "File ownership" below)
    Charts/  Values/  Manifests/  Checks/  Profiles/  Scoring/  Reporting/
  ChartPilot.Helm/              # IHelmClient: locate the binary, run `template` / `lint` safely
  ChartPilot.Api/               # Minimal API host + SPA hosting, binds 127.0.0.1 only
  ChartPilot.Cli/               # `chartpilot check`
  chartpilot-web/               # React 19 + TypeScript + Vite
tests/
  ChartPilot.Core.Tests/        # rules, parser, scoring, report snapshots — no Helm required
  ChartPilot.Helm.Tests/        # locator + argument construction against a fake process runner
  ChartPilot.Api.Tests/         # endpoint contract tests via WebApplicationFactory
samples/charts/                 # sample-service (helm create) + insecure-member-api (deliberately bad)
docs/
```

### `Contracts/` versus the feature folders

`docs/architecture.md` §3 assigns types to feature folders. The **shared** declarations — the
records, enums and interfaces every project compiles against — are physically collected under
`src/ChartPilot.Core/Contracts/<Area>/` while keeping exactly the namespaces the architecture
document specifies. The namespace is the contract; the folder is only a collision-avoidance device
so parallel work inside Core does not overlap.

So `RenderedResource` lives in `Contracts/Manifests/RenderedResource.cs` and its namespace is
`ChartPilot.Core.Manifests` — the same namespace the *implementation* of `ResourceGraph` uses from
`Manifests/`. Put new implementations in the feature folder; do not add to `Contracts/`.

---

## Running it

```bash
# API + SPA on http://127.0.0.1:5173
dotnet run --project src/ChartPilot.Api

# Frontend dev loop: vite on 5173 proxying /api to dotnet watch on 5080
cd src/chartpilot-web && npm install && npm run dev

# CLI
dotnet run --project src/ChartPilot.Cli -- check ./samples/charts/insecure-member-api \
  --profile sensitive-member-data-service --fail-on critical

# Everything
dotnet build ChartPilot.sln
dotnet test  ChartPilot.sln
```

Helm is required at runtime but not to build or to run the tests — every check is tested against
fixture manifests. On this machine winget put it at
`%LOCALAPPDATA%\Microsoft\WinGet\Packages\Helm.Helm_*\windows-amd64\helm.exe`, which is on the user
PATH but not always on a fresh shell's PATH. `HelmLocator` resolves configuration → PATH →
well-known install locations, and `GET /api/v1/environment` reports what it found.

---

## Rule ids

Every check has a stable id in the form `CP-<FAMILY>-<NNN>`:

| Prefix | Category | Covers |
|---|---|---|
| `CP-REL-*` | Reliability | probes, requests/limits, replica count, PodDisruptionBudget, update strategy |
| `CP-SEC-*` | Security | root, `privileged`, `runAsNonRoot`, `readOnlyRootFilesystem`, `latest` tag, inline secrets, SA token automount, NetworkPolicy, broad RBAC |
| `CP-NET-*` | Security/Operability | Istio: VirtualService/Gateway wiring, public route without AuthorizationPolicy, strict mTLS, DestinationRule, timeout/retry |
| `CP-CERT-*` | Operability | cert-manager: `renewBefore`, excessive duration, unknown issuer, dangling TLS secret |
| `CP-OBS-*` | Operability | ServiceMonitor/Prometheus annotations, standard labels, logging config |
| `CP-GOV-*` | Governance | `values.schema.json`, unpinned dependencies, ownership labels, data classification, expired suppressions, `helm lint` output |

**Ids are permanent.** Suppressions in users' `.chartpilot.yaml`, CI gating and report diffing are
all keyed on them. Never renumber, never reuse. Retire a rule by removing it; do not recycle its id.

A rule declares a *default* severity. The severity of an actual finding is resolved:
default → promoted by the profile's mandatory requirements → promoted by the data classification →
overridden by the profile's explicit `SeverityOverrides` → suppressed by `.chartpilot.yaml`.
That is why profiles are data, not code: `sandbox-service` and `sensitive-member-data-service` run
the same catalog.

Scoring, per `docs/architecture.md` §5.5:

```
categoryScore = clamp(0, 100, 100 - sum of deductions)     Critical 25, Warning 8, Info 0
overall       = 0.35*Security + 0.30*Reliability + 0.20*Operability + 0.15*Governance
```

Weights and deductions come from the profile, never from constants in code.

---

## Adding a check

1. One file in `src/ChartPilot.Core/Checks/`, implementing `IResourceCheck`. Query
   `context.Graph`, not raw YAML — "public VirtualService with no AuthorizationPolicy" is a graph
   question, and that class of finding is the reason this tool exists.
2. Set a concrete `Remediation`: the YAML to add, not a restatement of the problem.
3. Report `YamlPath` (`ManifestNavigator` builds these for you, e.g.
   `spec.template.spec.containers[0]`) — the GUI scrolls the editor to it, which is what turns a
   report into a workflow.
4. Two fixtures in `tests/ChartPilot.Core.Tests/Fixtures/`: one violating, one compliant. Both are
   plain manifest YAML; no Helm involved.

Checks must be pure: no disk, no process, no `DateTime.Now`. Anything time-dependent (suppression
expiry) is passed in.

---

## House rules

- **File ownership.** Work in this repo has been parallelised across agents. Do not edit, delete or
  reformat a file outside the area you were given. In particular, everything under
  `src/ChartPilot.Core/Contracts/`, `Directory.Build.props` and `ChartPilot.sln` are frozen
  contracts — if you need something from them, code against them; do not change them. Changing a
  shared signature breaks other people's compile, not just yours.
- **No stubs.** No `NotImplementedException`, no TODO standing in for logic. If something is out of
  scope, leave it out entirely rather than half-writing it.
- **English only** — code, comments, identifiers, UI strings, commit messages, docs.
- **No FluentAssertions** (licensing). Plain xunit asserts.
- **Public surface uses `IReadOnlyList<T>` / `IReadOnlyDictionary<K,V>`**, never arrays or mutable
  lists. Shared types are `sealed record`s.
- **Nothing is written back to the user's chart directory** except an explicit export action.
- Follow `docs/architecture.md` exactly on namespaces, type names, rule id format, API routes,
  severity resolution and the scoring formula. It is the contract between the parts.
