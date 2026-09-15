<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import {
  ElButton,
  ElCard,
  ElDescriptions,
  ElDescriptionsItem,
  ElDrawer,
  ElMessage,
  ElMessageBox,
  ElPagination,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus';
import type { FullNetProblemDetails, OidcAuthorization } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtSearchBar, { type ArtSearchBarItem } from '../framework/art-design/components/ArtSearchBar.vue';
import ArtTableActionButton from '../framework/art-design/components/ArtTableActionButton.vue';
import ArtTableActionGroup from '../framework/art-design/components/ArtTableActionGroup.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import { useArtCrudTableLayout } from '../framework/art-design/composables/useArtCrudTableLayout';
import PermissionGate from '../components/PermissionGate.vue';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  getOidcAuthorization,
  listOidcAuthorizations,
  revokeOidcAuthorization
} from '../api/oidc-authorizations';

defineOptions({ name: 'OidcAuthorizationsView' });

const { t } = useAdminI18n();
const items = ref<OidcAuthorization[]>([]);
const total = ref(0);
const page = ref(1);
const pageSize = ref(20);
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const searchForm = ref<Record<string, string | undefined>>({});
const appliedFilters = ref({
  subject: '',
  status: '',
  clientIdContains: ''
});
const detailOpen = ref(false);
const detailItem = ref<OidcAuthorization | null>(null);

const {
  tableMainRef,
  tableHeight,
  tableSize,
  tableZebra,
  tableBorder,
  tableHeaderBackground,
  tableHeaderCellStyle,
  updateTableHeight,
  watchLoading
} = useArtCrudTableLayout();

watchLoading(loading);

const searchItems = computed<ArtSearchBarItem[]>(() => [
  { key: 'clientIdContains', label: t('oidcAuthorizations.fieldClientId'), type: 'input' },
  { key: 'subject', label: t('oidcAuthorizations.fieldSubject'), type: 'input' },
  { key: 'status', label: t('oidcAuthorizations.fieldStatus'), type: 'input' }
]);

function rowIndex(index: number) {
  return (page.value - 1) * pageSize.value + index + 1;
}

function isRevoked(row: OidcAuthorization) {
  return row.status.toLowerCase() === 'revoked';
}

async function load() {
  loading.value = true;
  problem.value = undefined;
  try {
    const result = await listOidcAuthorizations({
      page: page.value,
      pageSize: pageSize.value,
      subject: appliedFilters.value.subject,
      status: appliedFilters.value.status,
      clientIdContains: appliedFilters.value.clientIdContains
    });
    items.value = result.items;
    total.value = result.total;
    await updateTableHeight();
  } catch (error) {
    problem.value = toProblem(error, 'oidcAuthorizations.loadFailed');
  } finally {
    loading.value = false;
  }
}

function handleSearch() {
  appliedFilters.value = {
    subject: searchForm.value.subject?.trim() ?? '',
    status: searchForm.value.status?.trim() ?? '',
    clientIdContains: searchForm.value.clientIdContains?.trim() ?? ''
  };
  page.value = 1;
  void load();
}

function resetSearch() {
  searchForm.value = {};
  appliedFilters.value = { subject: '', status: '', clientIdContains: '' };
  page.value = 1;
  void load();
}

async function openDetail(row: OidcAuthorization) {
  loading.value = true;
  try {
    detailItem.value = await getOidcAuthorization(row.id);
    detailOpen.value = true;
  } catch (error) {
    problem.value = toProblem(error);
  } finally {
    loading.value = false;
  }
}

async function confirmRevoke(row: OidcAuthorization) {
  await ElMessageBox.confirm(
    t('oidcAuthorizations.confirmRevoke', { clientId: row.clientId ?? row.id }),
    { type: 'warning' }
  );
  changing.value = true;
  try {
    await revokeOidcAuthorization(row.id);
    ElMessage.success(t('oidcAuthorizations.revokeSuccess'));
    detailOpen.value = false;
    await load();
  } catch (error) {
    problem.value = toProblem(error);
  } finally {
    changing.value = false;
  }
}

function toProblem(
  error: unknown,
  fallbackKey: 'oidcAuthorizations.loadFailed' | 'oidcAuthorizations.operationFailed' = 'oidcAuthorizations.operationFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { status: 500, code: 'client.oidc_authorization_failed', title: t(fallbackKey) };
}

onMounted(() => {
  void load();
});
</script>

