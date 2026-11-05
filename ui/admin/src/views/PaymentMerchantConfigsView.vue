<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, reactive, ref } from 'vue';
import {
  ElAlert,
  ElButton,
  ElCard,
  ElForm,
  ElFormItem,
  ElInput,
  ElMessage,
  ElOption,
  ElPagination,
  ElSelect,
  ElSwitch,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus';
import { Plus } from '@element-plus/icons-vue';
import type { FormInstance } from 'element-plus';
import type { PaymentMerchantConfigListItem, FullNetProblemDetails } from '@fullnet/client-contracts';
import { isFullNetProblemDetails, isGuid } from '@fullnet/client-contracts';
import { translatePaymentMerchantOperations } from '@fullnet/admin-i18n';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import ArtTableActionButton from '../framework/art-design/components/ArtTableActionButton.vue';
import ArtTableActionGroup from '../framework/art-design/components/ArtTableActionGroup.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import { useArtCrudTableLayout } from '../framework/art-design/composables/useArtCrudTableLayout';
import PermissionGate from '../components/PermissionGate.vue';
import { useAdminI18n } from '../i18n/adminI18n';
import { useSessionStore } from '../auth/session';
import { useAuthorizedViewScope } from '../composables/useAuthorizedViewScope';
import {
  createPaymentMerchantConfig,
  disablePaymentMerchantConfig,
  getPaymentMerchantConfig,
  listPaymentMerchantConfigs,
  updatePaymentMerchantConfig
} from '../api/payment-merchant-configs';

defineOptions({ name: 'PaymentMerchantConfigsView' });

type EditorMode = 'create' | 'edit';

const { t, locale } = useAdminI18n(); const session = useSessionStore();
const text = (key: Parameters<typeof translatePaymentMerchantOperations>[1]) => translatePaymentMerchantOperations(locale.value, key);
const host = computed(() => session.state === 'authenticated' && session.currentUser?.tenantId === null && session.currentUser.actorScope === 'host');
const canRead = computed(() => host.value && session.can('payments.merchants.read'));
const validId = (value: unknown): value is string => isGuid(value) && value !== '00000000-0000-0000-0000-000000000000';
const sameId = (left: string | null, right: string | null) => left === null ? right === null : validId(left) && validId(right) && left.toLowerCase() === right.toLowerCase();
const items = ref<PaymentMerchantConfigListItem[]>([]);
const total = ref(0);
const page = ref(1);
const pageSize = ref(20);
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const editorOpen = ref(false);
const disableTarget = ref<Readonly<PaymentMerchantConfigListItem>>();
const editorMode = ref<EditorMode>('create');
const editing = ref<PaymentMerchantConfigListItem | null>(null);
const editorFormRef = ref<FormInstance>();
const editorForm = reactive({
  tenantId: '',
  channelKey: 'wechat_native',
  name: '',
  appId: '',
  merchantId: '',
  certificateSerialNo: '',
  notifyUrl: '',
  returnUrl: '',
  apiV3Key: '',
  privateKeyPem: '',
  isDefault: false,
  isEnabled: true,
  version: 0
});

const isAlipayPage = computed(() => editorForm.channelKey === 'alipay_page');
const filters = reactive({ tenantId: '', channel: 'all', enabled: 'all', name: '' });
let appliedFilters = { ...filters }; let invalidFilters = false; let listSequence = 0; let editSequence = 0;
let listPending: { cancel(): void } | undefined; let editPending: { cancel(): void } | undefined;
const narrowViewport = ref(false); let viewportQuery: MediaQueryList | undefined;
function updateViewport() { narrowViewport.value = viewportQuery?.matches ?? false; }
function resetEditor() { editorOpen.value = false; editing.value = null; Object.assign(editorForm, {
  tenantId: '', channelKey: 'wechat_native', name: '', appId: '', merchantId: '', certificateSerialNo: '', notifyUrl: '', returnUrl: '',
  apiV3Key: '', privateKeyPem: '', isDefault: false, isEnabled: true, version: 0 }); }
function clearList() { listSequence++; listPending?.cancel(); listPending = undefined; items.value = []; loading.value = false; }
const scope = useAuthorizedViewScope(session, () => {
  clearList(); total.value = 0; page.value = 1; problem.value = undefined; resetEditor(); disableTarget.value = undefined; changing.value = false;
  editSequence++; editPending?.cancel(); editPending = undefined;
  Object.assign(filters, { tenantId: '', channel: 'all', enabled: 'all', name: '' }); appliedFilters = { ...filters }; invalidFilters = false;
}, () => { void load(); });

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

function toProblem(error: unknown, fallbackKey: Parameters<typeof t>[0]): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { code: 'client.request_failed', title: t(fallbackKey), status: 500, type: 'about:blank' };
}

