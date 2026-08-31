<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import {
  ElButton,
  ElCard,
  ElCheckbox,
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
  type NotificationBindingResponse,
  type NotificationBindingTargetInput,
  type NotificationProviderProfileResponse
} from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import PermissionGate from '../components/PermissionGate.vue';
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
const bindingKey = ref('');
const dispatchModeKey = ref('single');
const producerKey = ref('');
const sceneKey = ref('');
const channelKey = ref('');
const targetProfileKey = ref('');
const targets = ref<NotificationBindingTargetInput[]>([]);
const fanOutAck = ref(false);
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const canCreate = computed(() => session.can('notifications.bindings.create'));
const canUpdate = computed(() => session.can('notifications.bindings.update'));
const canPublish = computed(() => session.can('notifications.bindings.publish'));
const selected = computed(() => items.value.find(item => item.id === selectedId.value));
const isFanOut = computed(() => dispatchModeKey.value === 'fan_out');
const showForm = computed(() => selected.value ? (canUpdate.value || canPublish.value) : canCreate.value);
const canSubmitFanOut = computed(() => !isFanOut.value || fanOutAck.value);

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
  }
}

function selectItem(item: NotificationBindingResponse): void {
  selectedId.value = item.id;
  bindingKey.value = item.bindingKey;
  dispatchModeKey.value = item.draftDispatchModeKey;
  fanOutAck.value = false;
  const draft = parseDraft(item.draftJson);
  producerKey.value = draft.producerKey;
  sceneKey.value = draft.sceneKey;
  channelKey.value = draft.channelKey;
  targets.value = draft.targets;
}

function resetCreateForm(): void {
  selectedId.value = undefined;
  bindingKey.value = '';
  dispatchModeKey.value = 'single';
  producerKey.value = '';
  sceneKey.value = '';
  channelKey.value = '';
  targetProfileKey.value = '';
  targets.value = [];
  fanOutAck.value = false;
}

function addTarget(): void {
  const profileKey = targetProfileKey.value.trim();
  if (!profileKey || targets.value.some(item => item.profileKey === profileKey)) {
    return;
  }
  targets.value = [...targets.value, { profileKey, order: targets.value.length + 1 }];
  targetProfileKey.value = '';
}

function removeTarget(profileKey: string): void {
  targets.value = targets.value
    .filter(item => item.profileKey !== profileKey)
    .map((item, index) => ({ ...item, order: index + 1 }));
}

function buildBody() {
  return {
    bindingKey: bindingKey.value.trim(),
    dispatchModeKey: dispatchModeKey.value,
    producerKey: producerKey.value.trim(),
    sceneKey: sceneKey.value.trim(),
    channelKey: channelKey.value.trim(),
    targets: targets.value
  };
}

async function createItem(): Promise<void> {
  if (changing.value || !canSubmitFanOut.value) {
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
  if (!current || changing.value || !canSubmitFanOut.value) {
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
  if (!current || changing.value || !canSubmitFanOut.value) {
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
    <header class="art-page-header">
      <p class="art-eyebrow">{{ t('notificationBindings.eyebrow') }}</p>
      <h1 data-route-heading tabindex="-1">{{ t('notificationBindings.title') }}</h1>
      <p>{{ t('notificationBindings.description') }}</p>
    </header>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
    </div>

    <ElCard v-if="showForm" class="art-card" :aria-busy="changing">
      <template #header>
        <h2>{{ selected ? t('notificationBindings.editTitle') : t('notificationBindings.createTitle') }}</h2>
      </template>
      <div class="art-form-grid">
        <ElInput v-model="bindingKey" data-testid="notification-bindings-key" :disabled="!!selected" maxlength="128" :placeholder="t('notificationBindings.fieldKey')" />
        <label>
          <span>{{ t('notificationBindings.fieldMode') }}</span>
          <ElSelect v-model="dispatchModeKey" data-testid="notification-bindings-mode" :teleported="false" @change="fanOutAck = false">
            <ElOption :label="t('notificationBindings.modeSingle')" value="single" />
            <ElOption :label="t('notificationBindings.modeFanOut')" value="fan_out" />
            <ElOption :label="t('notificationBindings.modeFailover')" value="failover" />
            <ElOption :label="t('notificationBindings.modeMatch')" value="match" />
          </ElSelect>
        </label>
        <p class="art-muted">{{ t('notificationBindings.modeHint') }}</p>
        <ElInput v-model="producerKey" data-testid="notification-bindings-producer" maxlength="128" :placeholder="t('notificationBindings.fieldProducer')" />
        <ElInput v-model="sceneKey" data-testid="notification-bindings-scene" maxlength="128" :placeholder="t('notificationBindings.fieldScene')" />
        <ElInput v-model="channelKey" data-testid="notification-bindings-channel" maxlength="64" :placeholder="t('notificationBindings.fieldChannel')" />
        <div>
          <ElSelect v-model="targetProfileKey" data-testid="notification-bindings-target" :teleported="false" clearable filterable>
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
        <ul class="art-list">
          <li v-for="item in targets" :key="item.profileKey">
            <span translate="no">{{ item.order }}. {{ item.profileKey }}</span>
            <ElButton link data-testid="notification-bindings-target-remove" @click="removeTarget(item.profileKey)">
              {{ t('notificationBindings.removeTarget') }}
            </ElButton>
          </li>
        </ul>
        <div v-if="isFanOut" class="art-inline-alert" data-testid="notification-bindings-fanout">
          <p>{{ t('notificationBindings.fanOutWarning') }}</p>
          <ElCheckbox v-model="fanOutAck" data-testid="notification-bindings-fanout-ack">
            {{ t('notificationBindings.fanOutAck') }}
          </ElCheckbox>
        </div>
        <div class="art-form-actions">
          <PermissionGate code="notifications.bindings.create">
            <ElButton
              v-if="!selected"
              data-testid="notification-bindings-create"
              type="primary"
              :disabled="changing || !canSubmitFanOut"
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
              :disabled="changing || !canSubmitFanOut"
              @click="saveItem"
            >
              {{ t('notificationBindings.save') }}
            </ElButton>
          </PermissionGate>
          <PermissionGate code="notifications.bindings.publish">
            <ElButton
              v-if="selected"
              data-testid="notification-bindings-publish"
              :disabled="changing || !canSubmitFanOut"
              @click="publishItem"
            >
              {{ t('notificationBindings.publish') }}
            </ElButton>
          </PermissionGate>
          <ElButton plain data-testid="notification-bindings-reset" @click="resetCreateForm">
            {{ t('notificationBindings.reset') }}
          </ElButton>
        </div>
      </div>
    </ElCard>

    <ElCard class="art-card">
      <template #header>
        <h2>{{ t('notificationBindings.listTitle') }}</h2>
      </template>
      <p v-if="!items.length" class="art-empty-state">{{ t('notificationBindings.emptyList') }}</p>
      <ul v-else class="art-list">
        <li v-for="item in items" :key="item.id">
          <button type="button" data-testid="notification-bindings-load" :class="{ 'is-active': selectedId === item.id }" @click="selectItem(item)">
            <strong>{{ item.bindingKey }}</strong>
            <span class="art-muted">{{ item.draftDispatchModeKey }}</span>
            <ElTag v-if="item.latestPublishedVersionNumber">v{{ item.latestPublishedVersionNumber }}</ElTag>
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
        @current-change="value => { page = value; void load(); }"
      />
    </ElCard>
  </section>
</template>
