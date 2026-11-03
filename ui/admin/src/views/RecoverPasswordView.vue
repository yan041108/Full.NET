<script setup lang="ts">
import { ref, watch } from 'vue';
import { useRouter } from 'vue-router';
import { ElButton, ElInput, ElMessage } from 'element-plus';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import { confirmRecoverPassword, recoverPassword } from '../api/public-auth';
import ArtLoginLeftPanel from '../framework/art-design/auth/ArtLoginLeftPanel.vue';
import { useAdminI18n } from '../i18n/adminI18n';
import { accountSelfServiceText } from '../i18n/account-self-service';

const router = useRouter();
const email = ref('');
const challengeId = ref('');
const challengeCode = ref('');
const newPassword = ref('');
const submitting = ref(false);
const sending = ref(false);
const { locale } = useAdminI18n();
const text = (key: Parameters<typeof accountSelfServiceText>[1]) => accountSelfServiceText(locale.value, key);

watch(email, () => { challengeId.value = ''; challengeCode.value = ''; }, { flush: 'sync' });

async function requestCode(): Promise<void> {
  if (sending.value || submitting.value || !email.value.trim()) return;
  const requestedEmail = email.value;
  sending.value = true;
  // 发送结果未知时清除旧挑战，要求显式重发，避免误用已被服务端替换的验证码。
  challengeId.value = '';
  challengeCode.value = '';
  try {
    const result = await recoverPassword(requestedEmail);
    if (email.value !== requestedEmail) return;
    challengeId.value = result.challengeId;
    ElMessage.success(text('recoverySent'));
  } catch (error: unknown) {
    if (email.value === requestedEmail) ElMessage.error(isFullNetProblemDetails(error) ? error.title : text('sendFailed'));
  } finally {
    sending.value = false;
  }
}

async function submit(): Promise<void> {
  if (submitting.value || sending.value) return;
  if (!challengeId.value || !challengeCode.value) {
    ElMessage.error(text('required'));
    return;
  }
  submitting.value = true;
  try {
    await confirmRecoverPassword({
      challengeId: challengeId.value,
      challengeCode: challengeCode.value,
      newPassword: newPassword.value
    });
    ElMessage.success(text('recovered'));
    await router.replace('/login');
  } catch (error: unknown) {
    ElMessage.error(isFullNetProblemDetails(error) ? error.title : text('recoverFailed'));
  } finally {
    submitting.value = false;
  }
}
</script>

<template>
  <div class="art-login-page">
    <ArtLoginLeftPanel />
    <main class="art-login-page__main">
      <div class="art-login-page__form-wrap">
    <section class="art-login-form recover-form" aria-labelledby="recover-title">
      <h1 id="recover-title" class="art-login-form__title">{{ text('recoverTitle') }}</h1>
      <ElInput v-model="email" type="email" :placeholder="text('email')" :aria-label="text('email')" :disabled="submitting" />
      <ElInput v-model="challengeCode" :placeholder="text('code')" :aria-label="text('code')" :disabled="sending || submitting" />
      <ElInput v-model="newPassword" type="password" :placeholder="text('newPassword')" :aria-label="text('newPassword')" :disabled="submitting" show-password />
      <div class="actions">
        <ElButton :loading="sending" :disabled="submitting || !email.trim()" @click="requestCode">{{ text('send') }}</ElButton>
        <ElButton type="primary" :loading="submitting" :disabled="sending || !challengeId || !challengeCode" @click="submit">{{ text('recover') }}</ElButton>
        <ElButton link @click="router.replace('/login')">{{ text('back') }}</ElButton>
      </div>
    </section>
      </div>
    </main>
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
