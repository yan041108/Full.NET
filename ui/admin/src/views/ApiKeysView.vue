<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { ElButton, ElCard, ElMessage, ElMessageBox, ElTag } from 'element-plus';
import type { FullNetProblemDetails, HostApiKey } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  createHostApiKey,
  disableHostApiKey,
  listHostApiKeys
} from '../api/api-keys';

const session = useSessionStore();
const { t } = useAdminI18n();
const items = ref<HostApiKey[]>([]);
const userId = ref('');
const displayName = ref('');
const permissionsText = ref('');
const expiresAt = ref('');
const secret = ref('');
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const canWrite = computed(() => session.can('identity.api_keys.write'));

onMounted(load);

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    items.value = (await listHostApiKeys()).items;
  } catch (error: unknown) {
    problem.value = toProblem(error, 'apiKeys.loadFailed');
  } finally {
    loading.value = false;
  }
}

async function create(): Promise<void> {
  if (changing.value) return;
  const permissions = [...new Set(
    permissionsText.value
      .split(/[\n,]+/)
      .map(value => value.trim())
      .filter(Boolean)
  )];
  changing.value = true;
  problem.value = undefined;
  secret.value = '';
  try {
    const result = await createHostApiKey({
      userId: userId.value.trim(),
      displayName: displayName.value.trim(),
      permissions,
      expiresAtUtc: expiresAt.value ? new Date(expiresAt.value).toISOString() : null
    });
    secret.value = result.secret;
    displayName.value = '';
    permissionsText.value = '';
    expiresAt.value = '';
    ElMessage.success(t('apiKeys.createSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error);
  } finally {
    changing.value = false;
  }
}

async function copySecret(): Promise<void> {
  if (!secret.value) return;
  try {
    await navigator.clipboard.writeText(secret.value);
    ElMessage.success(t('apiKeys.copySuccess'));
  } catch (error: unknown) {
    problem.value = toProblem(error);
  }
}

async function disable(item: HostApiKey): Promise<void> {
  if (changing.value || !item.isActive) return;
  try {
    await ElMessageBox.confirm(
      t('apiKeys.confirmDisable', { name: item.displayName }),
      t('apiKeys.disable'),
      {
        type: 'warning',
        confirmButtonText: t('apiKeys.disable'),
        cancelButtonText: t('status.back')
      }
    );
    changing.value = true;
    problem.value = undefined;
    await disableHostApiKey(item.id);
    ElMessage.success(t('apiKeys.disableSuccess'));
    await load();
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') return;
    problem.value = toProblem(error);
  } finally {
    changing.value = false;
  }
}

function toProblem(
  error: unknown,
  fallbackKey: 'apiKeys.loadFailed' | 'apiKeys.operationFailed' = 'apiKeys.operationFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { status: 500, code: 'client.host_api_key_failed', title: t(fallbackKey) };
}
</script>

<template>
  <section class="api-keys-view art-page-stack art-full-height" :aria-busy="loading">
    <header class="art-page-header">
      <p class="art-eyebrow">{{ t('apiKeys.eyebrow') }}</p>
      <h1 data-route-heading tabindex="-1">{{ t('apiKeys.title') }}</h1>
      <p>{{ t('apiKeys.description') }}</p>
    </header>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <ElCard v-if="canWrite" class="art-card">
      <template #header><h2>{{ t('apiKeys.createTitle') }}</h2></template>
      <form
        class="art-form-grid"
        data-testid="api-key-create-form"
        @submit.prevent="create"
      >
        <label>
          <span>{{ t('apiKeys.fieldUserId') }}</span>
          <input
            v-model="userId"
            data-testid="api-key-user-id"
            required
            autocomplete="off"
            spellcheck="false"
          />
        </label>
        <label>
          <span>{{ t('apiKeys.fieldDisplayName') }}</span>
          <input v-model="displayName" data-testid="api-key-display-name" required />
        </label>
        <label class="art-span-2">
          <span>{{ t('apiKeys.fieldPermissions') }}</span>
          <textarea
            v-model="permissionsText"
            data-testid="api-key-permissions"
            required
            rows="3"
            spellcheck="false"
          />
          <small>{{ t('apiKeys.permissionsHint') }}</small>
        </label>
        <label>
          <span>{{ t('apiKeys.fieldExpiresAt') }}</span>
          <input v-model="expiresAt" type="datetime-local" />
        </label>
        <div class="art-form-actions">
          <ElButton type="primary" native-type="submit" :loading="changing">
            {{ t('apiKeys.create') }}
          </ElButton>
        </div>
      </form>
    </ElCard>

    <ElCard v-if="secret" class="art-card" data-testid="api-key-secret">
      <template #header><h2>{{ t('apiKeys.secretTitle') }}</h2></template>
      <p role="alert">{{ t('apiKeys.secretWarning') }}</p>
      <code translate="no">{{ secret }}</code>
      <ElButton type="primary" plain @click="copySecret">{{ t('apiKeys.copy') }}</ElButton>
    </ElCard>

    <ElCard class="art-card">
      <template #header>
        <div class="art-table-card__header">
          <h2>{{ t('apiKeys.listTitle') }}</h2>
          <span class="art-table-card__count">{{ items.length }}</span>
        </div>
      </template>
      <p v-if="items.length === 0" class="art-empty-state">{{ t('apiKeys.emptyList') }}</p>
      <article v-for="item in items" :key="item.id" class="art-data-row">
        <div class="art-data-row__main">
          <strong>{{ item.displayName }}</strong>
          <code translate="no">{{ item.username }}</code>
          <small>{{ t('apiKeys.prefix') }}: <code translate="no">{{ item.keyPrefix }}</code></small>
          <small>{{ t('apiKeys.permissions') }}: {{ item.permissions.join(', ') }}</small>
          <small>{{ t('apiKeys.expiresAt') }}: {{ item.expiresAtUtc ?? t('apiKeys.noExpiration') }}</small>
          <small>{{ t('apiKeys.lastUsedAt') }}: {{ item.lastUsedAtUtc ?? t('apiKeys.never') }}</small>
          <ElTag :type="item.isActive ? 'success' : 'info'">
            {{ item.isActive ? t('apiKeys.statusActive') : t('apiKeys.statusDisabled') }}
          </ElTag>
        </div>
        <div v-if="canWrite && item.isActive" class="art-data-row__actions">
          <ElButton type="danger" plain :disabled="changing" @click="disable(item)">
            {{ t('apiKeys.disable') }}
          </ElButton>
        </div>
      </article>
    </ElCard>
  </section>
</template>
