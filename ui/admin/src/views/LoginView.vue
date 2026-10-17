<script setup lang="ts">
import { computed, onActivated, onDeactivated, onMounted, onUnmounted, ref } from 'vue';
import { ElButton, ElInput } from 'element-plus';
import {
  isFullNetProblemDetails,
  type FullNetProblemDetails,
  type PublicOAuthProvider
} from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import LocaleSelector from '../i18n/LocaleSelector.vue';
import { useAdminI18n } from '../i18n/adminI18n';
import ArtLoginLeftPanel from '../framework/art-design/auth/ArtLoginLeftPanel.vue';
import { buildOAuthAuthorizeUrl } from '../api/oauth-links';
import { listPublicOAuthProviders } from '../api/oauth-providers';
import { beginAdminOidcCenterLogin } from '../auth/oidc-center-login';
import { resolveLoginProblemTitle } from '../auth/login-problem';
import { adminIdentityAuthMode } from '../config/identity-auth';

const session = useSessionStore();
const { t } = useAdminI18n();
const username = ref('');
const password = ref('');
const submitting = ref(false);
const problem = ref<FullNetProblemDetails>();
const oauthProviders = ref<PublicOAuthProvider[]>([]);
const isOidcCenterLogin = adminIdentityAuthMode === 'oidc-center';
const oidcSubmitting = ref(false);
let inactive = false;
let operation = 0;
let loginController: AbortController | undefined;
let providersController: AbortController | undefined;
const status = computed(() => session.state === 'authenticated'
  ? t('auth.statusAuthenticated')
  : t('auth.statusAnonymous'));
const problemTitle = computed(() =>
  problem.value ? resolveLoginProblemTitle(problem.value, t) : '');

function oauthReturnUrl(): string {
  const { origin, pathname, search } = window.location;
  return `${origin}${pathname}${search}#/oauth/callback`;
}

function startOAuthLogin(providerKey: string): void {
  window.location.href = buildOAuthAuthorizeUrl(providerKey, 'login', oauthReturnUrl());
}

async function loadOAuthProviders(): Promise<void> {
  if (inactive || isOidcCenterLogin) return;
  providersController?.abort();
  const controller = new AbortController(); providersController = controller;
  try {
    const providers = await listPublicOAuthProviders(controller.signal);
    if (!inactive && providersController === controller) oauthProviders.value = providers;
  } catch {
    if (!inactive && providersController === controller) oauthProviders.value = [];
  }
}

async function submitOidcCenter(): Promise<void> {
  if (inactive || oidcSubmitting.value || submitting.value) {
    return;
  }

  oidcSubmitting.value = true;
  problem.value = undefined;
  const current = ++operation; const controller = new AbortController(); loginController = controller;
  try {
    await beginAdminOidcCenterLogin(controller.signal);
  } catch (error: unknown) {
    if (inactive || current !== operation) return;
    problem.value = isFullNetProblemDetails(error)
      ? error
      : { status: 500, code: 'client.oidc_login_failed', title: t('auth.oidcCenterFailed') };
    oidcSubmitting.value = false;
  }
}

async function submit(): Promise<void> {
  if (inactive || submitting.value || oidcSubmitting.value) {
    return;
  }

  submitting.value = true;
  problem.value = undefined;
  const current = ++operation; const controller = new AbortController(); loginController = controller;
  try {
    await session.login(username.value, password.value, controller.signal);
  } catch (error: unknown) {
    if (inactive || current !== operation) return;
    problem.value = isFullNetProblemDetails(error)
      ? error
      : { status: 500, code: 'client.login_failed', title: t('auth.loginFailed') };
  } finally {
    if (current === operation) {
      password.value = ''; submitting.value = false; loginController = undefined;
    }
  }
}

function focusMainContent(): void {
  document.getElementById('main-content')?.focus();
}

onMounted(() => {
  void loadOAuthProviders();
});

/** 页面退出后清除密码并取消所属请求，迟到结果不得改写重入后的表单。 */
function suspend(): void {
  inactive = true; operation++; password.value = ''; problem.value = undefined;
  loginController?.abort(); providersController?.abort(); oauthProviders.value = [];
  submitting.value = false; oidcSubmitting.value = false;
}
onUnmounted(suspend);
onDeactivated(suspend);
onActivated(() => { if (inactive) { inactive = false; void loadOAuthProviders(); } });
</script>

<template>
  <a
    class="skip-link"
    href="#main-content"
    @click.prevent="focusMainContent"
  >{{ t('a11y.skipToMain') }}</a>
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
        <div
          v-if="isOidcCenterLogin"
          class="art-login-form"
          aria-labelledby="login-oidc-title"
        >
          <h2 id="login-oidc-title" class="art-login-form__title">{{ t('auth.title') }}</h2>
          <p class="art-login-form__subtitle">{{ t('auth.oidcCenterSubtitle') }}</p>
          <div v-if="problem" class="art-inline-alert" role="alert" aria-live="assertive">
            <strong translate="no">{{ problem.code }}</strong>
            <span>{{ problemTitle }}</span>
            <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
          </div>
          <el-button
            class="art-login-form__submit"
            type="primary"
            data-testid="login-oidc-center"
            :loading="oidcSubmitting"
            :aria-busy="oidcSubmitting"
            @click="submitOidcCenter"
          >
            {{ oidcSubmitting ? t('auth.submitting') : t('auth.oidcCenterSubmit') }}
          </el-button>
        </div>
        <form
          v-else
          class="art-login-form"
          aria-labelledby="login-form-title"
          @submit.prevent="submit"
        >
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
              :disabled="submitting"
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
              :disabled="submitting"
              show-password
              :placeholder="t('auth.passwordPlaceholder')"
              @keyup.enter="submit"
            />
          </div>

          <div v-if="problem" class="art-inline-alert" role="alert" aria-live="assertive">
            <strong translate="no">{{ problem.code }}</strong>
            <span>{{ problemTitle }}</span>
            <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
          </div>

          <el-button
            class="art-login-form__submit"
            type="primary"
            native-type="submit"
            :loading="submitting"
            :aria-busy="submitting"
          >
            {{ submitting ? t('auth.submitting') : t('auth.submit') }}
          </el-button>

          <div v-if="oauthProviders.length > 0" class="art-login-form__oauth">
            <p class="art-login-form__oauth-title">{{ t('auth.oauthTitle') }}</p>
            <div class="art-login-form__oauth-buttons">
              <el-button
                v-for="provider in oauthProviders"
                :key="provider.providerKey"
                class="art-login-form__oauth-button"
                :disabled="submitting"
                @click="startOAuthLogin(provider.providerKey)"
              >
                {{ provider.displayName }}
              </el-button>
            </div>
          </div>

          <div class="art-login-form__links">
            <router-link to="/register">{{ t('auth.createAccount') }}</router-link>
            <router-link to="/recover-password">{{ t('auth.forgotPassword') }}</router-link>
          </div>

          <small class="art-login-form__footnote">{{ t('auth.tokenNotice') }}</small>
        </form>
      </main>
    </div>
  </div>
</template>
