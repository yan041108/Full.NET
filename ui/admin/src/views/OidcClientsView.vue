<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue';
import {
  ElButton,
  ElCard,
  ElForm,
  ElFormItem,
  ElInput,
  ElMessage,
  ElMessageBox,
  ElPagination,
  ElSwitch,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus';
import { Plus } from '@element-plus/icons-vue';
import type { FullNetProblemDetails, OidcClient } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import ArtSearchBar, { type ArtSearchBarItem } from '../framework/art-design/components/ArtSearchBar.vue';
import ArtTableActionButton from '../framework/art-design/components/ArtTableActionButton.vue';
import ArtTableActionGroup from '../framework/art-design/components/ArtTableActionGroup.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import { useArtCrudTableLayout } from '../framework/art-design/composables/useArtCrudTableLayout';
import PermissionGate from '../components/PermissionGate.vue';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  createOidcClient,
  disableOidcClient,
  listOidcClients,
  rotateOidcClientSecret,
  updateOidcClient
} from '../api/oidc-clients';

defineOptions({ name: 'OidcClientsView' });

type EditorMode = 'create' | 'edit';

const { t } = useAdminI18n();
const items = ref<OidcClient[]>([]);
const total = ref(0);
const page = ref(1);
const pageSize = ref(20);
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const searchForm = ref<Record<string, string | undefined>>({});
const appliedFilters = ref({ clientIdContains: '' });
const editorOpen = ref(false);
const editorMode = ref<EditorMode>('create');
const editingClient = ref<OidcClient | null>(null);
const editorForm = reactive({
  clientId: '',
  displayName: '',
  redirectUrisText: '',
  postLogoutRedirectUrisText: '',
  scopesText: 'openid profile offline_access',
  isConfidential: true,
  isFirstParty: true,
  resourceAudience: ''
});
const secret = ref('');

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
  { key: 'clientIdContains', label: t('oidcClients.fieldClientId'), type: 'input' }
]);

function rowIndex(index: number) {
  return (page.value - 1) * pageSize.value + index + 1;
}

function parseLines(text: string): string[] {
  return text
    .split(/[\n,]+/u)
    .map((item) => item.trim())
    .filter(Boolean);
}

/** Scope 允许单行空格分隔，与种子表单默认值一致。 */
function parseScopes(text: string): string[] {
  return text
    .split(/[\n,\s]+/u)
    .map((item) => item.trim())
    .filter(Boolean);
}

async function load() {
  loading.value = true;
  problem.value = undefined;
  try {
    const result = await listOidcClients({
      page: page.value,
      pageSize: pageSize.value,
      clientIdContains: appliedFilters.value.clientIdContains
    });
    items.value = result.items;
    total.value = result.total;
    await updateTableHeight();
  } catch (error) {
    problem.value = toProblem(error, 'oidcClients.loadFailed');
  } finally {
    loading.value = false;
  }
}

function handleSearch() {
  appliedFilters.value = {
    clientIdContains: searchForm.value.clientIdContains?.trim() ?? ''
  };
  page.value = 1;
  void load();
}

function resetSearch() {
  searchForm.value = {};
  appliedFilters.value = { clientIdContains: '' };
  page.value = 1;
  void load();
}

function openCreate() {
  editorMode.value = 'create';
  editingClient.value = null;
  editorForm.clientId = '';
  editorForm.displayName = '';
  editorForm.redirectUrisText = '';
  editorForm.postLogoutRedirectUrisText = '';
  editorForm.scopesText = 'openid\nprofile\noffline_access';
  editorForm.isConfidential = true;
  editorForm.isFirstParty = true;
  editorForm.resourceAudience = '';
  editorOpen.value = true;
}

function openEdit(row: OidcClient) {
  editorMode.value = 'edit';
  editingClient.value = row;
  editorForm.displayName = row.displayName;
  editorForm.redirectUrisText = row.redirectUris.join('\n');
  editorForm.postLogoutRedirectUrisText = row.postLogoutRedirectUris.join('\n');
  editorForm.scopesText = row.scopes.join('\n');
  editorForm.isFirstParty = row.isFirstParty;
  editorForm.resourceAudience = row.resourceAudience ?? '';
  editorOpen.value = true;
}

