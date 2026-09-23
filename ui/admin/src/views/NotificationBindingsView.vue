<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue';
import {
  ElButton,
  ElCard,
  ElCheckbox,
  ElForm,
  ElFormItem,
  ElInput,
  ElMessage,
  ElOption,
  ElPagination,
  ElSelect,
  ElTable,
  ElTableColumn,
  ElTag,
  type FormInstance,
  type FormRules
} from 'element-plus';
import {
  isFullNetProblemDetails,
  type FullNetProblemDetails,
  type NotificationBindingResponse,
  type NotificationBindingTargetInput,
  type NotificationProviderProfileResponse
} from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import PermissionGate from '../components/PermissionGate.vue';
import { useArtPagedTableInCard } from '../framework/art-design/composables/useArtPagedTableInCard';
import {
  createNotificationBinding,
  listNotificationBindings,
  listNotificationProviderProfiles,
  publishNotificationBinding,
  updateNotificationBinding
} from '../api/notification-platform';

/** FanOut 必须显式列出目标并确认；Enabled Profile 不会自动进入扇出列表。 */
const session = useSessionStore();
const { t } = useAdminI18n();
const items = ref<NotificationBindingResponse[]>([]);
const profiles = ref<NotificationProviderProfileResponse[]>([]);
const page = ref(1);
const pageSize = ref(20);
const total = ref(0);
const selectedId = ref<string>();
const editorFormRef = ref<FormInstance>();
const editorForm = reactive({
  bindingKey: '',
  dispatchModeKey: 'single',
  producerKey: '',
  sceneKey: '',
  channelKey: '',
  targets: [] as NotificationBindingTargetInput[],
  fanOutAck: false
});
const targetProfileKey = ref('');
const loading = ref(false);
const { tableMainRef, tableHeight, syncTableLayout } = useArtPagedTableInCard(loading);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const canCreate = computed(() => session.can('notifications.bindings.create'));
const canUpdate = computed(() => session.can('notifications.bindings.update'));
const canPublish = computed(() => session.can('notifications.bindings.publish'));
const selected = computed(() => items.value.find(item => item.id === selectedId.value));
const isFanOut = computed(() => editorForm.dispatchModeKey === 'fan_out');
const showForm = computed(() => selected.value ? (canUpdate.value || canPublish.value) : canCreate.value);
const showSplitLayout = computed(() => showForm.value);
const editorRules = computed<FormRules>(() => {
  const requiredMessage = t('notificationBindings.validationRequired');
  const requiredTextRule = {
    validator: (_rule: unknown, value: unknown, callback: (error?: Error) => void) => {
      if (typeof value !== 'string' || !value.trim()) {
        callback(new Error(requiredMessage));
        return;
      }
      callback();
    },
    trigger: ['blur', 'change'] as const
  };
  const rules: FormRules = {
    dispatchModeKey: [requiredTextRule],
    producerKey: [requiredTextRule],
    sceneKey: [requiredTextRule],
    channelKey: [requiredTextRule],
    targets: [
      {
        validator: (_rule: unknown, value: unknown, callback: (error?: Error) => void) => {
          if (!Array.isArray(value) || value.length === 0) {
            callback(new Error(t('notificationBindings.validationTargetsRequired')));
            return;
          }
          callback();
        },
        trigger: 'change' as const
      }
    ],
    fanOutAck: [
      {
        validator: (_rule: unknown, value: unknown, callback: (error?: Error) => void) => {
          if (editorForm.dispatchModeKey !== 'fan_out') {
            callback();
            return;
          }
          if (value !== true) {
            callback(new Error(t('notificationBindings.validationFanOutAck')));
            return;
          }
          callback();
        },
        trigger: 'change' as const
      }
    ]
  };
  if (!selected.value) {
    rules.bindingKey = [requiredTextRule];
  }
  return rules;
});

onMounted(load);

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    const [bindingPage, profilePage] = await Promise.all([
      listNotificationBindings(page.value, pageSize.value),
      listNotificationProviderProfiles(1, 100)
    ]);
    items.value = bindingPage.items;
    page.value = bindingPage.page;
    pageSize.value = bindingPage.pageSize;
    total.value = bindingPage.total;
    profiles.value = profilePage.items;
  } catch (error: unknown) {
    problem.value = toProblem(error, 'notificationBindings.loadFailed');
  } finally {
    loading.value = false;
    void syncTableLayout();
  }
}

