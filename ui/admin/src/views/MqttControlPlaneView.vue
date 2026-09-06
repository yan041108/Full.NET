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
  ElInputNumber,
  ElMessage,
  ElPagination,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus';
import type { FullNetProblemDetails, MqttBrokerStatus, MqttClient, MqttMessage } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  getMqttBrokerStatus,
  listMqttClients,
  listMqttMessages,
  publishMqttMessage
} from '../api/mqtt-control-plane';

defineOptions({ name: 'MqttControlPlaneView' });

const session = useSessionStore();
const { t } = useAdminI18n();
const status = ref<MqttBrokerStatus | null>(null);
const clients = ref<MqttClient[]>([]);
const messages = ref<MqttMessage[]>([]);
const page = ref(1);
const pageSize = ref(20);
const total = ref(0);
const loading = ref(false);
const publishing = ref(false);
const problem = ref<FullNetProblemDetails>();
const publishTopic = ref('fullnet/control/ping');
const publishPayload = ref('{"hello":"fullnet"}');
const publishQos = ref(0);
const publishIdempotencyKey = ref('');

const canReadBroker = computed(() => session.can('mqtt.broker.read'));
const canReadClients = computed(() => session.can('mqtt.clients.read'));
const canReadMessages = computed(() => session.can('mqtt.messages.read'));
const canPublish = computed(() => session.can('mqtt.messages.publish'));

function statusLabel(value: string): string {
  switch (value) {
    case 'pending':
      return t('mqttControlPlane.status.pending');
    case 'published':
      return t('mqttControlPlane.status.published');
    case 'failed':
      return t('mqttControlPlane.status.failed');
    default:
      return value;
  }
}

function statusTagType(value: string): 'info' | 'success' | 'danger' {
  switch (value) {
    case 'published':
      return 'success';
    case 'failed':
      return 'danger';
    default:
      return 'info';
  }
}

async function loadAll(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    if (canReadBroker.value) {
      status.value = await getMqttBrokerStatus();
    }
    if (canReadClients.value) {
      clients.value = await listMqttClients();
    }
    if (canReadMessages.value) {
      const result = await listMqttMessages({ page: page.value, pageSize: pageSize.value });
      messages.value = result.items;
      total.value = result.total;
    }
  } catch (error) {
    problem.value = isFullNetProblemDetails(error)
      ? error
      : {
          type: 'about:blank',
          title: t('mqttControlPlane.loadFailed'),
          status: 500,
          code: 'client.unexpected_error'
        };
  } finally {
    loading.value = false;
  }
}

async function submitPublish(): Promise<void> {
  publishing.value = true;
  problem.value = undefined;
  try {
    await publishMqttMessage({
      topic: publishTopic.value,
      payload: publishPayload.value,
      qos: publishQos.value,
      idempotencyKey: publishIdempotencyKey.value.trim() || null
    });
    ElMessage.success(t('mqttControlPlane.publishSuccess'));
    page.value = 1;
    await loadAll();
  } catch (error) {
    problem.value = isFullNetProblemDetails(error)
      ? error
      : {
          type: 'about:blank',
          title: t('mqttControlPlane.loadFailed'),
          status: 500,
          code: 'client.unexpected_error'
        };
  } finally {
    publishing.value = false;
  }
}

async function onPageChange(nextPage: number): Promise<void> {
  page.value = nextPage;
  await loadAll();
}

onMounted(() => {
  if (canReadBroker.value || canReadClients.value || canReadMessages.value) {
    void loadAll();
  }
});
</script>

