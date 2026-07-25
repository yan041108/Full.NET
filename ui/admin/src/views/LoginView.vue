<script setup lang="ts">
import { computed, ref } from 'vue';
import { ElButton, ElInput } from 'element-plus';
import {
  isFullNetProblemDetails,
  type FullNetProblemDetails
} from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import LocaleSelector from '../i18n/LocaleSelector.vue';
import { useAdminI18n } from '../i18n/adminI18n';
import ArtLoginLeftPanel from '../framework/art-design/auth/ArtLoginLeftPanel.vue';

const session = useSessionStore();
const { t } = useAdminI18n();
const username = ref('');
const password = ref('');
const submitting = ref(false);
const problem = ref<FullNetProblemDetails>();
const status = computed(() => session.state === 'authenticated'
  ? t('auth.statusAuthenticated')
  : t('auth.statusAnonymous'));

async function submit(): Promise<void> {
  if (submitting.value) {
    return;
  }

  submitting.value = true;
  problem.value = undefined;
  try {
    await session.login(username.value, password.value);
  } catch (error: unknown) {
    problem.value = isFullNetProblemDetails(error)
      ? error
      : { status: 500, code: 'client.login_failed', title: t('auth.loginFailed') };
  } finally {
    submitting.value = false;
  }
}
</script>

<template>
  <a class="skip-link" href="#main-content">{{ t('a11y.skipToMain') }}</a>
  <div class="art-login-page">
    <ArtLoginLeftPanel />

    <div class="art-login-page__main">
      <header class="art-login-page__topbar">
        <LocaleSelector id="login-locale" compact />
      </header>

      <main
        id="main-content"
        class="art-login-page__form-wrap"
        data-testid="login-view"
        tabindex="-1"
      >
        <form class="art-login-form" aria-labelledby="login-form-title" @submit.prevent="submit">
          <h2 id="login-form-title" class="art-login-form__title">{{ t('auth.title') }}</h2>
          <p class="art-login-form__subtitle">{{ status }}</p>

          <div class="art-login-form__field">
            <label id="login-username-label" for="login-username">{{ t('auth.username') }}</label>
            <el-input
              id="login-username"
              v-model.trim="username"
              name="username"
              autocomplete="username"
              spellcheck="false"
              maxlength="128"
              :placeholder="t('auth.usernamePlaceholder')"
            />
          </div>

          <div class="art-login-form__field">
            <label id="login-password-label" for="login-password">{{ t('auth.password') }}</label>
            <el-input
              id="login-password"
              v-model="password"
              name="password"
              type="password"
              autocomplete="current-password"
              maxlength="1024"
              show-password
              :placeholder="t('auth.passwordPlaceholder')"
              @keyup.enter="submit"
            />
          </div>

          <div v-if="problem" class="art-inline-alert" role="alert" aria-live="assertive">
            <strong translate="no">{{ problem.code }}</strong>
            <span>{{ problem.title }}</span>
            <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
          </div>

          <el-button
            class="art-login-form__submit art-contrast-primary"
            type="primary"
            native-type="submit"
            :loading="submitting"
            :aria-busy="submitting"
          >
            {{ submitting ? t('auth.submitting') : t('auth.submit') }}
          </el-button>

          <small class="art-login-form__footnote">{{ t('auth.tokenNotice') }}</small>
        </form>
      </main>
    </div>
  </div>
</template>