function selectItem(item: NotificationBindingResponse): void {
  selectedId.value = item.id;
  editorForm.bindingKey = item.bindingKey;
  editorForm.dispatchModeKey = item.draftDispatchModeKey;
  editorForm.fanOutAck = false;
  const draft = parseDraft(item.draftJson);
  editorForm.producerKey = draft.producerKey;
  editorForm.sceneKey = draft.sceneKey;
  editorForm.channelKey = draft.channelKey;
  editorForm.targets = draft.targets;
  clearEditorValidation();
}

function resetCreateForm(): void {
  selectedId.value = undefined;
  editorForm.bindingKey = '';
  editorForm.dispatchModeKey = 'single';
  editorForm.producerKey = '';
  editorForm.sceneKey = '';
  editorForm.channelKey = '';
  editorForm.targets = [];
  editorForm.fanOutAck = false;
  targetProfileKey.value = '';
  clearEditorValidation();
}

function onDispatchModeChange(): void {
  editorForm.fanOutAck = false;
  editorFormRef.value?.clearValidate(['fanOutAck']);
}

function passesEditorValidation(): boolean {
  if (!selected.value && !editorForm.bindingKey.trim()) {
    return false;
  }
  if (!editorForm.dispatchModeKey.trim()) {
    return false;
  }
  if (!editorForm.producerKey.trim() || !editorForm.sceneKey.trim() || !editorForm.channelKey.trim()) {
    return false;
  }
  if (editorForm.targets.length === 0) {
    return false;
  }
  if (editorForm.dispatchModeKey === 'fan_out' && !editorForm.fanOutAck) {
    return false;
  }
  return true;
}

async function showEditorValidationErrors(): Promise<void> {
  const form = editorFormRef.value;
  if (!form) {
    return;
  }
  const fields: string[] = ['dispatchModeKey', 'producerKey', 'sceneKey', 'channelKey', 'targets'];
  if (!selected.value) {
    fields.unshift('bindingKey');
  }
  if (editorForm.dispatchModeKey === 'fan_out') {
    fields.push('fanOutAck');
  }
  for (const field of fields) {
    try {
      await form.validateField(field);
    } catch {
      /* 单字段错误由表单项展示 */
    }
  }
}

async function validateEditorForm(): Promise<boolean> {
  const form = editorFormRef.value;
  if (!form) {
    return false;
  }
  let formValid = true;
  try {
    await form.validate();
  } catch {
    formValid = false;
  }
  const logicValid = passesEditorValidation();
  if (!logicValid) {
    await showEditorValidationErrors();
    return false;
  }
  return formValid;
}

function clearEditorValidation(): void {
  editorFormRef.value?.clearValidate();
}

function addTarget(): void {
  const profileKey = targetProfileKey.value.trim();
  if (!profileKey || editorForm.targets.some(item => item.profileKey === profileKey)) {
    return;
  }
  editorForm.targets = [...editorForm.targets, { profileKey, order: editorForm.targets.length + 1 }];
  targetProfileKey.value = '';
  void editorFormRef.value?.validateField('targets');
}

function removeTarget(profileKey: string): void {
  editorForm.targets = editorForm.targets
    .filter(item => item.profileKey !== profileKey)
    .map((item, index) => ({ ...item, order: index + 1 }));
  void editorFormRef.value?.validateField('targets');
}

function buildBody() {
  return {
    bindingKey: editorForm.bindingKey.trim(),
    dispatchModeKey: editorForm.dispatchModeKey,
    producerKey: editorForm.producerKey.trim(),
    sceneKey: editorForm.sceneKey.trim(),
    channelKey: editorForm.channelKey.trim(),
    targets: editorForm.targets
  };
}

async function createItem(): Promise<void> {
  if (changing.value || !(await validateEditorForm())) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    const saved = await createNotificationBinding(buildBody());
    ElMessage.success(t('notificationBindings.createSuccess'));
    selectItem(saved);
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error);
  } finally {
    changing.value = false;
  }
}

async function saveItem(): Promise<void> {
  const current = selected.value;
  if (!current || changing.value || !(await validateEditorForm())) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    const saved = await updateNotificationBinding(current.id, {
      ...buildBody(),
      version: current.version
    });
    ElMessage.success(t('notificationBindings.saveSuccess'));
    selectItem(saved);
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error);
  } finally {
    changing.value = false;
  }
}

