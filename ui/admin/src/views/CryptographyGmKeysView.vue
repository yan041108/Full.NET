<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import {
  ElAlert,
  ElButton,
  ElCard,
  ElDescriptions,
  ElDescriptionsItem,
  ElForm,
  ElFormItem,
  ElInput,
  ElMessage,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus';
import type { CryptographyKey, CryptographyStatus, FullNetProblemDetails } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  getCryptographyStatus,
  listCryptographyKeys,
  sm2Sign,
  sm2Verify
} from '../api/cryptography-gm-keys';

defineOptions({ name: 'CryptographyGmKeysView' });

const session = useSessionStore();
const { t } = useAdminI18n();
const status = ref<CryptographyStatus | null>(null);
const keys = ref<CryptographyKey[]>([]);
const loading = ref(false);
const signing = ref(false);
const verifying = ref(false);
const problem = ref<FullNetProblemDetails>();
const signKeyKey = ref('host-integration-signing');
const signMessage = ref('fullnet-integration-payload');
const verifySignature = ref('');
const verifyResult = ref<boolean | null>(null);

const canRead = computed(() => session.can('cryptography.keys.read'));
const canSign = computed(() => session.can('cryptography.sm2.sign'));
const canVerify = computed(() => session.can('cryptography.sm2.verify'));

function statusLabel(value: string): string {
  switch (value) {
    case 'active':
      return t('cryptographyGmKeys.status.active');
    case 'retired':
      return t('cryptographyGmKeys.status.retired');
    default:
      return value;
  }
}

async function loadAll(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    if (canRead.value) {
      status.value = await getCryptographyStatus();
      keys.value = await listCryptographyKeys();
    }
  } catch (error) {
    problem.value = isFullNetProblemDetails(error)
      ? error
      : {
          type: 'about:blank',
          title: t('cryptographyGmKeys.loadFailed'),
          status: 500,
          code: 'client.unexpected_error'
        };
  } finally {
    loading.value = false;
  }
}

async function submitSign(): Promise<void> {
  signing.value = true;
  problem.value = undefined;
  verifyResult.value = null;
  try {
    const result = await sm2Sign({
      keyKey: signKeyKey.value,
      message: signMessage.value
    });
    verifySignature.value = result.signatureHex;
    ElMessage.success(t('cryptographyGmKeys.signSuccess'));
  } catch (error) {
    problem.value = isFullNetProblemDetails(error)
      ? error
      : {
          type: 'about:blank',
          title: t('cryptographyGmKeys.loadFailed'),
          status: 500,
          code: 'client.unexpected_error'
        };
  } finally {
    signing.value = false;
  }
}

async function submitVerify(): Promise<void> {
  verifying.value = true;
  problem.value = undefined;
  try {
    const result = await sm2Verify({
      keyKey: signKeyKey.value,
      message: signMessage.value,
      signatureHex: verifySignature.value
    });
    verifyResult.value = result.isValid;
    ElMessage.success(t('cryptographyGmKeys.verifySuccess'));
  } catch (error) {
    verifyResult.value = false;
    problem.value = isFullNetProblemDetails(error)
      ? error
      : {
          type: 'about:blank',
          title: t('cryptographyGmKeys.loadFailed'),
          status: 500,
          code: 'client.unexpected_error'
        };
  } finally {
    verifying.value = false;
  }
}

onMounted(() => {
  if (canRead.value) {
    void loadAll();
  }
});
</script>

