<script setup lang="ts">
import { onActivated, onBeforeUnmount, onDeactivated, onMounted, ref, watch } from 'vue';
import { ElButton, ElCard, ElForm, ElFormItem, ElInput } from 'element-plus';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { beginTotpEnrollment, confirmTotpEnrollment, getTotpEnrollmentStatus, type BeginTotpEnrollmentResponse, type TotpEnrollmentStatus } from '../api/totpEnrollment';
import { showProblem, showSuccess, showWarning } from '../feedback/fullNetMessage';

const props = defineProps<{ disabled?: boolean }>();
const emit = defineEmits<{ busy: [value: boolean] }>();
const session = useSessionStore();
const { t } = useAdminI18n();
const status = ref<TotpEnrollmentStatus | null>(null);
const material = ref<BeginTotpEnrollmentResponse | null>(null);
const code = ref('');
const busy = ref(false);
let generation = 0;
let controller: AbortController | undefined;
let disposed = false;
let inactive = false;

function setBusy(value: boolean): void { busy.value = value; emit('busy', value); }
function invalidate(): void {
  generation++; controller?.abort(); controller = undefined;
  status.value = null; material.value = null; code.value = ''; setBusy(false);
}
function current(ticket: number): boolean { return !disposed && !inactive && ticket === generation; }
async function perform(action: 'status' | 'begin' | 'confirm'): Promise<void> {
  if (disposed || inactive || busy.value || props.disabled || !session.currentUser) return;
  if (action === 'begin' && (!status.value || status.value.isEnabled)) return;
  if (action === 'confirm') {
    if (!material.value) return;
    if (!/^[0-9]{6}$/.test(code.value)) { showWarning(t('totpEnrollment.codeInvalid')); return; }
  }
  const ticket = ++generation;
  controller = new AbortController();
  const signal = controller.signal;
  setBusy(true);
  try {
    if (action === 'status') {
      material.value = null; code.value = '';
      const result = await getTotpEnrollmentStatus(signal);
      if (current(ticket)) status.value = result;
    } else if (action === 'begin') {
      material.value = null; code.value = '';
      const result = await beginTotpEnrollment(signal);
      if (current(ticket)) { material.value = result; status.value = { isEnrolled: true, isEnabled: false }; }
    } else {
      const result = await confirmTotpEnrollment(code.value, signal);
      if (!result.isEnabled) throw new Error('client.invalid_totp_confirm');
      if (current(ticket)) {
        status.value = result; material.value = null; code.value = '';
        showSuccess(t('totpEnrollment.confirmSuccess'));
      }
    }
  } catch (error: unknown) {
    if (current(ticket)) {
      showProblem(error, t('totpEnrollment.operationFailed'));
      // 登记冲突需重新读取权威状态，避免页面仍展示已被替换的密钥。
      if (typeof error === 'object' && error !== null && 'status' in error && error.status === 409) {
        material.value = null; code.value = ''; status.value = null;
        try {
          const result = await getTotpEnrollmentStatus(signal);
          if (current(ticket)) status.value = result;
        } catch { /* 状态未知时保留显式刷新入口。 */ }
      }
    }
  } finally {
    if (current(ticket)) { controller = undefined; setBusy(false); }
  }
}

watch([() => session.currentUser?.id, () => session.currentUser?.sessionId], () => {
  invalidate();
  if (!disposed && !inactive) void perform('status');
}, { flush: 'sync' });
onMounted(() => { void perform('status'); });
onBeforeUnmount(() => { disposed = true; invalidate(); });
onDeactivated(() => { inactive = true; invalidate(); });
onActivated(() => { if (inactive) { inactive = false; void perform('status'); } });
</script>

<template>
  <el-card shadow="never" class="totp-enrollment-card">
    <template #header>
      <h2>{{ t('totpEnrollment.title') }}</h2>
      <p>{{ t('totpEnrollment.subtitle') }}</p>
    </template>
    <p role="status">{{ status?.isEnabled ? t('totpEnrollment.enabled') : status ? t('totpEnrollment.notEnabled') : t('totpEnrollment.statusUnknown') }}</p>
    <el-button :loading="busy" :disabled="disabled" @click="perform('status')">{{ t('totpEnrollment.refresh') }}</el-button>
    <el-button v-if="status && !status.isEnabled && !material" type="primary" :disabled="busy || disabled" @click="perform('begin')">{{ t('totpEnrollment.begin') }}</el-button>
    <template v-if="material">
      <p>{{ t('totpEnrollment.setupHint') }}</p>
      <el-form label-position="top" @submit.prevent="perform('confirm')">
        <el-form-item :label="t('totpEnrollment.secret')">
          <el-input :model-value="material.sharedSecretBase32" readonly autocomplete="off" />
        </el-form-item>
        <el-form-item :label="t('totpEnrollment.uri')">
          <el-input :model-value="material.otpAuthUri" type="textarea" readonly autocomplete="off" />
        </el-form-item>
        <el-form-item :label="t('totpEnrollment.code')" required>
          <el-input v-model="code" :disabled="busy || disabled" inputmode="numeric" maxlength="6" autocomplete="one-time-code" />
        </el-form-item>
        <el-button native-type="submit" type="primary" :loading="busy" :disabled="disabled">{{ t('totpEnrollment.confirm') }}</el-button>
        <el-button :disabled="busy || disabled" @click="material = null; code = ''">{{ t('totpEnrollment.hide') }}</el-button>
      </el-form>
    </template>
  </el-card>
</template>

<style scoped>
.totp-enrollment-card { width: min(760px, 100%); }
h2 { margin: 0; font-size: 18px; }
p { font-size: 13px; overflow-wrap: anywhere; }
</style>
