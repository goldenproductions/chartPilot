# ChartPilot

**Helm Chart Review & Golden Path GUI**

ChartPilot er en lokal webbaseret GUI til at forstå, konfigurere og kvalitetssikre Helm charts før deployment til Kubernetes.

> ChartPilot parser chart metadata og values, renderer Kubernetes manifests live og kører platform checks for sikkerhed, driftbarhed og governance. Målet er at gøre det let for udviklere at bruge sikre standarder og let for platform-teams at skalere golden paths uden at blive flaskehals.

## Hvad den gør

1. Vælg et Helm chart — se metadata, dependencies og values-filer
2. Redigér `values.yaml` i en YAML-editor (schema-guidet hvis `values.schema.json` findes)
3. Render manifests live med `helm template`
4. Udforsk de genererede Kubernetes-resources i et træ
5. Kør platform readiness checks (reliability, security, Istio, cert-manager, observability)
6. Få en platform score pr. kategori
7. Sammenlign values på tværs af dev/test/prod
8. Generér GitHub Actions workflow
9. Eksportér en Markdown review-rapport

## Status

Design-fase. Fuld specifikation: [`docs/chartpilot-spec.md`](docs/chartpilot-spec.md).

## Påtænkt stack

- **Frontend**: React + Vite + Monaco Editor
- **Backend**: .NET Minimal API (kører `helm template` / `helm lint`, parser manifests, evaluerer policies)
- **Bonus**: `chartpilot check` CLI til CI/CD