async function load(): Promise<void> {
  clearList(); if (!canRead.value || invalidFilters) return;
  const operation = scope.begin('payments.merchants.read'); if (!operation) return;
  const ticket = listSequence; listPending = operation; loading.value = true;
  const requested = { ...appliedFilters }; const requestedPage = page.value, requestedSize = pageSize.value;
  problem.value = undefined;
  try {
    const result = await listPaymentMerchantConfigs({ page: requestedPage, pageSize: requestedSize,
      tenantId: requested.tenantId || undefined, channelKey: requested.channel === 'all' ? undefined : requested.channel,
      nameContains: requested.name || undefined, isEnabled: requested.enabled === 'all' ? undefined : requested.enabled === 'enabled' }, operation.signal);
    if (!operation.current() || ticket !== listSequence) return;
    if (result.page !== requestedPage || result.pageSize !== requestedSize || !Number.isSafeInteger(result.total) || result.total < 0
      || !result.items.every(row => validId(row.id) && (row.tenantId === null || validId(row.tenantId))
        && ['wechat_native', 'alipay_page'].includes(row.channelKey) && typeof row.name === 'string' && !!row.name.trim()
        && typeof row.isEnabled === 'boolean' && typeof row.isDefault === 'boolean' && Number.isSafeInteger(row.version) && row.version >= 1
        && [row.maskedAppId, row.maskedMerchantId, row.maskedCertificateSerialNo, row.maskedNotifyUrl, row.maskedReturnUrl].every(value => typeof value === 'string')
        && (!requested.tenantId || sameId(row.tenantId, requested.tenantId)) && (requested.channel === 'all' || row.channelKey === requested.channel)
        && (requested.enabled === 'all' || row.isEnabled === (requested.enabled === 'enabled'))))
      throw { type: 'about:blank', status: 502, code: 'client.merchant_page_mismatch', title: text('mismatch') };
    items.value = result.items;
    total.value = result.total;
    await updateTableHeight();
  } catch (error: unknown) {
    if (operation.current() && ticket === listSequence) { problem.value = toProblem(error, 'paymentMerchantConfigs.loadFailed'); total.value = 0; page.value = 1; }
  } finally {
    operation.finish(); if (ticket === listSequence) { loading.value = false; listPending = undefined; }
  }
}

function search() {
  if (!canRead.value) return; clearList(); page.value = 1;
  const next = { tenantId: filters.tenantId.trim().toLowerCase(), channel: filters.channel, enabled: filters.enabled, name: filters.name.trim() };
  invalidFilters = (!!next.tenantId && !validId(next.tenantId)) || !['all', 'wechat_native', 'alipay_page'].includes(next.channel)
    || !['all', 'enabled', 'disabled'].includes(next.enabled) || next.name.length > 128;
  if (invalidFilters) { total.value = 0; problem.value = { type: 'about:blank', status: 400, code: 'client.invalid_merchant_filter', title: text('invalid') }; return; }
  appliedFilters = next; void load();
}
function resetFilters() { Object.assign(filters, { tenantId: '', channel: 'all', enabled: 'all', name: '' }); search(); }
function changePage(value: number) { if (value !== page.value) { page.value = value; void load(); } }

