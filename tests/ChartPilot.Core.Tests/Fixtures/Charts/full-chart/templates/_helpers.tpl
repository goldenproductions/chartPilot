{{/*
Helper definitions only. The scanner must report no kinds for this file,
even though the word "kind" appears below.
*/}}
{{- define "fixture-api.name" -}}
{{- .Chart.Name | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{- define "fixture-api.labels" -}}
app.kubernetes.io/name: {{ include "fixture-api.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end -}}
