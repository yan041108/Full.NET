<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue';
import {
  ElButton,
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
  <section class="menus-view" :aria-busy="loading">
    <header class="menus-heading">
      <div>
        <p>{{ t('menus.eyebrow') }}</p>
        <h1 data-route-heading tabindex="-1">{{ t('menus.title') }}</h1>
        <span>{{ t('menus.description') }}</span>
      </div>
    </header>

    <div v-if="problem" class="menus-problem" role="alert">
      <strong translate="no">{{ problem.code }}</strong><span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <section v-if="canWrite" class="create-strip" aria-labelledby="create-title">
      <div><small>01</small><h2 id="create-title">{{ t('menus.createTitle') }}</h2></div>
      <label>
        <span>{{ t('menus.routeName') }}</span>
        <el-input v-model="routeName" :placeholder="t('menus.routeNamePlaceholder')" />
      </label>
      <label>
        <span>{{ t('menus.componentKey') }}</span>
        <el-select v-model="componentKey">
          <el-option
            v-for="option in componentOptions"
            :key="option.componentKey"
            :label="option.componentKey"
            :value="option.componentKey"
          />
        </el-select>
      </label>
      <label>
        <span>{{ t('menus.titleField') }}</span>
        <el-input v-model="title" :placeholder="t('menus.titlePlaceholder')" />
      </label>
      <label>
        <span>{{ t('menus.requiredPermission') }}</span>
        <el-select v-model="requiredPermission">
          <el-option
            v-for="permission in assignablePermissions"
            :key="permission"
            :label="permission"
            :value="permission"
          />
        </el-select>
      </label>
      <el-button type="primary" :loading="changing" @click="create">{{ t('menus.create') }}</el-button>
    </section>

    <section class="identity-ledger">
      <header>
        <div><small>02</small><h2>{{ t('menus.directoryTitle') }}</h2></div>
        <b>{{ menus.length }}</b>
      </header>
      <p v-if="menus.length === 0" class="menus-empty">{{ t('menus.emptyDirectory') }}</p>
      <article v-for="menu in menus" :key="menu.id">
        <span class="identity-mark">{{ menu.routeName.slice(0, 2).toUpperCase() }}</span>
        <div>
          <strong translate="no">{{ menu.title }}</strong>
          <code translate="no">{{ menu.routeName }} · {{ menu.componentKey }}</code>
        </div>
        <div class="menus-tags">
          <el-tag v-if="menu.isSystem" type="warning">{{ t('menus.system') }}</el-tag>
          <el-tag :type="menu.isActive ? 'success' : 'info'">
            {{ t(menu.isActive ? 'menus.active' : 'menus.inactive') }}
          </el-tag>
        </div>
        <div class="menus-actions">
          <el-button
            v-if="canWrite && !menu.isSystem"
            plain
            :disabled="changing"
            @click="edit(menu)"
          >
            {{ t('menus.edit') }}
          </el-button>
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
    </section>
  </section>
</template>

<style scoped>
.menus-view { display: grid; gap: 18px; }
.menus-heading { display: flex; align-items: flex-end; justify-content: space-between; gap: 24px; padding: 8px 2px; }
.menus-heading p { margin: 0 0 10px; color: var(--fullnet-color-accent); font-family: var(--fullnet-font-display); font-size: 10px; font-weight: 700; letter-spacing: .2em; }
.menus-heading h1 { margin: 0; font-family: var(--fullnet-font-display); font-size: clamp(30px, 4vw, 48px); font-weight: 500; letter-spacing: -.05em; }
.menus-heading span { display: block; margin-top: 10px; color: var(--fullnet-color-ink-muted); font-size: 13px; }
.menus-problem { display: flex; gap: 14px; padding: 13px 16px; border-left: 3px solid var(--fullnet-color-danger); background: rgb(201 74 74 / 8%); }
.menus-problem code { margin-left: auto; }
.create-strip { display: grid; grid-template-columns: minmax(160px, .7fr) repeat(3, minmax(160px, 1fr)) auto; align-items: end; gap: 16px; padding: 20px; border-radius: var(--fullnet-radius-md); background: var(--fullnet-color-sidebar); color: #fff; }
.create-strip > div { align-self: center; }
.create-strip small, .identity-ledger small { color: var(--fullnet-color-accent-bright); font-family: var(--fullnet-font-display); }
.create-strip h2, .identity-ledger h2 { margin: 4px 0 0; font-size: 17px; }
.create-strip label span { display: block; margin-bottom: 7px; color: #aeb8b9; font-size: 11px; }
.identity-ledger { overflow: hidden; border: 1px solid var(--fullnet-color-line); border-radius: var(--fullnet-radius-md); background: var(--fullnet-color-panel); box-shadow: var(--fullnet-shadow-panel); }
.identity-ledger > header { display: flex; min-height: 66px; align-items: center; justify-content: space-between; padding: 0 22px; border-bottom: 1px solid var(--fullnet-color-line); }
.identity-ledger article { display: grid; grid-template-columns: 44px minmax(180px, 1fr) auto auto; align-items: center; gap: 16px; padding: 15px 22px; border-bottom: 1px solid var(--fullnet-color-line); }
.menus-tags { display: flex; gap: 8px; flex-wrap: wrap; }
.menus-actions { display: flex; gap: 8px; justify-content: flex-end; flex-wrap: wrap; }
.identity-mark { display: grid; width: 40px; height: 40px; place-items: center; border-radius: 12px; background: var(--fullnet-color-ink); color: #fff; font-weight: 700; }
.identity-ledger article div { display: grid; gap: 4px; }
.identity-ledger code { color: var(--fullnet-color-ink-muted); font-size: 11px; }
.menus-empty { padding: 28px; margin: 0; text-align: center; color: var(--fullnet-color-ink-muted); }
@media (max-width: 1080px) {
  .create-strip { grid-template-columns: 1fr; }
  .identity-ledger article { grid-template-columns: 44px 1fr auto; }
  .identity-ledger article .menus-actions { grid-column: 2 / -1; }
}
</style>
