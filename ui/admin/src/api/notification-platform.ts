import {
  notificationsCreateBinding,
  notificationsCreateMyRecipientEndpoint,
  notificationsCreateProviderProfile,
  notificationsCreateTemplate,
  notificationsDisableProviderProfile,
  notificationsDeleteMyRecipientEndpoint,
  notificationsEnableProviderProfile,
  notificationsGetBinding,
  notificationsGetDelivery,
  notificationsGetProviderProfile,
  notificationsGetTemplate,
  notificationsListBindings,
  notificationsListDeliveries,
  notificationsListMyRecipientEndpoints,
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
  type CreateMyRecipientEndpointRequest,
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
  type RecipientEndpointResponse,
  type UpdateNotificationBindingRequest,
  type UpdateNotificationProviderProfileRequest,
  type UpdateNotificationTemplateRequest
} from '@fullnet/client-contracts';
import { http } from './http';

/** 受控非密钥字段类型；未知 TypeKey 或密钥字段必须失败关闭，禁止退回自由 JSON 编辑。 */
const allowedConfigTypes = new Set(['string', 'integer', 'boolean']);

/** 分页查询通知模板列表。 */
export function listNotificationTemplates(
  page = 1,
  pageSize = 20,
  signal?: AbortSignal
): Promise<PagedResultOfNotificationTemplateResponse> {
  return notificationsListTemplates(http, { page, pageSize }, signal);
}

/** 读取单个通知模板详情。 */
export function getNotificationTemplate(
  templateId: string,
  signal?: AbortSignal
): Promise<NotificationTemplateResponse> {
  return notificationsGetTemplate(http, { templateId }, signal);
}

/** 创建通知模板草稿。 */
export function createNotificationTemplate(
  body: CreateNotificationTemplateRequest,
  signal?: AbortSignal
): Promise<NotificationTemplateResponse> {
  return notificationsCreateTemplate(http, { body }, signal);
}

/** 更新通知模板草稿内容。 */
export function updateNotificationTemplate(
  templateId: string,
  body: UpdateNotificationTemplateRequest,
  signal?: AbortSignal
): Promise<NotificationTemplateResponse> {
  return notificationsUpdateTemplate(http, { templateId, body }, signal);
}

/** 发布指定版本的通知模板。 */
export function publishNotificationTemplate(
  templateId: string,
  body: PublishNotificationTemplateRequest,
  signal?: AbortSignal
): Promise<NotificationTemplateResponse> {
  return notificationsPublishTemplate(http, { templateId, body }, signal);
}

/** 查询通知渠道类型描述器，驱动前端动态表单与字段校验。 */
export function listNotificationProviderTypes(
  signal?: AbortSignal
): Promise<NotificationProviderTypeDescriptor[]> {
  return notificationsListProviderTypes(http, {}, signal);
}

/** 分页查询通知渠道配置列表。 */
export function listNotificationProviderProfiles(
  page = 1,
  pageSize = 20,
  signal?: AbortSignal
): Promise<PagedResultOfNotificationProviderProfileResponse> {
  return notificationsListProviderProfiles(http, { page, pageSize }, signal);
}

/** 读取单个通知渠道配置详情。 */
export function getNotificationProviderProfile(
  profileId: string,
  signal?: AbortSignal
): Promise<NotificationProviderProfileResponse> {
  return notificationsGetProviderProfile(http, { profileId }, signal);
}

/** 创建通知渠道配置草稿。 */
export function createNotificationProviderProfile(
  body: CreateNotificationProviderProfileRequest,
  signal?: AbortSignal
): Promise<NotificationProviderProfileResponse> {
  return notificationsCreateProviderProfile(http, { body }, signal);
}

/** 更新通知渠道配置草稿。 */
export function updateNotificationProviderProfile(
  profileId: string,
  body: UpdateNotificationProviderProfileRequest,
  signal?: AbortSignal
): Promise<NotificationProviderProfileResponse> {
  return notificationsUpdateProviderProfile(http, { profileId, body }, signal);
}

/** 发布指定版本的通知渠道配置。 */
export function publishNotificationProviderProfile(
  profileId: string,
  version: number,
  signal?: AbortSignal
): Promise<NotificationProviderProfileResponse> {
  return notificationsPublishProviderProfile(http, { profileId, body: { version } }, signal);
}

