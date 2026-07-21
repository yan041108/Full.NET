<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import {
  ElButton,
  ElCheckbox,
  ElMessage,
  ElMessageBox,
  ElOption,
  ElSelect,
  ElTag
} from 'element-plus';
import {
  type FullNetProblemDetails,
  type HostUser,
  type OrganizationUnit,
  type OrganizationUserUnit
} from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { listOrganizationUnits } from '../api/org-units';
import {
  createOrganizationUserUnit,
  disableOrganizationUserUnit,
  listOrganizationUserUnits,
  updateOrganizationUserUnit
} from '../api/org-user-units';
import { listHostUsers } from '../api/users';

const session = useSessionStore();
const { t } = useAdminI18n();
const assignments = ref<OrganizationUserUnit[]>([]);
const users = ref<HostUser[]>([]);
const units = ref<OrganizationUnit[]>([]);
const selectedUserId = ref('');
const selectedUnitId = ref('');
const isPrimary = ref(false);
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const canWrite = computed(() => session.can('organization.user_units.write'));

onMounted(load);

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    const [assignmentPage, unitPage, userPage] = await Promise.all([
      listOrganizationUserUnits(),
      listOrganizationUnits(),
      // 租户上下文通常无 identity.users.read；选择器可降级，列表仍应可渲染。
      listHostUsers().catch(() => ({
        items: [] as HostUser[],
        page: 1,
        pageSize: 20,
        total: 0
      }))
    ]);
    assignments.value = assignmentPage.items;
    users.value = userPage.items.filter(user => user.isActive);
    units.value = unitPage.items.filter(unit => unit.isActive);
  } catch (error: unknown) {
    problem.value = toProblem(error, 'orgUserUnits.loadFailed');
  } finally {
    loading.value = false;
  }
}

