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
  type OrganizationPosition,
  type OrganizationUserPosition
} from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { listOrganizationPositions } from '../api/org-positions';
import {
  createOrganizationUserPosition,
  disableOrganizationUserPosition,
  listOrganizationUserPositions,
  updateOrganizationUserPosition
} from '../api/org-user-positions';
import { listHostUsers } from '../api/users';

const session = useSessionStore();
const { t } = useAdminI18n();
const assignments = ref<OrganizationUserPosition[]>([]);
const users = ref<HostUser[]>([]);
const positions = ref<OrganizationPosition[]>([]);
const selectedUserId = ref('');
const selectedPositionId = ref('');
const isPrimary = ref(false);
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const canWrite = computed(() => session.can('organization.user_positions.write'));

onMounted(load);

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    const [assignmentPage, positionPage, userPage] = await Promise.all([
      listOrganizationUserPositions(),
      listOrganizationPositions(),
      listHostUsers().catch(() => ({
        items: [] as HostUser[],
        page: 1,
        pageSize: 20,
        total: 0
      }))
    ]);
    assignments.value = assignmentPage.items;
    users.value = userPage.items.filter(user => user.isActive);
    positions.value = positionPage.items.filter(position => position.isActive);
  } catch (error: unknown) {
    problem.value = toProblem(error, 'orgUserPositions.loadFailed');
  } finally {
    loading.value = false;
  }
}

async function create(): Promise<void> {
  if (changing.value || !selectedUserId.value || !selectedPositionId.value) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    await createOrganizationUserPosition(
      selectedUserId.value,
      selectedPositionId.value,
      isPrimary.value
    );
    selectedUserId.value = '';
    selectedPositionId.value = '';
    isPrimary.value = false;
    ElMessage.success(t('orgUserPositions.createSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'orgUserPositions.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function setPrimary(assignment: OrganizationUserPosition): Promise<void> {
  if (changing.value || !assignment.isActive || assignment.isPrimary) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    await updateOrganizationUserPosition(assignment.id, true, assignment.version);
    ElMessage.success(t('orgUserPositions.primarySuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'orgUserPositions.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function disable(assignment: OrganizationUserPosition): Promise<void> {
  if (changing.value || !assignment.isActive) return;
  try {
    await ElMessageBox.confirm(
      t('orgUserPositions.confirmDisable', {
        name: `${assignment.displayName} / ${assignment.positionName}`
      }),
      t('orgUserPositions.disable'),
      {
        type: 'warning',
        confirmButtonText: t('orgUserPositions.disable'),
        cancelButtonText: t('status.back')
      }
    );
    changing.value = true;
    await disableOrganizationUserPosition(assignment.id);
    ElMessage.success(t('orgUserPositions.disableSuccess'));
    await load();
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') return;
    problem.value = toProblem(error, 'orgUserPositions.operationFailed');
  } finally {
    changing.value = false;
  }
}

function toProblem(
  error: unknown,
  fallbackKey: 'orgUserPositions.loadFailed' | 'orgUserPositions.operationFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { status: 500, code: 'client.organization_user_position_failed', title: t(fallbackKey) };
}
</script>

<template>
  <section class="org-user-positions-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('orgUserPositions.title') }}</h1>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <el-card v-if="canWrite" class="art-form-card" shadow="never">
      <div class="art-form-grid art-form-grid--cols-2 art-form-grid--align-center" aria-labelledby="create-title">
        <div><h2 id="create-title">{{ t('orgUserPositions.createTitle') }}</h2></div>
        <label>
          <span>{{ t('orgUserPositions.user') }}</span>
          <el-select v-model="selectedUserId" :placeholder="t('orgUserPositions.userPlaceholder')">
            <el-option
              v-for="user in users"
              :key="user.id"
              :label="`${user.displayName} (${user.username})`"
              :value="user.id"
            />
          </el-select>
        </label>
        <label>
          <span>{{ t('orgUserPositions.position') }}</span>
          <el-select v-model="selectedPositionId" :placeholder="t('orgUserPositions.positionPlaceholder')">
            <el-option
              v-for="position in positions"
              :key="position.id"
              :label="`${position.name} (${position.code})`"
              :value="position.id"
            />
          </el-select>
        </label>
        <label>
          <el-checkbox v-model="isPrimary">{{ t('orgUserPositions.isPrimary') }}</el-checkbox>
        </label>
        <el-button type="primary" :loading="changing" @click="create">{{ t('orgUserPositions.create') }}</el-button>
      </div>
    </el-card>

    <el-card class="art-table-card" shadow="never">
      <template #header>
        <div class="art-table-card__header">
          <h2>{{ t('orgUserPositions.directoryTitle') }}</h2>
          <span class="art-table-card__count">{{ assignments.length }}</span>
        </div>
      </template>

      <p v-if="assignments.length === 0" class="art-empty-state">{{ t('orgUserPositions.emptyDirectory') }}</p>
      <article v-for="assignment in assignments" :key="assignment.id" class="art-data-row">
        <span class="art-data-row__avatar">{{ assignment.positionCode.slice(0, 2).toUpperCase() }}</span>
        <div class="art-data-row__main">
          <strong translate="no">{{ assignment.displayName }}</strong>
          <code translate="no">{{ assignment.username }} · {{ assignment.positionName }}</code>
        </div>
        <div class="art-tag-group">
          <el-tag v-if="assignment.isPrimary" type="warning">{{ t('orgUserPositions.primary') }}</el-tag>
          <el-tag :type="assignment.isActive ? 'success' : 'info'">
            {{ t(assignment.isActive ? 'orgUserPositions.active' : 'orgUserPositions.inactive') }}
          </el-tag>
        </div>
        <div class="art-data-row__actions">
          <el-button
            v-if="canWrite && assignment.isActive && !assignment.isPrimary"
            plain
            :disabled="changing"
            @click="setPrimary(assignment)"
          >
            {{ t('orgUserPositions.setPrimary') }}
          </el-button>
          <el-button
            v-if="canWrite && assignment.isActive"
            type="danger"
            plain
            :disabled="changing"
            @click="disable(assignment)"
          >
            {{ t('orgUserPositions.disable') }}
          </el-button>
        </div>
      </article>
    </el-card>
  </section>
</template>
