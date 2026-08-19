{{- define "insecure-member-api.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{- define "insecure-member-api.fullname" -}}
{{- printf "%s-%s" .Release.Name (include "insecure-member-api.name" .) | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{- define "insecure-member-api.serviceAccountName" -}}
{{- if .Values.serviceAccount.create -}}
{{- default (include "insecure-member-api.fullname" .) .Values.serviceAccount.name -}}
{{- else -}}
{{- default "default" .Values.serviceAccount.name -}}
{{- end -}}
{{- end -}}

{{/*
Deliberately minimal: no app.kubernetes.io/* labels, no owner label, no version label.
*/}}
{{- define "insecure-member-api.labels" -}}
app: {{ include "insecure-member-api.name" . }}
{{- end -}}
