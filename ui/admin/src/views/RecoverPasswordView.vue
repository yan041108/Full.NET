<script setup lang="ts">
import { onBeforeUnmount, ref, watch } from 'vue';
import { useRouter } from 'vue-router';
import { ElButton, ElInput } from 'element-plus';
import { showProblem, showSuccess, showWarning } from '../feedback/fullNetMessage';
import { confirmRecoverPassword, recoverPassword } from '../api/public-auth';
import { useAdminI18n } from '../i18n/adminI18n';
import { isIdentityPasswordValid } from '../auth/identity-password-policy';
import LocaleSelector from '../i18n/LocaleSelector.vue';
import ArtLoginLeftPanel from '../framework/art-design/auth/ArtLoginLeftPanel.vue';

const router = useRouter();
const { t } = useAdminI18n();
const email = ref('');
const challengeId = ref('');
const challengeCode = ref('');
const newPassword = ref('');
const submitting = ref(false);
const requesting = ref(false);
let disposed = false;
let emailRevision = 0;
onBeforeUnmount(() => { disposed = true; });
// 目标变化后旧挑战不再代表当前输入；迟到响应也不能把旧目标重新带回页面。
watch(email, () => { emailRevision++; challengeId.value = ''; challengeCode.value = ''; }, { flush: 'sync' });

async function requestCode(): Promise<void> {
  if (disposed || requesting.value || submitting.value) return;
  const target = email.value.trim();
  if (!target) { showWarning(t('accountChallenges.emailRequired')); return; }
  const revision = emailRevision;
  requesting.value = true;
  challengeId.value = ''; challengeCode.value = '';
  try {
    const result = await recoverPassword(target);
    if (disposed || emailRevision !== revision || email.value.trim() !== target) return;
    challengeId.value = result.challengeId;
    showSuccess(t('accountChallenges.recoveryAccepted'));
  } catch (error: unknown) {
    if (!disposed && emailRevision === revision && email.value.trim() === target) showProblem(error, t('accountChallenges.requestFailed'));
  } finally { requesting.value = false; }
}

async function submit(): Promise<void> {
  if (disposed || requesting.value || submitting.value) return;
  if (!challengeId.value || !challengeCode.value.trim() || !newPassword.value) {
    showWarning(t('accountChallenges.verificationRequired')); return;
  }
  if (!isIdentityPasswordValid(newPassword.value)) { showWarning(t('securitySettings.passwordInvalid')); return; }
  submitting.value = true;
  try {
    await confirmRecoverPassword({
      challengeId: challengeId.value,
      challengeCode: challengeCode.value,
      newPassword: newPassword.value
    });
    if (disposed) return;
    challengeCode.value = ''; newPassword.value = ''; challengeId.value = '';
    showSuccess(t('accountChallenges.passwordUpdated'));
    await router.replace('/login');
  } catch (error: unknown) {
    if (!disposed) showProblem(error, t('accountChallenges.recoveryFailed'));
  } finally {
    submitting.value = false;
  }
}
</script>

<template>
  <div class="art-login-page">
    <ArtLoginLeftPanel />
    <form class="recover-form" aria-labelledby="recover-title" @submit.prevent="submit">
      <LocaleSelector id="recover-locale" compact />
      <h1 id="recover-title">{{ t('accountChallenges.recoveryTitle') }}</h1>
      <ElInput v-model="email" name="email" type="email" autocomplete="email" :disabled="submitting" :placeholder="t('accountChallenges.email')" :aria-label="t('accountChallenges.email')" />
      <ElInput v-model="challengeCode" name="challengeCode" autocomplete="one-time-code" inputmode="numeric" :disabled="requesting || submitting" :placeholder="t('accountChallenges.recoveryCode')" :aria-label="t('accountChallenges.recoveryCode')" />
      <ElInput v-model="newPassword" name="newPassword" type="password" autocomplete="new-password" :disabled="submitting" :placeholder="t('accountChallenges.newPassword')" :aria-label="t('accountChallenges.newPassword')" show-password />
      <div class="actions">
        <ElButton :loading="requesting" :disabled="submitting" @click="requestCode">{{ t('accountChallenges.sendCode') }}</ElButton>
        <ElButton type="primary" :loading="submitting" :disabled="requesting" native-type="submit">{{ t('accountChallenges.updatePassword') }}</ElButton>
        <ElButton link @click="router.replace('/login')">{{ t('accountChallenges.backToSignIn') }}</ElButton>
      </div>
    </form>
  </div>
</template>

<style scoped>
.recover-form {
  display: flex;
  flex-direction: column;
  gap: 12px;
  width: min(420px, 100%);
}

.actions {
  display: flex;
  gap: 8px;
  flex-wrap: wrap;
}
</style>
