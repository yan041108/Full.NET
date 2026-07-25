<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue';
import {
  ElButton,
  ElCard,
  ElInput,
  ElMessage,
  ElMessageBox,
  ElOption,
  ElSelect,
  ElTag
} from 'element-plus';
import {
  HOST_MENU_ASSIGNABLE_PERMISSIONS,
  HOST_MENU_COMPONENT_OPTIONS,
  type FullNetProblemDetails
} from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  createHostMenu,
  disableHostMenu,
  listHostMenus,
  updateHostMenu
} from '../api/menus';
import type { HostMenu } from '@fullnet/client-contracts';

const session = useSessionStore();
const { t } = useAdminI18n();
const menus = ref<HostMenu[]>([]);
const routeName = ref('');
const componentKey = ref('overview');
const path = ref('/');
const title = ref('');
const caption = ref('');
const icon = ref('grid');
const displayOrder = ref(50);
const requiredPermission = ref<string>(HOST_MENU_ASSIGNABLE_PERMISSIONS[0]);
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const componentOptions = HOST_MENU_COMPONENT_OPTIONS;
const assignablePermissions = HOST_MENU_ASSIGNABLE_PERMISSIONS;
const canWrite = computed(() => session.can('identity.menus.write'));

watch(componentKey, value => {
  const entry = componentOptions.find(option => option.componentKey === value);
  path.value = entry?.path ?? '/';
});

onMounted(load);

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    const page = await listHostMenus();
    menus.value = page.items;
  } catch (error: unknown) {
    problem.value = toProblem(error, 'menus.loadFailed');
  } finally {
    loading.value = false;
  }
}

async function create(): Promise<void> {
  if (changing.value || !routeName.value.trim() || !title.value.trim()) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    await createHostMenu({
      parentId: null,
      routeName: routeName.value.trim(),
      path: path.value,
      componentKey: componentKey.value,
      title: title.value.trim(),
      caption: caption.value.trim() || title.value.trim(),
      icon: icon.value.trim() || 'grid',
      displayOrder: displayOrder.value,
      requiredPermission: requiredPermission.value
    });
    routeName.value = '';
    title.value = '';
    caption.value = '';
    ElMessage.success(t('menus.createSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'menus.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function edit(menu: HostMenu): Promise<void> {
  if (changing.value || menu.isSystem) return;
  try {
    const result = await ElMessageBox.prompt(
      t('menus.editTitle'),
      t('menus.edit'),
      {
        inputValue: menu.title,
        inputPattern: /.+/,
        showCancelButton: true
      }
    );
    changing.value = true;
    await updateHostMenu(menu.id, {
      parentId: menu.parentId,
      path: menu.path,
      componentKey: menu.componentKey,
      title: result.value.trim(),
      caption: menu.caption,
      icon: menu.icon,
      displayOrder: menu.displayOrder,
      requiredPermission: menu.requiredPermission,
      version: menu.version
    });
    ElMessage.success(t('menus.updateSuccess'));
    await load();
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') return;
    problem.value = toProblem(error, 'menus.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function disable(menu: HostMenu): Promise<void> {
  if (changing.value || !menu.isActive || menu.isSystem) return;
  try {
    await ElMessageBox.confirm(
      t('menus.confirmDisable', { name: menu.routeName }),
      t('menus.disable'),
      { type: 'warning', confirmButtonText: t('menus.disable'), cancelButtonText: t('status.back') }
    );
    changing.value = true;
    await disableHostMenu(menu.id);
    ElMessage.success(t('menus.disableSuccess'));
    await load();
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') return;
    problem.value = toProblem(error, 'menus.operationFailed');
  } finally {
    changing.value = false;
  }
}

function toProblem(
  error: unknown,
  fallbackKey: 'menus.loadFailed' | 'menus.operationFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { status: 500, code: 'client.host_menu_failed', title: t(fallbackKey) };
}
</script>

<template>
  <section class="menus-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('menus.title') }}</h1>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <el-card v-if="canWrite" class="art-form-card" shadow="never">
      <div class="art-form-grid" aria-labelledby="create-title">
        <div><h2 id="create-title">{{ t('menus.createTitle') }}</h2></div>
        <label>
          <span>{{ t('menus.routeName') }}</span>
          <el-input v-model="routeName" :placeholder="t('menus.routeNamePlaceholder')" />
        </label>
        <label>
          <span>{{ t('menus.componentKey') }}</span>
          <el-select v-model="componentKey">
            <el-option v-for="option in componentOptions" :key="option.componentKey" :label="option.componentKey" :value="option.componentKey" />
          </el-select>
        </label>
        <label>
          <span>{{ t('menus.titleField') }}</span>
          <el-input v-model="title" :placeholder="t('menus.titlePlaceholder')" />
        </label>
        <label>
          <span>{{ t('menus.requiredPermission') }}</span>
          <el-select v-model="requiredPermission">
            <el-option v-for="permission in assignablePermissions" :key="permission" :label="permission" :value="permission" />
          </el-select>
        </label>
        <el-button type="primary" :loading="changing" @click="create">{{ t('menus.create') }}</el-button>
      </div>
    </el-card>

    <el-card class="art-table-card" shadow="never">
      <template #header>
        <div class="art-table-card__header">
          <h2>{{ t('menus.directoryTitle') }}</h2>
          <span class="art-table-card__count">{{ menus.length }}</span>
        </div>
      </template>

      <p v-if="menus.length === 0" class="art-empty-state">{{ t('menus.emptyDirectory') }}</p>
      <article v-for="menu in menus" :key="menu.id" class="art-data-row">
        <span class="art-data-row__avatar">{{ menu.routeName.slice(0, 2).toUpperCase() }}</span>
        <div class="art-data-row__main">
          <strong translate="no">{{ menu.title }}</strong>
          <code translate="no">{{ menu.routeName }} · {{ menu.componentKey }}</code>
        </div>
        <div class="art-tag-group">
          <el-tag v-if="menu.isSystem" type="warning">{{ t('menus.system') }}</el-tag>
          <el-tag :type="menu.isActive ? 'success' : 'info'">
            {{ t(menu.isActive ? 'menus.active' : 'menus.inactive') }}
          </el-tag>
        </div>
        <div class="art-data-row__actions">
          <el-button v-if="canWrite && !menu.isSystem" plain :disabled="changing" @click="edit(menu)">{{ t('menus.edit') }}</el-button>
          <el-button
            v-if="canWrite && menu.isActive && !menu.isSystem"
            type="danger"
            plain
            :disabled="changing"
            @click="disable(menu)"
          >
            {{ t('menus.disable') }}
          </el-button>
        </div>
      </article>
    </el-card>
  </section>
</template>