function openCreate(): void {
  if (!host.value || !session.can('payments.merchants.create') || changing.value) return;
  editSequence++; editPending?.cancel(); editPending = undefined;
  editorMode.value = 'create';
  editing.value = null;
  Object.assign(editorForm, {
    tenantId: '',
    channelKey: 'wechat_native',
    name: '',
    appId: '',
    merchantId: '',
    certificateSerialNo: '',
    notifyUrl: '',
    returnUrl: '',
    apiV3Key: '',
    privateKeyPem: '',
    isDefault: false,
    isEnabled: true,
    version: 0
  });
  editorOpen.value = true;
}

async function openEdit(row: PaymentMerchantConfigListItem): Promise<void> {
  if (!canRead.value || !session.can('payments.merchants.update') || changing.value || !validId(row.id)) return;
  editPending?.cancel(); const ticket = ++editSequence; const original = Object.freeze({ ...row });
  const operation = scope.begin('payments.merchants.read'); if (!operation) return; editPending = operation;
  try {
    const detail = await getPaymentMerchantConfig(original.id, operation.signal);
    if (!operation.current() || ticket !== editSequence || !session.can('payments.merchants.update')) return;
    if (!sameId(detail.id, original.id) || !sameId(detail.tenantId, original.tenantId) || detail.channelKey !== original.channelKey
      || !Number.isSafeInteger(detail.version) || detail.version < original.version)
      throw { type: 'about:blank', status: 502, code: 'client.merchant_detail_mismatch', title: text('mismatch') };
    editorMode.value = 'edit'; editing.value = original;
    Object.assign(editorForm, {
      tenantId: detail.tenantId ?? '',
      channelKey: detail.channelKey,
      name: detail.name,
      appId: detail.appId,
      merchantId: detail.merchantId === '-' ? '' : detail.merchantId,
      certificateSerialNo: detail.certificateSerialNo === '-' ? '' : detail.certificateSerialNo,
      notifyUrl: detail.notifyUrl,
      returnUrl: detail.returnUrl === '-' ? '' : detail.returnUrl,
      apiV3Key: '',
      privateKeyPem: '',
      isDefault: detail.isDefault,
      isEnabled: detail.isEnabled,
      version: detail.version
    });
    editorOpen.value = true;
  } catch (error) { if (operation.current() && ticket === editSequence) problem.value = toProblem(error, 'paymentMerchantConfigs.loadFailed'); }
  finally { operation.finish(); if (ticket === editSequence) editPending = undefined; }
}

function closeEditor() { editSequence++; editPending?.cancel(); editPending = undefined; resetEditor(); }

async function saveEditor(): Promise<void> {
  if (!host.value || changing.value || !editorOpen.value) return;
  const permission = editorMode.value === 'create' ? 'payments.merchants.create' : 'payments.merchants.update';
  const operation = scope.begin(permission); if (!operation) return;
  changing.value = true;
  try {
    if (editorMode.value === 'create') {
      await createPaymentMerchantConfig({
        tenantId: editorForm.tenantId.trim() || null,
        name: editorForm.name.trim(),
        channelKey: editorForm.channelKey,
        appId: editorForm.appId.trim(),
        merchantId: editorForm.merchantId.trim(),
        certificateSerialNo: editorForm.certificateSerialNo.trim(),
        notifyUrl: editorForm.notifyUrl.trim(),
        returnUrl: editorForm.returnUrl.trim(),
        apiV3Key: editorForm.apiV3Key.trim() || null,
        privateKeyPem: editorForm.privateKeyPem.trim() || null,
        isDefault: editorForm.isDefault,
        isEnabled: editorForm.isEnabled
      }, operation.signal);
      if (!operation.current()) return;
      ElMessage.success(t('paymentMerchantConfigs.createSuccess'));
    } else if (editing.value) {
      await updatePaymentMerchantConfig(editing.value.id, {
        name: editorForm.name.trim(),
        channelKey: editorForm.channelKey,
        appId: editorForm.appId.trim(),
        merchantId: editorForm.merchantId.trim(),
        certificateSerialNo: editorForm.certificateSerialNo.trim(),
        notifyUrl: editorForm.notifyUrl.trim(),
        returnUrl: editorForm.returnUrl.trim(),
        apiV3Key: editorForm.apiV3Key.trim() || null,
        clearApiV3Key: false,
        privateKeyPem: editorForm.privateKeyPem.trim() || null,
        clearPrivateKey: false,
        isDefault: editorForm.isDefault,
        isEnabled: editorForm.isEnabled,
        version: editorForm.version
      }, operation.signal);
      if (!operation.current()) return;
      ElMessage.success(t('paymentMerchantConfigs.updateSuccess'));
    }
    closeEditor();
    await load();
  } catch (error: unknown) {
    if (operation.current()) ElMessage.error(toProblem(error, 'paymentMerchantConfigs.saveFailed').title);
  } finally {
    if (operation.current()) changing.value = false; operation.finish();
  }
}

