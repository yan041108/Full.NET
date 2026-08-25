{{/*
Full.NET Helm helpers：命名、校验与安全默认值。
*/}}

{{- define "fullnet.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{- define "fullnet.fullname" -}}
{{- if .Values.fullnameOverride -}}
{{- .Values.fullnameOverride | trunc 63 | trimSuffix "-" -}}
{{- else -}}
{{- printf "%s-%s" .Release.Name (include "fullnet.name" .) | trunc 63 | trimSuffix "-" -}}
{{- end -}}
{{- end -}}

{{- define "fullnet.labels" -}}
app.kubernetes.io/name: {{ include "fullnet.name" . }}
helm.sh/chart: {{ printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" }}
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
app.kubernetes.io/part-of: fullnet
{{- end -}}

{{- define "fullnet.selectorLabels" -}}
app.kubernetes.io/name: {{ include "fullnet.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end -}}

{{- define "fullnet.serviceAccountName" -}}
{{- if .Values.serviceAccount.create -}}
{{- default (include "fullnet.fullname" .) .Values.serviceAccount.name -}}
{{- else -}}
{{- default "default" .Values.serviceAccount.name -}}
{{- end -}}
{{- end -}}

{{- define "fullnet.image" -}}
{{- $role := .role -}}
{{- $root := .root -}}
{{- $registry := $root.Values.image.registry -}}
{{- $repo := printf "%s-%s" $root.Values.image.repository $role -}}
{{- if $registry -}}
{{- printf "%s/%s:%s" $registry $repo $root.Values.image.tag -}}
{{- else -}}
{{- printf "%s:%s" $repo $root.Values.image.tag -}}
{{- end -}}
{{- end -}}

{{- define "fullnet.roleCount" -}}
{{- $count := 0 -}}
{{- if .Values.roles.api }}{{ $count = add $count 1 }}{{ end -}}
{{- if .Values.roles.worker }}{{ $count = add $count 1 }}{{ end -}}
{{- if .Values.roles.migrator }}{{ $count = add $count 1 }}{{ end -}}
{{- $count -}}
{{- end -}}

{{- define "fullnet.connectionBudgetNeeded" -}}
{{- $api := mul (.Values.api.hpa.maxReplicas | int) (.Values.databaseConnectionBudget.apiMaxPoolSize | int) -}}
{{- $worker := mul (.Values.worker.hpa.maxReplicas | int) (.Values.databaseConnectionBudget.workerMaxPoolSize | int) -}}
{{- $reserve := .Values.databaseConnectionBudget.migrationReserve | int -}}
{{- add $api (add $worker $reserve) -}}
{{- end -}}

{{- define "fullnet.validate" -}}
{{- $roleCount := include "fullnet.roleCount" . | int -}}
{{- if .Values.production -}}
  {{- if ne $roleCount 1 -}}
    {{- fail "production Full.NET releases must enable exactly one role (api|worker|migrator); use three independent releases." -}}
  {{- end -}}
  {{- if not .Values.edgeProtection.declared -}}
    {{- fail "production requires edgeProtection.declared=true with an external CDN/WAF/API Gateway or distributed rate-limit service." -}}
  {{- end -}}
  {{- if or (le (.Values.edgeProtection.globalRateLimitPerSecond | int) 0) (le (.Values.edgeProtection.globalBurst | int) 0) (le (.Values.edgeProtection.globalConcurrentConnections | int) 0) -}}
    {{- fail "production edgeProtection must set positive globalRateLimitPerSecond, globalBurst, and globalConcurrentConnections." -}}
  {{- end -}}
  {{- if not (or (eq .Values.edgeProtection.unavailablePolicy "fail-closed") (eq .Values.edgeProtection.unavailablePolicy "fail-open")) -}}
    {{- fail "edgeProtection.unavailablePolicy must be fail-closed or fail-open." -}}
  {{- end -}}
  {{- $perReplica := .Values.applicationRateLimiting.globalApiPermitLimitPerMinutePerReplica | int -}}
  {{- $scaled := mul $perReplica (.Values.api.hpa.maxReplicas | int) -}}
  {{- $globalPerMinute := mul (.Values.edgeProtection.globalRateLimitPerSecond | int) 60 -}}
  {{- if gt $scaled $globalPerMinute -}}
    {{- fail (printf "applicationRateLimiting scaled to api maxReplicas (%d) exceeds edge global rate budget (%d/min)." $scaled $globalPerMinute) -}}
  {{- end -}}
{{- end -}}

{{- $needed := include "fullnet.connectionBudgetNeeded" . | int -}}
{{- $budget := .Values.databaseConnectionBudget.total | int -}}
{{- if gt $needed $budget -}}
  {{- fail (printf "database connection budget exceeded: need %d but total=%d (apiMaxReplicas*apiMaxPoolSize + workerMaxReplicas*workerMaxPoolSize + migrationReserve)." $needed $budget) -}}
{{- end -}}

{{- $apiProcessNeeded := add (.Values.databaseConnectionBudget.apiPermitLimit | int) (.Values.databaseConnectionBudget.healthReserve | int) -}}
{{- if gt $apiProcessNeeded (.Values.databaseConnectionBudget.apiMaxPoolSize | int) -}}
  {{- fail (printf "api database admission plus health reserve requires %d connections but apiMaxPoolSize=%d." $apiProcessNeeded (.Values.databaseConnectionBudget.apiMaxPoolSize | int)) -}}
{{- end -}}
{{- $workerProcessNeeded := add (.Values.databaseConnectionBudget.workerPermitLimit | int) (add (.Values.databaseConnectionBudget.healthReserve | int) (.Values.databaseConnectionBudget.workerCriticalReserve | int)) -}}
{{- if gt $workerProcessNeeded (.Values.databaseConnectionBudget.workerMaxPoolSize | int) -}}
  {{- fail (printf "worker database admission plus health/critical reserves requires %d connections but workerMaxPoolSize=%d." $workerProcessNeeded (.Values.databaseConnectionBudget.workerMaxPoolSize | int)) -}}
{{- end -}}

{{- if and .Values.api.hpa.customMetrics.enabled (not .Values.api.hpa.customMetrics.adapterInstalledAndVerified) -}}
  {{- fail "api.hpa.customMetrics requires adapterInstalledAndVerified=true with a verified Metrics Adapter query/metricName." -}}
{{- end -}}
{{- if and .Values.worker.hpa.customMetrics.enabled (not .Values.worker.hpa.customMetrics.adapterInstalledAndVerified) -}}
  {{- fail "worker.hpa.customMetrics requires adapterInstalledAndVerified=true with a verified Metrics Adapter query/metricName." -}}
{{- end -}}

{{- if .Values.api.kafkaReplay.enabled -}}
  {{- if not .Values.roles.api -}}
    {{- fail "api.kafkaReplay.enabled=true is valid only for the API role release." -}}
  {{- end -}}
  {{- if eq .Values.api.kafkaReplay.bootstrapSecretName "" -}}
    {{- fail "api.kafkaReplay.bootstrapSecretName is required when API Kafka replay is enabled." -}}
  {{- end -}}
  {{- if eq .Values.api.kafkaReplay.securityProtocol "SaslSsl" -}}
    {{- if or (eq .Values.api.kafkaReplay.saslMechanism "") (eq .Values.api.kafkaReplay.saslUsernameSecretName "") (eq .Values.api.kafkaReplay.saslPasswordSecretName "") -}}
      {{- fail "api.kafkaReplay SaslSsl requires saslMechanism and username/password Secret names." -}}
    {{- end -}}
  {{- end -}}
{{- end -}}

{{- $affinityOptional := and (eq .Values.realtime.transportMode "WebSocketsOnly") .Values.realtime.skipNegotiation -}}
{{- if and (not .Values.realtime.requireSessionAffinity) (not $affinityOptional) -}}
  {{- fail "realtime.requireSessionAffinity may be false only when transportMode=WebSocketsOnly and skipNegotiation=true." -}}
{{- end -}}
{{- if and .Values.realtime.skipNegotiation (ne .Values.realtime.transportMode "WebSocketsOnly") -}}
  {{- fail "realtime.skipNegotiation requires transportMode=WebSocketsOnly." -}}
{{- end -}}

{{- if or .Values.roles.api .Values.roles.worker -}}
  {{- if and (eq .Values.dataProtection.existingClaimName "") (not .Values.dataProtection.persistence.create) -}}
    {{- fail "API/Worker require dataProtection.existingClaimName or persistence.create with a verified RWX StorageClass." -}}
  {{- end -}}
  {{- if and .Values.dataProtection.persistence.create (not .Values.dataProtection.persistence.storageClassVerifiedRwxSnapshotBackup) -}}
    {{- fail "creating a Data Protection PVC requires persistence.storageClassVerifiedRwxSnapshotBackup=true." -}}
  {{- end -}}
{{- end -}}

{{- if ne (.Values.worker.maxConcurrency | int) 1 -}}
  {{- if .Values.production -}}
    {{- fail "production worker.maxConcurrency must remain 1." -}}
  {{- end -}}
{{- end -}}

{{- if and .Values.roles.worker (eq .Values.worker.messaging.mode "CdcKafka") -}}
  {{- if eq .Values.worker.messaging.kafka.bootstrapSecretName "" -}}
    {{- fail "worker.messaging.kafka.bootstrapSecretName is required when worker.messaging.mode is CdcKafka." -}}
  {{- end -}}
  {{- if and (eq .Values.worker.messaging.kafka.consumerGroupProtocol "Consumer") (lt (.Values.worker.messaging.kafka.brokerMajorVersion | int) 4) -}}
    {{- fail "worker.messaging.kafka.brokerMajorVersion must be at least 4 when consumerGroupProtocol is Consumer." -}}
  {{- end -}}
  {{- if and (eq .Values.worker.messaging.kafka.classicPartitionAssignment "CooperativeSticky") (not .Values.worker.messaging.kafka.cooperativeStickyMigrationCompleted) -}}
    {{- fail "worker.messaging.kafka.cooperativeStickyMigrationCompleted=true is required before enabling CooperativeSticky." -}}
  {{- end -}}
  {{- if lt (.Values.worker.messaging.kafka.producerQueueMaxKbytes | int) 1024 -}}
    {{- fail "worker.messaging.kafka.producerQueueMaxKbytes must be at least 1024 KiB." -}}
  {{- end -}}
  {{- if ge (.Values.worker.messaging.kafka.consumerBufferLowWatermark | int) (.Values.worker.messaging.kafka.consumerBufferHighWatermark | int) -}}
    {{- fail "worker.messaging.kafka.consumerBufferLowWatermark must be less than consumerBufferHighWatermark." -}}
  {{- end -}}
  {{- if ge (.Values.worker.messaging.kafka.partitionBufferLowWatermark | int) (.Values.worker.messaging.kafka.partitionBufferHighWatermark | int) -}}
    {{- fail "worker.messaging.kafka.partitionBufferLowWatermark must be less than partitionBufferHighWatermark." -}}
  {{- end -}}
  {{- if gt (.Values.worker.messaging.kafka.partitionKeyConcurrencySlots | int) (.Values.worker.messaging.kafka.partitionBufferHighWatermark | int) -}}
    {{- fail "worker.messaging.kafka.partitionKeyConcurrencySlots cannot exceed partitionBufferHighWatermark." -}}
  {{- end -}}
  {{- if and .Values.production (eq .Values.worker.messaging.kafka.offsetCommitMode "PeriodicWatermark") (not .Values.worker.messaging.kafka.periodicOffsetCommitVerified) -}}
    {{- fail "worker.messaging.kafka.periodicOffsetCommitVerified=true is required before PeriodicWatermark in production." -}}
  {{- end -}}
{{- end -}}

{{- $codegenEnabled := or .Values.codeGeneration.apply.enabled (and .Values.production .Values.codeGeneration.apply.enabledWhenProduction) -}}
{{- if and $codegenEnabled (or .Values.roles.api .Values.roles.worker) -}}
  {{- if and (eq .Values.codeGeneration.workspace.existingClaimName "") (not .Values.codeGeneration.workspace.persistence.create) -}}
    {{- fail "CodeGeneration Apply requires codeGeneration.workspace.existingClaimName or workspace.persistence.create with storageClassVerifiedRwxSnapshotBackup=true." -}}
  {{- end -}}
  {{- if and .Values.codeGeneration.workspace.persistence.create (not .Values.codeGeneration.workspace.persistence.storageClassVerifiedRwxSnapshotBackup) -}}
    {{- fail "creating a CodeGeneration workspace PVC requires workspace.persistence.storageClassVerifiedRwxSnapshotBackup=true." -}}
  {{- end -}}
{{- end -}}
{{- end -}}

{{- define "fullnet.sessionAffinityEnabled" -}}
{{- if and (eq .Values.realtime.transportMode "WebSocketsOnly") .Values.realtime.skipNegotiation -}}
{{- .Values.realtime.requireSessionAffinity -}}
{{- else -}}
true
{{- end -}}
{{- end -}}

{{- define "fullnet.codeGenerationApplyEnabled" -}}
{{- if .Values.codeGeneration.apply.enabled -}}true{{- else if and .Values.production .Values.codeGeneration.apply.enabledWhenProduction -}}true{{- else -}}false{{- end -}}
{{- end -}}

{{- define "fullnet.codeGenerationWorkspaceClaimName" -}}
{{- if .Values.codeGeneration.workspace.existingClaimName -}}
{{- .Values.codeGeneration.workspace.existingClaimName -}}
{{- else -}}
{{- include "fullnet.fullname" . }}-codegeneration-workspace{{- end -}}
{{- end -}}
