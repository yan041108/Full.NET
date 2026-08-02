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
  type OrganizationAssignableUser,
  type OrganizationUnit,
  type OrganizationUserUnit
} from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import PermissionGate from '../components/PermissionGate.vue';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { listOrganizationUnits } from '../api/org-units';
import {
  createOrganizationUserUnit,
  disableOrganizationUserUnit,
  listAssignableOrganizationUserUnitUsers,
  listOrganizationUserUnits,
  updateOrganizationUserUnit
} from '../api/org-user-units';

const session = useSessionStore();
const { t } = useAdminI18n();
const assignments = ref<OrganizationUserUnit[]>([]);
const users = ref<OrganizationAssignableUser[]>([]);
const units = ref<OrganizationUnit[]>([]);
const selectedUserId = ref('');
const selectedUnitId = ref('');
const isPrimary = ref(false);
const loading = ref(false);
const changing = ref(false);
const loadingMoreUsers = ref(false);
const userPage = ref(1);
const userTotal = ref(0);
const problem = ref<FullNetProblemDetails>();
const canCreate = computed(() => session.can('organization.user_units.create'));
const canUpdate = computed(() => session.can('organization.user_units.update'));
const canDisable = computed(() => session.can('organization.user_units.disable'));
const hasMoreUsers = computed(() => users.value.length < userTotal.value);

onMounted(load);

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    const [assignmentPage, unitPage, assignableUserPage] = await Promise.all([
      listOrganizationUserUnits(),
      listOrganizationUnits(),
      canCreate.value
        ? listAssignableOrganizationUserUnitUsers().catch(error => {
          if (isForbidden(error)) {
            return {
              items: [] as OrganizationAssignableUser[],
              page: 1,
              pageSize: 100,
              total: 0
            };
          }
          throw error;
        })
        : Promise.resolve({
          items: [] as OrganizationAssignableUser[],
          page: 1,
          pageSize: 100,
          total: 0
        })
    ]);
    assignments.value = assignmentPage.items;
    users.value = assignableUserPage.items;
    userPage.value = assignableUserPage.page;
    userTotal.value = assignableUserPage.total;
    units.value = unitPage.items.filter(unit => unit.isActive);
  } catch (error: unknown) {
    problem.value = toProblem(error, 'orgUserUnits.loadFailed');
  } finally {
    loading.value = false;
  }
}

async function loadMoreUsers(): Promise<void> {
  if (loadingMoreUsers.value || !canCreate.value || !hasMoreUsers.value) {
    return;
  }
  loadingMoreUsers.value = true;
  problem.value = undefined;
  try {
    const nextPage = await listAssignableOrganizationUserUnitUsers(userPage.value + 1);
    users.value = appendUniqueUsers(users.value, nextPage.items);
    userPage.value = nextPage.page;
    userTotal.value = nextPage.total;
  } catch (error: unknown) {
    if (isForbidden(error)) {
      userTotal.value = users.value.length;
      return;
    }
    problem.value = toProblem(error, 'orgUserUnits.loadFailed');
  } finally {
    loadingMoreUsers.value = false;
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

function isForbidden(error: unknown): boolean {
  return typeof error === 'object'
    && error !== null
    && 'status' in error
    && error.status === 403;
}

function appendUniqueUsers(
  current: OrganizationAssignableUser[],
  incoming: OrganizationAssignableUser[]
): OrganizationAssignableUser[] {
  const byId = new Map(current.map(user => [user.id, user]));
  incoming.forEach(user => byId.set(user.id, user));
  return [...byId.values()];
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

    <PermissionGate code="organization.user_units.create">
    <el-card class="art-form-card" shadow="never">
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
          <el-button
            v-if="hasMoreUsers"
            link
            :loading="loadingMoreUsers"
            data-testid="org-user-units-load-more-users"
            @click.prevent="loadMoreUsers"
          >
            {{ t('orgUserUnits.loadMoreUsers') }}
          </el-button>
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
    </PermissionGate>

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
          <PermissionGate code="organization.user_units.update">
          <el-button
            v-if="assignment.isActive && !assignment.isPrimary"
            plain
            :disabled="changing"
            @click="setPrimary(assignment)"
          >
            {{ t('orgUserUnits.setPrimary') }}
          </el-button>
          </PermissionGate>
          <PermissionGate v-if="assignment.isActive" code="organization.user_units.disable">
          <el-button
            type="danger"
            plain
            :disabled="changing"
            @click="disable(assignment)"
          >
            {{ t('orgUserUnits.disable') }}
          </el-button>
          </PermissionGate>
        </div>
      </article>
    </el-card>
  </section>
</template>
