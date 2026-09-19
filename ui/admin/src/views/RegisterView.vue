<script setup lang="ts">
import { onMounted, ref } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { ElButton, ElInput, ElMessage } from 'element-plus';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import { registerAccount, requestEmailChallenge, verifyInvitation } from '../api/public-auth';
import ArtLoginLeftPanel from '../framework/art-design/auth/ArtLoginLeftPanel.vue';

const RegistrationEmailVerification = 1;
const InvitationEmailVerification = 3;

const route = useRoute();
const router = useRouter();
const email = ref('');
const password = ref('');
const displayName = ref('');
const challengeId = ref('');
const challengeCode = ref('');
const invitationId = ref('');
const invitationToken = ref('');
const registrationWayId = ref('');
const submitting = ref(false);
const invitationSessionKey = 'fullnet.registration.invitation';

onMounted(async () => {
  const queryToken = typeof route.query.invitationToken === 'string' ? route.query.invitationToken : '';
  const queryId = typeof route.query.invitationId === 'string' ? route.query.invitationId : '';
  if (queryToken && queryId) {
    sessionStorage.setItem(
      invitationSessionKey,
      JSON.stringify({ invitationId: queryId, invitationToken: queryToken })
    );
    await router.replace({ path: route.path });
  }

  const stored = sessionStorage.getItem(invitationSessionKey);
  if (!stored) {
    return;
  }

  let parsed: { invitationId?: string; invitationToken?: string };
  try {
    parsed = JSON.parse(stored) as { invitationId?: string; invitationToken?: string };
  } catch {
    sessionStorage.removeItem(invitationSessionKey);
    return;
  }

  if (!parsed.invitationId || !parsed.invitationToken) {
    return;
  }

  invitationToken.value = parsed.invitationToken;
  invitationId.value = parsed.invitationId;
  try {
    const info = await verifyInvitation(parsed.invitationId, parsed.invitationToken);
    email.value = info.email;
    registrationWayId.value = info.registrationWayId;
  } catch {
    ElMessage.error('Invitation is invalid or expired.');
  }
});

async function sendChallenge(): Promise<void> {
  const purpose = invitationId.value ? InvitationEmailVerification : RegistrationEmailVerification;
  const result = await requestEmailChallenge(
    email.value,
    purpose,
    invitationId.value || undefined,
    invitationToken.value || undefined
  );
  challengeId.value = result.challengeId;
  ElMessage.success('Verification code sent if the address is eligible.');
}

async function submit(): Promise<void> {
  if (!challengeId.value || !challengeCode.value) {
    ElMessage.error('Email verification is required.');
    return;
  }

  submitting.value = true;
  try {
    await registerAccount({
      email: email.value,
      password: password.value,
      displayName: displayName.value,
      challengeId: challengeId.value,
      challengeCode: challengeCode.value,
      registrationWayId: registrationWayId.value || undefined,
      invitationId: invitationId.value || undefined,
      invitationToken: invitationToken.value || undefined
    });
    sessionStorage.removeItem(invitationSessionKey);
    ElMessage.success('Account created. You can sign in now.');
    await router.replace('/login');
  } catch (error: unknown) {
    ElMessage.error(isFullNetProblemDetails(error) ? error.title : 'Registration failed.');
  } finally {
    submitting.value = false;
  }
}
</script>

<template>
  <div class="art-login-page">
    <ArtLoginLeftPanel />
    <section class="register-form">
      <h1>Create account</h1>
      <ElInput v-model="displayName" placeholder="Display name" />
      <ElInput v-model="email" type="email" placeholder="Email" />
      <ElInput v-model="password" type="password" placeholder="Password" show-password />
      <ElInput v-model="challengeCode" placeholder="Email verification code" />
      <div class="actions">
        <ElButton @click="sendChallenge">Send code</ElButton>
        <ElButton type="primary" :loading="submitting" @click="submit">Register</ElButton>
        <ElButton link @click="router.replace('/login')">Back to sign in</ElButton>
      </div>
    </section>
  </div>
</template>

<style scoped>
.register-form {
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
