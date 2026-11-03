<script setup lang="ts">
import { computed, inject, onActivated, onBeforeUnmount, onDeactivated, ref, watch } from 'vue';
import { routeLocationKey, routerKey } from 'vue-router';
import { ElAlert, ElButton, ElDrawer } from 'element-plus';
import type { HostDocumentItemResponse } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { getDocumentItem } from '../api/host-document-items';
import { isSearchObjectId } from '../search/objectSearch';

const route = inject(routeLocationKey, undefined), router = inject(routerKey, undefined);
const session = useSessionStore(), { t } = useAdminI18n();
const active = ref(true), open = ref(false), loading = ref(false), failed = ref(false), item = ref<HostDocumentItemResponse>();
const context = computed(() => [session.currentUser?.id, session.currentUser?.tenantId, session.currentUser?.sessionId,
  session.currentUser?.permissions.join('|')].join('|'));
let generation = 0, controller: AbortController | undefined;
function reset() { generation++; controller?.abort(); item.value = undefined; loading.value = false; failed.value = false; open.value = false; }
function clearRoute() {
  if (!route || !router || route.name !== 'host-document-items' || route.query.documentId === undefined) return;
  const { documentId: _documentId, ...query } = route.query; void router.replace({ query });
}
function close() { reset(); clearRoute(); }
watch(() => [route?.name, route?.query.documentId, context.value, session.switching, active.value] as const, (value, previous) => {
  reset();
  if (previous && previous[2] !== value[2]) { clearRoute(); return; }
  if (value[0] !== 'host-document-items' || !isSearchObjectId(value[1]) || !value[4] || value[3]
    || !session.currentUser || session.currentUser.tenantId || !session.can('document.host_documents.read')) return;
  void load(value[1]);
}, { immediate: true, flush: 'sync' });
async function load(id: string) {
  const requestGeneration = generation, request = new AbortController(); controller = request; open.value = true; loading.value = true;
  try {
    const value = await getDocumentItem(id, request.signal);
    if (requestGeneration === generation && !request.signal.aborted) item.value = value;
  } catch { if (requestGeneration === generation && !request.signal.aborted) failed.value = true; }
  finally { if (requestGeneration === generation) loading.value = false; }
}
onActivated(() => { active.value = true; }); onDeactivated(() => { active.value = false; reset(); }); onBeforeUnmount(reset);
</script>
<template>
  <ElDrawer :model-value="open" :title="t('objectSearch.detail')" size="min(640px, 100vw)" data-testid="document-search-detail" @update:model-value="value => { if (!value) close(); }">
    <p v-if="loading" role="status">{{ t('objectSearch.loading') }}</p>
    <ElAlert v-else-if="failed" type="error" :closable="false" :title="t('objectSearch.detailFailed')" />
    <dl v-else-if="item">
      <dt>{{ t('hostDocumentItems.titleLabel') }}</dt><dd data-testid="document-search-detail-title">{{ item.title }}</dd>
      <dt>{{ t('objectSearch.number') }}</dt><dd>{{ item.documentNo }}</dd>
      <dt>{{ t('hostDocumentItems.descriptionLabel') }}</dt><dd>{{ item.description }}</dd>
    </dl>
    <template #footer><ElButton @click="close">{{ t('objectSearch.close') }}</ElButton></template>
  </ElDrawer>
</template>
<style scoped>
dt { color: var(--el-text-color-regular); margin-top: 16px; } dd { margin: 8px 0; overflow-wrap: anywhere; white-space: pre-wrap; }
</style>
