<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue';
import {
  ElAlert, ElButton, ElCard, ElDialog, ElForm, ElFormItem, ElInput,
  ElInputNumber, ElMessageBox, ElOption, ElPagination, ElSelect,
  ElSwitch, ElTable, ElTableColumn, ElTag
} from 'element-plus';
import type {
  NotificationProducerSceneResponse, NotificationQuotaPolicyResponse, NotificationQuotaOutstandingResponse
} from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { showProblem, showSuccess, showWarning } from '../feedback/fullNetMessage';
import ArtTableActionGroup from '../framework/art-design/components/ArtTableActionGroup.vue';
import ArtTableActionButton from '../framework/art-design/components/ArtTableActionButton.vue';
import { ART_TABLE_ACTION_COLUMN_WIDTH } from '../framework/art-design/components/artTableActions';
import { useArtPagedTableInCard } from '../framework/art-design/composables/useArtPagedTableInCard';
import {
  listNotificationProducerScenes, createNotificationProducerScene, updateNotificationProducerScene,
  setNotificationProducerSceneEnabled, listNotificationQuotaPolicies, getNotificationQuotaOutstanding,
  createNotificationQuotaPolicy, updateNotificationQuotaPolicy, setNotificationQuotaPolicyEnabled
} from '../api/notification-platform';

/** 两个已发布控制面共享列表与 CAS 编辑交互，不共享业务身份或数据库访问。 */
const props = defineProps<{ kind: 'scenes' | 'quotas' }>();
type Row = NotificationProducerSceneResponse | NotificationQuotaPolicyResponse;
const session = useSessionStore();
const { t } = useAdminI18n();
const scenes = computed(() => props.kind === 'scenes');
const title = computed(() => t(scenes.value ? 'notificationGovernance.scenes' : 'notificationGovernance.quotas'));
const canRead = computed(() => session.can(scenes.value ? 'notifications.producer_scenes.read' : 'notifications.quotas.read'));
const canCreate = computed(() => session.can(scenes.value ? 'notifications.producer_scenes.create' : 'notifications.quotas.create'));
const canUpdate = computed(() => session.can(scenes.value ? 'notifications.producer_scenes.update' : 'notifications.quotas.update'));
const canSetEnabled = computed(() => session.can(scenes.value ? 'notifications.producer_scenes.set_enabled' : 'notifications.quotas.set_enabled'));
const items = ref<Row[]>([]);
const outstanding = ref<NotificationQuotaOutstandingResponse[]>([]);
const total = ref(0);
const page = ref(1);
const loading = ref(false);
const changing = ref(false);
const loadFailed = ref(false);
const open = ref(false);
const editing = ref<Row>();
const canSave = computed(() => canRead.value && (editing.value ? canUpdate.value : canCreate.value));
let loadGeneration = 0;
const { tableMainRef, tableHeight, syncTableLayout } = useArtPagedTableInCard(loading);
const form = reactive({
  key1: '', key2: '', category: 'transactional', channels: 'inbox', emergency: false,
  dimension: 'provider', value: '*', windowSeconds: 60, maxCount: 100
});
// 同一组件承载两条路由，旧页面的异步响应不能覆盖新控制面。
watch(() => props.kind, () => {
  loadGeneration += 1;
  loading.value = false;
  items.value = [];
  outstanding.value = [];
  total.value = 0;
  page.value = 1;
  open.value = false;
  editing.value = undefined;
  void load();
}, { immediate: true });

async function load(): Promise<void> {
  if (!canRead.value || loading.value || changing.value) return;
  const generation = ++loadGeneration;
  loading.value = true;
  loadFailed.value = false;
  try {
    if (scenes.value) {
      const result = await listNotificationProducerScenes(page.value, 20);
      if (generation !== loadGeneration || !canRead.value) return;
      items.value = result.items;
      total.value = result.total;
    } else {
      const [result, ledger] = await Promise.all([
        listNotificationQuotaPolicies(page.value, 20), getNotificationQuotaOutstanding()
      ]);
      if (generation !== loadGeneration || !canRead.value) return;
      items.value = result.items;
      total.value = result.total;
      outstanding.value = ledger;
    }
  } catch {
    if (generation !== loadGeneration) return;
    loadFailed.value = true;
    items.value = [];
    outstanding.value = [];
  } finally {
    if (generation === loadGeneration) {
      loading.value = false;
      void syncTableLayout();
    }
  }
}