async function publishItem(): Promise<void> {
  const current = selected.value;
  if (!current || changing.value) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    const saved = await publishNotificationBinding(current.id, current.version);
    ElMessage.success(t('notificationBindings.publishSuccess'));
    selectItem(saved);
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error);
  } finally {
    changing.value = false;
  }
}

function modeLabel(modeKey: string): string {
  switch (modeKey) {
    case 'single':
      return t('notificationBindings.modeSingle');
    case 'fan_out':
      return t('notificationBindings.modeFanOut');
    case 'failover':
      return t('notificationBindings.modeFailover');
    case 'match':
      return t('notificationBindings.modeMatch');
    default:
      return modeKey;
  }
}

function publishStateLabel(item: NotificationBindingResponse): string {
  return item.latestPublishedVersionNumber == null
    ? t('notificationTemplates.draftState')
    : `${t('notificationTemplates.publishedState')} v${item.latestPublishedVersionNumber}`;
}

function parseDraft(json: string): {
  producerKey: string;
  sceneKey: string;
  channelKey: string;
  targets: NotificationBindingTargetInput[];
} {
  try {
    const parsed = JSON.parse(json) as {
      producerKey?: unknown;
      sceneKey?: unknown;
      channelKey?: unknown;
      targets?: NotificationBindingTargetInput[];
    };
    return {
      producerKey: typeof parsed.producerKey === 'string' ? parsed.producerKey : '',
      sceneKey: typeof parsed.sceneKey === 'string' ? parsed.sceneKey : '',
      channelKey: typeof parsed.channelKey === 'string' ? parsed.channelKey : '',
      targets: Array.isArray(parsed.targets) ? parsed.targets : []
    };
  } catch {
    return { producerKey: '', sceneKey: '', channelKey: '', targets: [] };
  }
}

function toProblem(
  error: unknown,
  fallbackCode: 'notificationBindings.loadFailed' | 'notificationBindings.operationFailed'
    = 'notificationBindings.operationFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { status: 500, code: fallbackCode, title: t(fallbackCode) };
}
</script>

