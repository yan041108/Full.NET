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
  type NotificationProviderProfileResponse,
  type NotificationProviderTypeDescriptor
} from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import PermissionGate from '../components/PermissionGate.vue';
import {
  buildNonSecretConfig,
  createNotificationProviderProfile,
  disableNotificationProviderProfile,
  enableNotificationProviderProfile,
  listNotificationProviderProfiles,
  listNotificationProviderTypes,
  parseNonSecretConfigJson,
  publishNotificationProviderProfile,
  updateNotificationProviderProfile
} from '../api/notification-platform';

/** 空目录不得提供虚假类型；密钥输入永不回显已保存引用；启用不等于自动多发。 */
const session = useSessionStore();
const { t } = useAdminI18n();
const types = ref<NotificationProviderTypeDescriptor[]>([]);
const items = ref<NotificationProviderProfileResponse[]>([]);
const page = ref(1);
const pageSize = ref(20);
const total = ref(0);
const selectedId = ref<string>();
const profileKey = ref('');
const providerTypeKey = ref('');
const secretReference = ref('');
const fieldValues = ref<Record<string, string | number | boolean>>({});
const pendingAction = ref<'enable' | 'disable' | null>(null);
const configError = ref<string>();
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const canCreate = computed(() => session.can('notifications.provider_profiles.create'));
const canUpdate = computed(() => session.can('notifications.provider_profiles.update'));
const canEnable = computed(() => session.can('notifications.provider_profiles.enable'));
const canDisable = computed(() => session.can('notifications.provider_profiles.disable'));
const selected = computed(() => items.value.find(item => item.id === selectedId.value));
const selectedType = computed(() =>
  types.value.find(item => item.providerTypeKey === providerTypeKey.value)
);
const catalogEmpty = computed(() => types.value.length === 0);
const showForm = computed(() => {
  if (catalogEmpty.value) {
    return false;
  }

  // 只读用户仍需看到密钥状态；密钥引用输入只在可写时出现。
  return selected.value ? true : canCreate.value;
});

onMounted(load);

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    types.value = await listNotificationProviderTypes();
    const result = await listNotificationProviderProfiles(page.value, pageSize.value);
    items.value = result.items;
    page.value = result.page;
    pageSize.value = result.pageSize;
    total.value = result.total;
    if (!providerTypeKey.value && types.value[0]) {
      providerTypeKey.value = types.value[0].providerTypeKey;
    }
  } catch (error: unknown) {
    problem.value = toProblem(error, 'notificationProfiles.loadFailed');
  } finally {
    loading.value = false;
  }
}

function selectItem(item: NotificationProviderProfileResponse): void {
  selectedId.value = item.id;
  profileKey.value = item.profileKey;
  providerTypeKey.value = item.providerTypeKey;
  secretReference.value = '';
  pendingAction.value = null;
  configError.value = undefined;
  const descriptor = types.value.find(type => type.providerTypeKey === item.providerTypeKey);
  if (!descriptor) {
    fieldValues.value = {};
    configError.value = 'client.unknown_provider_type';
    problem.value = {
      status: 400,
      code: 'client.unknown_provider_type',
      title: t('notificationProfiles.unknownType')
    };
    return;
  }
  try {
    fieldValues.value = parseNonSecretConfigJson(item.nonSecretConfigJson, descriptor);
  } catch {
    fieldValues.value = {};
    configError.value = 'client.unknown_provider_config_field';
    problem.value = {
      status: 400,
      code: 'client.unknown_provider_config_field',
      title: t('notificationProfiles.unknownField')
    };
  }
}

function resetCreateForm(): void {
  selectedId.value = undefined;
  profileKey.value = '';
  secretReference.value = '';
  fieldValues.value = {};
  pendingAction.value = null;
  configError.value = undefined;
  problem.value = undefined;
  if (types.value[0]) {
    providerTypeKey.value = types.value[0].providerTypeKey;
  }
}

function setField(name: string, value: string | number | boolean): void {
  fieldValues.value = { ...fieldValues.value, [name]: value };
}