function edit(row?: Row): void {
  if (!canRead.value || !(row ? canUpdate.value : canCreate.value) || changing.value) return;
  editing.value = row;
  Object.assign(form, {
    key1: '', key2: '', category: 'transactional', channels: 'inbox', emergency: false,
    dimension: 'provider', value: '*', windowSeconds: 60, maxCount: 100
  });
  if (row && 'producerKey' in row) {
    Object.assign(form, { key1: row.producerKey, key2: row.sceneKey, category: row.policyCategoryKey,
      channels: row.allowedChannelKeys.join(', '), emergency: row.emergencyOverrideAllowed });
  } else if (row) {
    Object.assign(form, { dimension: row.dimensionKey, value: row.dimensionValue,
      windowSeconds: row.windowSeconds, maxCount: row.maxCount });
  }
  open.value = true;
}

async function save(): Promise<void> {
  if (!canSave.value || changing.value) return;
  const channels = form.channels.split(',').map(value => value.trim()).filter(Boolean);
  if (scenes.value
    ? !form.key1.trim() || !form.key2.trim() || !channels.length || new Set(channels).size !== channels.length
    : !form.value.trim() || !Number.isSafeInteger(form.windowSeconds) || form.windowSeconds < 1
      || !Number.isSafeInteger(form.maxCount) || form.maxCount < 1) {
    showWarning(t('notificationGovernance.invalid'));
    return;
  }
  changing.value = true;
  try {
    const row = editing.value;
    if (scenes.value) {
      const body = { policyCategoryKey: form.category, allowedChannelKeys: channels, emergencyOverrideAllowed: form.emergency };
      if (row) {
        await updateNotificationProducerScene(row.id, { ...body, version: row.version });
      } else {
        await createNotificationProducerScene({ ...body, producerKey: form.key1.trim(), sceneKey: form.key2.trim() });
      }
    } else if (row) {
      await updateNotificationQuotaPolicy(row.id, { windowSeconds: form.windowSeconds, maxCount: form.maxCount, version: row.version });
    } else {
      await createNotificationQuotaPolicy({ dimensionKey: form.dimension, dimensionValue: form.value.trim(),
        windowSeconds: form.windowSeconds, maxCount: form.maxCount });
    }
    open.value = false;
    showSuccess(t('notificationGovernance.saved'));
  } catch (error) {
    showProblem(error, t('notificationGovernance.failed'));
    return;
  } finally {
    changing.value = false;
  }
  await load();
}

async function toggle(row: Row): Promise<void> {
  if (!canRead.value || !canSetEnabled.value || changing.value) return;
  const kind = props.kind;
  try {
    await ElMessageBox.confirm(t('notificationGovernance.confirmToggle'), title.value, {
      confirmButtonText: t('notificationGovernance.confirm'), cancelButtonText: t('notificationGovernance.cancel')
    });
  } catch { return; }
  // 确认期间可能撤权或切换控制面，不能把旧页面的行标识交给新的管理入口。
  if (props.kind !== kind || !canRead.value || !canSetEnabled.value || changing.value) return;
  changing.value = true;
  try {
    const body = { isEnabled: !row.isEnabled, version: row.version };
    if (scenes.value) await setNotificationProducerSceneEnabled(row.id, body);
    else await setNotificationQuotaPolicyEnabled(row.id, body);
    showSuccess(t('notificationGovernance.saved'));
  } catch (error) {
    showProblem(error, t('notificationGovernance.failed'));
    return;
  } finally { changing.value = false; }
  await load();
}
</script>

