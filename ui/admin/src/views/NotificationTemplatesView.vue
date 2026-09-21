<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue';
import {
  ElButton,
  ElCard,
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
  type NotificationTemplateParameterDefinition,
  type NotificationTemplateResponse
} from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import PermissionGate from '../components/PermissionGate.vue';
import {
  createNotificationTemplate,
  listNotificationProviderTypes,
  listNotificationTemplates,
  publishNotificationTemplate,
  updateNotificationTemplate
} from '../api/notification-platform';

/** 生产未安装 Provider 时渠道只允许 inbox；参数类型闭合为 string/integer/boolean。 */
const session = useSessionStore();
const { t } = useAdminI18n();
const items = ref<NotificationTemplateResponse[]>([]);
const page = ref(1);
const pageSize = ref(20);
const total = ref(0);
const selectedId = ref<string>();
const editorFormRef = ref<FormInstance>();
const editorForm = reactive({
  templateKey: '',
  localeTag: 'zh-CN',
  defaultLocaleTag: 'zh-CN',
  channelKey: 'inbox',
  contentCategoryKey: 'transactional',
  draftSubject: '',
  draftBody: ''
});
const parameterName = ref('');
const parameterTypeKey = ref('string');
const parameterRequired = ref(false);
const parameters = ref<NotificationTemplateParameterDefinition[]>([]);
const classificationKey = ref('c1');
const channelOptions = ref<string[]>(['inbox']);
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const canCreate = computed(() => session.can('notifications.templates.create'));
const canUpdate = computed(() => session.can('notifications.templates.update'));
const canPublish = computed(() => session.can('notifications.templates.publish'));
const localeOptions = ['zh-CN', 'en-US'] as const;
const selected = computed(() => items.value.find(item => item.id === selectedId.value));
const showForm = computed(() => (selected.value ? (canUpdate.value || canPublish.value) : canCreate.value));
const showSplitLayout = computed(() => showForm.value);
const editorRules = computed<FormRules>(() => {
  const message = t('notificationTemplates.validationRequired');
  const requiredTextRule = {
    validator: (_rule: unknown, value: unknown, callback: (error?: Error) => void) => {
      if (typeof value !== 'string' || !value.trim()) {
        callback(new Error(message));
        return;
      }
      callback();
    },
    trigger: ['blur', 'change'] as const
  };
  const rules: FormRules = {
    draftSubject: [requiredTextRule],
    draftBody: [requiredTextRule]
  };
  if (!selected.value) {
    rules.templateKey = [requiredTextRule];
  }
  return rules;
});

onMounted(load);

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    const types = await listNotificationProviderTypes();
    const channels = new Set<string>(['inbox']);
    for (const type of types) {
      for (const channel of type.supportedChannelKeys) {
        channels.add(channel);
      }
    }
    channelOptions.value = [...channels];
    const result = await listNotificationTemplates(page.value, pageSize.value);
    items.value = result.items;
    page.value = result.page;
    pageSize.value = result.pageSize;
    total.value = result.total;
  } catch (error: unknown) {
    problem.value = toProblem(error, 'notificationTemplates.loadFailed');
  } finally {
    loading.value = false;
  }
}

function selectItem(item: NotificationTemplateResponse): void {
  selectedId.value = item.id;
  editorForm.templateKey = item.templateKey;
  editorForm.localeTag = item.localeTag;
  editorForm.defaultLocaleTag = item.defaultLocaleTag;
  editorForm.channelKey = item.channelKey;
  editorForm.contentCategoryKey = item.contentCategoryKey;
  editorForm.draftSubject = item.draftSubject;
  editorForm.draftBody = readDraftText(item.draftBodyJson);
  parameters.value = readParameters(item.draftParameterSchemaJson);
  clearEditorValidation();
}

function resetCreateForm(): void {
  selectedId.value = undefined;
  editorForm.templateKey = '';
  editorForm.localeTag = 'zh-CN';
  editorForm.defaultLocaleTag = 'zh-CN';
  editorForm.channelKey = 'inbox';
  editorForm.contentCategoryKey = 'transactional';
  editorForm.draftSubject = '';
  editorForm.draftBody = '';
  parameters.value = [];
  parameterName.value = '';
  clearEditorValidation();
}

