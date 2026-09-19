<script setup lang="ts">
import { ref } from 'vue';
import { useRouter } from 'vue-router';
import { ElButton, ElInput, ElMessage } from 'element-plus';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import { confirmRecoverPassword, recoverPassword } from '../api/public-auth';
import ArtLoginLeftPanel from '../framework/art-design/auth/ArtLoginLeftPanel.vue';

const router = useRouter();
const email = ref('');
const challengeId = ref('');
const challengeCode = ref('');
const newPassword = ref('');
const submitting = ref(false);

async function requestCode(): Promise<void> {
  const result = await recoverPassword(email.value);
  if (result?.challengeId) {
    challengeId.value = result.challengeId;
  }
  ElMessage.success('If the account exists, a recovery code was sent.');
}

async function submit(): Promise<void> {
  submitting.value = true;
  try {
    await confirmRecoverPassword({
      challengeId: challengeId.value,
      challengeCode: challengeCode.value,
      newPassword: newPassword.value
    });
    ElMessage.success('Password updated. Sign in with the new password.');
    await router.replace('/login');
  } catch (error: unknown) {
    ElMessage.error(isFullNetProblemDetails(error) ? error.title : 'Recovery failed.');
  } finally {
    submitting.value = false;
  }
}
</script>

<template>
  <div class="art-login-page">
    <ArtLoginLeftPanel />
    <section class="recover-form">
      <h1>Recover password</h1>
      <ElInput v-model="email" type="email" placeholder="Email" />
      <ElInput v-model="challengeCode" placeholder="Recovery code" />
      <ElInput v-model="newPassword" type="password" placeholder="New password" show-password />
      <div class="actions">
        <ElButton @click="requestCode">Send code</ElButton>
        <ElButton type="primary" :loading="submitting" @click="submit">Update password</ElButton>
        <ElButton link @click="router.replace('/login')">Back to sign in</ElButton>
      </div>
    </section>
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