/** 启用通知渠道配置。 */
export function enableNotificationProviderProfile(
  profileId: string,
  version: number,
  signal?: AbortSignal
): Promise<NotificationProviderProfileResponse> {
  return notificationsEnableProviderProfile(http, { profileId, body: { version } }, signal);
}

/** 停用通知渠道配置。 */
export function disableNotificationProviderProfile(
  profileId: string,
  version: number,
  signal?: AbortSignal
): Promise<NotificationProviderProfileResponse> {
  return notificationsDisableProviderProfile(http, { profileId, body: { version } }, signal);
}

/** 查询当前用户在当前受信作用域下的脱敏收件端点。 */
export function listMyRecipientEndpoints(
  signal?: AbortSignal
): Promise<RecipientEndpointResponse[]> {
  return notificationsListMyRecipientEndpoints(http, {}, signal);
}

/** 登记当前用户的待验证收件端点；请求体不允许携带用户、租户或验证状态。 */
export function createMyRecipientEndpoint(
  body: CreateMyRecipientEndpointRequest,
  signal?: AbortSignal
): Promise<RecipientEndpointResponse> {
  return notificationsCreateMyRecipientEndpoint(http, { body }, signal);
}

/** 删除当前用户在当前受信作用域下的精确收件端点。 */
export function deleteMyRecipientEndpoint(
  endpointId: string,
  signal?: AbortSignal
): Promise<void> {
  return notificationsDeleteMyRecipientEndpoint(http, { endpointId }, signal);
}

/** 分页查询通知绑定列表。 */
export function listNotificationBindings(
  page = 1,
  pageSize = 20,
  signal?: AbortSignal
): Promise<PagedResultOfNotificationBindingResponse> {
  return notificationsListBindings(http, { page, pageSize }, signal);
}

/** 读取单个通知绑定详情。 */
export function getNotificationBinding(
  bindingId: string,
  signal?: AbortSignal
): Promise<NotificationBindingResponse> {
  return notificationsGetBinding(http, { bindingId }, signal);
}

/** 创建通知绑定草稿。 */
export function createNotificationBinding(
  body: CreateNotificationBindingRequest,
  signal?: AbortSignal
): Promise<NotificationBindingResponse> {
  return notificationsCreateBinding(http, { body }, signal);
}

/** 更新通知绑定草稿。 */
export function updateNotificationBinding(
  bindingId: string,
  body: UpdateNotificationBindingRequest,
  signal?: AbortSignal
): Promise<NotificationBindingResponse> {
  return notificationsUpdateBinding(http, { bindingId, body }, signal);
}

/** 发布指定版本的通知绑定。 */
export function publishNotificationBinding(
  bindingId: string,
  version: number,
  signal?: AbortSignal
): Promise<NotificationBindingResponse> {
  return notificationsPublishBinding(http, { bindingId, body: { version } }, signal);
}

/** 分页查询通知投递记录。 */
export function listNotificationDeliveries(
  page = 1,
  pageSize = 20,
  signal?: AbortSignal
): Promise<PagedResultOfNotificationDeliveryResponse> {
  return notificationsListDeliveries(http, { page, pageSize }, signal);
}

/** 读取单条通知投递详情。 */
export function getNotificationDelivery(
  deliveryId: string,
  signal?: AbortSignal
): Promise<NotificationDeliveryResponse> {
  return notificationsGetDelivery(http, { deliveryId }, signal);
}

/** 对失败或待重试的通知投递发起显式重试。 */
export function retryNotificationDelivery(
  deliveryId: string,
  body: RetryNotificationDeliveryRequest,
  signal?: AbortSignal
): Promise<NotificationDeliveryResponse> {
  return notificationsRetryDelivery(http, { deliveryId, body }, signal);
}

/** 仅保留描述器显式允许的非密钥字段，并按声明类型做失败关闭校验。 */
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

/** 将用户输入的 JSON 文本解析为受控非密钥配置；结构不合法时统一返回客户端错误码。 */
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

/** 导出通知模板、渠道、绑定与投递模型，供配置页、详情页与重试流程共享同一契约。 */
export type {
  CreateNotificationBindingRequest,
  CreateMyRecipientEndpointRequest,
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
  RecipientEndpointResponse,
  UpdateNotificationBindingRequest,
  UpdateNotificationProviderProfileRequest,
  UpdateNotificationTemplateRequest
};