async function validateEditorForm(): Promise<boolean> {
  const form = editorFormRef.value;
  if (!form) {
    return false;
  }

  try {
    await form.validate();
    return true;
  } catch {
    return false;
  }
}

function clearEditorValidation(): void {
  editorFormRef.value?.clearValidate();
}

function addParameter(): void {
  const name = parameterName.value.trim();
  if (!name || parameters.value.some(item => item.name === name)) {
    return;
  }
  parameters.value = [
    ...parameters.value,
    {
      name,
      typeKey: parameterTypeKey.value,
      required: parameterRequired.value,
      maxLength: parameterTypeKey.value === 'string' ? 128 : null
    }
  ];
  parameterName.value = '';
}

function removeParameter(name: string): void {
  parameters.value = parameters.value.filter(item => item.name !== name);
}

function publishStateLabel(item: NotificationTemplateResponse): string {
  return item.latestPublishedVersionNumber == null
    ? t('notificationTemplates.draftState')
    : `${t('notificationTemplates.publishedState')} v${item.latestPublishedVersionNumber}`;
}

async function createItem(): Promise<void> {
  if (changing.value || !(await validateEditorForm())) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    const saved = await createNotificationTemplate({
      templateKey: editorForm.templateKey.trim(),
      localeTag: editorForm.localeTag,
      defaultLocaleTag: editorForm.defaultLocaleTag,
      channelKey: editorForm.channelKey,
      contentCategoryKey: editorForm.contentCategoryKey,
      draftSubject: editorForm.draftSubject.trim(),
      draftBody: { text: editorForm.draftBody },
      parameterSchema: { schemaVersion: 1, parameters: parameters.value }
    });
    ElMessage.success(t('notificationTemplates.createSuccess'));
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
    const saved = await updateNotificationTemplate(current.id, {
      draftSubject: editorForm.draftSubject.trim(),
      draftBody: { text: editorForm.draftBody },
      parameterSchema: { schemaVersion: 1, parameters: parameters.value },
      version: current.version
    });
    ElMessage.success(t('notificationTemplates.saveSuccess'));
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
    const saved = await publishNotificationTemplate(current.id, {
      version: current.version,
      contentClassificationKey: classificationKey.value
    });
    ElMessage.success(t('notificationTemplates.publishSuccess'));
    selectItem(saved);
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error);
  } finally {
    changing.value = false;
  }
}

async function onPageChange(nextPage: number): Promise<void> {
  page.value = nextPage;
  await load();
}

function readDraftText(json: string): string {
  try {
    const parsed = JSON.parse(json) as { text?: unknown };
    return typeof parsed.text === 'string' ? parsed.text : '';
  } catch {
    return '';
  }
}

function readParameters(json: string): NotificationTemplateParameterDefinition[] {
  try {
    const parsed = JSON.parse(json) as { parameters?: NotificationTemplateParameterDefinition[] };
    return Array.isArray(parsed.parameters) ? parsed.parameters : [];
  } catch {
    return [];
  }
}

function toProblem(
  error: unknown,
  fallbackCode: 'notificationTemplates.loadFailed' | 'notificationTemplates.operationFailed'
    = 'notificationTemplates.operationFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { status: 500, code: fallbackCode, title: t(fallbackCode) };
}
</script>