<template>
  <section class="notification-bindings-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('notificationBindings.title') }}</h1>
    <p class="art-sr-heading">{{ t('notificationBindings.description') }}</p>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
    </div>

    <div
      class="notification-bindings-layout art-split-layout"
      :class="{ 'is-list-only': !showSplitLayout }"
    >
      <ElCard class="notification-bindings-list art-table-card" shadow="never">
        <template #header>
          <div class="notification-bindings-list__header">
            <h2>{{ t('notificationBindings.listTitle') }}</h2>
            <PermissionGate code="notifications.bindings.create">
              <ElButton plain size="small" data-testid="notification-bindings-reset" @click="resetCreateForm">
                {{ t('notificationBindings.reset') }}
              </ElButton>
            </PermissionGate>
          </div>
        </template>

        <div ref="tableMainRef" class="art-crud-table-main">
        <ElTable
          v-loading="loading"
          :data="items"
          :height="tableHeight"
          class="notification-bindings-table"
          highlight-current-row
          row-key="id"
          empty-text=""
          @row-click="selectItem"
        >
          <ElTableColumn :label="t('notificationBindings.fieldKey')" min-width="180" show-overflow-tooltip>
            <template #default="{ row }">
              <button
                type="button"
                class="notification-bindings-table__key"
                data-testid="notification-bindings-load"
                translate="no"
                @click.stop="selectItem(row as NotificationBindingResponse)"
              >
                {{ row.bindingKey }}
              </button>
            </template>
          </ElTableColumn>

          <ElTableColumn :label="t('notificationBindings.fieldMode')" min-width="140">
            <template #default="{ row }">
              {{ modeLabel(row.draftDispatchModeKey) }}
            </template>
          </ElTableColumn>

          <ElTableColumn :label="t('users.status')" width="120" align="center">
            <template #default="{ row }">
              <ElTag
                size="small"
                :type="row.latestPublishedVersionNumber == null ? 'info' : 'success'"
              >
                {{ publishStateLabel(row as NotificationBindingResponse) }}
              </ElTag>
            </template>
          </ElTableColumn>

          <template #empty>
            <p class="art-empty-state">{{ t('notificationBindings.emptyList') }}</p>
          </template>
        </ElTable>

        <ElPagination
          v-if="total > 0"
          class="art-table-pagination"
          background
          layout="prev, pager, next, total"
          :current-page="page"
          :page-size="pageSize"
          :total="total"
          @current-change="value => { page = value; void load(); }"
        />
        </div>
      </ElCard>

      <ElCard v-if="showForm" class="notification-bindings-editor art-form-card" shadow="never" :aria-busy="changing">
        <template #header>
          <h2>{{ selected ? t('notificationBindings.editTitle') : t('notificationBindings.createTitle') }}</h2>
        </template>

        <ElForm
          ref="editorFormRef"
          :model="editorForm"
          :rules="editorRules"
          label-position="top"
          class="notification-bindings-editor__grid"
          @submit.prevent
        >
          <ElFormItem
            v-if="selected"
            :label="t('notificationBindings.fieldKey')"
            required
          >
            <ElInput
              v-model="editorForm.bindingKey"
              data-testid="notification-bindings-key"
              disabled
              maxlength="128"
            />
          </ElFormItem>
          <ElFormItem
            v-else
            prop="bindingKey"
            :label="t('notificationBindings.fieldKey')"
            required
          >
            <ElInput v-model="editorForm.bindingKey" data-testid="notification-bindings-key" maxlength="128" />
          </ElFormItem>
          <ElFormItem prop="dispatchModeKey" :label="t('notificationBindings.fieldMode')" required>
            <ElSelect
              v-model="editorForm.dispatchModeKey"
              data-testid="notification-bindings-mode"
              :teleported="false"
              @change="onDispatchModeChange"
            >
              <ElOption :label="t('notificationBindings.modeSingle')" value="single" />
              <ElOption :label="t('notificationBindings.modeFanOut')" value="fan_out" />
              <ElOption :label="t('notificationBindings.modeFailover')" value="failover" />
              <ElOption :label="t('notificationBindings.modeMatch')" value="match" />
            </ElSelect>
          </ElFormItem>
          <ElFormItem prop="producerKey" :label="t('notificationBindings.fieldProducer')" required>
            <ElInput v-model="editorForm.producerKey" data-testid="notification-bindings-producer" maxlength="128" />
          </ElFormItem>
          <ElFormItem prop="sceneKey" :label="t('notificationBindings.fieldScene')" required>
            <ElInput v-model="editorForm.sceneKey" data-testid="notification-bindings-scene" maxlength="128" />
          </ElFormItem>
          <ElFormItem prop="channelKey" :label="t('notificationBindings.fieldChannel')" required>
            <ElInput v-model="editorForm.channelKey" data-testid="notification-bindings-channel" maxlength="64" />
          </ElFormItem>

          <p class="notification-bindings-editor__full notification-bindings-editor__hint art-muted">
            {{ t('notificationBindings.modeHint') }}
          </p>

          <ElFormItem
            class="notification-bindings-editor__full notification-bindings-targets"
            prop="targets"
            :label="t('notificationBindings.addTarget')"
            required
          >
            <div class="notification-bindings-targets__row">
              <ElSelect
                v-model="targetProfileKey"
                data-testid="notification-bindings-target"
                :teleported="false"
                clearable
                filterable
              >
                <ElOption
                  v-for="item in profiles"
                  :key="item.id"
                  :label="item.profileKey"
                  :value="item.profileKey"
                />
              </ElSelect>
              <ElButton data-testid="notification-bindings-target-add" @click="addTarget">
                {{ t('notificationBindings.addTarget') }}
              </ElButton>
            </div>
            <ul v-if="editorForm.targets.length" class="notification-bindings-target-list">
              <li v-for="item in editorForm.targets" :key="item.profileKey">
                <span translate="no">{{ item.order }}. {{ item.profileKey }}</span>
                <ElButton link data-testid="notification-bindings-target-remove" @click="removeTarget(item.profileKey)">
                  {{ t('notificationBindings.removeTarget') }}
                </ElButton>
              </li>
            </ul>
          </ElFormItem>

          <div
            v-if="isFanOut"
            class="notification-bindings-editor__full art-inline-alert"
            data-testid="notification-bindings-fanout"
          >
            <p>{{ t('notificationBindings.fanOutWarning') }}</p>
            <ElFormItem prop="fanOutAck" class="notification-bindings-fanout-ack">
              <ElCheckbox v-model="editorForm.fanOutAck" data-testid="notification-bindings-fanout-ack">
                {{ t('notificationBindings.fanOutAck') }}
              </ElCheckbox>
            </ElFormItem>
          </div>

          <div class="notification-bindings-editor__full art-form-actions">
            <PermissionGate code="notifications.bindings.create">
              <ElButton
                v-if="!selected"
                data-testid="notification-bindings-create"
                type="primary"
                :disabled="changing"
                @click="createItem"
              >
                {{ t('notificationBindings.create') }}
              </ElButton>
            </PermissionGate>
            <PermissionGate code="notifications.bindings.update">
              <ElButton
                v-if="selected"
                data-testid="notification-bindings-save"
                type="primary"
                :disabled="changing"
                @click="saveItem"
              >
                {{ t('notificationBindings.save') }}
              </ElButton>
            </PermissionGate>
            <PermissionGate code="notifications.bindings.publish">
              <ElButton
                v-if="selected"
                data-testid="notification-bindings-publish"
                :disabled="changing"
                @click="publishItem"
              >
                {{ t('notificationBindings.publish') }}
              </ElButton>
            </PermissionGate>
            <ElButton plain data-testid="notification-bindings-reset-form" @click="resetCreateForm">
              {{ t('notificationBindings.reset') }}
            </ElButton>
          </div>
        </ElForm>
      </ElCard>
    </div>
  </section>
