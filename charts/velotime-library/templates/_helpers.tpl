{{/*
Expand the name of the chart.
*/}}
{{- define "velotime.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" }}
{{- end }}

{{/*
Create a default fully qualified app name.
We truncate at 63 chars because some Kubernetes name fields are limited to this (by the DNS naming spec).
If release name contains chart name it will be used as a full name.
*/}}
{{- define "velotime.fullname" -}}
{{- if .Values.fullnameOverride }}
{{- .Values.fullnameOverride | trunc 63 | trimSuffix "-" }}
{{- else }}
{{- $name := default .Chart.Name .Values.nameOverride }}
{{- if contains $name .Release.Name }}
{{- .Release.Name | trunc 63 | trimSuffix "-" }}
{{- else }}
{{- printf "%s-%s" .Release.Name $name | trunc 63 | trimSuffix "-" }}
{{- end }}
{{- end }}
{{- end }}

{{/*
Create chart name and version as used by the chart label.
*/}}
{{- define "velotime.chart" -}}
{{- printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" | trunc 63 | trimSuffix "-" }}
{{- end }}

{{/*
Common labels
*/}}
{{- define "velotime.labels" -}}
helm.sh/chart: {{ include "velotime.chart" . }}
{{ include "velotime.selectorLabels" . }}
{{- if .Chart.AppVersion }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
{{- end }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
{{- end }}

{{/*
Selector labels
*/}}
{{- define "velotime.selectorLabels" -}}
app.kubernetes.io/name: {{ include "velotime.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end }}

{{/*
Create the name of the service account to use
*/}}
{{- define "velotime.serviceAccountName" -}}
{{- if .Values.serviceAccount.create }}
{{- default (include "velotime.fullname" .) .Values.serviceAccount.name }}
{{- else }}
{{- default "default" .Values.serviceAccount.name }}
{{- end }}
{{- end }}

{{- define "velotime.connectionStringPostgres" -}}
{{- printf "Host=%s;port=%s;Username=%s;Password=%s;Database=%s" .host (.port | toString) .user .password (.name | default "velotimedb") }}
{{- end -}}

{{- define "velotime.connectionStringRedis" -}}
{{- printf "%s:%s,user=%s,password=%s,ssl=true" .host (.port | toString) .user .password }}
{{- end -}}

{{- define "velotime.moduleApiHttp" -}}
{{ $ := .root }}
{{- printf "http://module-%s-api/" .module }}
{{- end -}}

{{- define "velotime.databaseUri" -}}
{{- printf "%s://%s:%s@%s:%s/%s" (.type | default "postgresql") .user .password .host (.port | toString) (.name | default "velotimedb") }}
{{- end -}}

{{- define "velotime.redisUri" -}}
{{- printf "redis://%s:%s" .host (.port | toString) }}
{{- end -}}