<template>
  <section class="art-page-stack art-full-height notification-governance">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ title }}</h1>
    <ElAlert :title="t(scenes ? 'notificationGovernance.sceneHint' : 'notificationGovernance.quotaHint')" type="info" :closable="false" />
    <ElAlert v-if="loadFailed" :title="t('notificationGovernance.loadFailed')" type="error" :closable="false" />
    <ElCard class="art-table-card" shadow="never">
      <template #header>
        <ElButton v-if="canRead" :loading="loading" :disabled="changing" @click="load">{{ t('notificationPreferences.refresh') }}</ElButton>
        <ElButton v-if="canRead && canCreate" type="primary" :disabled="changing" data-testid="governance-create" @click="edit()">{{ t('notificationGovernance.create') }}</ElButton>
      </template>
      <p v-if="!scenes" class="art-muted">{{ t('notificationGovernance.ledgerHint') }}</p>
      <div v-if="!scenes" class="ledger">
        <ElTag v-for="entry in outstanding" :key="`${entry.dimensionKey}:${entry.stateKey}`" type="info">
          <span translate="no">{{ entry.dimensionKey }} / {{ entry.stateKey }}: {{ entry.count }}</span>
        </ElTag>
      </div>
      <div ref="tableMainRef" class="art-crud-table-main">
        <ElTable :data="items" :height="tableHeight" row-key="id">
          <ElTableColumn :label="t('notificationGovernance.identity')" min-width="220">
            <template #default="{ row }"><span translate="no">{{ scenes ? `${row.producerKey} / ${row.sceneKey}` : `${row.dimensionKey} / ${row.dimensionValue}` }}</span></template>
          </ElTableColumn>
          <ElTableColumn :label="t('notificationGovernance.policy')" min-width="220">
            <template #default="{ row }"><span translate="no">{{ scenes ? `${row.policyCategoryKey}: ${row.allowedChannelKeys.join(', ')}` : `${row.maxCount} / ${row.windowSeconds}s` }}</span></template>
          </ElTableColumn>
          <ElTableColumn :label="t('notificationGovernance.enabled')" width="100">
            <template #default="{ row }">{{ row.isEnabled ? t('notificationGovernance.yes') : t('notificationGovernance.no') }}</template>
          </ElTableColumn>
          <ElTableColumn v-if="canRead && (canUpdate || canSetEnabled)" :label="t('notificationPreferences.actions')" :width="ART_TABLE_ACTION_COLUMN_WIDTH">
            <template #default="{ row }">
              <ArtTableActionGroup>
                <ArtTableActionButton v-if="canUpdate" type="edit" :title="t('notificationGovernance.edit')" test-id="governance-edit" @click="edit(row as Row)" />
                <ArtTableActionButton v-if="canSetEnabled" type="view" :title="t('notificationGovernance.toggle')" test-id="governance-toggle" @click="toggle(row as Row)" />
              </ArtTableActionGroup>
            </template>
          </ElTableColumn>
        </ElTable>
        <ElPagination v-if="canRead" v-model:current-page="page" :total="total" :page-size="20" :disabled="changing || loading"
          layout="prev, pager, next" class="art-table-pagination" @current-change="load" />
      </div>
    </ElCard>
    <ElDialog v-model="open" :title="title" :close-on-click-modal="!changing" :show-close="!changing">
      <ElForm label-position="top" :disabled="changing || !canSave" @submit.prevent="save">
        <template v-if="scenes">
          <ElFormItem :label="t('notificationGovernance.producer')" :required="!editing"><ElInput v-model="form.key1" :disabled="Boolean(editing)" data-testid="governance-producer" /></ElFormItem>
          <ElFormItem :label="t('notificationGovernance.scene')" :required="!editing"><ElInput v-model="form.key2" :disabled="Boolean(editing)" data-testid="governance-scene" /></ElFormItem>
          <ElFormItem :label="t('notificationGovernance.category')" required>
            <ElSelect v-model="form.category"><ElOption v-for="category in ['mandatory', 'transactional', 'informational', 'marketing']" :key="category" :value="category" :label="category" /></ElSelect>
          </ElFormItem>
          <ElFormItem :label="t('notificationGovernance.channels')" required><ElInput v-model="form.channels" data-testid="governance-channels" /></ElFormItem>
          <ElFormItem :label="t('notificationGovernance.emergency')"><ElSwitch v-model="form.emergency" /></ElFormItem>
        </template>
        <template v-else>
          <ElFormItem :label="t('notificationGovernance.dimension')" :required="!editing">
            <ElSelect v-model="form.dimension" :disabled="Boolean(editing)"><ElOption v-for="dimension in ['provider', 'tenant', 'producer', 'scene']" :key="dimension" :value="dimension" :label="dimension" /></ElSelect>
          </ElFormItem>
          <ElFormItem :label="t('notificationGovernance.value')" :required="!editing"><ElInput v-model="form.value" :disabled="Boolean(editing)" data-testid="governance-value" /></ElFormItem>
          <ElFormItem :label="t('notificationGovernance.window')" required><ElInputNumber v-model="form.windowSeconds" :min="1" :max="2147483647" :precision="0" /></ElFormItem>
          <ElFormItem :label="t('notificationGovernance.limit')" required><ElInputNumber v-model="form.maxCount" :min="1" :max="2147483647" :precision="0" /></ElFormItem>
        </template>
      </ElForm>
      <template #footer>
        <ElButton :disabled="changing" @click="open = false">{{ t('notificationGovernance.cancel') }}</ElButton>
        <ElButton v-if="canSave" :loading="changing" type="primary" data-testid="governance-save" @click="save">{{ t('notificationGovernance.save') }}</ElButton>
      </template>
    </ElDialog>
  </section>
</template>

<style scoped>
.notification-governance > .el-card { flex: 1; min-height: 0; }
.notification-governance :deep(.el-card__body) { display: flex; flex-direction: column; min-height: 0; }
.notification-governance .ledger { display: flex; gap: 8px; flex-wrap: wrap; margin-bottom: 8px; }
</style>
