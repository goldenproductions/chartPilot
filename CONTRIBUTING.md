# Contributing to ChartPilot

Thanks for taking a look. This document covers how to build the project, how it is laid out, and —
most usefully — how to add a check, which is the change most contributions turn out to be.

## Prerequisites

| Tool | Why |
|---|---|
| **.NET 10 SDK** | Build and test everything |
| **Node.js 20+** | Only needed to build or develop the web frontend |
| **Helm 3 or 4** | Only needed at *runtime* to render charts — the test suite does not use it |

The whole test suite runs without Helm installed: every check is tested against fixture manifests,
and the Helm client is tested through a fake process runner. That is deliberate, and worth keeping.

## Build and test

```bash
dotnet build ChartPilot.sln
dotnet test  ChartPilot.sln

cd src/chartpilot-web
npm install
npm run build        # tsc --noEmit + vite build
```

CI runs the same commands, then gates the repository on ChartPilot's own output: the golden-path
sample chart must stay clean, and the deliberately bad sample charts must keep failing the critical
gate. A change that makes `samples/charts/member-api` score below 100 will fail the build, and that
is usually telling you something real.

## Running it

```bash
# API + GUI on http://127.0.0.1:5080  (needs src/chartpilot-web/dist to have been built)
dotnet run --project src/ChartPilot.Api

# Frontend dev loop: Vite on 5173, proxying /api to the API on 5080
cd src/chartpilot-web && npm run dev

# CLI
dotnet run --project src/ChartPilot.Cli -- check samples/charts/legacy-importer --explain
```

## Layout

```
src/ChartPilot.Core/     Domain and pipeline. No HTTP, no process, no disk beyond abstractions.
src/ChartPilot.Helm/     The only code that shells out to helm.
src/ChartPilot.Api/      Minimal API, loopback only, serves the built SPA.
src/ChartPilot.Cli/      chartpilot check / profiles / checks.
src/chartpilot-web/      React + TypeScript + Vite + Monaco.
tests/                   Fixture-driven tests; no cluster, no network.
samples/charts/          Sample charts, including deliberately bad ones CI gates on.
```

`docs/architecture.md` is the contract between these pieces. If a change contradicts it, change the
document in the same pull request — a stale architecture doc is worse than none.

## Adding a check

This is the common case, and it is deliberately small: a rule, its guidance, and two fixtures.

**1. Write the rule** in the right family file under `src/ChartPilot.Core/Checks/`:

```csharp
/// <summary>CP-SEC-015 — one sentence on what this catches.</summary>
internal sealed class MyCheck : WorkloadCheckBase
{
    public override CheckDescriptor Descriptor { get; } = new(
        "CP-SEC-015",
        "Short title in the imperative",
        CheckCategory.Security,
        Severity.Warning,
        "Why this rule exists at all - the cost of ignoring it, concretely.",
        "securityContext:\n  someField: true",
        "https://kubernetes.io/docs/...");

    public override IEnumerable<Finding> Evaluate(CheckContext context) { /* ... */ }
}
```

Rules are registered by assembly scan, so there is nothing to wire up. Keep the rule a pure
predicate over the resource graph: it must not decide its own severity, consult the profile, or
know about suppressions. The engine owns all three, which is what keeps rules testable.

**2. Write its guidance** in the matching file under `src/ChartPilot.Core/Checks/Guidance/`:

```csharp
yield return new("CP-SEC-015", new(
    "The finding restated without jargon, for someone who has not met this rule before.",
    [
        new FixOption(
            "Do the obvious thing",
            "One sentence on what this option does.",
            "securityContext:\n  someField: true",
            "When to pick it, and what it costs. An option with no trade-off is not a choice.",
            IsRecommended: true),
        new FixOption(/* a second, genuinely different approach */)
    ]));
```

`GuidanceCatalogTests` fails the build if a rule has no guidance, if a rule marks zero or several
options as recommended, or if an option omits its YAML or its trade-off. That is intentional: a
finding a reader cannot act on is noise.

**3. Add fixtures** — one manifest that violates the rule and one that satisfies it — and a test in
`tests/ChartPilot.Core.Tests/Checks/`. Prove both directions. A check that never fires and a check
that always fires are equally useless, and only the compliant fixture catches the second case.

**4. If a profile should promote it**, add the id to the requirement map in
`src/ChartPilot.Core/Profiles/SeverityResolver.cs`. Severity promotion lives in that one table so
that "why is this Critical?" stays answerable from a single place.

### Rule id families

| Prefix | Category |
|---|---|
| `CP-SEC` | Security — what the workload can do, and what an attacker can do with it |
| `CP-REL` | Reliability — probes, resources, replicas, disruption |
| `CP-NET` | Istio and mesh routing |
| `CP-CERT` | cert-manager |
| `CP-OBS` | Metrics, labels, ownership, logging, tracing |
| `CP-GOV` | Schema, pinning, classification, helm lint |

Ids are permanent. They appear in `.chartpilot.yaml` suppressions, in CI output and in review
reports, so renaming one silently breaks someone's waiver. Retire an id rather than reusing it.

## House style

- **English only**, in code, comments, commit messages and UI strings.
- **No stubs.** `NotImplementedException` and TODO placeholders do not belong in `main`; leave a
  feature out entirely rather than half-present.
- **Comments explain *why*.** The code already says what it does.
- **`ChartPilot.Core` stays pure.** If a change needs I/O there, it needs an abstraction instead.
- **Never weaken a test to make it pass.** If a test encodes a wrong expectation, correct it and say
  so in the commit message.

## Updating a snapshot

The Markdown report is snapshot-tested because it is user-facing text. When you change it
deliberately:

```bash
CHARTPILOT_UPDATE_SNAPSHOTS=1 dotnet test tests/ChartPilot.Core.Tests
git diff tests/ChartPilot.Core.Tests/Fixtures/Reporting/review-report.md
```

Read the diff before committing it. That is the entire point of the snapshot.

## Pull requests

- One concern per pull request.
- `dotnet build`, `dotnet test` and `npm run build` all green.
- Update `docs/` when behaviour changes.
- Describe what you verified, not only what you changed. "Tests pass" is weaker than "ran the CLI
  against `samples/charts/legacy-importer` and the new rule fires once, on the right container".

## Reporting a security issue

Please do not open a public issue — see [SECURITY.md](SECURITY.md).