<template>
  <section class="notification-templates-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('notificationTemplates.title') }}</h1>
    <p class="art-sr-heading">{{ t('notificationTemplates.description') }}</p>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <div
      class="notification-templates-layout"
      :class="{ 'is-list-only': !showSplitLayout }"
    >
      <ElCard class="notification-templates-list art-table-card" shadow="never">
        <template #header>
          <div class="notification-templates-list__header">
            <h2>{{ t('notificationTemplates.listTitle') }}</h2>
            <PermissionGate code="notifications.templates.create">
              <ElButton plain size="small" data-testid="notification-templates-reset" @click="resetCreateForm">
                {{ t('notificationTemplates.createTitle') }}
              </ElButton>
            </PermissionGate>
          </div>
        </template>

        <ElTable
          v-loading="loading"
          :data="items"
          class="notification-templates-table"
          highlight-current-row
          row-key="id"
          empty-text=""
          @row-click="selectItem"
        >
          <ElTableColumn :label="t('notificationTemplates.fieldKey')" min-width="200" show-overflow-tooltip>
            <template #default="{ row }">
              <button
                type="button"
                class="notification-templates-table__key"
                data-testid="notification-templates-load"
                translate="no"
                @click.stop="selectItem(row)"
              >
                {{ row.templateKey }}
              </button>
            </template>
          </ElTableColumn>

          <ElTableColumn :label="t('notificationTemplates.fieldLocale')" width="96" align="center">
            <template #default="{ row }">
              <ElTag size="small" data-testid="notification-templates-locale-tag">{{ row.localeTag }}</ElTag>
            </template>
          </ElTableColumn>

          <ElTableColumn :label="t('notificationTemplates.fieldChannel')" width="88" prop="channelKey" />

          <ElTableColumn :label="t('notificationTemplates.fieldCategory')" width="120" align="center">
            <template #default="{ row }">
              <ElTag size="small" type="info">{{ row.contentCategoryKey }}</ElTag>
            </template>
          </ElTableColumn>

          <ElTableColumn :label="t('users.status')" min-width="120">
            <template #default="{ row }">
              <ElTag
                size="small"
                data-testid="notification-templates-state"
                :type="row.latestPublishedVersionNumber == null ? 'info' : 'success'"
              >
                {{ publishStateLabel(row) }}
              </ElTag>
            </template>
          </ElTableColumn>

          <template #empty>
            <p class="art-empty-state">{{ t('notificationTemplates.emptyList') }}</p>
          </template>
        </ElTable>

        <div v-if="total > 0" class="notification-templates-list__pagination">
          <ElPagination
            background
            layout="prev, pager, next, total"
            :current-page="page"
            :page-size="pageSize"
            :total="total"
            @current-change="onPageChange"
          />
        </div>
      </ElCard>

      <ElCard v-if="showForm" class="notification-templates-editor art-form-card" shadow="never" :aria-busy="changing">
        <template #header>
          <h2>{{ selected ? t('notificationTemplates.editTitle') : t('notificationTemplates.createTitle') }}</h2>
        </template>

        <ElForm
          ref="editorFormRef"
          :model="editorForm"
          :rules="editorRules"
          label-position="top"
          class="notification-templates-editor__grid"
          @submit.prevent
        >
          <ElFormItem
            v-if="selected"
            :label="t('notificationTemplates.fieldKey')"
            required
          >
            <ElInput v-model="editorForm.templateKey" data-testid="notification-templates-key" disabled maxlength="128" />
          </ElFormItem>
          <ElFormItem
            v-else
            prop="templateKey"
            :label="t('notificationTemplates.fieldKey')"
            required
          >
            <ElInput v-model="editorForm.templateKey" data-testid="notification-templates-key" maxlength="128" />
          </ElFormItem>
          <ElFormItem :label="t('notificationTemplates.fieldLocale')" required>
            <ElSelect v-model="editorForm.localeTag" data-testid="notification-templates-locale" :disabled="!!selected">
              <ElOption v-for="option in localeOptions" :key="option" :label="option" :value="option" />
            </ElSelect>
          </ElFormItem>
          <ElFormItem v-if="selected" :label="t('notificationTemplates.fieldChannel')" required>
            <ElSelect v-model="editorForm.channelKey" data-testid="notification-templates-channel" disabled>
              <ElOption v-for="option in channelOptions" :key="option" :label="option" :value="option" />
            </ElSelect>
          </ElFormItem>
          <ElFormItem v-else :label="t('notificationTemplates.fieldDefaultLocale')" required>
            <ElSelect v-model="editorForm.defaultLocaleTag" data-testid="notification-templates-default-locale">
              <ElOption v-for="option in localeOptions" :key="option" :label="option" :value="option" />
            </ElSelect>
          </ElFormItem>
          <ElFormItem v-if="!selected" :label="t('notificationTemplates.fieldChannel')" required>
            <ElSelect v-model="editorForm.channelKey" data-testid="notification-templates-channel">
              <ElOption v-for="option in channelOptions" :key="option" :label="option" :value="option" />
            </ElSelect>
          </ElFormItem>
          <ElFormItem :label="t('notificationTemplates.fieldCategory')" required>
            <ElSelect v-model="editorForm.contentCategoryKey" data-testid="notification-templates-category">
              <ElOption :label="t('notificationTemplates.categoryMandatory')" value="mandatory" />
              <ElOption :label="t('notificationTemplates.categoryTransactional')" value="transactional" />
              <ElOption :label="t('notificationTemplates.categoryInformational')" value="informational" />
              <ElOption :label="t('notificationTemplates.categoryMarketing')" value="marketing" />
            </ElSelect>
          </ElFormItem>
          <ElFormItem v-if="selected && canPublish" :label="t('notificationTemplates.fieldClassification')" required>
            <ElSelect v-model="classificationKey" data-testid="notification-templates-classification">
              <ElOption label="c0" value="c0" />
              <ElOption label="c1" value="c1" />
              <ElOption label="s2" value="s2" />
            </ElSelect>
          </ElFormItem>
          <ElFormItem
            class="notification-templates-editor__full"
            prop="draftSubject"
            :label="t('notificationTemplates.fieldSubject')"
            required
          >
            <ElInput v-model="editorForm.draftSubject" data-testid="notification-templates-subject" maxlength="200" />
          </ElFormItem>
          <ElFormItem
            class="notification-templates-editor__full"
            prop="draftBody"
            :label="t('notificationTemplates.fieldBody')"
            required
          >
            <ElInput v-model="editorForm.draftBody" type="textarea" :rows="4" data-testid="notification-templates-body" />
          </ElFormItem>

          <ElFormItem class="notification-templates-editor__full notification-templates-params" :label="t('notificationTemplates.addParameter')">
            <div class="notification-templates-params__row">
              <ElInput
                v-model="parameterName"
                data-testid="notification-templates-parameter-name"
                maxlength="64"
                :placeholder="t('notificationTemplates.fieldParameterName')"
              />
              <ElSelect v-model="parameterTypeKey" data-testid="notification-templates-parameter-type">
                <ElOption label="string" value="string" />
                <ElOption label="integer" value="integer" />
                <ElOption label="boolean" value="boolean" />
              </ElSelect>
              <ElButton data-testid="notification-templates-parameter-add" @click="addParameter">
                {{ t('notificationTemplates.addParameter') }}
              </ElButton>
            </div>
            <ul v-if="parameters.length" class="notification-templates-param-list">
              <li v-for="item in parameters" :key="item.name">
                <span translate="no">{{ item.name }}</span>
                <ElTag size="small">{{ item.typeKey }}</ElTag>
                <ElButton link data-testid="notification-templates-parameter-remove" @click="removeParameter(item.name)">
                  {{ t('notificationTemplates.removeParameter') }}
                </ElButton>
              </li>
            </ul>
          </ElFormItem>

          <div
            v-if="selected && (selected.publishedLocaleTags.length || selected.missingLocaleTags.length)"
            class="notification-templates-editor__full notification-templates-locale-hints"
            data-testid="notification-templates-locale-hints"
          >
            <p v-if="selected.publishedLocaleTags.length">
              <strong>{{ t('notificationTemplates.publishedLocales') }}:</strong>
              <ElTag
                v-for="tag in selected.publishedLocaleTags"
                :key="tag"
                data-testid="notification-templates-published-locale"
                type="success"
                size="small"
              >
                {{ tag }}
              </ElTag>
            </p>
            <p v-if="selected.missingLocaleTags.length">
              <strong>{{ t('notificationTemplates.missingLocales') }}:</strong>
              <ElTag
                v-for="tag in selected.missingLocaleTags"
                :key="tag"
                data-testid="notification-templates-missing-locale"
                type="warning"
                size="small"
              >
                {{ tag }}
              </ElTag>
              <span class="art-muted">{{ t('notificationTemplates.missingLocalesHint') }}</span>
            </p>
          </div>

          <div class="notification-templates-editor__full art-form-actions">
            <PermissionGate code="notifications.templates.create">
              <ElButton
                v-if="!selected"
                data-testid="notification-templates-create"
                type="primary"
                :disabled="changing"
                @click="createItem"
              >
                {{ t('notificationTemplates.create') }}
              </ElButton>
            </PermissionGate>
            <PermissionGate code="notifications.templates.update">
              <ElButton v-if="selected" data-testid="notification-templates-save" type="primary" :disabled="changing" @click="saveItem">
                {{ t('notificationTemplates.save') }}
              </ElButton>
            </PermissionGate>
            <PermissionGate code="notifications.templates.publish">
              <ElButton v-if="selected" data-testid="notification-templates-publish" :disabled="changing" @click="publishItem">
                {{ t('notificationTemplates.publish') }}
              </ElButton>
            </PermissionGate>
          </div>
        </ElForm>
      </ElCard>
    </div>
  </section>
