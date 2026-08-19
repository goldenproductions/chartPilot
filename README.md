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

## Status

Design phase. Full specification: [`docs/chartpilot-spec.md`](docs/chartpilot-spec.md).

## Intended stack

- **Frontend**: React + Vite + Monaco Editor
- **Backend**: .NET Minimal API (runs `helm template` / `helm lint`, parses manifests, evaluates policies)
- **Bonus**: a `chartpilot check` CLI for CI/CD