<template>
  <section class="oidc-authorizations-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('oidcAuthorizations.title') }}</h1>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
    </div>

    <ArtSearchBar
      v-model="searchForm"
      :items="searchItems"
      :search-label="t('oidcAuthorizations.query')"
      :reset-label="t('oidcAuthorizations.reset')"
      @search="handleSearch"
      @reset="resetSearch"
    />

    <el-card class="art-table-card" shadow="never">
      <div ref="tableMainRef" class="art-crud-table-main">
        <ArtTableHeader
          v-model:table-size="tableSize"
          v-model:zebra="tableZebra"
          v-model:border="tableBorder"
          v-model:header-background="tableHeaderBackground"
          :loading="loading"
          full-class="art-crud-table-main"
          layout="refresh,size,fullscreen,settings"
          @refresh="load"
        />

        <el-table
          v-loading="loading"
          :data="items"
          row-key="id"
          :height="tableHeight"
          :size="tableSize"
          :stripe="tableZebra"
          :border="tableBorder"
          :header-cell-style="tableHeaderCellStyle"
        >
          <el-table-column :label="t('users.columnIndex')" width="72" align="center">
            <template #default="{ $index }">{{ rowIndex($index) }}</template>
          </el-table-column>
          <el-table-column :label="t('oidcAuthorizations.fieldClientId')" min-width="160" prop="clientId" />
          <el-table-column :label="t('oidcAuthorizations.fieldSubject')" min-width="200" prop="subject" />
          <el-table-column :label="t('oidcAuthorizations.scopes')" min-width="220">
            <template #default="{ row }">{{ row.scopes.join(', ') }}</template>
          </el-table-column>
          <el-table-column :label="t('oidcAuthorizations.fieldStatus')" width="110">
            <template #default="{ row }">
              <el-tag :type="isRevoked(row) ? 'info' : 'success'">{{ row.status }}</el-tag>
            </template>
          </el-table-column>
          <el-table-column :label="t('oidcAuthorizations.fieldType')" width="120" prop="type" />
          <el-table-column :label="t('oidcAuthorizations.createdAt')" min-width="180" prop="createdAtUtc" />
          <!-- @vue-generic {OidcAuthorization} -->
          <el-table-column :label="t('users.columnActions')" width="160" fixed="right">
            <template #default="{ row }">
              <ArtTableActionGroup>
                <PermissionGate code="identity.oidc_authorizations.read">
                  <ArtTableActionButton type="view"
                    :title="t('oidcAuthorizations.detail')"
                    test-id="oidc-authorizations-action-detail"
                    @click="openDetail(row)"
                  />
                </PermissionGate>
                <PermissionGate code="identity.oidc_authorizations.revoke">
                  <ArtTableActionButton type="delete"
                    :title="t('oidcAuthorizations.revoke')"
                    :disabled="isRevoked(row)"
                    test-id="oidc-authorizations-action-revoke"
                    @click="confirmRevoke(row)"
                  />
                </PermissionGate>
              </ArtTableActionGroup>
            </template>
          </el-table-column>
        </el-table>

        <el-pagination
          v-model:current-page="page"
          v-model:page-size="pageSize"
          :total="total"
          layout="total, prev, pager, next, sizes"
          @current-change="load"
          @size-change="load"
        />
      </div>
    </el-card>

    <el-drawer
      v-model="detailOpen"
      :title="t('oidcAuthorizations.detailTitle')"
      size="480px"
      data-testid="oidc-authorizations-detail-drawer"
    >
      <template v-if="detailItem">
        <el-descriptions :column="1" border>
          <el-descriptions-item :label="t('oidcAuthorizations.fieldId')">
            <span translate="no">{{ detailItem.id }}</span>
          </el-descriptions-item>
          <el-descriptions-item :label="t('oidcAuthorizations.fieldApplicationId')">
            <span translate="no">{{ detailItem.applicationId ?? '—' }}</span>
          </el-descriptions-item>
          <el-descriptions-item :label="t('oidcAuthorizations.fieldClientId')">
            <span translate="no">{{ detailItem.clientId ?? '—' }}</span>
          </el-descriptions-item>
          <el-descriptions-item :label="t('oidcAuthorizations.fieldSubject')">
            <span translate="no">{{ detailItem.subject ?? '—' }}</span>
          </el-descriptions-item>
          <el-descriptions-item :label="t('oidcAuthorizations.scopes')">
            {{ detailItem.scopes.join(', ') }}
          </el-descriptions-item>
          <el-descriptions-item :label="t('oidcAuthorizations.fieldStatus')">
            {{ detailItem.status }}
          </el-descriptions-item>
          <el-descriptions-item :label="t('oidcAuthorizations.fieldType')">
            {{ detailItem.type }}
          </el-descriptions-item>
          <el-descriptions-item :label="t('oidcAuthorizations.creationDate')">
            {{ detailItem.creationDateUtc ?? '—' }}
          </el-descriptions-item>
          <el-descriptions-item :label="t('oidcAuthorizations.createdAt')">
            {{ detailItem.createdAtUtc }}
          </el-descriptions-item>
        </el-descriptions>
        <div class="oidc-authorizations-detail-actions">
          <PermissionGate code="identity.oidc_authorizations.revoke">
            <el-button
              type="danger"
              plain
              :disabled="isRevoked(detailItem)"
              :loading="changing"
              data-testid="oidc-authorizations-detail-revoke"
              @click="confirmRevoke(detailItem)"
            >
              {{ t('oidcAuthorizations.revoke') }}
            </el-button>
          </PermissionGate>
        </div>
      </template>
    </el-drawer>
  </section>
</template>

<style scoped>
.oidc-authorizations-detail-actions {
  margin-top: 16px;
}
</style>
