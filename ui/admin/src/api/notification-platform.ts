import {
  notificationsReconcileDelivery,
  notificationsGetDeliverySummary,
  notificationsDeadLetterDelivery,
  notificationsListProducerScenes,
  notificationsCreateProducerScene,
  notificationsUpdateProducerScene,
  notificationsSetProducerSceneEnabled,
  notificationsListQuotaPolicies,
  notificationsGetQuotaOutstanding,
  notificationsCreateQuotaPolicy,
  notificationsUpdateQuotaPolicy,
  notificationsSetQuotaPolicyEnabled,
  type NotificationProducerSceneResponse,
  type NotificationQuotaPolicyResponse,
  type NotificationQuotaOutstandingResponse,
  type PagedResultOfNotificationProducerSceneResponse,
  type PagedResultOfNotificationQuotaPolicyResponse,
  type CreateNotificationProducerSceneRequest,
  type UpdateNotificationProducerSceneRequest,
  type SetNotificationProducerSceneEnabledRequest,
  type CreateNotificationQuotaPolicyRequest,
  type UpdateNotificationQuotaPolicyRequest,
  type SetNotificationQuotaPolicyEnabledRequest,
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
  notificationsListMyPreferences,
  notificationsListMyPreferenceChannels,
  notificationsUpdateMyPreference,
  type NotificationPreferenceResponse,
  type UpdateMyNotificationPreferenceRequest,
  notificationsListProviderProfiles,
  notificationsListProviderTypes,
  notificationsListTemplates,
  notificationsPublishBinding,
  notificationsPublishProviderProfile,
  notificationsPublishTemplate,
  notificationsRetryDelivery,
  notificationsSendMyRecipientEndpointVerification,
  notificationsUpdateBinding,
  notificationsUpdateProviderProfile,
  notificationsUpdateTemplate,
  notificationsVerifyMyRecipientEndpoint,
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
  type SendRecipientEndpointVerificationResponse,
  type UpdateNotificationBindingRequest,
  type UpdateNotificationProviderProfileRequest,
  type UpdateNotificationTemplateRequest
} from '@fullnet/client-contracts';
import { http } from './http';

/** 当前作用域的投递状态与回执覆盖汇总。 */
export function getNotificationDeliverySummary() { return notificationsGetDeliverySummary(http, {}); }

/** 人工停止失败/未知投递并保留未决额度，不能当作未发送证明。 */
export function deadLetterNotificationDelivery(deliveryId: string, revision: number, reason: string) {
  return notificationsDeadLetterDelivery(http, { deliveryId, body: { revision, reason } });
}

/** 人工证据只确认调用事实，不提升为可信送达。 */
export function reconcileNotificationDelivery(deliveryId: string, revision: number,
  outcomeKey: 'confirmed_accepted' | 'confirmed_not_called', evidenceReference: string) {
  return notificationsReconcileDelivery(http, { deliveryId, body: { revision, outcomeKey, evidenceReference } });
}

/** 受控非密钥字段类型；未知 TypeKey 或密钥字段必须失败关闭，禁止退回自由 JSON 编辑。 */
const allowedConfigTypes = new Set(['string', 'integer', 'boolean']);

/** 读取受控场景目录，分类和渠道仍由服务端校验。 */
export function listNotificationProducerScenes(page = 1, pageSize = 20, signal?: AbortSignal): Promise<PagedResultOfNotificationProducerSceneResponse> {
  return notificationsListProducerScenes(http, { page, pageSize }, signal);
}

/** 创建场景声明，不接受客户端提供的操作者或租户身份。 */
export function createNotificationProducerScene(body: CreateNotificationProducerSceneRequest, signal?: AbortSignal): Promise<NotificationProducerSceneResponse> {
  return notificationsCreateProducerScene(http, { body }, signal);
}

