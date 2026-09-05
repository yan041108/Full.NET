<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import {
  ElButton,
  ElCard,
  ElInput,
  ElMessage,
  ElOption,
  ElPagination,
  ElSelect,
  ElTag
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
const templateKey = ref('');
const localeTag = ref('zh-CN');
const defaultLocaleTag = ref('zh-CN');
const channelKey = ref('inbox');
const contentCategoryKey = ref('transactional');
const draftSubject = ref('');
const draftBody = ref('');
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
const showForm = computed(() => selected.value ? (canUpdate.value || canPublish.value) : canCreate.value);

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
  templateKey.value = item.templateKey;
  localeTag.value = item.localeTag;
  defaultLocaleTag.value = item.defaultLocaleTag;
  channelKey.value = item.channelKey;
  contentCategoryKey.value = item.contentCategoryKey;
  draftSubject.value = item.draftSubject;
  draftBody.value = readDraftText(item.draftBodyJson);
  parameters.value = readParameters(item.draftParameterSchemaJson);
}

function resetCreateForm(): void {
  selectedId.value = undefined;
  templateKey.value = '';
  localeTag.value = 'zh-CN';
  defaultLocaleTag.value = 'zh-CN';
  channelKey.value = 'inbox';
  contentCategoryKey.value = 'transactional';
  draftSubject.value = '';
  draftBody.value = '';
  parameters.value = [];
  parameterName.value = '';
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

async function createItem(): Promise<void> {
  if (changing.value) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    const saved = await createNotificationTemplate({
      templateKey: templateKey.value.trim(),
      localeTag: localeTag.value,
      defaultLocaleTag: defaultLocaleTag.value,
      channelKey: channelKey.value,
      contentCategoryKey: contentCategoryKey.value,
      draftSubject: draftSubject.value.trim(),
      draftBody: { text: draftBody.value },
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
  if (!current || changing.value) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    const saved = await updateNotificationTemplate(current.id, {
      draftSubject: draftSubject.value.trim(),
      draftBody: { text: draftBody.value },
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
    <header class="art-page-header">
      <p class="art-eyebrow">{{ t('notificationTemplates.eyebrow') }}</p>
      <h1 data-route-heading tabindex="-1">{{ t('notificationTemplates.title') }}</h1>
      <p>{{ t('notificationTemplates.description') }}</p>
    </header>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <ElCard v-if="showForm" class="art-card" :aria-busy="changing">
      <template #header>
        <h2>{{ selected ? t('notificationTemplates.editTitle') : t('notificationTemplates.createTitle') }}</h2>
      </template>
      <div class="art-form-grid">
        <label>
          <span>{{ t('notificationTemplates.fieldKey') }}</span>
          <ElInput v-model="templateKey" data-testid="notification-templates-key" :disabled="!!selected" maxlength="128" />
        </label>
        <label>
          <span>{{ t('notificationTemplates.fieldLocale') }}</span>
          <ElSelect v-model="localeTag" data-testid="notification-templates-locale" :disabled="!!selected">
            <ElOption v-for="option in localeOptions" :key="option" :label="option" :value="option" />
          </ElSelect>
        </label>
        <label v-if="!selected">
          <span>{{ t('notificationTemplates.fieldDefaultLocale') }}</span>
          <ElSelect v-model="defaultLocaleTag" data-testid="notification-templates-default-locale">
            <ElOption v-for="option in localeOptions" :key="option" :label="option" :value="option" />
          </ElSelect>
        </label>
        <label>
          <span>{{ t('notificationTemplates.fieldChannel') }}</span>
          <ElSelect v-model="channelKey" data-testid="notification-templates-channel" :disabled="!!selected">
            <ElOption v-for="option in channelOptions" :key="option" :label="option" :value="option" />
          </ElSelect>
        </label>
        <label>
          <span>{{ t('notificationTemplates.fieldCategory') }}</span>
          <ElSelect v-model="contentCategoryKey" data-testid="notification-templates-category">
            <ElOption :label="t('notificationTemplates.categoryMandatory')" value="mandatory" />
            <ElOption :label="t('notificationTemplates.categoryTransactional')" value="transactional" />
            <ElOption :label="t('notificationTemplates.categoryInformational')" value="informational" />
            <ElOption :label="t('notificationTemplates.categoryMarketing')" value="marketing" />
          </ElSelect>
        </label>
        <ElInput v-model="draftSubject" data-testid="notification-templates-subject" maxlength="200" :placeholder="t('notificationTemplates.fieldSubject')" />
        <ElInput v-model="draftBody" type="textarea" :rows="4" data-testid="notification-templates-body" :placeholder="t('notificationTemplates.fieldBody')" />
        <div class="notification-templates-params">
          <ElInput v-model="parameterName" data-testid="notification-templates-parameter-name" maxlength="64" :placeholder="t('notificationTemplates.fieldParameterName')" />
          <ElSelect v-model="parameterTypeKey" data-testid="notification-templates-parameter-type">
            <ElOption label="string" value="string" />
            <ElOption label="integer" value="integer" />
            <ElOption label="boolean" value="boolean" />
          </ElSelect>
          <ElButton data-testid="notification-templates-parameter-add" @click="addParameter">
            {{ t('notificationTemplates.addParameter') }}
          </ElButton>
        </div>
        <ul v-if="parameters.length" class="art-list">
          <li v-for="item in parameters" :key="item.name">
            <span translate="no">{{ item.name }}</span>
            <ElTag>{{ item.typeKey }}</ElTag>
            <ElButton link data-testid="notification-templates-parameter-remove" @click="removeParameter(item.name)">
              {{ t('notificationTemplates.removeParameter') }}
            </ElButton>
          </li>
        </ul>
        <label v-if="selected && canPublish">
          <span>{{ t('notificationTemplates.fieldClassification') }}</span>
          <ElSelect v-model="classificationKey" data-testid="notification-templates-classification">
            <ElOption label="c0" value="c0" />
            <ElOption label="c1" value="c1" />
            <ElOption label="s2" value="s2" />
          </ElSelect>
        </label>
        <div
          v-if="selected && (selected.publishedLocaleTags.length || selected.missingLocaleTags.length)"
          class="notification-templates-locale-hints"
          data-testid="notification-templates-locale-hints"
        >
          <p v-if="selected.publishedLocaleTags.length">
            <strong>{{ t('notificationTemplates.publishedLocales') }}:</strong>
            <ElTag
              v-for="tag in selected.publishedLocaleTags"
              :key="tag"
              data-testid="notification-templates-published-locale"
              type="success"
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
            >
              {{ tag }}
            </ElTag>
            <span class="art-muted">{{ t('notificationTemplates.missingLocalesHint') }}</span>
          </p>
        </div>
        <div class="art-form-actions">
          <PermissionGate code="notifications.templates.create">
            <ElButton v-if="!selected" data-testid="notification-templates-create" type="primary" :disabled="changing" @click="createItem">
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
          <ElButton plain data-testid="notification-templates-reset" :disabled="changing" @click="resetCreateForm">
            {{ t('notificationTemplates.reset') }}
          </ElButton>
        </div>
      </div>
    </ElCard>

    <ElCard class="art-card">
      <template #header>
        <h2>{{ t('notificationTemplates.listTitle') }}</h2>
      </template>
      <p v-if="!items.length" class="art-empty-state">{{ t('notificationTemplates.emptyList') }}</p>
      <ul v-else class="art-list">
        <li v-for="item in items" :key="item.id">
          <button type="button" data-testid="notification-templates-load" :class="{ 'is-active': selectedId === item.id }" @click="selectItem(item)">
            <strong>{{ item.templateKey }}</strong>
            <ElTag data-testid="notification-templates-locale-tag">{{ item.localeTag }}</ElTag>
            <span class="art-muted">{{ item.channelKey }}</span>
            <ElTag>{{ item.contentCategoryKey }}</ElTag>
            <ElTag
              data-testid="notification-templates-state"
              :type="item.latestPublishedVersionNumber == null ? 'info' : 'success'"
            >
              {{ item.latestPublishedVersionNumber == null
                ? t('notificationTemplates.draftState')
                : `${t('notificationTemplates.publishedState')} v${item.latestPublishedVersionNumber}` }}
            </ElTag>
          </button>
        </li>
      </ul>
      <ElPagination
        v-if="total > 0"
        background
        layout="prev, pager, next, total"
        :current-page="page"
        :page-size="pageSize"
        :total="total"
        @current-change="onPageChange"
      />
    </ElCard>
  </section>
</template>

<style scoped>
.notification-templates-params {
  display: flex;
  gap: 8px;
  flex-wrap: wrap;
}

.notification-templates-locale-hints p {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: center;
  margin: 0 0 8px;
}
</style>
