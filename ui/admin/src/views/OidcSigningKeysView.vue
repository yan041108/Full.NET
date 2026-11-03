<script setup lang="ts">
import 'element-plus/theme-chalk/el-descriptions.css';
import { computed, onMounted, ref } from 'vue';
import {
  ElAlert,
  ElCard,
  ElDescriptions,
  ElDescriptionsItem,
  ElDrawer,
  ElMessage,
  ElMessageBox,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus';
import type { FullNetProblemDetails, OidcSigningKey } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtTableActionButton from '../framework/art-design/components/ArtTableActionButton.vue';
import ArtTableActionGroup from '../framework/art-design/components/ArtTableActionGroup.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import { useArtCrudTableLayout } from '../framework/art-design/composables/useArtCrudTableLayout';
import PermissionGate from '../components/PermissionGate.vue';
import { useAdminI18n } from '../i18n/adminI18n';
import { activateOidcSigningKey, changeOidcSigningKeyLifecycle, listOidcSigningKeys } from '../api/oidc-signing-keys';

defineOptions({ name: 'OidcSigningKeysView' });

const { t } = useAdminI18n();
const keys = ref<OidcSigningKey[]>([]);
const usesEphemeralDevelopmentKey = ref(false);
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const detailOpen = ref(false);
const detailItem = ref<OidcSigningKey | null>(null);

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

const sortedKeys = computed(() =>
  [...keys.value].sort((left, right) => {
    if (left.isActive !== right.isActive) {
      return left.isActive ? -1 : 1;
    }
    return left.keyId.localeCompare(right.keyId);
  })
);

function canActivate(row: OidcSigningKey) {
  return !row.isActive && row.hasPrivateKey && row.isActivationReady === true && !usesEphemeralDevelopmentKey.value;
}

function lifecycleLabel(value: unknown): string {
  switch (value) {
    case 'prepublished': return t('oidcSigningKeys.lifecycle.prepublished');
    case 'active': return t('oidcSigningKeys.lifecycle.active');
    case 'retained': return t('oidcSigningKeys.lifecycle.retained');
    case 'retired': return t('oidcSigningKeys.lifecycle.retired');
    default: return t('oidcSigningKeys.lifecycle.unregistered');
  }
}

async function changeLifecycle(row: OidcSigningKey, operation: 'publish' | 'retire'): Promise<void> {
  await ElMessageBox.confirm(t(operation === 'publish' ? 'oidcSigningKeys.confirmPublish' : 'oidcSigningKeys.confirmRetire', { keyId: row.keyId }), { type: 'warning' });
  changing.value = true;
  try {
    const result = await changeOidcSigningKeyLifecycle(row.keyId, operation);
    keys.value = result.keys; usesEphemeralDevelopmentKey.value = result.usesEphemeralDevelopmentKey;
    detailOpen.value = false; ElMessage.success(t('oidcSigningKeys.lifecycleSuccess'));
  } catch (error) { problem.value = toProblem(error); }
  finally { changing.value = false; }
}

async function load() {
  loading.value = true;
  problem.value = undefined;
  try {
    const result = await listOidcSigningKeys();
    keys.value = result.keys;
    usesEphemeralDevelopmentKey.value = result.usesEphemeralDevelopmentKey;
    await updateTableHeight();
  } catch (error) {
    problem.value = toProblem(error, 'oidcSigningKeys.loadFailed');
  } finally {
    loading.value = false;
  }
}

function openDetail(row: OidcSigningKey) {
  detailItem.value = row;
  detailOpen.value = true;
}

async function confirmActivate(row: OidcSigningKey) {
  await ElMessageBox.confirm(
    t('oidcSigningKeys.confirmActivate', { keyId: row.keyId }),
    { type: 'warning' }
  );
  changing.value = true;
  try {
    const result = await activateOidcSigningKey(row.keyId);
    keys.value = result.keys;
    usesEphemeralDevelopmentKey.value = result.usesEphemeralDevelopmentKey;
    detailOpen.value = false;
    ElMessage.success(t('oidcSigningKeys.activateSuccess'));
  } catch (error) {
    problem.value = toProblem(error);
  } finally {
    changing.value = false;
  }
}

function toProblem(
  error: unknown,
  fallbackKey: 'oidcSigningKeys.loadFailed' | 'oidcSigningKeys.operationFailed' = 'oidcSigningKeys.operationFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { status: 500, code: 'client.oidc_signing_key_failed', title: t(fallbackKey) };
}

onMounted(() => {
  void load();
});
</script>

<template>
  <section class="oidc-signing-keys-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('oidcSigningKeys.title') }}</h1>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
    </div>

    <el-alert
      v-if="usesEphemeralDevelopmentKey"
      class="oidc-signing-keys-ephemeral-alert"
      type="warning"
      :title="t('oidcSigningKeys.ephemeralTitle')"
      :description="t('oidcSigningKeys.ephemeralWarning')"
      show-icon
      :closable="false"
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
          :data="sortedKeys"
          row-key="keyId"
          :height="tableHeight"
          :size="tableSize"
          :stripe="tableZebra"
          :border="tableBorder"
          :header-cell-style="tableHeaderCellStyle"
        >
          <el-table-column type="index" width="56" />
          <el-table-column :label="t('oidcSigningKeys.fieldKeyId')" min-width="180" prop="keyId" />
          <el-table-column :label="t('oidcSigningKeys.fieldAlgorithm')" width="120" prop="algorithm" />
          <el-table-column :label="t('oidcSigningKeys.fieldStatus')" width="120">
            <template #default="{ row }">
              <el-tag :type="row.isActive ? 'success' : 'info'">
                {{ row.isActive ? t('oidcSigningKeys.statusActive') : t('oidcSigningKeys.statusInactive') }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column :label="t('oidcSigningKeys.fieldHasPrivateKey')" width="120" align="center">
            <template #default="{ row }">
              {{ row.hasPrivateKey ? t('oidcSigningKeys.yes') : t('oidcSigningKeys.no') }}
            </template>
          </el-table-column>
          <el-table-column :label="t('oidcSigningKeys.actions')" width="180" fixed="right">
            <template #default="{ row }">
              <ArtTableActionGroup>
                <PermissionGate code="identity.oidc_signing_keys.read">
                  <ArtTableActionButton
                    type="view"
                    :title="t('oidcSigningKeys.detail')"
                    test-id="oidc-signing-keys-action-detail"
                    @click="openDetail(row as OidcSigningKey)"
                  />
                </PermissionGate>
                <PermissionGate code="identity.oidc_signing_keys.activate">
                  <ArtTableActionButton
                    v-if="canActivate(row as OidcSigningKey)"
                    type="password"
                    :title="t('oidcSigningKeys.activate')"
                    test-id="oidc-signing-keys-action-activate"
                    @click="confirmActivate(row as OidcSigningKey)"
                  />
                </PermissionGate>
                <PermissionGate code="identity.oidc_signing_keys.publish">
                  <ArtTableActionButton v-if="row.lifecycleState === 'unregistered' && !usesEphemeralDevelopmentKey" type="add"
                    :title="t('oidcSigningKeys.publish')" test-id="oidc-signing-keys-action-publish" @click="changeLifecycle(row as OidcSigningKey, 'publish')" />
                </PermissionGate>
                <PermissionGate code="identity.oidc_signing_keys.retire">
                  <ArtTableActionButton v-if="row.isRetirementReady === true" type="delete"
                    :title="t('oidcSigningKeys.retire')" test-id="oidc-signing-keys-action-retire" @click="changeLifecycle(row as OidcSigningKey, 'retire')" />
                </PermissionGate>
              </ArtTableActionGroup>
            </template>
          </el-table-column>
        </el-table>
      </div>
    </el-card>

    <el-drawer
      v-model="detailOpen"
      :title="t('oidcSigningKeys.detailTitle')"
      size="min(680px, 100vw)"
      destroy-on-close
    >
      <template v-if="detailItem">
        <el-descriptions :column="1" border>
          <el-descriptions-item :label="t('oidcSigningKeys.fieldKeyId')">
            {{ detailItem.keyId }}
          </el-descriptions-item>
          <el-descriptions-item :label="t('oidcSigningKeys.fieldAlgorithm')">
            {{ detailItem.algorithm }}
          </el-descriptions-item>
          <el-descriptions-item :label="t('oidcSigningKeys.fieldStatus')">
            {{ detailItem.isActive ? t('oidcSigningKeys.statusActive') : t('oidcSigningKeys.statusInactive') }}
          </el-descriptions-item>
          <el-descriptions-item :label="t('oidcSigningKeys.fieldHasPrivateKey')">
            {{ detailItem.hasPrivateKey ? t('oidcSigningKeys.yes') : t('oidcSigningKeys.no') }}
          </el-descriptions-item>
          <el-descriptions-item :label="t('oidcSigningKeys.lifecycleTitle')">{{ lifecycleLabel(detailItem.lifecycleState) }}</el-descriptions-item>
          <el-descriptions-item :label="t('oidcSigningKeys.publicKeyHash')"><code class="oidc-signing-keys-hash" translate="no">{{ detailItem.publicKeyHash }}</code></el-descriptions-item>
          <el-descriptions-item :label="t('oidcSigningKeys.retireAfter')">{{ detailItem.retireAfterUtc ?? '—' }}</el-descriptions-item>
          <el-descriptions-item :label="t('oidcSigningKeys.fieldPublicKeyPem')">
            <pre class="oidc-signing-keys-pem" translate="no">{{ detailItem.publicKeyPem }}</pre>
          </el-descriptions-item>
        </el-descriptions>
        <PermissionGate code="identity.oidc_signing_keys.activate">
          <div v-if="canActivate(detailItem)" class="oidc-signing-keys-drawer-actions">
            <el-button
              type="primary"
              :loading="changing"
              data-testid="oidc-signing-keys-drawer-activate"
              @click="confirmActivate(detailItem)"
            >
              {{ t('oidcSigningKeys.activate') }}
            </el-button>
          </div>
        </PermissionGate>
      </template>
    </el-drawer>
  </section>
</template>

<style scoped>
.oidc-signing-keys-ephemeral-alert {
  margin-bottom: 12px;
}

.oidc-signing-keys-pem {
  margin: 0;
  white-space: pre-wrap;
  word-break: break-all;
  font-family: var(--art-font-mono, monospace);
  font-size: 12px;
}

.oidc-signing-keys-hash {
  overflow-wrap: anywhere;
}

.oidc-signing-keys-drawer-actions {
  margin-top: 16px;
}
</style>