/** 使用读取版本执行场景更新，冲突由标准 ProblemDetails 返回。 */
export function updateNotificationProducerScene(producerSceneId: string, body: UpdateNotificationProducerSceneRequest, signal?: AbortSignal): Promise<NotificationProducerSceneResponse> {
  return notificationsUpdateProducerScene(http, { producerSceneId, body }, signal);
}

/** 启停声明携带期望版本，避免覆盖并发管理变更。 */
export function setNotificationProducerSceneEnabled(producerSceneId: string, body: SetNotificationProducerSceneEnabledRequest, signal?: AbortSignal): Promise<NotificationProducerSceneResponse> {
  return notificationsSetProducerSceneEnabled(http, { producerSceneId, body }, signal);
}

/** 分页读取当前作用域的有界配额声明。 */
export function listNotificationQuotaPolicies(page = 1, pageSize = 20, signal?: AbortSignal): Promise<PagedResultOfNotificationQuotaPolicyResponse> {
  return notificationsListQuotaPolicies(http, { page, pageSize }, signal);
}

/** 台账聚合不是窗口剩余额度，不能作为发送准入依据。 */
export function getNotificationQuotaOutstanding(signal?: AbortSignal): Promise<NotificationQuotaOutstandingResponse[]> {
  return notificationsGetQuotaOutstanding(http, {}, signal);
}

/** 创建配额声明，身份与作用域由服务端可信上下文提供。 */
export function createNotificationQuotaPolicy(body: CreateNotificationQuotaPolicyRequest, signal?: AbortSignal): Promise<NotificationQuotaPolicyResponse> {
  return notificationsCreateQuotaPolicy(http, { body }, signal);
}

/** 更新额度携带期望版本，不允许改名维度或取值。 */
export function updateNotificationQuotaPolicy(quotaPolicyId: string, body: UpdateNotificationQuotaPolicyRequest, signal?: AbortSignal): Promise<NotificationQuotaPolicyResponse> {
  return notificationsUpdateQuotaPolicy(http, { quotaPolicyId, body }, signal);
}

/** 配额启停仍经过服务端版本和权限检查。 */
export function setNotificationQuotaPolicyEnabled(quotaPolicyId: string, body: SetNotificationQuotaPolicyEnabledRequest, signal?: AbortSignal): Promise<NotificationQuotaPolicyResponse> {
  return notificationsSetQuotaPolicyEnabled(http, { quotaPolicyId, body }, signal);
}

/** 查询本人发送意愿，不返回端点原值或其他用户身份。 */
export function listMyNotificationPreferences(signal?: AbortSignal): Promise<NotificationPreferenceResponse[]> {
  return notificationsListMyPreferences(http, {}, signal);
}

/** 最小个人渠道目录，避免偏好页读取渠道控制面配置。 */
export function listMyNotificationPreferenceChannels(signal?: AbortSignal): Promise<string[]> {
  return notificationsListMyPreferenceChannels(http, {}, signal);
}

/** 按服务端路由渠道更新本人偏好，响应必须通过生成守卫验证。 */
export function updateMyNotificationPreference(
  channelKey: string,
  body: UpdateMyNotificationPreferenceRequest,
  signal?: AbortSignal
): Promise<NotificationPreferenceResponse> {
  return notificationsUpdateMyPreference(http, { channelKey, body }, signal);
}

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

/** 向待验证收件端点发送邮件验证码。 */
export function sendMyRecipientEndpointVerification(
  endpointId: string,
  signal?: AbortSignal
): Promise<SendRecipientEndpointVerificationResponse> {
  return notificationsSendMyRecipientEndpointVerification(http, { endpointId }, signal);
}

/** 校验验证码并在成功时自动升级端点为 verified。 */
export function verifyMyRecipientEndpoint(
  endpointId: string,
  code: string,
  signal?: AbortSignal
): Promise<RecipientEndpointResponse> {
  return notificationsVerifyMyRecipientEndpoint(http, { endpointId, body: { code } }, signal);
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
