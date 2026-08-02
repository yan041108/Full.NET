<script setup lang="ts">
import { onMounted, ref } from 'vue';
import { ElButton, ElCard, ElMessage, ElMessageBox } from 'element-plus';
import type { FullNetProblemDetails, HostOnlineSession } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import PermissionGate from '../components/PermissionGate.vue';
import { listHostOnlineSessions, revokeHostOnlineSession } from '../api/online-sessions';

const session = useSessionStore();
const { t } = useAdminI18n();
const items = ref<HostOnlineSession[]>([]);
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();

onMounted(load);

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    const page = await listHostOnlineSessions();
    items.value = page.items;
  } catch (error: unknown) {
    problem.value = toProblem(error);
  } finally {
    loading.value = false;
  }
}

async function revoke(item: HostOnlineSession): Promise<void> {
  if (changing.value || !session.can('identity.sessions.revoke')) return;
  try {
    await ElMessageBox.confirm(
      t('onlineSessions.confirmRevoke', { name: item.username }),
      t('onlineSessions.revoke'),
      {
        type: 'warning',
        confirmButtonText: t('onlineSessions.revoke'),
        cancelButtonText: t('status.back')
      }
    );
    changing.value = true;
    await revokeHostOnlineSession(item.id);
    ElMessage.success(t('onlineSessions.revokeSuccess'));
    await load();
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') return;
    problem.value = toProblem(error, 'onlineSessions.operationFailed');
  } finally {
    changing.value = false;
  }
}

function toProblem(
  error: unknown,
  fallbackKey: 'onlineSessions.loadFailed' | 'onlineSessions.operationFailed' = 'onlineSessions.loadFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { status: 500, code: 'client.host_online_session_failed', title: t(fallbackKey) };
}
</script>

<template>
  <section class="online-sessions-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('onlineSessions.title') }}</h1>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <el-card class="art-table-card" shadow="never">
      <template #header>
        <div class="art-table-card__header">
          <h2>{{ t('onlineSessions.directoryTitle') }}</h2>
          <span class="art-table-card__count">{{ items.length }}</span>
        </div>
      </template>

      <p v-if="items.length === 0" class="art-empty-state">{{ t('onlineSessions.emptyDirectory') }}</p>
      <article v-for="item in items" :key="item.id" class="art-data-row">
        <div class="art-data-row__main">
          <strong translate="no">{{ item.displayName }}</strong>
          <code translate="no">{{ item.username }}</code>
          <small>{{ t('onlineSessions.clientId') }}: {{ item.clientId }}</small>
          <small>{{ t('onlineSessions.createdAt') }}: {{ item.createdAtUtc }}</small>
          <small>{{ t('onlineSessions.expiresAt') }}: {{ item.expiresAtUtc }}</small>
        </div>
        <PermissionGate code="identity.sessions.revoke">
          <div class="art-data-row__actions">
            <el-button
              type="danger"
              plain
              :disabled="changing"
              @click="revoke(item)"
            >
              {{ t('onlineSessions.revoke') }}
            </el-button>
          </div>
        </PermissionGate>
      </article>
    </el-card>
  </section>
</template>
