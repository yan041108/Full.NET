import {
  notificationsCreateBinding,
  notificationsCreateProviderProfile,
  notificationsCreateTemplate,
  notificationsDisableProviderProfile,
  notificationsEnableProviderProfile,
  notificationsGetBinding,
  notificationsGetDelivery,
  notificationsGetProviderProfile,
  notificationsGetTemplate,
  notificationsListBindings,
  notificationsListDeliveries,
  notificationsListProviderProfiles,
  notificationsListProviderTypes,
  notificationsListTemplates,
  notificationsPublishBinding,
  notificationsPublishProviderProfile,
  notificationsPublishTemplate,
  notificationsRetryDelivery,
  notificationsUpdateBinding,
  notificationsUpdateProviderProfile,
  notificationsUpdateTemplate,
  type CreateNotificationBindingRequest,
  type CreateNotificationProviderProfileRequest,
  type CreateNotificationTemplateRequest,
  type NotificationBindingResponse,
  type NotificationDeliveryResponse,
  type NotificationProviderProfileResponse,
  type NotificationProviderTypeDescriptor,
  type NotificationTemplateResponse,
  type PagedResultOfNotificationBindingResponse,
  type PagedResultOfNotificationDeliveryResponse,
  type PagedResultOfNotificationProviderProfileResponse,
  type PagedResultOfNotificationTemplateResponse,
  type PublishNotificationTemplateRequest,
  type RetryNotificationDeliveryRequest,
  type UpdateNotificationBindingRequest,
  type UpdateNotificationProviderProfileRequest,
  type UpdateNotificationTemplateRequest
} from '@fullnet/client-contracts';
import { http } from './http';

/** 受控非密钥字段类型；未知 TypeKey 或密钥字段必须失败关闭，禁止退回自由 JSON 编辑。 */
const allowedConfigTypes = new Set(['string', 'integer', 'boolean']);

export function listNotificationTemplates(
  page = 1,
  pageSize = 20,
  signal?: AbortSignal
): Promise<PagedResultOfNotificationTemplateResponse> {
  return notificationsListTemplates(http, { page, pageSize }, signal);
}

export function getNotificationTemplate(
  templateId: string,
  signal?: AbortSignal
): Promise<NotificationTemplateResponse> {
  return notificationsGetTemplate(http, { templateId }, signal);
}

export function createNotificationTemplate(
  body: CreateNotificationTemplateRequest,
  signal?: AbortSignal
): Promise<NotificationTemplateResponse> {
  return notificationsCreateTemplate(http, { body }, signal);
}

export function updateNotificationTemplate(
  templateId: string,
  body: UpdateNotificationTemplateRequest,
  signal?: AbortSignal
): Promise<NotificationTemplateResponse> {
  return notificationsUpdateTemplate(http, { templateId, body }, signal);
}

export function publishNotificationTemplate(
  templateId: string,
  body: PublishNotificationTemplateRequest,
  signal?: AbortSignal
): Promise<NotificationTemplateResponse> {
  return notificationsPublishTemplate(http, { templateId, body }, signal);
}

export function listNotificationProviderTypes(
  signal?: AbortSignal
): Promise<NotificationProviderTypeDescriptor[]> {
  return notificationsListProviderTypes(http, {}, signal);
}

export function listNotificationProviderProfiles(
  page = 1,
  pageSize = 20,
  signal?: AbortSignal
): Promise<PagedResultOfNotificationProviderProfileResponse> {
  return notificationsListProviderProfiles(http, { page, pageSize }, signal);
}

export function getNotificationProviderProfile(
  profileId: string,
  signal?: AbortSignal
): Promise<NotificationProviderProfileResponse> {
  return notificationsGetProviderProfile(http, { profileId }, signal);
}

export function createNotificationProviderProfile(
  body: CreateNotificationProviderProfileRequest,
  signal?: AbortSignal
): Promise<NotificationProviderProfileResponse> {
  return notificationsCreateProviderProfile(http, { body }, signal);
}

export function updateNotificationProviderProfile(
  profileId: string,
  body: UpdateNotificationProviderProfileRequest,
  signal?: AbortSignal
): Promise<NotificationProviderProfileResponse> {
  return notificationsUpdateProviderProfile(http, { profileId, body }, signal);
}

export function publishNotificationProviderProfile(
  profileId: string,
  version: number,
  signal?: AbortSignal
): Promise<NotificationProviderProfileResponse> {
  return notificationsPublishProviderProfile(http, { profileId, body: { version } }, signal);
}

export function enableNotificationProviderProfile(
  profileId: string,
  version: number,
  signal?: AbortSignal
): Promise<NotificationProviderProfileResponse> {
  return notificationsEnableProviderProfile(http, { profileId, body: { version } }, signal);
}

