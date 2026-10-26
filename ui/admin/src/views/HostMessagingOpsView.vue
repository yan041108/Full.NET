<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { ElCard, ElEmpty, ElTabPane, ElTabs } from 'element-plus';
import PermissionGate from '../components/PermissionGate.vue';
import { usePermission } from '../auth/permission';
import { useAdminI18n } from '../i18n/adminI18n';
import HostMessagingDeadLettersPanel from './HostMessagingDeadLettersPanel.vue';
import HostMessagingOverviewPanel from './HostMessagingOverviewPanel.vue';
import HostMessagingReplayPanel from './HostMessagingReplayPanel.vue';

defineOptions({ name: 'HostMessagingOpsView' });
const { can } = usePermission();
const { t } = useAdminI18n();
const mayEnter = computed(() => can('messaging.operations.read'));
const mayReadEvents = computed(() => mayEnter.value && can('messaging.events.read'));
const mayReadDeadLetters = computed(() => mayEnter.value && can('messaging.dead_letters.read'));
type Area = 'overview' | 'legacy' | 'kafka' | 'operations';
const area = ref<Area>('overview');
watch([mayEnter, mayReadEvents, mayReadDeadLetters], () => {
  if ((area.value === 'overview' || area.value === 'operations') && !mayReadEvents.value)
    area.value = mayReadDeadLetters.value ? 'legacy' : 'overview';
  if ((area.value === 'legacy' || area.value === 'kafka') && !mayReadDeadLetters.value)
    area.value = 'overview';
}, { immediate: true });
</script>

<template>
  <PermissionGate code="messaging.operations.read">
    <section class="host-messaging-ops-view art-page-stack art-full-height">
      <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('navigation.hostMessagingOps.title') }}</h1>
      <p class="art-page-caption">{{ t('messagingOps.caption') }}</p>
      <ElCard shadow="never" class="messaging-area-tabs">
        <ElTabs v-model="area">
          <ElTabPane v-if="mayReadEvents" :label="t('messagingOps.overview')" name="overview" />
          <ElTabPane v-if="mayReadDeadLetters" :label="t('messagingOps.legacy')" name="legacy" />
          <ElTabPane v-if="mayReadDeadLetters" :label="t('messagingOps.kafka')" name="kafka" />
          <ElTabPane v-if="mayReadEvents" :label="t('messagingOps.operations')" name="operations" />
        </ElTabs>
      </ElCard>
      <HostMessagingOverviewPanel v-if="mayReadEvents && area === 'overview'" />
      <HostMessagingDeadLettersPanel v-else-if="mayReadDeadLetters && (area === 'legacy' || area === 'kafka')" :key="area" :source="area" />
      <HostMessagingReplayPanel v-else-if="mayReadEvents && area === 'operations'" />
      <ElEmpty v-else :description="t('messagingOps.noReadableArea')" />
    </section>
  </PermissionGate>
</template>

<style scoped>
.messaging-area-tabs { flex: 0 0 auto; }
.messaging-area-tabs :deep(.el-tabs__header) { margin-bottom: 0; }
</style>