function disableRow(row: PaymentMerchantConfigListItem): void {
  if (!host.value || !session.can('payments.merchants.update') || changing.value || !row.isEnabled || !validId(row.id)) return;
  disableTarget.value = Object.freeze({ ...row });
}
async function confirmDisable() {
  const target = disableTarget.value; if (!target || changing.value || !host.value) return;
  const current = items.value.find(row => sameId(row.id, target.id));
  if (!current || current.version !== target.version || !sameId(current.tenantId, target.tenantId) || current.channelKey !== target.channelKey) {
    disableTarget.value = undefined; problem.value = { type: 'about:blank', status: 409, code: 'client.merchant_changed', title: text('changed') }; return;
  }
  const operation = scope.begin('payments.merchants.update'); if (!operation) return; changing.value = true;
  try { await disablePaymentMerchantConfig(target.id, operation.signal); if (!operation.current()) return;
    disableTarget.value = undefined; ElMessage.success(t('paymentMerchantConfigs.disableSuccess')); await load();
  } catch (error) { if (operation.current()) ElMessage.error(toProblem(error, 'paymentMerchantConfigs.saveFailed').title); }
  finally { if (operation.current()) changing.value = false; operation.finish(); }
}
onMounted(() => { if (typeof window.matchMedia !== 'function') return; viewportQuery = window.matchMedia('(max-width: 640px)');
  updateViewport(); viewportQuery.addEventListener?.('change', updateViewport); });
onBeforeUnmount(() => viewportQuery?.removeEventListener?.('change', updateViewport));
</script>