</template>

<style scoped>
.notification-templates-view {
  min-height: 0;
}

.notification-templates-layout {
  display: flex;
  flex: 1;
  gap: 12px;
  min-height: 0;
}

.notification-templates-layout.is-list-only .notification-templates-list {
  flex: 1;
}

.notification-templates-list {
  flex: 1 1 0;
  display: flex;
  flex-direction: column;
  min-width: 0;
  min-height: 0;
}

.notification-templates-list :deep(.el-card__body) {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
  padding-top: 0;
}

.notification-templates-list__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

.notification-templates-list__header h2 {
  margin: 0;
  font-size: 16px;
  font-weight: 600;
}

.notification-templates-table {
  flex: 1;
  min-height: 200px;
}

.notification-templates-table :deep(.el-table__row) {
  cursor: pointer;
}

.notification-templates-table__key {
  padding: 0;
  border: none;
  background: none;
  color: var(--el-color-primary);
  font: inherit;
  text-align: left;
  cursor: pointer;
}

.notification-templates-list__pagination {
  display: flex;
  justify-content: flex-end;
  padding-top: 12px;
}

.notification-templates-editor {
  flex: 1 1 0;
  min-width: 0;
  min-height: 0;
}

.notification-templates-editor :deep(.el-card__header) {
  padding: 12px 16px;
}

