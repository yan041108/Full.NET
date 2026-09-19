<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { ElButton, ElCard, ElMessage, ElTable, ElTableColumn } from 'element-plus';
import { useAdminI18n } from '../i18n/adminI18n';
import { useSessionStore } from '../auth/session';
import { listTenantSubscriptions, type TenantSubscription } from '../api/tenant-subscriptions';

defineOptions({ name: 'TenantSubscriptionView' });

const { t } = useAdminI18n();
const session = useSessionStore();
const loading = ref(false);
const rows = ref<TenantSubscription[]>([]);
const tenantId = computed(() => session.currentUser?.tenantId ?? '');

async function refresh(): Promise<void> {
  if (!tenantId.value) {
    rows.value = [];
    return;
  }

  loading.value = true;
  try {
    rows.value = await listTenantSubscriptions(tenantId.value);
  } catch {
    ElMessage.error(t('common.loadFailed'));
  } finally {
    loading.value = false;
  }
}

onMounted(() => {
  void refresh();
});
</script>

<template>
  <div class="tenant-subscription-view">
    <ElCard>
      <template #header>
        <div class="view-header">
          <h2>{{ t('tenancy.tenantSubscriptions.title') }}</h2>
          <ElButton type="primary" :loading="loading" @click="refresh">
            {{ t('common.refresh') }}
          </ElButton>
        </div>
      </template>
      <ElTable v-loading="loading" :data="rows" row-key="id">
        <ElTableColumn prop="status" :label="t('tenancy.tenantSubscriptions.status')" />
        <ElTableColumn prop="currentPeriodEndUtc" :label="t('tenancy.tenantSubscriptions.periodEnd')" />
      </ElTable>
    </ElCard>
  </div>
</template>

<style scoped>
.view-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}
</style>