<template>
  <div class="cryptography-gm-keys-view art-page-stack">
    <ArtTableHeader :title="t('cryptographyGmKeys.title')" />
    <p class="art-muted">{{ t('cryptographyGmKeys.description') }}</p>

    <ElAlert
      v-if="status"
      type="info"
      :closable="false"
      :title="t('cryptographyGmKeys.deploymentNoticeTitle')"
      :description="status.deploymentNotice"
      show-icon
    />

    <ElAlert
      v-if="problem"
      type="error"
      :closable="false"
      :title="problem.title"
      :description="problem.code"
      show-icon
    />

    <ElCard v-if="canRead && status" v-loading="loading">
      <ElDescriptions :column="2" border>
        <ElDescriptionsItem :label="t('cryptographyGmKeys.algorithm')">
          {{ status.algorithm }}
        </ElDescriptionsItem>
        <ElDescriptionsItem :label="t('cryptographyGmKeys.signingPurpose')">
          {{ status.signingPurpose }}
        </ElDescriptionsItem>
        <ElDescriptionsItem :label="t('cryptographyGmKeys.defaultUserId')">
          {{ status.defaultUserId }}
        </ElDescriptionsItem>
      </ElDescriptions>
      <div class="cryptography-gm-keys-actions">
        <ElButton :loading="loading" @click="loadAll">
          {{ t('cryptographyGmKeys.refresh') }}
        </ElButton>
      </div>
    </ElCard>

    <ElCard v-if="canRead" :header="t('cryptographyGmKeys.keysTitle')" v-loading="loading">
      <ElTable :data="keys" stripe>
        <ElTableColumn prop="keyKey" :label="t('cryptographyGmKeys.keyKey')" />
        <ElTableColumn prop="displayName" :label="t('cryptographyGmKeys.displayName')" />
        <ElTableColumn prop="purpose" :label="t('cryptographyGmKeys.purpose')" />
        <ElTableColumn prop="publicKeyFingerprint" :label="t('cryptographyGmKeys.fingerprint')" />
        <ElTableColumn prop="status" :label="t('cryptographyGmKeys.statusLabel')">
          <template #default="{ row }">
            <ElTag :type="row.status === 'active' ? 'success' : 'info'">
              {{ statusLabel(row.status) }}
            </ElTag>
          </template>
        </ElTableColumn>
        <ElTableColumn prop="privateKeyConfigured" :label="t('cryptographyGmKeys.privateKeyConfigured')">
          <template #default="{ row }">
            <ElTag :type="row.privateKeyConfigured ? 'success' : 'warning'">
              {{ row.privateKeyConfigured ? t('cryptographyGmKeys.yes') : t('cryptographyGmKeys.no') }}
            </ElTag>
          </template>
        </ElTableColumn>
      </ElTable>
    </ElCard>

    <ElCard v-if="canSign || canVerify" :header="t('cryptographyGmKeys.workspaceTitle')">
      <ElForm label-position="top">
        <ElFormItem :label="t('cryptographyGmKeys.keyKey')">
          <ElInput v-model="signKeyKey" />
        </ElFormItem>
        <ElFormItem :label="t('cryptographyGmKeys.message')">
          <ElInput v-model="signMessage" type="textarea" :rows="3" />
        </ElFormItem>
        <ElFormItem v-if="canSign">
          <ElButton type="primary" :loading="signing" @click="submitSign">
            {{ t('cryptographyGmKeys.sign') }}
          </ElButton>
        </ElFormItem>
        <ElFormItem :label="t('cryptographyGmKeys.signatureHex')">
          <ElInput v-model="verifySignature" type="textarea" :rows="3" />
        </ElFormItem>
        <ElFormItem v-if="canVerify">
          <ElButton :loading="verifying" @click="submitVerify">
            {{ t('cryptographyGmKeys.verify') }}
          </ElButton>
          <ElTag v-if="verifyResult === true" type="success" class="cryptography-gm-keys-verify-tag">
            {{ t('cryptographyGmKeys.verifyValid') }}
          </ElTag>
          <ElTag v-else-if="verifyResult === false" type="danger" class="cryptography-gm-keys-verify-tag">
            {{ t('cryptographyGmKeys.verifyInvalid') }}
          </ElTag>
        </ElFormItem>
      </ElForm>
    </ElCard>
  </div>
</template>

<style scoped>
.cryptography-gm-keys-view {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.cryptography-gm-keys-actions {
  margin-top: 16px;
}

.cryptography-gm-keys-verify-tag {
  margin-left: 12px;
}
</style>
