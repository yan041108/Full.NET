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
import type { FormInstance } from 'element-plus';
import type { FullNetProblemDetails, OAuthProvider } from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import ArtSearchBar, { type ArtSearchBarItem } from '../framework/art-design/components/ArtSearchBar.vue';
import ArtTableActionButton from '../framework/art-design/components/ArtTableActionButton.vue';
import ArtTableActionGroup from '../framework/art-design/components/ArtTableActionGroup.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import { useArtCrudTableLayout } from '../framework/art-design/composables/useArtCrudTableLayout';
import PermissionGate from '../components/PermissionGate.vue';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  createOAuthProvider,
  deleteOAuthProvider,
  listOAuthProviders,
  updateOAuthProvider
} from '../api/oauth-providers';

defineOptions({ name: 'OAuthProvidersView' });

type EditorMode = 'create' | 'edit';

const { t } = useAdminI18n();
const items = ref<OAuthProvider[]>([]);
const total = ref(0);
const page = ref(1);
const pageSize = ref(20);
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const searchForm = ref<Record<string, string | undefined>>({});
const appliedFilters = ref({ providerKey: '', displayName: '' });
const editorOpen = ref(false);
const editorMode = ref<EditorMode>('create');
const editingProvider = ref<OAuthProvider | null>(null);
const editorFormRef = ref<FormInstance>();
const editorForm = reactive({
  providerKey: '',
  displayName: '',
  authority: '',
  clientId: '',
  clientSecret: '',
  scopes: 'openid profile email',
  redirectPath: '/api/v1/identity/oauth/callback',
  isEnabled: true
});

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
  { key: 'providerKey', label: t('oauthProviders.fieldProviderKey'), type: 'input' },
  { key: 'displayName', label: t('oauthProviders.fieldDisplayName'), type: 'input' }
]);

function rowIndex(index: number) {
  return (page.value - 1) * pageSize.value + index + 1;
}

async function load() {
  loading.value = true;
  problem.value = undefined;
  try {
    const result = await listOAuthProviders({
      page: page.value,
      pageSize: pageSize.value,
      providerKeyContains: appliedFilters.value.providerKey,
      displayNameContains: appliedFilters.value.displayName
    });
    items.value = result.items;
    total.value = result.total;
    updateTableHeight();
  } catch (error) {
    problem.value = error as FullNetProblemDetails;
  } finally {
    loading.value = false;
  }
}

function applySearch(values: Record<string, string | undefined>) {
  appliedFilters.value = {
    providerKey: values.providerKey?.trim() ?? '',
    displayName: values.displayName?.trim() ?? ''
  };
  page.value = 1;
  void load();
}

function resetSearch() {
  searchForm.value = {};
  appliedFilters.value = { providerKey: '', displayName: '' };
  page.value = 1;
  void load();
}

function openCreate() {
  editorMode.value = 'create';
  editingProvider.value = null;
  Object.assign(editorForm, {
    providerKey: '',
    displayName: '',
    authority: '',
    clientId: '',
    clientSecret: '',
    scopes: 'openid profile email',
    redirectPath: '/api/v1/identity/oauth/callback',
    isEnabled: true
  });
  editorOpen.value = true;
}

function openEdit(provider: OAuthProvider) {
  editorMode.value = 'edit';
  editingProvider.value = provider;
  Object.assign(editorForm, {
    providerKey: provider.providerKey,
    displayName: provider.displayName,
    authority: provider.authority,
    clientId: provider.clientId,
    clientSecret: '',
    scopes: provider.scopes,
    redirectPath: provider.redirectPath,
    isEnabled: provider.isEnabled
  });
  editorOpen.value = true;
}

async function submitEditor() {
  changing.value = true;
  try {
    if (editorMode.value === 'create') {
      await createOAuthProvider({
        providerKey: editorForm.providerKey,
        displayName: editorForm.displayName,
        authority: editorForm.authority,
        clientId: editorForm.clientId,
        clientSecret: editorForm.clientSecret,
        scopes: editorForm.scopes,
        redirectPath: editorForm.redirectPath,
        isEnabled: editorForm.isEnabled
      });
      ElMessage.success(t('oauthProviders.createSuccess'));
    } else if (editingProvider.value) {
      await updateOAuthProvider(editingProvider.value.id, {
        displayName: editorForm.displayName,
        authority: editorForm.authority,
        clientId: editorForm.clientId,
        clientSecret: editorForm.clientSecret || null,
        scopes: editorForm.scopes,
        redirectPath: editorForm.redirectPath,
        isEnabled: editorForm.isEnabled,
        version: editingProvider.value.version
      });
      ElMessage.success(t('oauthProviders.updateSuccess'));
    }
    editorOpen.value = false;
    await load();
  } finally {
    changing.value = false;
  }
}

async function removeProvider(provider: OAuthProvider) {
  await ElMessageBox.confirm(
    t('oauthProviders.confirmDelete', { name: provider.displayName }),
    { type: 'warning' }
  );
  changing.value = true;
  try {
    await deleteOAuthProvider(provider.id);
    ElMessage.success(t('oauthProviders.deleteSuccess'));
    await load();
  } finally {
    changing.value = false;
  }
}

