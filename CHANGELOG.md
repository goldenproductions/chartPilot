# Changelog

All notable changes to ChartPilot are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project intends to follow
[semantic versioning](https://semver.org/) once it is released.

## [Unreleased]

Everything so far. There is no released version yet; `main` is the only supported branch.

### Added

- **Chart review pipeline** — `helm template` render, multi-document manifest parsing, and a
  resource graph with cross-resource edges (VirtualService→Service→Gateway, Certificate→Issuer→
  Secret, workload→ServiceAccount/NetworkPolicy).
- **51 checks** across six families: `CP-SEC` (14), `CP-REL` (10), `CP-NET` (8), `CP-GOV` (8),
  `CP-OBS` (6), `CP-CERT` (5).
- **7 golden path profiles** with severity promotion, data-classification awareness, and
  `.chartpilot.yaml` suppressions that require a reason and expire.
- **Per-category scoring** — security, reliability, operability, governance.
- **Web GUI** — React + Vite + Monaco: chart overview, resource tree, values editor with live
  debounced re-render, findings with click-to-navigate, score card, environment diff, and export of
  values, a Markdown report and a GitHub Actions workflow.
- **CLI** — `chartpilot check` with `--profile`, `--values`, `--report`, `--workflow`, `--fail-on`
  and `--json`, plus `chartpilot profiles` and `chartpilot checks`. Exit codes 0/1/2.
- **Finding guidance** — every rule ships a plain-language explanation and two to four options, each
  with its YAML and its trade-off. Available in the GUI, under `--explain` in the CLI, and as an
  appendix in the Markdown report. Authored and shipped with the binary: no network, no API key.
- **Severity reasons** — derived from the profile table, so a finding can say why *it* is Critical
  for *this* review.
- **Chart folder browser** — `GET /api/v1/browse`, confined to the allowlist root.
- **CI** — build, test, frontend build, and a gate on ChartPilot's own output against the sample
  charts.

### Fixed

- Viewing the rendered manifest silently overwrote the values draft with the manifest. Monaco raises
  a content-change event when its model is swapped, which was indistinguishable from the user
  typing; a change is now only accepted when the editor's model matches the document it was asked to
  show.
- The API bound port 5173, which is the Vite dev server's port and is declared `strictPort`, so
  starting the API stopped the frontend dev server from starting at all. The API now listens on 5080
  in every environment.
- A relative chart path is resolved against the allowlist root, which is what the GUI's hint
  promises; charts outside the root are rejected at open time with an actionable message rather than
  failing later inside the Helm client.