export function disableNotificationProviderProfile(
  profileId: string,
  version: number,
  signal?: AbortSignal
): Promise<NotificationProviderProfileResponse> {
  return notificationsDisableProviderProfile(http, { profileId, body: { version } }, signal);
}

export function listNotificationBindings(
  page = 1,
  pageSize = 20,
  signal?: AbortSignal
): Promise<PagedResultOfNotificationBindingResponse> {
  return notificationsListBindings(http, { page, pageSize }, signal);
}

export function getNotificationBinding(
  bindingId: string,
  signal?: AbortSignal
): Promise<NotificationBindingResponse> {
  return notificationsGetBinding(http, { bindingId }, signal);
}

export function createNotificationBinding(
  body: CreateNotificationBindingRequest,
  signal?: AbortSignal
): Promise<NotificationBindingResponse> {
  return notificationsCreateBinding(http, { body }, signal);
}

export function updateNotificationBinding(
  bindingId: string,
  body: UpdateNotificationBindingRequest,
  signal?: AbortSignal
): Promise<NotificationBindingResponse> {
  return notificationsUpdateBinding(http, { bindingId, body }, signal);
}

export function publishNotificationBinding(
  bindingId: string,
  version: number,
  signal?: AbortSignal
): Promise<NotificationBindingResponse> {
  return notificationsPublishBinding(http, { bindingId, body: { version } }, signal);
}

export function listNotificationDeliveries(
  page = 1,
  pageSize = 20,
  signal?: AbortSignal
): Promise<PagedResultOfNotificationDeliveryResponse> {
  return notificationsListDeliveries(http, { page, pageSize }, signal);
}

export function getNotificationDelivery(
  deliveryId: string,
  signal?: AbortSignal
): Promise<NotificationDeliveryResponse> {
  return notificationsGetDelivery(http, { deliveryId }, signal);
}

export function retryNotificationDelivery(
  deliveryId: string,
  body: RetryNotificationDeliveryRequest,
  signal?: AbortSignal
): Promise<NotificationDeliveryResponse> {
  return notificationsRetryDelivery(http, { deliveryId, body }, signal);
}

export function buildNonSecretConfig(
  descriptor: NotificationProviderTypeDescriptor,
  values: Record<string, unknown>
): Record<string, string | number | boolean> {
  const secretNames = new Set(descriptor.secretFieldKeys);
  const allowed = new Map(descriptor.nonSecretFields.map(field => [field.name, field]));
  const result: Record<string, string | number | boolean> = {};

  for (const [name, raw] of Object.entries(values)) {
    if (secretNames.has(name) || !allowed.has(name)) {
      throw new Error('client.unknown_provider_config_field');
    }

    const field = allowed.get(name)!;
    if (!allowedConfigTypes.has(field.typeKey)) {
      throw new Error('client.unknown_provider_config_field');
    }

    if (raw === undefined || raw === null || raw === '') {
      continue;
    }

    if (field.typeKey === 'string' && typeof raw === 'string') {
      result[name] = raw;
      continue;
    }

    // Schema 驱动表单用 input[type=number] 仍会给出十进制字符串，只接受完整整数。
    if (field.typeKey === 'integer') {
      const parsed = typeof raw === 'number' ? raw : Number(raw);
      if (Number.isInteger(parsed)) {
        result[name] = parsed;
        continue;
      }
    }

    if (field.typeKey === 'boolean' && typeof raw === 'boolean') {
      result[name] = raw;
      continue;
    }

    throw new Error('client.unknown_provider_config_field');
  }

  return result;
}

export function parseNonSecretConfigJson(
  json: string,
  descriptor: NotificationProviderTypeDescriptor
): Record<string, string | number | boolean> {
  let parsed: unknown;
  try {
    parsed = JSON.parse(json) as unknown;
  } catch {
    throw new Error('client.invalid_provider_config');
  }

  if (parsed === null || typeof parsed !== 'object' || Array.isArray(parsed)) {
    throw new Error('client.invalid_provider_config');
  }

  return buildNonSecretConfig(descriptor, parsed as Record<string, unknown>);
}

export type {
  CreateNotificationBindingRequest,
  CreateNotificationProviderProfileRequest,
  CreateNotificationTemplateRequest,
  NotificationBindingResponse,
  NotificationDeliveryResponse,
  NotificationProviderProfileResponse,
  NotificationProviderTypeDescriptor,
  NotificationTemplateResponse,
  PagedResultOfNotificationBindingResponse,
  PagedResultOfNotificationDeliveryResponse,
  PagedResultOfNotificationProviderProfileResponse,
  PagedResultOfNotificationTemplateResponse,
  PublishNotificationTemplateRequest,
  RetryNotificationDeliveryRequest,
  UpdateNotificationBindingRequest,
  UpdateNotificationProviderProfileRequest,
  UpdateNotificationTemplateRequest
};