onMounted(() => {
  void load();
});
</script>

<template>
  <section class="oauth-providers-view">
    <ArtTableHeader
      :title="t('oauthProviders.title')"
      :problem="problem"
    >
      <PermissionGate code="identity.oauth_providers.create">
        <ElButton type="primary" :icon="Plus" @click="openCreate">
          {{ t('oauthProviders.create') }}
        </ElButton>
      </PermissionGate>
    </ArtTableHeader>

    <ArtSearchBar
      v-model="searchForm"
      :items="searchItems"
      @search="applySearch"
      @reset="resetSearch"
    />

    <ElCard shadow="never">
      <div ref="tableMainRef">
        <ElTable
          :data="items"
          :height="tableHeight"
          :size="tableSize"
          :stripe="tableZebra"
          :border="tableBorder"
          :header-cell-style="tableHeaderCellStyle"
          :header-cell-class-name="tableHeaderBackground ? 'art-table-header-background' : ''"
          v-loading="loading"
        >
          <ElTableColumn type="index" :index="rowIndex" width="60" />
          <ElTableColumn prop="providerKey" :label="t('oauthProviders.fieldProviderKey')" min-width="140" />
          <ElTableColumn prop="displayName" :label="t('oauthProviders.fieldDisplayName')" min-width="160" />
          <ElTableColumn prop="authority" :label="t('oauthProviders.fieldAuthority')" min-width="220" show-overflow-tooltip />
          <ElTableColumn prop="clientId" :label="t('oauthProviders.fieldClientId')" min-width="160" show-overflow-tooltip />
          <ElTableColumn :label="t('oauthProviders.status')" width="100">
            <template #default="{ row }">
              <ElTag :type="row.isEnabled ? 'success' : 'info'">
                {{ row.isEnabled ? t('oauthProviders.statusEnabled') : t('oauthProviders.statusDisabled') }}
              </ElTag>
            </template>
          </ElTableColumn>
          <!-- @vue-generic {OAuthProvider} -->
          <ElTableColumn width="180" fixed="right">
            <template #default="{ row }">
              <ArtTableActionGroup>
                <PermissionGate code="identity.oauth_providers.update">
                  <ArtTableActionButton type="edit" @click="openEdit(row)">{{ t('oauthProviders.edit') }}</ArtTableActionButton>
                </PermissionGate>
                <PermissionGate code="identity.oauth_providers.delete">
                  <ArtTableActionButton type="delete" @click="removeProvider(row)">
                    {{ t('oauthProviders.delete') }}
                  </ArtTableActionButton>
                </PermissionGate>
              </ArtTableActionGroup>
            </template>
          </ElTableColumn>
        </ElTable>
      </div>
      <ElPagination
        v-model:current-page="page"
        v-model:page-size="pageSize"
        layout="total, prev, pager, next"
        :total="total"
        @current-change="load"
        @size-change="load"
      />
    </ElCard>

    <ArtFormDialog
      v-model:open="editorOpen"
      :title="editorMode === 'create' ? t('oauthProviders.createTitle') : t('oauthProviders.editTitle')"
      :saving="changing"
      @confirm="submitEditor"
    >
      <ElForm ref="editorFormRef" label-width="140px">
        <ElFormItem v-if="editorMode === 'create'" :label="t('oauthProviders.fieldProviderKey')">
          <ElInput v-model.trim="editorForm.providerKey" maxlength="64" />
        </ElFormItem>
        <ElFormItem :label="t('oauthProviders.fieldDisplayName')">
          <ElInput v-model.trim="editorForm.displayName" maxlength="128" />
        </ElFormItem>
        <ElFormItem :label="t('oauthProviders.fieldAuthority')">
          <ElInput v-model.trim="editorForm.authority" maxlength="512" />
        </ElFormItem>
        <ElFormItem :label="t('oauthProviders.fieldClientId')">
          <ElInput v-model.trim="editorForm.clientId" maxlength="256" />
        </ElFormItem>
        <ElFormItem :label="editorMode === 'create' ? t('oauthProviders.fieldClientSecret') : t('oauthProviders.fieldClientSecretOptional')">
          <ElInput v-model="editorForm.clientSecret" type="password" show-password maxlength="1024" />
        </ElFormItem>
        <ElFormItem :label="t('oauthProviders.fieldScopes')">
          <ElInput v-model.trim="editorForm.scopes" maxlength="512" />
        </ElFormItem>
        <ElFormItem :label="t('oauthProviders.fieldRedirectPath')">
          <ElInput v-model.trim="editorForm.redirectPath" maxlength="256" />
        </ElFormItem>
        <ElFormItem :label="t('oauthProviders.fieldIsEnabled')">
          <ElSwitch v-model="editorForm.isEnabled" />
        </ElFormItem>
      </ElForm>
    </ArtFormDialog>
  </section>
</template>