function currentConfig(): Record<string, string | number | boolean> {
  const descriptor = selectedType.value;
  if (!descriptor) {
    throw new Error('client.unknown_provider_type');
  }
  return buildNonSecretConfig(descriptor, fieldValues.value);
}

async function createItem(): Promise<void> {
  if (changing.value || !selectedType.value) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    const saved = await createNotificationProviderProfile({
      profileKey: profileKey.value.trim(),
      providerTypeKey: providerTypeKey.value,
      nonSecretConfig: currentConfig(),
      secretReference: secretReference.value.trim() || null
    });
    ElMessage.success(t('notificationProfiles.createSuccess'));
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
    const saved = await updateNotificationProviderProfile(current.id, {
      nonSecretConfig: currentConfig(),
      secretReference: secretReference.value.trim() || null,
      version: current.version
    });
    ElMessage.success(t('notificationProfiles.saveSuccess'));
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
    const saved = await publishNotificationProviderProfile(current.id, current.version);
    ElMessage.success(t('notificationProfiles.publishSuccess'));
    selectItem(saved);
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error);
  } finally {
    changing.value = false;
  }
}

async function confirmToggle(): Promise<void> {
  const current = selected.value;
  const action = pendingAction.value;
  if (!current || !action || changing.value) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    const saved = action === 'enable'
      ? await enableNotificationProviderProfile(current.id, current.version)
      : await disableNotificationProviderProfile(current.id, current.version);
    ElMessage.success(t(action === 'enable'
      ? 'notificationProfiles.enableSuccess'
      : 'notificationProfiles.disableSuccess'));
    pendingAction.value = null;
    selectItem(saved);
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error);
  } finally {
    changing.value = false;
  }
}

function toProblem(
  error: unknown,
  fallbackCode: 'notificationProfiles.loadFailed' | 'notificationProfiles.operationFailed'
    = 'notificationProfiles.operationFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { status: 500, code: fallbackCode, title: t(fallbackCode) };
}
</script>

