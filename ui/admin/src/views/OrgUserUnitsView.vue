<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import {
  ElButton,
  ElCard,
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
  <section class="org-user-units-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('orgUserUnits.title') }}</h1>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <el-card v-if="canWrite" class="art-form-card" shadow="never">
      <div class="art-form-grid art-form-grid--cols-2 art-form-grid--align-center" aria-labelledby="create-title">
        <div><h2 id="create-title">{{ t('orgUserUnits.createTitle') }}</h2></div>
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
        <label>
          <el-checkbox v-model="isPrimary">{{ t('orgUserUnits.isPrimary') }}</el-checkbox>
        </label>
        <el-button type="primary" :loading="changing" @click="create">{{ t('orgUserUnits.create') }}</el-button>
      </div>
    </el-card>

    <el-card class="art-table-card" shadow="never">
      <template #header>
        <div class="art-table-card__header">
          <h2>{{ t('orgUserUnits.directoryTitle') }}</h2>
          <span class="art-table-card__count">{{ assignments.length }}</span>
        </div>
      </template>

      <p v-if="assignments.length === 0" class="art-empty-state">{{ t('orgUserUnits.emptyDirectory') }}</p>
      <article v-for="assignment in assignments" :key="assignment.id" class="art-data-row">
        <span class="art-data-row__avatar">{{ assignment.unitCode.slice(0, 2).toUpperCase() }}</span>
        <div class="art-data-row__main">
          <strong translate="no">{{ assignment.displayName }}</strong>
          <code translate="no">{{ assignment.username }} · {{ assignment.unitName }}</code>
        </div>
        <div class="art-tag-group">
          <el-tag v-if="assignment.isPrimary" type="warning">{{ t('orgUserUnits.primary') }}</el-tag>
          <el-tag :type="assignment.isActive ? 'success' : 'info'">
            {{ t(assignment.isActive ? 'orgUserUnits.active' : 'orgUserUnits.inactive') }}
          </el-tag>
        </div>
        <div class="art-data-row__actions">
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
    </el-card>
  </section>
</template>
