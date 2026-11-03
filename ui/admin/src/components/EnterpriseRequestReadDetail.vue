<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue';
import { ElAlert, ElButton, ElDrawer } from 'element-plus';
import { createEnterpriseRequestsApi, enterpriseRequestsHttp, type EnterpriseRequestResponse } from '../api/enterprise-requests';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { isSearchObjectId } from '../search/objectSearch';

const props = defineProps<{ requestId?: string }>(); const open = defineModel<boolean>({ default: false });
const session = useSessionStore(), { t } = useAdminI18n(); const item = ref<EnterpriseRequestResponse>(), loading = ref(false), failed = ref(false);
const context = computed(() => [session.currentUser?.id, session.currentUser?.tenantId, session.currentUser?.sessionId,
  session.currentUser?.permissions.join('|'), session.switching].join('|'));
let generation = 0, controller: AbortController | undefined;
function reset() { generation++; controller?.abort(); item.value = undefined; loading.value = false; failed.value = false; }
watch(() => [open.value, props.requestId, context.value] as const, (value, previous) => {
  reset(); if (previous && previous[2] !== value[2]) { open.value = false; return; }
  if (!value[0] || !isSearchObjectId(value[1]) || session.switching || !session.currentUser?.tenantId || !session.can('enterprise_request.enterprise_requests.read')) return;
  void load(value[1]);
}, { immediate: true, flush: 'sync' });
async function load(id: string) {
  const current = generation, request = new AbortController(); controller = request; loading.value = true;
  try {
    const value = await createEnterpriseRequestsApi(enterpriseRequestsHttp, () => request.signal).get(id);
    if (value.id.toLowerCase() !== id.toLowerCase()) throw new Error('client.invalid_enterprise_request');
    if (current === generation && !request.signal.aborted) item.value = value;
  } catch { if (current === generation && !request.signal.aborted) failed.value = true; }
  finally { if (current === generation) loading.value = false; }
}
onBeforeUnmount(reset);
</script>
<template>
  <ElDrawer v-model="open" :title="t('objectSearch.detail')" size="min(640px, 100vw)" data-testid="enterprise-request-read-detail">
    <p v-if="loading" role="status">{{ t('objectSearch.loading') }}</p>
    <ElAlert v-else-if="failed" type="error" :closable="false" :title="t('objectSearch.detailFailed')" />
    <dl v-else-if="item">
      <dt>{{ t('objectSearch.number') }}</dt><dd>{{ item.requestNumber }}</dd>
      <dt>{{ t('objectSearch.titleLabel') }}</dt><dd data-testid="request-read-detail-title">{{ item.title }}</dd>
    </dl>
    <template #footer><ElButton @click="open = false">{{ t('objectSearch.close') }}</ElButton></template>
  </ElDrawer>
</template>
<style scoped>dt { color: var(--el-text-color-regular); margin-top: 16px; } dd { margin: 8px 0; overflow-wrap: anywhere; }</style>