<template>
  <div class="mqtt-control-plane-view art-page-stack">
    <ArtTableHeader :title="t('mqttControlPlane.title')" />
    <p class="art-muted">{{ t('mqttControlPlane.description') }}</p>

    <ElAlert
      v-if="status"
      type="info"
      :closable="false"
      :title="t('mqttControlPlane.deploymentNoticeTitle')"
      :description="status.deploymentNotice"
      show-icon
      class="mqtt-control-plane-notice"
    />

    <ElAlert
      v-if="problem"
      type="error"
      :closable="false"
      :title="problem.title"
      :description="problem.code"
      show-icon
    />

    <ElCard v-if="canReadBroker && status" v-loading="loading">
      <ElDescriptions :column="2" border>
        <ElDescriptionsItem :label="t('mqttControlPlane.isEnabled')">
          <ElTag :type="status.isEnabled ? 'success' : 'info'">
            {{ status.isEnabled ? t('mqttControlPlane.yes') : t('mqttControlPlane.no') }}
          </ElTag>
        </ElDescriptionsItem>
        <ElDescriptionsItem :label="t('mqttControlPlane.host')">
          {{ status.host }}:{{ status.port }}
        </ElDescriptionsItem>
        <ElDescriptionsItem :label="t('mqttControlPlane.useTls')">
          {{ status.useTls ? t('mqttControlPlane.yes') : t('mqttControlPlane.no') }}
        </ElDescriptionsItem>
        <ElDescriptionsItem :label="t('mqttControlPlane.maximumPayloadBytes')">
          {{ status.maximumPayloadBytes }}
        </ElDescriptionsItem>
        <ElDescriptionsItem :label="t('mqttControlPlane.maximumPublishRatePerMinute')">
          {{ status.maximumPublishRatePerMinute }}
        </ElDescriptionsItem>
        <ElDescriptionsItem :label="t('mqttControlPlane.allowedPrefixes')">
          {{ status.allowedPublishTopicPrefixes.join(', ') }}
        </ElDescriptionsItem>
      </ElDescriptions>
      <div class="mqtt-control-plane-actions">
        <ElButton :loading="loading" @click="loadAll">
          {{ t('mqttControlPlane.refresh') }}
        </ElButton>
      </div>
    </ElCard>

    <ElCard v-if="canPublish" :header="t('mqttControlPlane.publishTitle')">
      <ElForm label-position="top" @submit.prevent="submitPublish">
        <ElFormItem :label="t('mqttControlPlane.topic')">
          <ElInput v-model="publishTopic" />
        </ElFormItem>
        <ElFormItem :label="t('mqttControlPlane.payload')">
          <ElInput v-model="publishPayload" type="textarea" :rows="4" />
        </ElFormItem>
        <ElFormItem :label="t('mqttControlPlane.qos')">
          <ElInputNumber v-model="publishQos" :min="0" :max="2" />
        </ElFormItem>
        <ElFormItem :label="t('mqttControlPlane.idempotencyKey')">
          <ElInput v-model="publishIdempotencyKey" />
        </ElFormItem>
        <ElButton type="primary" :loading="publishing" @click="submitPublish">
          {{ t('mqttControlPlane.publish') }}
        </ElButton>
      </ElForm>
    </ElCard>

    <ElCard v-if="canReadClients" :header="t('mqttControlPlane.clientsTitle')" v-loading="loading">
      <ElTable :data="clients" stripe>
        <ElTableColumn prop="clientKey" :label="t('mqttControlPlane.clientKey')" />
        <ElTableColumn prop="displayName" :label="t('mqttControlPlane.displayName')" />
        <ElTableColumn prop="isEnabled" :label="t('mqttControlPlane.status')">
          <template #default="{ row }">
            <ElTag :type="row.isEnabled ? 'success' : 'info'">
              {{ row.isEnabled ? t('mqttControlPlane.yes') : t('mqttControlPlane.no') }}
            </ElTag>
          </template>
        </ElTableColumn>
      </ElTable>
    </ElCard>

    <ElCard v-if="canReadMessages" :header="t('mqttControlPlane.messagesTitle')" v-loading="loading">
      <ElTable :data="messages" stripe>
        <ElTableColumn prop="topic" :label="t('mqttControlPlane.topic')" />
        <ElTableColumn prop="clientKey" :label="t('mqttControlPlane.clientKey')" />
        <ElTableColumn prop="payloadSizeBytes" :label="t('mqttControlPlane.payloadSizeBytes')" />
        <ElTableColumn prop="qos" :label="t('mqttControlPlane.qos')" />
        <ElTableColumn prop="status" :label="t('mqttControlPlane.status')">
          <template #default="{ row }">
            <ElTag :type="statusTagType(row.status)">
              {{ statusLabel(row.status) }}
            </ElTag>
          </template>
        </ElTableColumn>
        <ElTableColumn prop="createdAtUtc" :label="t('mqttControlPlane.createdAtUtc')" />
      </ElTable>
      <ElPagination
        v-model:current-page="page"
        :page-size="pageSize"
        :total="total"
        layout="total, prev, pager, next"
        @current-change="onPageChange"
      />
    </ElCard>
  </div>
</template>

<style scoped>
.mqtt-control-plane-view {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.mqtt-control-plane-actions {
  margin-top: 16px;
}

.mqtt-control-plane-notice {
  margin-bottom: 0;
}
</style>
