{{/*
Expand the name of the chart.
*/}}
{{- define "member-api.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{/*
Fully qualified app name.
*/}}
{{- define "member-api.fullname" -}}
{{- if .Values.fullnameOverride -}}
{{- .Values.fullnameOverride | trunc 63 | trimSuffix "-" -}}
{{- else -}}
{{- $name := default .Chart.Name .Values.nameOverride -}}
{{- if contains $name .Release.Name -}}
{{- .Release.Name | trunc 63 | trimSuffix "-" -}}
{{- else -}}
{{- printf "%s-%s" .Release.Name $name | trunc 63 | trimSuffix "-" -}}
{{- end -}}
{{- end -}}
{{- end -}}

{{/*
Chart name and version, as used by app.kubernetes.io/managed-by consumers.
*/}}
{{- define "member-api.chart" -}}
{{- printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{/*
Standard labels. Every resource in this chart carries them.
*/}}
{{- define "member-api.labels" -}}
helm.sh/chart: {{ include "member-api.chart" . }}
app.kubernetes.io/name: {{ include "member-api.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
app.kubernetes.io/part-of: {{ .Values.platform.partOf | default .Values.platform.team }}
app.kubernetes.io/component: api
platform.example.com/team: {{ .Values.platform.team }}
platform.example.com/data-classification: {{ .Values.platform.dataClassification }}
platform.example.com/exposure: {{ .Values.platform.exposure }}
{{- end -}}

{{/*
Selector labels. Stable across upgrades, so they never include the version.
*/}}
{{- define "member-api.selectorLabels" -}}
app.kubernetes.io/name: {{ include "member-api.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end -}}

{{/*
Name of the service account to use.
*/}}
{{- define "member-api.serviceAccountName" -}}
{{- if .Values.serviceAccount.create -}}
{{- default (include "member-api.fullname" .) .Values.serviceAccount.name -}}
{{- else -}}
{{- default "default" .Values.serviceAccount.name -}}
{{- end -}}
{{- end -}}
