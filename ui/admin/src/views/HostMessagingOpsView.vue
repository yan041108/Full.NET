<script setup lang="ts">
import { onMounted, ref } from 'vue';
import {
  ElButton,
  ElCard,
  ElInput,
  ElPagination,
  ElTable,
  ElTableColumn
} from 'element-plus';
import type { FullNetProblemDetails } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import PermissionGate from '../components/PermissionGate.vue';
import { useArtCrudTableLayout } from '../framework/art-design/composables/useArtCrudTableLayout';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  listMessagingDeadLetters,
  type MessagingDeadLetter
} from '../api/messaging-operations';

defineOptions({ name: 'HostMessagingOpsView' });

const { t } = useAdminI18n();
const items = ref<MessagingDeadLetter[]>([]);
const page = ref(1);
const pageSize = ref(20);
const total = ref(0);
const loading = ref(false);
const problem = ref<FullNetProblemDetails>();
const consumerFilter = ref('');

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

onMounted(() => {
  void load();
});

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    const result = await listMessagingDeadLetters(
      page.value,
      pageSize.value,
      consumerFilter.value
    );
    items.value = result.items;
    total.value = result.total;
    await updateTableHeight();
  } catch (error) {
    if (isFullNetProblemDetails(error)) {
      problem.value = error;
    }
    items.value = [];
    total.value = 0;
  } finally {
    loading.value = false;
  }
}

function onSearch(): void {
  page.value = 1;
  void load();
}
</script>

<template>
  <PermissionGate code="messaging.dead_letters.read">
    <section class="host-messaging-ops-view art-page-stack art-full-height" :aria-busy="loading">
      <h1 class="art-sr-heading" data-route-heading tabindex="-1">
        {{ t('navigation.hostMessagingOps.title') }}
      </h1>
      <p class="art-page-caption">{{ t('navigation.hostMessagingOps.caption') }}</p>

      <div v-if="problem" class="art-inline-alert" role="alert">
        <strong translate="no">{{ problem.code }}</strong>
        <span>{{ problem.title }}</span>
      </div>

      <ElCard shadow="never" class="art-table-card art-crud-page">
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
              <ElInput
                v-model="consumerFilter"
                clearable
                placeholder="Consumer"
                style="max-width: 280px"
                @keyup.enter="onSearch"
              />
              <ElButton @click="onSearch">{{ t('users.query') }}</ElButton>
            </template>
          </ArtTableHeader>
          <ElTable
            v-loading="loading"
            :data="items"
            :height="tableHeight"
            :size="tableSize"
            :stripe="tableZebra"
            :border="tableBorder"
            :header-cell-style="tableHeaderCellStyle"
          >
            <ElTableColumn prop="consumerName" label="Consumer" min-width="160" />
            <ElTableColumn prop="messageType" label="MessageType" min-width="220" show-overflow-tooltip />
            <ElTableColumn prop="attempts" label="Attempts" width="100" />
            <ElTableColumn prop="lastErrorCode" label="ErrorCode" width="140" />
            <ElTableColumn prop="lastError" label="Error" min-width="200" show-overflow-tooltip />
            <ElTableColumn prop="receivedAtUtc" label="ReceivedAtUtc" width="190" />
          </ElTable>
        </div>
        <ElPagination
          v-model:current-page="page"
          v-model:page-size="pageSize"
          class="art-crud-pagination"
          layout="total, sizes, prev, pager, next"
          :total="total"
          @current-change="load"
          @size-change="load"
        />
      </ElCard>
    </section>
  </PermissionGate>
</template>