async function create(): Promise<void> {
  if (changing.value || !selectedUserId.value || !selectedUnitId.value) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    await createOrganizationUserUnit(
      selectedUserId.value,
      selectedUnitId.value,
      isPrimary.value
    );
    selectedUserId.value = '';
    selectedUnitId.value = '';
    isPrimary.value = false;
    ElMessage.success(t('orgUserUnits.createSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'orgUserUnits.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function setPrimary(assignment: OrganizationUserUnit): Promise<void> {
  if (changing.value || !assignment.isActive || assignment.isPrimary) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    await updateOrganizationUserUnit(assignment.id, true, assignment.version);
    ElMessage.success(t('orgUserUnits.primarySuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'orgUserUnits.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function disable(assignment: OrganizationUserUnit): Promise<void> {
  if (changing.value || !assignment.isActive) return;
  try {
    await ElMessageBox.confirm(
      t('orgUserUnits.confirmDisable', {
        name: `${assignment.displayName} / ${assignment.unitName}`
      }),
      t('orgUserUnits.disable'),
      {
        type: 'warning',
        confirmButtonText: t('orgUserUnits.disable'),
        cancelButtonText: t('status.back')
      }
    );
    changing.value = true;
    await disableOrganizationUserUnit(assignment.id);
    ElMessage.success(t('orgUserUnits.disableSuccess'));
    await load();
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') return;
    problem.value = toProblem(error, 'orgUserUnits.operationFailed');
  } finally {
    changing.value = false;
  }
}

function toProblem(
  error: unknown,
  fallbackKey: 'orgUserUnits.loadFailed' | 'orgUserUnits.operationFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { status: 500, code: 'client.organization_user_unit_failed', title: t(fallbackKey) };
}
</script>

<template>
  <section class="org-user-units-view" :aria-busy="loading">
    <header class="org-user-units-heading">
      <div>
        <p>{{ t('orgUserUnits.eyebrow') }}</p>
        <h1 data-route-heading tabindex="-1">{{ t('orgUserUnits.title') }}</h1>
        <span>{{ t('orgUserUnits.description') }}</span>
      </div>
    </header>

    <div v-if="problem" class="org-user-units-problem" role="alert">
      <strong translate="no">{{ problem.code }}</strong><span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <section v-if="canWrite" class="create-strip" aria-labelledby="create-title">
      <div><small>01</small><h2 id="create-title">{{ t('orgUserUnits.createTitle') }}</h2></div>
      <label>
        <span>{{ t('orgUserUnits.user') }}</span>
        <el-select v-model="selectedUserId" :placeholder="t('orgUserUnits.userPlaceholder')">
          <el-option
            v-for="user in users"
            :key="user.id"
            :label="`${user.displayName} (${user.username})`"
            :value="user.id"
          />
        </el-select>
      </label>
      <label>
        <span>{{ t('orgUserUnits.unit') }}</span>
        <el-select v-model="selectedUnitId" :placeholder="t('orgUserUnits.unitPlaceholder')">
          <el-option
            v-for="unit in units"
            :key="unit.id"
            :label="`${unit.name} (${unit.code})`"
            :value="unit.id"
          />
        </el-select>
      </label>
      <label class="org-user-units-primary">
        <el-checkbox v-model="isPrimary">{{ t('orgUserUnits.isPrimary') }}</el-checkbox>
      </label>
      <el-button type="primary" :loading="changing" @click="create">
        {{ t('orgUserUnits.create') }}
      </el-button>
    </section>

    <section class="identity-ledger">
      <header>
        <div><small>02</small><h2>{{ t('orgUserUnits.directoryTitle') }}</h2></div>
        <b>{{ assignments.length }}</b>
      </header>
      <p v-if="assignments.length === 0" class="org-user-units-empty">
        {{ t('orgUserUnits.emptyDirectory') }}
      </p>
      <article v-for="assignment in assignments" :key="assignment.id">
        <span class="identity-mark">
          {{ assignment.unitCode.slice(0, 2).toUpperCase() }}
        </span>
        <div>
          <strong translate="no">{{ assignment.displayName }}</strong>
          <code translate="no">{{ assignment.username }} · {{ assignment.unitName }}</code>
        </div>
        <div class="org-user-units-tags">
          <el-tag v-if="assignment.isPrimary" type="warning">
            {{ t('orgUserUnits.primary') }}
          </el-tag>
          <el-tag :type="assignment.isActive ? 'success' : 'info'">
            {{ t(assignment.isActive ? 'orgUserUnits.active' : 'orgUserUnits.inactive') }}
          </el-tag>
        </div>
        <div class="org-user-units-actions">
          <el-button
            v-if="canWrite && assignment.isActive && !assignment.isPrimary"
            plain
            :disabled="changing"
            @click="setPrimary(assignment)"
          >
            {{ t('orgUserUnits.setPrimary') }}
          </el-button>
          <el-button
            v-if="canWrite && assignment.isActive"
            type="danger"
            plain
            :disabled="changing"
            @click="disable(assignment)"
          >
            {{ t('orgUserUnits.disable') }}
          </el-button>
        </div>
      </article>
    </section>
  </section>
</template>

<style scoped>
.org-user-units-view { display: grid; gap: 18px; }
.org-user-units-heading { display: flex; align-items: flex-end; justify-content: space-between; gap: 24px; padding: 8px 2px; }
.org-user-units-heading p { margin: 0 0 10px; color: var(--fullnet-color-accent); font-family: var(--fullnet-font-display); font-size: 10px; font-weight: 700; letter-spacing: .2em; }
.org-user-units-heading h1 { margin: 0; font-family: var(--fullnet-font-display); font-size: clamp(30px, 4vw, 48px); font-weight: 500; letter-spacing: -.05em; }
.org-user-units-heading span { display: block; margin-top: 10px; color: var(--fullnet-color-ink-muted); font-size: 13px; }
.org-user-units-problem { display: flex; gap: 14px; padding: 13px 16px; border-left: 3px solid var(--fullnet-color-danger); background: rgb(201 74 74 / 8%); }
.org-user-units-problem code { margin-left: auto; }
.org-user-units-primary { display: flex; align-items: center; }
.org-user-units-tags { display: flex; gap: 8px; flex-wrap: wrap; }
.org-user-units-actions { display: flex; gap: 8px; justify-content: flex-end; flex-wrap: wrap; }
.org-user-units-empty { padding: 28px; margin: 0; text-align: center; color: var(--fullnet-color-ink-muted); }
@media (min-width: 960px) {
  .identity-ledger article .org-user-units-actions { grid-column: 2 / -1; }
}
</style>