.notification-templates-editor :deep(.el-card__body) {
  padding: 12px 16px 16px;
}

.notification-templates-list :deep(.el-card__header) {
  padding: 12px 16px;
}

.notification-templates-editor__grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 10px 12px;
  align-items: start;
}

.notification-templates-editor__grid :deep(.el-form-item) {
  margin-bottom: 0;
}

.notification-templates-editor__grid :deep(.el-form-item__label) {
  padding-bottom: 4px;
  line-height: 1.3;
}

.notification-templates-editor__full {
  grid-column: 1 / -1;
}

.notification-templates-params :deep(.el-form-item__content) {
  width: 100%;
}

.notification-templates-params__row {
  display: flex;
  flex-direction: row;
  align-items: center;
  gap: 8px;
  width: 100%;
}

.notification-templates-params__row :deep(.el-input) {
  flex: 1;
  min-width: 0;
}

.notification-templates-params__row :deep(.el-select) {
  width: 132px;
  flex-shrink: 0;
}

.notification-templates-params__row .el-button {
  flex-shrink: 0;
}

.notification-templates-param-list {
  margin: 12px 0 0;
  padding: 0;
  list-style: none;
}

.notification-templates-param-list li {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  padding: 4px 0;
  border-bottom: 1px solid var(--el-border-color-lighter);
}

.notification-templates-locale-hints {
  font-size: 12px;
}

.notification-templates-locale-hints p {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  align-items: center;
  margin: 0 0 4px;
}

.notification-templates-editor__grid .art-form-actions {
  margin-top: 4px;
  gap: 8px;
}
</style>
