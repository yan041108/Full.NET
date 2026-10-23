<script setup lang="ts">
import { computed, reactive, watch } from 'vue';
import { ElButton, ElCheckbox, ElForm, ElFormItem, ElInput, ElSwitch } from 'element-plus';
import { useAdminI18n } from '../i18n/adminI18n';
import { showWarning } from '../feedback/fullNetMessage';

/** 编辑器只处理表单状态；用户、租户与 HTTP 契约由上层可信 API 边界负责。 */
const props = defineProps<{
  channelKey: string;
  optedOut: boolean;
  marketingConsent: boolean;
  quietHoursJson: string | null;
  canUpdate: boolean;
  saving: boolean;
}>();
const emit = defineEmits<{
  save: [value: { optedOut: boolean; marketingConsent: boolean; quietHoursJson: string | null }];
}>();
const { t } = useAdminI18n();
const form = reactive({
  enabled: true,
  marketingConsent: false,
  quiet: false,
  timeZone: '',
  start: '22:00',
  end: '08:00'
});
const invalidStoredQuietHours = computed(() => {
  if (!props.quietHoursJson) return false;
  try {
    const value: unknown = JSON.parse(props.quietHoursJson);
    if (!isQuietHours(value)) return true;
    const time = /^([01]\d|2[0-3]):[0-5]\d$/;
    if (!time.test(value.start) || !time.test(value.end) || value.start === value.end) return true;
    new Intl.DateTimeFormat('en', { timeZone: value.timeZone }).format();
    return false;
  } catch {
    return true;
  }
});

function isQuietHours(value: unknown): value is { timeZone: string; start: string; end: string } {
  return value !== null && typeof value === 'object'
    && 'timeZone' in value && typeof value.timeZone === 'string'
    && 'start' in value && typeof value.start === 'string'
    && 'end' in value && typeof value.end === 'string';
}

watch(() => [props.optedOut, props.marketingConsent, props.quietHoursJson], () => {
  form.enabled = !props.optedOut;
  form.marketingConsent = props.marketingConsent;
  form.quiet = props.quietHoursJson !== null;
  form.timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone;
  form.start = '22:00';
  form.end = '08:00';
  if (props.quietHoursJson) {
    try {
      const value: unknown = JSON.parse(props.quietHoursJson);
      if (isQuietHours(value)) {
        form.timeZone = value.timeZone;
        form.start = value.start;
        form.end = value.end;
      }
    } catch {
      // 损坏的持久化值禁止静默覆盖，等待重新加载或服务端修复。
    }
  }
}, { immediate: true });

function save(): void {
  if (!props.canUpdate || props.saving || invalidStoredQuietHours.value) return;
  if (form.quiet) {
    const time = /^([01]\d|2[0-3]):[0-5]\d$/;
    let validZone = true;
    try {
      new Intl.DateTimeFormat('en', { timeZone: form.timeZone.trim() }).format();
    } catch {
      validZone = false;
    }
    if (!validZone || !time.test(form.start) || !time.test(form.end) || form.start === form.end) {
      showWarning(t('notificationSendingPreferences.invalidQuietHours'));
      return;
    }
  }
  emit('save', {
    optedOut: !form.enabled,
    marketingConsent: form.marketingConsent,
    quietHoursJson: form.quiet
      ? JSON.stringify({ timeZone: form.timeZone.trim(), start: form.start, end: form.end })
      : null
  });
}
</script>

<template>
  <ElForm label-position="top" :disabled="!canUpdate || saving || invalidStoredQuietHours" @submit.prevent="save">
    <h2 translate="no">{{ channelKey }}</h2>
    <ElFormItem :label="t('notificationSendingPreferences.enabled')">
      <ElSwitch v-model="form.enabled" data-testid="preference-enabled" />
    </ElFormItem>
    <ElFormItem>
      <ElCheckbox v-model="form.marketingConsent" data-testid="preference-consent">
        {{ t('notificationSendingPreferences.marketingConsent') }}
      </ElCheckbox>
    </ElFormItem>
    <ElFormItem :label="t('notificationSendingPreferences.quiet')">
      <ElSwitch v-model="form.quiet" data-testid="preference-quiet" />
    </ElFormItem>
    <template v-if="form.quiet">
      <ElFormItem :label="t('notificationSendingPreferences.timeZone')" required>
        <ElInput v-model="form.timeZone" data-testid="preference-timezone" />
      </ElFormItem>
      <ElFormItem :label="t('notificationSendingPreferences.start')" required>
        <ElInput v-model="form.start" type="time" data-testid="preference-start" />
      </ElFormItem>
      <ElFormItem :label="t('notificationSendingPreferences.end')" required>
        <ElInput v-model="form.end" type="time" data-testid="preference-end" />
      </ElFormItem>
    </template>
    <p v-if="invalidStoredQuietHours" role="alert">{{ t('notificationSendingPreferences.invalidStored') }}</p>
    <ElButton v-if="canUpdate" type="primary" :loading="saving" :disabled="invalidStoredQuietHours"
      data-testid="preference-save" @click="save">
      {{ t('notificationSendingPreferences.save') }}
    </ElButton>
  </ElForm>
</template>