async function submitEditor() {
  changing.value = true;
  problem.value = undefined;
  try {
    const redirectUris = parseLines(editorForm.redirectUrisText);
    const postLogoutRedirectUris = parseLines(editorForm.postLogoutRedirectUrisText);
    const scopes = parseScopes(editorForm.scopesText);
    const resourceAudience = editorForm.resourceAudience.trim() || null;
    if (editorMode.value === 'create') {
      const created = await createOidcClient({
        clientId: editorForm.clientId.trim(),
        displayName: editorForm.displayName.trim(),
        redirectUris,
        postLogoutRedirectUris: postLogoutRedirectUris.length > 0 ? postLogoutRedirectUris : null,
        scopes,
        isConfidential: editorForm.isConfidential,
        isFirstParty: editorForm.isFirstParty,
        resourceAudience
      });
      secret.value = created.secret ?? '';
      ElMessage.success(t('oidcClients.createSuccess'));
    } else if (editingClient.value) {
      await updateOidcClient(editingClient.value.id, {
        displayName: editorForm.displayName.trim(),
        redirectUris,
        postLogoutRedirectUris: postLogoutRedirectUris.length > 0 ? postLogoutRedirectUris : null,
        scopes,
        isFirstParty: editorForm.isFirstParty,
        resourceAudience,
        version: editingClient.value.version
      });
      ElMessage.success(t('oidcClients.updateSuccess'));
    }
    editorOpen.value = false;
    await load();
  } catch (error) {
    problem.value = toProblem(error);
  } finally {
    changing.value = false;
  }
}

async function confirmRotate(row: OidcClient) {
  await ElMessageBox.confirm(
    t('oidcClients.confirmRotate', { name: row.displayName }),
    { type: 'warning' }
  );
  changing.value = true;
  try {
    const rotated = await rotateOidcClientSecret(row.id);
    secret.value = rotated.secret;
    ElMessage.success(t('oidcClients.rotateSuccess'));
    await load();
  } catch (error) {
    problem.value = toProblem(error);
  } finally {
    changing.value = false;
  }
}

async function confirmDisable(row: OidcClient) {
  await ElMessageBox.confirm(
    t('oidcClients.confirmDisable', { name: row.displayName }),
    { type: 'warning' }
  );
  changing.value = true;
  try {
    await disableOidcClient(row.id);
    ElMessage.success(t('oidcClients.disableSuccess'));
    await load();
  } catch (error) {
    problem.value = toProblem(error);
  } finally {
    changing.value = false;
  }
}

async function copySecret() {
  if (!secret.value) {
    return;
  }
  await navigator.clipboard.writeText(secret.value);
  ElMessage.success(t('oidcClients.copySuccess'));
}

function toProblem(
  error: unknown,
  fallbackKey: 'oidcClients.loadFailed' | 'oidcClients.operationFailed' = 'oidcClients.operationFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { status: 500, code: 'client.oidc_client_failed', title: t(fallbackKey) };
}

onMounted(() => {
  void load();
});
</script>

