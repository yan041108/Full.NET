{{- define "fullnet.loggingModeEnv" -}}
{{- if ne .Values.logging.ingress "Legacy" -}}
- name: FullNet__Logging__DeliveryMode
  value: {{ .Values.logging.ingress | quote }}
- name: FullNet__Logging__ExpectedDeliveryMode
  value: {{ .Values.logging.ingress | quote }}
{{- end }}
{{ if gt (.Values.logging.indexRouteVersion | int) 0 }}
- name: FullNet__Logging__IndexRouteVersion
  value: {{ .Values.logging.indexRouteVersion | quote }}
- name: FullNet__Logging__IndexRetentionDays
  value: {{ .Values.logging.indexRetentionDays | quote }}
{{- end -}}
{{- end -}}

{{- define "fullnet.loggingKafkaEnv" -}}
{{- if eq .Values.logging.ingress "ApplicationKafka" -}}
- name: FullNet__Logging__Kafka__BootstrapServers
  valueFrom:
    secretKeyRef:
      name: {{ .Values.logging.kafka.configurationSecretName | quote }}
      key: bootstrapServers
- name: FullNet__Logging__Kafka__GeneralTopic
  valueFrom:
    secretKeyRef:
      name: {{ .Values.logging.kafka.configurationSecretName | quote }}
      key: generalTopic
- name: FullNet__Logging__Kafka__PriorityTopic
  valueFrom:
    secretKeyRef:
      name: {{ .Values.logging.kafka.configurationSecretName | quote }}
      key: priorityTopic
- name: FullNet__Logging__Kafka__SecurityProtocol
  valueFrom:
    secretKeyRef:
      name: {{ .Values.logging.kafka.configurationSecretName | quote }}
      key: securityProtocol
- name: FullNet__Logging__Kafka__SaslMechanism
  valueFrom:
    secretKeyRef:
      name: {{ .Values.logging.kafka.configurationSecretName | quote }}
      key: saslMechanism
      optional: true
- name: FullNet__Logging__Kafka__SaslUsername
  valueFrom:
    secretKeyRef:
      name: {{ .Values.logging.kafka.configurationSecretName | quote }}
      key: saslUsername
      optional: true
- name: FullNet__Logging__Kafka__SaslPassword
  valueFrom:
    secretKeyRef:
      name: {{ .Values.logging.kafka.configurationSecretName | quote }}
      key: saslPassword
      optional: true
{{- if .Values.logging.kafka.caSecretName }}
- name: FullNet__Logging__Kafka__SslCaLocation
  value: "/var/run/fullnet/logging/kafka-ca/ca.crt"
{{- end }}
{{- end -}}
{{- end -}}

{{- define "fullnet.loggingKafkaCaMount" -}}
{{- if and (eq .Values.logging.ingress "ApplicationKafka") .Values.logging.kafka.caSecretName -}}
- name: kafka-log-ca
  mountPath: /var/run/fullnet/logging/kafka-ca
  readOnly: true
{{- end -}}
{{- end -}}

{{- define "fullnet.loggingKafkaCaVolume" -}}
{{- if and (eq .Values.logging.ingress "ApplicationKafka") .Values.logging.kafka.caSecretName -}}
- name: kafka-log-ca
  secret:
    secretName: {{ .Values.logging.kafka.caSecretName | quote }}
    items:
      - key: ca.crt
        path: ca.crt
{{- end -}}
{{- end -}}
