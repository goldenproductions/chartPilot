{{- define "batch-report.name" -}}
{{- .Chart.Name | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{- define "batch-report.fullname" -}}
{{- if contains .Chart.Name .Release.Name -}}
{{- .Release.Name | trunc 63 | trimSuffix "-" -}}
{{- else -}}
{{- printf "%s-%s" .Release.Name .Chart.Name | trunc 63 | trimSuffix "-" -}}
{{- end -}}
{{- end -}}

{{- define "batch-report.chart" -}}
{{- printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{- define "batch-report.labels" -}}
helm.sh/chart: {{ include "batch-report.chart" . }}
app.kubernetes.io/name: {{ include "batch-report.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
app.kubernetes.io/part-of: {{ .Values.platform.partOf | default .Values.platform.team }}
app.kubernetes.io/component: batch
platform.example.com/team: {{ .Values.platform.team }}
platform.example.com/data-classification: {{ .Values.platform.dataClassification }}
{{- end -}}

{{- define "batch-report.selectorLabels" -}}
app.kubernetes.io/name: {{ include "batch-report.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end -}}

{{- define "batch-report.serviceAccountName" -}}
{{- if .Values.serviceAccount.create -}}
{{- default (include "batch-report.fullname" .) .Values.serviceAccount.name -}}
{{- else -}}
{{- default "default" .Values.serviceAccount.name -}}
{{- end -}}
{{- end -}}
