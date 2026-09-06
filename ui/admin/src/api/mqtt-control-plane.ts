import {
  isMqttBrokerStatus,
  isMqttClient,
  isMqttMessage,
  isMqttMessagePage,
  type MqttBrokerStatus,
  type MqttClient,
  type MqttMessage,
  type MqttMessageListQuery,
  type MqttMessagePage,
  type PublishMqttMessageRequest
} from '@fullnet/client-contracts';
import { request } from './http';

function buildMessageListQuery(query: MqttMessageListQuery): string {
  const params = new URLSearchParams();
  params.set('page', String(query.page ?? 1));
  params.set('pageSize', String(query.pageSize ?? 20));
  if (query.clientId) {
    params.set('clientId', query.clientId);
  }
  if (query.status) {
    params.set('status', query.status);
  }
  if (query.topic) {
    params.set('topic', query.topic);
  }
  if (query.fromUtc) {
    params.set('fromUtc', query.fromUtc);
  }
  if (query.toUtc) {
    params.set('toUtc', query.toUtc);
  }
  return params.toString();
}

/** 读取 MQTT Broker 部署状态。 */
export async function getMqttBrokerStatus(
  signal?: AbortSignal
): Promise<MqttBrokerStatus> {
  const value = await request<unknown>(
    '/api/v1/mqtt/status',
    { method: 'GET' },
    signal
  );
  if (!isMqttBrokerStatus(value)) {
    throw new Error('client.invalid_mqtt_broker_status');
  }

  return value;
}

/** 列出 MQTT 客户端目录。 */
export async function listMqttClients(
  signal?: AbortSignal
): Promise<MqttClient[]> {
  const value = await request<unknown>(
    '/api/v1/mqtt/clients',
    { method: 'GET' },
    signal
  );
  if (!Array.isArray(value) || !value.every(isMqttClient)) {
    throw new Error('client.invalid_mqtt_clients');
  }

  return value;
}

/** 分页查询 MQTT 消息记录。 */
export async function listMqttMessages(
  query: MqttMessageListQuery = {},
  signal?: AbortSignal
): Promise<MqttMessagePage> {
  const value = await request<unknown>(
    `/api/v1/mqtt/messages?${buildMessageListQuery(query)}`,
    { method: 'GET' },
    signal
  );
  if (!isMqttMessagePage(value)) {
    throw new Error('client.invalid_mqtt_messages');
  }

  return value;
}

/** 受控发布 MQTT 消息。 */
export async function publishMqttMessage(
  body: PublishMqttMessageRequest,
  signal?: AbortSignal
): Promise<MqttMessage> {
  const value = await request<unknown>(
    '/api/v1/mqtt/messages/publish',
    {
      method: 'POST',
      body: JSON.stringify(body)
    },
    signal
  );
  if (!isMqttMessage(value)) {
    throw new Error('client.invalid_mqtt_message');
  }

  return value;
}

export type {
  MqttBrokerStatus,
  MqttClient,
  MqttMessage,
  MqttMessageListQuery,
  MqttMessagePage,
  PublishMqttMessageRequest
};