</template>

<style scoped>
.notification-bindings-view {
  min-height: 0;
}

.notification-bindings-layout {
  display: flex;
  flex: 1;
  gap: 12px;
  min-height: 0;
}

.notification-bindings-layout.is-list-only .notification-bindings-list {
  flex: 1;
}

.notification-bindings-list {
  flex: 1 1 0;
  display: flex;
  flex-direction: column;
  min-width: 0;
  min-height: 0;
}

.notification-bindings-list :deep(.el-card__body) {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
  padding-top: 0;
}

.notification-bindings-list :deep(.el-card__header) {
  padding: 12px 16px;
}

.notification-bindings-list__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

.notification-bindings-list__header h2 {
  margin: 0;
  font-size: 16px;
  font-weight: 600;
}

.notification-bindings-table {
  flex: 1;
  min-height: 200px;
}

.notification-bindings-table :deep(.el-table__row) {
  cursor: pointer;
}

.notification-bindings-table__key {
  padding: 0;
  border: none;
  background: none;
  color: var(--el-color-primary);
  font: inherit;
  text-align: left;
  cursor: pointer;
}

.notification-bindings-list__pagination {
  display: flex;
  justify-content: flex-end;
  padding-top: 12px;
}

.notification-bindings-editor {
  flex: 1 1 0;
  min-width: 0;
  min-height: 0;
  overflow: auto;
}

.notification-bindings-editor :deep(.el-card__header) {
  padding: 12px 16px;
}

.notification-bindings-editor :deep(.el-card__body) {
  padding: 12px 16px 16px;
}

.notification-bindings-editor__grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 10px 12px;
  align-items: start;
}

.notification-bindings-editor__grid :deep(.el-form-item) {
  margin-bottom: 0;
}

.notification-bindings-editor__grid :deep(.el-form-item__label) {
  padding-bottom: 4px;
  line-height: 1.3;
}

.notification-bindings-editor__full {
  grid-column: 1 / -1;
}

.notification-bindings-targets :deep(.el-form-item__content) {
  width: 100%;
}

.notification-bindings-fanout-ack {
  margin-bottom: 0;
}

.notification-bindings-fanout-ack :deep(.el-form-item__content) {
  margin-left: 0 !important;
}

.notification-bindings-editor__hint {
  margin: 0;
  font-size: 12px;
  line-height: 1.5;
}

.notification-bindings-targets__row {
  display: flex;
  flex-direction: row;
  align-items: center;
  gap: 8px;
  width: 100%;
}

.notification-bindings-targets__row :deep(.el-select) {
  flex: 1;
  min-width: 0;
}

.notification-bindings-targets__row .el-button {
  flex-shrink: 0;
}

.notification-bindings-target-list {
  margin: 10px 0 0;
  padding: 0;
  list-style: none;
}

.notification-bindings-target-list li {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  padding: 4px 0;
  border-bottom: 1px solid var(--el-border-color-lighter);
}

.notification-bindings-editor__grid .art-form-actions {
  margin-top: 4px;
  gap: 8px;
}
</style>
