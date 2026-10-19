{{/* Local 投递时注入 FullNet:Logging:LocalFile；目录须与 PVC 挂载路径一致，否则进程无法获取目录租约。 */}}
{{- define "fullnet.loggingLocalFileEnv" -}}
{{- if .Values.logging.localFile.enabled -}}
- name: FullNet__Logging__LocalFile__Enabled
  value: "true"
- name: FullNet__Logging__LocalFile__Directory
  value: {{ .Values.logging.localFile.directory | quote }}
- name: FullNet__Logging__LocalFile__FileSizeLimitBytes
  value: {{ .Values.logging.localFile.fileSizeLimitBytes | quote }}
- name: FullNet__Logging__LocalFile__RetentionDays
  value: {{ .Values.logging.localFile.retentionDays | quote }}
- name: FullNet__Logging__LocalFile__MaxDirectoryBytes
  value: {{ .Values.logging.localFile.maxDirectoryBytes | quote }}
- name: FullNet__Logging__LocalFile__PriorityReservePercent
  value: {{ .Values.logging.localFile.priorityReservePercent | quote }}
- name: FullNet__Logging__LocalFile__FlushInterval
  value: {{ .Values.logging.localFile.flushInterval | quote }}
{{- end -}}
{{- end -}}

{{- define "fullnet.loggingLocalFileMount" -}}
{{- if .Values.logging.localFile.enabled -}}
- name: local-log-files
  mountPath: {{ .Values.logging.localFile.directory | quote }}
{{- end -}}
{{- end -}}

{{- define "fullnet.loggingLocalFileVolume" -}}
{{- if .Values.logging.localFile.enabled -}}
- name: local-log-files
  persistentVolumeClaim:
    claimName: {{ .Values.logging.localFile.existingClaimName | quote }}
{{- end -}}
{{- end -}}