<template>
  <section class="notification-profiles-view art-page-stack art-full-height" :aria-busy="loading">
    <header class="art-page-header">
      <p class="art-eyebrow">{{ t('notificationProfiles.eyebrow') }}</p>
      <h1 data-route-heading tabindex="-1">{{ t('notificationProfiles.title') }}</h1>
      <p>{{ t('notificationProfiles.description') }}</p>
    </header>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
    </div>

    <ElCard v-if="catalogEmpty" class="art-card" data-testid="notification-profiles-empty-catalog">
      <p>{{ t('notificationProfiles.emptyCatalog') }}</p>
    </ElCard>

    <ElCard v-if="showForm" class="art-card" :aria-busy="changing">
      <template #header>
        <h2>{{ selected ? t('notificationProfiles.editTitle') : t('notificationProfiles.createTitle') }}</h2>
      </template>
      <div class="art-form-grid">
        <label>
          <span>{{ t('notificationProfiles.fieldKey') }}</span>
          <ElInput v-model="profileKey" data-testid="notification-profiles-key" :disabled="!!selected" maxlength="128" />
        </label>
        <label>
          <span>{{ t('notificationProfiles.fieldType') }}</span>
          <ElSelect v-model="providerTypeKey" data-testid="notification-profiles-type" :disabled="!!selected" :teleported="false">
            <ElOption
              v-for="item in types"
              :key="item.providerTypeKey"
              :label="item.providerTypeKey"
              :value="item.providerTypeKey"
            />
          </ElSelect>
        </label>
        <template v-if="selectedType && !configError">
          <label v-for="field in selectedType.nonSecretFields" :key="field.name">
            <span>{{ field.name }}</span>
            <ElCheckbox
              v-if="field.typeKey === 'boolean'"
              :model-value="fieldValues[field.name] === true"
              :disabled="!!selected && !canUpdate"
              :data-testid="`notification-profiles-field-${field.name}`"
              @update:model-value="value => setField(field.name, value === true)"
            />
            <ElInput
              v-else
              :model-value="String(fieldValues[field.name] ?? '')"
              :type="field.typeKey === 'integer' ? 'number' : 'text'"
              :disabled="!!selected && !canUpdate"
              :data-testid="`notification-profiles-field-${field.name}`"
              @update:model-value="value => setField(field.name, value)"
            />
          </label>
        </template>
        <p v-if="selected" data-testid="notification-profiles-secret-status">
          {{ t('notificationProfiles.secretStatus') }}:
          <ElTag :type="selected.secretStatus === 'configured' ? 'success' : 'info'">
            {{ selected.secretStatus }}
          </ElTag>
        </p>
        <label v-if="canCreate || canUpdate">
          <span>{{ t('notificationProfiles.fieldSecretReference') }}</span>
          <ElInput
            v-model="secretReference"
            data-testid="notification-profiles-secret-reference"
            maxlength="256"
            :placeholder="t('notificationProfiles.secretReferencePlaceholder')"
          />
        </label>
        <div v-if="pendingAction" class="art-inline-alert" data-testid="notification-profiles-confirm">
          <p>{{ pendingAction === 'enable' ? t('notificationProfiles.enableConfirm') : t('notificationProfiles.disableConfirm') }}</p>
          <ElButton data-testid="notification-profiles-confirm-yes" type="primary" :disabled="changing" @click="confirmToggle">
            {{ t('notificationProfiles.confirm') }}
          </ElButton>
          <ElButton data-testid="notification-profiles-confirm-no" plain @click="pendingAction = null">
            {{ t('notificationProfiles.cancel') }}
          </ElButton>
        </div>
        <div class="art-form-actions">
          <PermissionGate code="notifications.provider_profiles.create">
            <ElButton v-if="!selected" data-testid="notification-profiles-create" type="primary" :disabled="changing" @click="createItem">
              {{ t('notificationProfiles.create') }}
            </ElButton>
          </PermissionGate>
          <PermissionGate code="notifications.provider_profiles.update">
            <ElButton v-if="selected" data-testid="notification-profiles-save" type="primary" :disabled="changing" @click="saveItem">
              {{ t('notificationProfiles.save') }}
            </ElButton>
          </PermissionGate>
          <PermissionGate code="notifications.provider_profiles.publish">
            <ElButton v-if="selected" data-testid="notification-profiles-publish" :disabled="changing" @click="publishItem">
              {{ t('notificationProfiles.publish') }}
            </ElButton>
          </PermissionGate>
          <PermissionGate code="notifications.provider_profiles.enable">
            <ElButton
              v-if="selected && !selected.isEnabled"
              data-testid="notification-profiles-enable"
              :disabled="changing"
              @click="pendingAction = 'enable'"
            >
              {{ t('notificationProfiles.enable') }}
            </ElButton>
          </PermissionGate>
          <PermissionGate code="notifications.provider_profiles.disable">
            <ElButton
              v-if="selected && selected.isEnabled"
              data-testid="notification-profiles-disable"
              type="warning"
              plain
              :disabled="changing"
              @click="pendingAction = 'disable'"
            >
              {{ t('notificationProfiles.disable') }}
            </ElButton>
          </PermissionGate>
          <ElButton plain data-testid="notification-profiles-reset" @click="resetCreateForm">
            {{ t('notificationProfiles.reset') }}
          </ElButton>
        </div>
      </div>
    </ElCard>

    <ElCard class="art-card">
      <template #header>
        <h2>{{ t('notificationProfiles.listTitle') }}</h2>
      </template>
      <p v-if="!items.length" class="art-empty-state">{{ t('notificationProfiles.emptyList') }}</p>
      <ul v-else class="art-list">
        <li v-for="item in items" :key="item.id">
          <button type="button" data-testid="notification-profiles-load" :class="{ 'is-active': selectedId === item.id }" @click="selectItem(item)">
            <strong>{{ item.profileKey }}</strong>
            <span class="art-muted">{{ item.providerTypeKey }}</span>
            <ElTag :type="item.isEnabled ? 'success' : 'info'">
              {{ item.isEnabled ? t('notificationProfiles.statusEnabled') : t('notificationProfiles.statusDisabled') }}
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
        @current-change="value => { page = value; void load(); }"
      />
    </ElCard>
  </section>
</template>