<template>
  <section class="oidc-clients-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('oidcClients.title') }}</h1>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
    </div>

    <el-card v-if="secret" class="art-form-card" shadow="never" data-testid="oidc-client-secret">
      <h2>{{ t('oidcClients.secretTitle') }}</h2>
      <p role="alert">{{ t('oidcClients.secretWarning') }}</p>
      <code translate="no">{{ secret }}</code>
      <el-button type="primary" plain @click="copySecret">{{ t('oidcClients.copy') }}</el-button>
    </el-card>

    <ArtSearchBar
      v-model="searchForm"
      :items="searchItems"
      :search-label="t('oidcClients.query')"
      :reset-label="t('oidcClients.reset')"
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
        >
          <template #left>
            <PermissionGate code="identity.oidc_clients.create">
              <el-button
                type="primary"
                plain
                :icon="Plus"
                data-testid="oidc-clients-action-create"
                @click="openCreate"
              >
                {{ t('oidcClients.create') }}
              </el-button>
            </PermissionGate>
          </template>
        </ArtTableHeader>

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
          <el-table-column :label="t('oidcClients.fieldClientId')" min-width="160" prop="clientId" />
          <el-table-column :label="t('oidcClients.fieldDisplayName')" min-width="180" prop="displayName" />
          <el-table-column :label="t('oidcClients.fieldClientType')" width="120" prop="clientType" />
          <el-table-column :label="t('oidcClients.scopes')" min-width="200">
            <template #default="{ row }">{{ row.scopes.join(', ') }}</template>
          </el-table-column>
          <el-table-column :label="t('oidcClients.isFirstParty')" width="100" align="center">
            <template #default="{ row }">
              <el-tag :type="row.isFirstParty ? 'success' : 'info'">
                {{ row.isFirstParty ? t('oidcClients.yes') : t('oidcClients.no') }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column :label="t('oidcClients.status')" width="100">
            <template #default="{ row }">
              <el-tag :type="row.isDisabled ? 'info' : 'success'">
                {{ row.isDisabled ? t('oidcClients.statusDisabled') : t('oidcClients.statusActive') }}
              </el-tag>
            </template>
          </el-table-column>
          <!-- @vue-generic {OidcClient} -->
          <el-table-column :label="t('users.columnActions')" width="220" fixed="right">
            <template #default="{ row }">
              <ArtTableActionGroup>
                <PermissionGate code="identity.oidc_clients.update">
                  <ArtTableActionButton type="edit"
                    :title="t('oidcClients.edit')"
                    test-id="oidc-clients-action-edit"
                    @click="openEdit(row)"
                  />
                </PermissionGate>
                <PermissionGate code="identity.oidc_clients.rotate">
                  <ArtTableActionButton type="delete"
                    :title="t('oidcClients.rotate')"
                    :disabled="row.isDisabled || row.clientType !== 'confidential'"
                    test-id="oidc-clients-action-rotate"
                    @click="confirmRotate(row)"
                  />
                </PermissionGate>
                <PermissionGate code="identity.oidc_clients.disable">
                  <ArtTableActionButton type="delete"
                    :title="t('oidcClients.disable')"
                    :disabled="row.isDisabled"
                    test-id="oidc-clients-action-disable"
                    @click="confirmDisable(row)"
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

    <ArtFormDialog
      v-model:open="editorOpen"
      :title="editorMode === 'create' ? t('oidcClients.createTitle') : t('oidcClients.editTitle')"
      :confirm-label="editorMode === 'create' ? t('oidcClients.create') : t('oidcClients.save')"
      :saving="changing"
      confirm-test-id="oidc-clients-editor-submit"
      @confirm="submitEditor"
    >
      <el-form label-position="top" class="oidc-clients-editor-form">
        <el-form-item v-if="editorMode === 'create'" :label="t('oidcClients.fieldClientId')">
          <el-input v-model="editorForm.clientId" translate="no" />
        </el-form-item>
        <el-form-item :label="t('oidcClients.fieldDisplayName')">
          <el-input v-model="editorForm.displayName" />
        </el-form-item>
        <el-form-item :label="t('oidcClients.fieldRedirectUris')">
          <el-input v-model="editorForm.redirectUrisText" type="textarea" :rows="3" />
        </el-form-item>
        <el-form-item :label="t('oidcClients.fieldPostLogoutRedirectUris')">
          <el-input v-model="editorForm.postLogoutRedirectUrisText" type="textarea" :rows="2" />
        </el-form-item>
        <el-form-item :label="t('oidcClients.fieldScopes')">
          <el-input v-model="editorForm.scopesText" type="textarea" :rows="3" />
        </el-form-item>
        <el-form-item v-if="editorMode === 'create'" :label="t('oidcClients.fieldIsConfidential')">
          <el-switch v-model="editorForm.isConfidential" />
        </el-form-item>
        <el-form-item :label="t('oidcClients.fieldIsFirstParty')">
          <el-switch v-model="editorForm.isFirstParty" />
        </el-form-item>
        <el-form-item :label="t('oidcClients.fieldResourceAudience')">
          <el-input v-model="editorForm.resourceAudience" translate="no" />
        </el-form-item>
      </el-form>
    </ArtFormDialog>
  </section>
</template>
