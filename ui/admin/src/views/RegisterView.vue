<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { ElButton, ElInput } from 'element-plus';
import { showProblem, showSuccess, showWarning } from '../feedback/fullNetMessage';
import { registerAccount, requestEmailChallenge, verifyInvitation } from '../api/public-auth';
import { useAdminI18n } from '../i18n/adminI18n';
import { isGuid, isRecord } from '@fullnet/client-contracts';
import { isIdentityPasswordValid } from '../auth/identity-password-policy';
import LocaleSelector from '../i18n/LocaleSelector.vue';
import ArtLoginLeftPanel from '../framework/art-design/auth/ArtLoginLeftPanel.vue';

const RegistrationEmailVerification = 1;
const InvitationEmailVerification = 3;

const route = useRoute();
const router = useRouter();
const { t } = useAdminI18n();
const email = ref('');
const password = ref('');
const displayName = ref('');
const challengeId = ref('');
const challengeCode = ref('');
const invitationId = ref('');
const invitationToken = ref('');
const registrationWayId = ref('');
const submitting = ref(false);
const requesting = ref(false);
const invitationState = ref<'none' | 'checking' | 'valid' | 'invalid'>('none');
let disposed = false;
let emailRevision = 0;
onBeforeUnmount(() => { disposed = true; });
watch(email, () => { emailRevision++; challengeId.value = ''; challengeCode.value = ''; }, { flush: 'sync' });
const invitationSessionKey = 'fullnet.registration.invitation';

onMounted(async () => {
  const queryToken = typeof route.query.invitationToken === 'string' ? route.query.invitationToken : '';
  const queryId = typeof route.query.invitationId === 'string' ? route.query.invitationId : '';
  if (queryToken && queryId) {
    invitationState.value = 'checking';
    sessionStorage.setItem(invitationSessionKey, JSON.stringify({ invitationId: queryId, invitationToken: queryToken }));
    await router.replace({ path: route.path });
    if (disposed) return;
  }
  const stored = sessionStorage.getItem(invitationSessionKey);
  if (!stored) return;
  let parsed: unknown;
  try { parsed = JSON.parse(stored); } catch { parsed = undefined; }
  if (!isRecord(parsed) || !isGuid(parsed.invitationId)
    || typeof parsed.invitationToken !== 'string' || !parsed.invitationToken.trim()) {
    sessionStorage.removeItem(invitationSessionKey);
    invitationState.value = 'none';
    return;
  }
  invitationState.value = 'checking';
  invitationToken.value = parsed.invitationToken;
  invitationId.value = parsed.invitationId;
  try {
    const info = await verifyInvitation(parsed.invitationId, parsed.invitationToken);
    if (disposed) return;
    email.value = info.email;
    registrationWayId.value = info.registrationWayId;
    invitationState.value = 'valid';
  } catch (error: unknown) {
    if (disposed) return;
    invitationState.value = 'invalid';
    invitationId.value = ''; invitationToken.value = ''; registrationWayId.value = '';
    sessionStorage.removeItem(invitationSessionKey);
    showProblem(error, t('accountChallenges.invitationInvalid'));
  }
});

async function sendChallenge(): Promise<void> {
  if (disposed || requesting.value || submitting.value || invitationState.value === 'checking' || invitationState.value === 'invalid') return;
  const target = email.value.trim();
  if (!target) { showWarning(t('accountChallenges.emailRequired')); return; }
  const revision = emailRevision;
  requesting.value = true;
  challengeId.value = ''; challengeCode.value = '';
  const purpose = invitationId.value ? InvitationEmailVerification : RegistrationEmailVerification;
  try {
    const result = await requestEmailChallenge(target, purpose, invitationId.value || undefined, invitationToken.value || undefined);
    if (disposed || emailRevision !== revision || email.value.trim() !== target) return;
    challengeId.value = result.challengeId;
    showSuccess(t('accountChallenges.registrationAccepted'));
  } catch (error: unknown) {
    if (!disposed && emailRevision === revision && email.value.trim() === target) showProblem(error, t('accountChallenges.requestFailed'));
  } finally { requesting.value = false; }
}

async function submit(): Promise<void> {
  if (disposed || requesting.value || submitting.value || invitationState.value === 'checking' || invitationState.value === 'invalid') return;
  if (!challengeId.value || !challengeCode.value.trim()) {
    showWarning(t('accountChallenges.verificationRequired'));
    return;
  }

  if (!email.value.trim() || !displayName.value.trim()) { showWarning(t('accountChallenges.requiredFields')); return; }
  if (!isIdentityPasswordValid(password.value)) { showWarning(t('securitySettings.passwordInvalid')); return; }
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
    if (disposed) return;
    password.value = ''; challengeCode.value = ''; challengeId.value = '';
    sessionStorage.removeItem(invitationSessionKey);
    showSuccess(t('accountChallenges.accountCreated'));
    await router.replace('/login');
  } catch (error: unknown) {
    if (!disposed) showProblem(error, t('accountChallenges.registrationFailed'));
  } finally {
    submitting.value = false;
  }
}
</script>

<template>
  <div class="art-login-page">
    <ArtLoginLeftPanel />
    <form class="register-form" aria-labelledby="register-title" @submit.prevent="submit">
      <LocaleSelector id="register-locale" compact />
      <h1 id="register-title">{{ t('accountChallenges.registrationTitle') }}</h1>
      <ElInput v-model="displayName" name="displayName" autocomplete="name" :disabled="submitting || invitationState === 'checking' || invitationState === 'invalid'" :placeholder="t('accountChallenges.displayName')" :aria-label="t('accountChallenges.displayName')" />
      <ElInput v-model="email" name="email" type="email" :readonly="invitationState === 'valid'" autocomplete="email" :disabled="submitting || invitationState === 'checking' || invitationState === 'invalid'" :placeholder="t('accountChallenges.email')" :aria-label="t('accountChallenges.email')" />
      <ElInput v-model="password" name="password" type="password" autocomplete="new-password" show-password :disabled="submitting || invitationState === 'checking' || invitationState === 'invalid'" :placeholder="t('accountChallenges.password')" :aria-label="t('accountChallenges.password')" />
      <ElInput v-model="challengeCode" name="challengeCode" autocomplete="one-time-code" inputmode="numeric" :disabled="submitting || requesting" :placeholder="t('accountChallenges.verificationCode')" :aria-label="t('accountChallenges.verificationCode')" />
      <div class="actions">
        <ElButton :loading="requesting" :disabled="submitting || invitationState === 'checking' || invitationState === 'invalid'" @click="sendChallenge">{{ t('accountChallenges.sendCode') }}</ElButton>
        <ElButton type="primary" :loading="submitting" :disabled="requesting || invitationState === 'checking' || invitationState === 'invalid'" native-type="submit">{{ t('accountChallenges.register') }}</ElButton>
        <ElButton link @click="router.replace('/login')">{{ t('accountChallenges.backToSignIn') }}</ElButton>
      </div>
    </form>
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
