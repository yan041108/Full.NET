<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { ElAlert, ElButton, ElInput, ElMessage } from 'element-plus';
import { isFullNetProblemDetails, isGuid } from '@fullnet/client-contracts';
import { registerAccount, requestEmailChallenge, verifyInvitation } from '../api/public-auth';
import ArtLoginLeftPanel from '../framework/art-design/auth/ArtLoginLeftPanel.vue';
import { useAdminI18n } from '../i18n/adminI18n';
import { accountSelfServiceText } from '../i18n/account-self-service';
import { invitationOnboardingText } from '../i18n/invitation-onboarding';
import { rememberRegistrationInvitation, readRegistrationInvitation, forgetRegistrationInvitation } from '../auth/registration-invitation';

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
const invitationChecking = ref(false);
const invitationVerified = ref(false);
const invitationInvalid = ref(false);
const entryReady = computed(() => !invitationInvalid.value && (invitationId.value ? invitationVerified.value : !!registrationWayId.value));
const submitting = ref(false);
const sending = ref(false);
const { locale } = useAdminI18n();
const text = (key: Parameters<typeof accountSelfServiceText>[1]) => accountSelfServiceText(locale.value, key);

watch(email, () => { challengeId.value = ''; challengeCode.value = ''; }, { flush: 'sync' });

onMounted(async () => {
  // 链接只选择注册路径；方式启用、目标租户及分配规则仍由服务端权威校验。
  const queryWay = route.query.registrationWayId;
  if (isGuid(queryWay) && queryWay !== '00000000-0000-0000-0000-000000000000') registrationWayId.value = queryWay;
  const queryToken = typeof route.query.invitationToken === 'string' ? route.query.invitationToken : '';
  const queryId = typeof route.query.invitationId === 'string' ? route.query.invitationId : '';
  if ((route.query.invitationToken !== undefined || route.query.invitationId !== undefined)
      && (!queryToken || !isGuid(queryId) || queryId === '00000000-0000-0000-0000-000000000000')) {
    invitationInvalid.value = true;
    await router.replace({ path: route.path });
    return;
  }
  if (queryToken && queryId) {
    rememberRegistrationInvitation({ invitationId: queryId, invitationToken: queryToken });
    await router.replace({ path: route.path });
  }

  // 明确的方式链接不得复用先前放弃的邀请；无参数页面仍可恢复已剥离 URL 凭据的邀请。
  if (!queryToken && !queryId && route.query.registrationWayId !== undefined) {
    forgetRegistrationInvitation();
    return;
  }
  const parsed = queryId && queryToken ? { invitationId: queryId, invitationToken: queryToken } : readRegistrationInvitation();

  if (!parsed?.invitationId || !parsed.invitationToken) {
    return;
  }

  invitationToken.value = parsed.invitationToken;
  invitationId.value = parsed.invitationId;
  invitationChecking.value = true;
  try {
    const info = await verifyInvitation(parsed.invitationId, parsed.invitationToken);
    email.value = info.email;
    registrationWayId.value = info.registrationWayId;
    invitationVerified.value = true;
  } catch {
    invitationInvalid.value = true;
    ElMessage.error(text('invalidInvitation'));
  } finally {
    invitationChecking.value = false;
  }
});

async function sendChallenge(): Promise<void> {
  if (sending.value || submitting.value || !email.value.trim() || !entryReady.value) return;
  const requestedEmail = email.value;
  sending.value = true;
  // 重发可能已经在服务端替换旧码，响应未知时也不能继续提交旧挑战。
  challengeId.value = '';
  challengeCode.value = '';
  const purpose = invitationId.value ? InvitationEmailVerification : RegistrationEmailVerification;
  try {
    const result = await requestEmailChallenge(requestedEmail, purpose, invitationId.value || undefined, invitationToken.value || undefined);
    if (email.value !== requestedEmail) return;
    challengeId.value = result.challengeId;
    ElMessage.success(text('sent'));
  } catch (error: unknown) {
    if (email.value === requestedEmail) ElMessage.error(isFullNetProblemDetails(error) ? error.title : text('sendFailed'));
  } finally {
    sending.value = false;
  }
}

async function submit(): Promise<void> {
  if (submitting.value || sending.value) return;
  if (!entryReady.value) { ElMessage.error(text('entryRequired')); return; }
  if (!challengeId.value || !challengeCode.value) {
    ElMessage.error(text('required'));
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
    forgetRegistrationInvitation();
    ElMessage.success(text('registered'));
    await router.replace('/login?redirect=/tenant-context');
  } catch (error: unknown) {
    ElMessage.error(isFullNetProblemDetails(error) ? error.title : text('registerFailed'));
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
    <section class="art-login-form register-form" aria-labelledby="register-title">
      <h1 id="register-title" class="art-login-form__title">{{ text('registerTitle') }}</h1>
      <ElAlert v-if="!entryReady" :title="text(invitationChecking ? 'checkingInvitation' : invitationInvalid ? 'invalidInvitation' : 'entryRequired')"
        :type="invitationInvalid ? 'error' : 'info'" :closable="false" show-icon />
      <ElInput v-model="displayName" :placeholder="text('displayName')" :aria-label="text('displayName')" :disabled="submitting" />
      <ElInput v-model="email" type="email" :placeholder="text('email')" :aria-label="text('email')" :disabled="submitting" />
      <ElInput v-model="password" type="password" :placeholder="text('password')" :aria-label="text('password')" :disabled="submitting" show-password />
      <ElInput v-model="challengeCode" :placeholder="text('code')" :aria-label="text('code')" :disabled="sending || submitting" />
      <div class="actions">
        <ElButton :loading="sending" :disabled="submitting || !email.trim() || !entryReady" @click="sendChallenge">{{ text('send') }}</ElButton>
        <ElButton type="primary" :loading="submitting" :disabled="sending || !entryReady || !challengeId || !challengeCode" @click="submit">{{ text('register') }}</ElButton>
        <ElButton link @click="router.replace('/login')">{{ text('back') }}</ElButton>
        <ElButton v-if="invitationVerified" link data-testid="invitation-existing-account" @click="router.replace('/login?redirect=/tenant-context')">{{ invitationOnboardingText(locale, 'existing') }}</ElButton>
      </div>
    </section>
      </div>
    </main>
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
