<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue';
import {
  ElAlert,
  ElButton,
  ElCard,
  ElForm,
  ElFormItem,
  ElInput,
  ElMessage,
  ElMessageBox,
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
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import ArtTableActionButton from '../framework/art-design/components/ArtTableActionButton.vue';
import ArtTableActionGroup from '../framework/art-design/components/ArtTableActionGroup.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import { useArtCrudTableLayout } from '../framework/art-design/composables/useArtCrudTableLayout';
import PermissionGate from '../components/PermissionGate.vue';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  createPaymentMerchantConfig,
  disablePaymentMerchantConfig,
  getPaymentMerchantConfig,
  listPaymentMerchantConfigs,
  updatePaymentMerchantConfig
} from '../api/payment-merchant-configs';

defineOptions({ name: 'PaymentMerchantConfigsView' });

type EditorMode = 'create' | 'edit';

const { t } = useAdminI18n();
const items = ref<PaymentMerchantConfigListItem[]>([]);
const total = ref(0);
const page = ref(1);
const pageSize = ref(20);
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const editorOpen = ref(false);
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

function toProblem(error: unknown, fallbackKey: string): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { title: t(fallbackKey), status: 500, type: 'about:blank' };
}

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    const result = await listPaymentMerchantConfigs({ page: page.value, pageSize: pageSize.value });
    items.value = result.items;
    total.value = result.total;
    await updateTableHeight();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'paymentMerchantConfigs.loadFailed');
  } finally {
    loading.value = false;
  }
}

function openCreate(): void {
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
  editorMode.value = 'edit';
  editing.value = row;
  const detail = await getPaymentMerchantConfig(row.id);
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
}

async function saveEditor(): Promise<void> {
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
      });
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
      });
      ElMessage.success(t('paymentMerchantConfigs.updateSuccess'));
    }
    editorOpen.value = false;
    await load();
  } catch (error: unknown) {
    ElMessage.error(toProblem(error, 'paymentMerchantConfigs.saveFailed').title);
  } finally {
    changing.value = false;
  }
}

async function disableRow(row: PaymentMerchantConfigListItem): Promise<void> {
  await ElMessageBox.confirm(
    t('paymentMerchantConfigs.confirmDisable', { name: row.name }),
    { type: 'warning' }
  );
  await disablePaymentMerchantConfig(row.id);
  ElMessage.success(t('paymentMerchantConfigs.disableSuccess'));
  await load();
}

onMounted(() => {
  void load();
});
</script>

<template>
  <section class="payment-merchant-configs-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('paymentMerchantConfigs.title') }}</h1>
    <el-alert v-if="problem" type="error" :title="problem.title" show-icon class="art-page-alert" />

    <el-card class="art-full-height-card" shadow="never">
      <ArtTableHeader>
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
          <el-table-column prop="maskedMerchantId" :label="t('paymentMerchantConfigs.fieldMerchantId')" min-width="140" />
          <el-table-column prop="maskedAppId" :label="t('paymentMerchantConfigs.fieldAppId')" min-width="140" />
          <el-table-column :label="t('paymentMerchantConfigs.fieldEnabled')" width="90">
            <template #default="{ row }">
              <el-tag :type="row.isEnabled ? 'success' : 'info'">
                {{ row.isEnabled ? t('paymentMerchantConfigs.enabledYes') : t('paymentMerchantConfigs.enabledNo') }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column :label="t('paymentMerchantConfigs.actions')" width="180" fixed="right">
            <template #default="{ row }">
              <ArtTableActionGroup>
                <PermissionGate code="payments.merchants.update">
                  <ArtTableActionButton @click="openEdit(row)">{{ t('paymentMerchantConfigs.actionEdit') }}</ArtTableActionButton>
                  <ArtTableActionButton @click="disableRow(row)">{{ t('paymentMerchantConfigs.actionDisable') }}</ArtTableActionButton>
                </PermissionGate>
              </ArtTableActionGroup>
            </template>
          </el-table-column>
        </el-table>
      </div>

      <el-pagination
        v-model:current-page="page"
        v-model:page-size="pageSize"
        layout="total, prev, pager, next"
        :total="total"
        @current-change="load"
      />
    </el-card>

    <ArtFormDialog
      v-model="editorOpen"
      :title="editorMode === 'create' ? t('paymentMerchantConfigs.createTitle') : t('paymentMerchantConfigs.editTitle')"
      :loading="changing"
      @submit="saveEditor"
    >
      <el-form ref="editorFormRef" label-width="120px">
        <el-form-item :label="t('paymentMerchantConfigs.fieldTenantId')">
          <el-input v-model="editorForm.tenantId" />
        </el-form-item>
        <el-form-item :label="t('paymentMerchantConfigs.fieldChannelKey')" required>
          <el-select v-model="editorForm.channelKey" :disabled="editorMode === 'edit'">
            <el-option :label="t('paymentMerchantConfigs.channelWeChatNative')" value="wechat_native" />
            <el-option :label="t('paymentMerchantConfigs.channelAlipayPage')" value="alipay_page" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('paymentMerchantConfigs.fieldName')" required>
          <el-input v-model="editorForm.name" />
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
          <el-input v-model="editorForm.apiV3Key" type="password" show-password />
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