<template>
  <section class="payment-merchant-configs-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('paymentMerchantConfigs.title') }}</h1>
    <el-alert v-if="problem" type="error" :title="problem.title" show-icon class="art-page-alert" />

    <el-card class="art-full-height-card" shadow="never">
      <ElForm class="merchant-filters" label-position="top" @submit.prevent="search">
        <ElFormItem :label="t('paymentMerchantConfigs.fieldTenantId')"><ElInput v-model="filters.tenantId" data-testid="merchant-filter-tenant" /></ElFormItem>
        <ElFormItem :label="t('paymentMerchantConfigs.fieldChannelKey')"><ElSelect v-model="filters.channel" data-testid="merchant-filter-channel" :aria-label="t('paymentMerchantConfigs.fieldChannelKey')">
          <ElOption :label="text('all')" value="all" /><ElOption :label="t('paymentMerchantConfigs.channelWeChatNative')" value="wechat_native" /><ElOption :label="t('paymentMerchantConfigs.channelAlipayPage')" value="alipay_page" />
        </ElSelect></ElFormItem>
        <ElFormItem :label="t('paymentMerchantConfigs.fieldEnabled')"><ElSelect v-model="filters.enabled" data-testid="merchant-filter-enabled" :aria-label="t('paymentMerchantConfigs.fieldEnabled')">
          <ElOption :label="text('all')" value="all" /><ElOption :label="t('paymentMerchantConfigs.enabledYes')" value="enabled" /><ElOption :label="t('paymentMerchantConfigs.enabledNo')" value="disabled" />
        </ElSelect></ElFormItem>
        <ElFormItem :label="text('nameSearch')"><ElInput v-model="filters.name" data-testid="merchant-filter-name" /></ElFormItem>
        <div class="merchant-filter-actions"><ElButton native-type="submit" :disabled="!canRead" data-testid="merchant-filter-search">{{ text('search') }}</ElButton><ElButton :disabled="!canRead" data-testid="merchant-filter-reset" @click="resetFilters">{{ text('reset') }}</ElButton></div>
      </ElForm>
      <p class="merchant-filter-hint">{{ text('hint') }}</p>
      <ArtTableHeader @refresh="load">
        <template #left>
          <PermissionGate code="payments.merchants.create">
            <el-button type="primary" :icon="Plus" data-testid="payment-merchant-config-create" @click="openCreate">
              {{ t('paymentMerchantConfigs.addConfig') }}
            </el-button>
          </PermissionGate>
        </template>
      </ArtTableHeader>

      <div ref="tableMainRef" class="art-table-main">
        <el-table
          :data="items"
          :height="tableHeight"
          :size="tableSize"
          :stripe="tableZebra"
          :border="tableBorder"
          :header-cell-style="tableHeaderCellStyle"
        >
          <el-table-column prop="channelKey" :label="t('paymentMerchantConfigs.fieldChannelKey')" width="140" />
          <el-table-column prop="name" :label="t('paymentMerchantConfigs.fieldName')" min-width="140" />
          <ElTableColumn prop="id" :label="text('id')" min-width="300" />
          <ElTableColumn :label="t('paymentMerchantConfigs.fieldTenantId')" min-width="300"><template #default="{ row }">{{ row.tenantId ?? 'Host' }}</template></ElTableColumn>
          <el-table-column prop="maskedMerchantId" :label="t('paymentMerchantConfigs.fieldMerchantId')" min-width="140" />
          <el-table-column prop="maskedAppId" :label="t('paymentMerchantConfigs.fieldAppId')" min-width="140" />
          <el-table-column :label="t('paymentMerchantConfigs.fieldEnabled')" width="90">
            <template #default="{ row }">
              <el-tag :type="row.isEnabled ? 'success' : 'info'">
                {{ row.isEnabled ? t('paymentMerchantConfigs.enabledYes') : t('paymentMerchantConfigs.enabledNo') }}
              </el-tag>
            </template>
          </el-table-column>
          <!-- @vue-generic {PaymentMerchantConfigListItem} -->
          <el-table-column :label="t('paymentMerchantConfigs.actions')" width="180" :fixed="narrowViewport ? undefined : 'right'">
            <template #default="{ row }">
              <ArtTableActionGroup>
                <PermissionGate code="payments.merchants.update">
                  <ArtTableActionButton type="edit" :disabled="changing || !canRead" :test-id="`merchant-edit-${row.id}`" :title="t('paymentMerchantConfigs.actionEdit')" :aria-label="t('paymentMerchantConfigs.actionEdit')" @click="openEdit(row)" />
                  <ArtTableActionButton type="delete" :disabled="changing || !row.isEnabled || !host" :test-id="`merchant-disable-${row.id}`" :title="t('paymentMerchantConfigs.actionDisable')" :aria-label="t('paymentMerchantConfigs.actionDisable')" @click="disableRow(row)" />
                </PermissionGate>
              </ArtTableActionGroup>
            </template>
          </el-table-column>
        </el-table>
      </div>

      <el-pagination
        :current-page="page"
        v-model:page-size="pageSize"
        layout="total, prev, pager, next"
        :total="total"
        @current-change="changePage"
      />
    </el-card>

    <ArtFormDialog :open="!!disableTarget && host && session.can('payments.merchants.update')" :title="t('paymentMerchantConfigs.actionDisable')" :saving="changing"
      :confirm-disabled="changing" :confirm-label="text('confirm')" :cancel-label="text('close')" confirm-test-id="merchant-disable-confirm" trap-focus
      width="min(560px, calc(100vw - 32px))" @update:open="disableTarget = undefined" @confirm="confirmDisable">
      <p>{{ t('paymentMerchantConfigs.confirmDisable', { name: disableTarget?.name ?? '' }) }}</p><p class="merchant-identity" data-testid="merchant-disable-identity">{{ disableTarget?.id }}<br>{{ disableTarget?.tenantId ?? 'Host' }}</p>
    </ArtFormDialog>
    <ArtFormDialog
      :open="editorOpen && host"
      :title="editorMode === 'create' ? t('paymentMerchantConfigs.createTitle') : t('paymentMerchantConfigs.editTitle')"
      :saving="changing"
      :confirm-label="text('confirm')" :cancel-label="text('close')" :confirm-disabled="changing" confirm-test-id="merchant-editor-confirm" trap-focus
      width="min(560px, calc(100vw - 32px))" @update:open="closeEditor"
      @confirm="saveEditor"
    >
      <el-form ref="editorFormRef" label-position="top" :disabled="changing">
        <el-form-item :label="t('paymentMerchantConfigs.fieldTenantId')">
          <el-input v-model="editorForm.tenantId" :readonly="editorMode === 'edit'" data-testid="merchant-editor-tenant" />
        </el-form-item>
        <el-form-item :label="t('paymentMerchantConfigs.fieldChannelKey')" required>
          <el-select v-model="editorForm.channelKey" :disabled="editorMode === 'edit'">
            <el-option :label="t('paymentMerchantConfigs.channelWeChatNative')" value="wechat_native" />
            <el-option :label="t('paymentMerchantConfigs.channelAlipayPage')" value="alipay_page" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('paymentMerchantConfigs.fieldName')" required>
          <el-input v-model="editorForm.name" data-testid="merchant-editor-name" />
        </el-form-item>
        <el-form-item :label="t('paymentMerchantConfigs.fieldAppId')" required>
          <el-input v-model="editorForm.appId" />
        </el-form-item>
        <el-form-item v-if="!isAlipayPage" :label="t('paymentMerchantConfigs.fieldMerchantId')" required>
          <el-input v-model="editorForm.merchantId" />
        </el-form-item>
        <el-form-item v-if="!isAlipayPage" :label="t('paymentMerchantConfigs.fieldCertificateSerialNo')" required>
          <el-input v-model="editorForm.certificateSerialNo" />
        </el-form-item>
        <el-form-item :label="t('paymentMerchantConfigs.fieldNotifyUrl')" required>
          <el-input v-model="editorForm.notifyUrl" />
        </el-form-item>
        <el-form-item v-if="isAlipayPage" :label="t('paymentMerchantConfigs.fieldReturnUrl')" required>
          <el-input v-model="editorForm.returnUrl" />
        </el-form-item>
        <el-form-item v-if="!isAlipayPage" :label="t('paymentMerchantConfigs.fieldApiV3Key')">
          <el-input v-model="editorForm.apiV3Key" type="password" show-password data-testid="merchant-editor-secret" />
        </el-form-item>
        <el-form-item :label="t('paymentMerchantConfigs.fieldPrivateKey')">
          <el-input v-model="editorForm.privateKeyPem" type="textarea" :rows="4" />
        </el-form-item>
        <el-form-item :label="t('paymentMerchantConfigs.fieldDefault')">
          <el-switch v-model="editorForm.isDefault" />
        </el-form-item>
        <el-form-item :label="t('paymentMerchantConfigs.fieldEnabled')">
          <el-switch v-model="editorForm.isEnabled" />
        </el-form-item>
      </el-form>
    </ArtFormDialog>
  </section>
</template>

<style scoped>
.merchant-filters { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(230px, 100%), 1fr)); gap: 4px 12px; }
.merchant-filters :deep(.el-form-item) { margin-bottom: 8px; }
.merchant-filters :deep(.el-select) { width: 100%; }
.merchant-filter-actions { display: flex; align-items: end; gap: 8px; padding-bottom: 8px; flex-wrap: wrap; }
.merchant-filter-actions :deep(.el-button + .el-button) { margin-left: 0; }
.merchant-filter-hint, .merchant-identity { overflow-wrap: anywhere; }
</style>
