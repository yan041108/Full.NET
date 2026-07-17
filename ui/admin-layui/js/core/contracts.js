const stableKeyPattern = /^[a-z][a-z0-9-]{0,63}$/;
const routePathPattern = /^\/(?:[a-z0-9-]+(?:\/[a-z0-9-]+)*)?$/;
const permissionPattern = /^[a-z][a-z0-9]*(?:[.-][a-z0-9]+)+$/;
const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const identifierPattern = /^[a-z0-9][a-z0-9-]{0,62}$/;

/** 校验标准 ProblemDetails 中客户端稳定依赖的字段。 */
export function isFullNetProblemDetails(value) {
  return isRecord(value)
    && Number.isInteger(value.status)
    && typeof value.code === 'string'
    && value.code.length > 0;
}

/** 校验短期访问令牌响应。 */
export function isTokenResponse(value) {
  return isRecord(value)
    && typeof value.accessToken === 'string'
    && value.accessToken.length > 0
    && value.tokenType === 'Bearer'
    && typeof value.expiresAtUtc === 'string'
    && value.expiresAtUtc.length > 0
    && !('preferredLocale' in value)
    && !('profileVersion' in value);
}

/** 校验当前用户与授权摘要。 */
export function isCurrentUserResponse(value) {
  return isRecord(value)
    && typeof value.id === 'string'
    && typeof value.username === 'string'
    && typeof value.displayName === 'string'
    && (typeof value.tenantId === 'string' || value.tenantId === null)
    && typeof value.actorScope === 'string'
    && value.actorScope.length > 0
    && typeof value.scope === 'string'
    && Array.isArray(value.permissions)
    && value.permissions.every(permission => typeof permission === 'string')
    && typeof value.sessionId === 'string'
    && isSupportedLocale(value.preferredLocale)
    && isPositiveInteger(value.profileVersion);
}

/** 校验语言偏好响应，损坏响应不得触发客户端乐观切换。 */
export function isLocalePreferenceResponse(value) {
  return isRecord(value)
    && isSupportedLocale(value.preferredLocale)
    && isPositiveInteger(value.profileVersion);
}

/** 校验完整导航树，不修补或修改任何不可信输入。 */
export function isNavigationTree(value) {
  if (!Array.isArray(value)) {
    return false;
  }

  const ids = new Set();
  return value.every(node => isNavigationNode(node, null, ids));
}

/** 校验可用租户列表并拒绝重复选择键。 */
export function isTenantContextSummaryArray(value) {
  if (!Array.isArray(value)) {
    return false;
  }

  const ids = new Set();
  const identifiers = new Set();
  for (const tenant of value) {
    if (!isTenantContextSummary(tenant)
      || ids.has(tenant.id.toLowerCase())
      || identifiers.has(tenant.identifier)) {
      return false;
    }

    ids.add(tenant.id.toLowerCase());
    identifiers.add(tenant.identifier);
  }

  return true;
}

/** 校验切换后新令牌与上下文描述的一致性。 */
export function isTenantContextTokenResponse(value) {
  return isTokenResponse(value)
    && isRecord(value)
    && isTenantContextDescriptor(value.context);
}

function isNavigationNode(value, expectedParentId, ids) {
  if (!isRecord(value)
    || typeof value.id !== 'string'
    || !stableKeyPattern.test(value.id)
    || ids.has(value.id)
    || value.parentId !== expectedParentId
    || typeof value.routeName !== 'string'
    || !stableKeyPattern.test(value.routeName)
    || typeof value.path !== 'string'
    || !routePathPattern.test(value.path)
    || typeof value.componentKey !== 'string'
    || !stableKeyPattern.test(value.componentKey)
    || !isDisplayText(value.title)
    || typeof value.caption !== 'string'
    || !isDisplayText(value.icon)
    || !Number.isInteger(value.order)
    || typeof value.requiredPermission !== 'string'
    || !permissionPattern.test(value.requiredPermission)
    || !Array.isArray(value.children)) {
    return false;
  }

  ids.add(value.id);
  return value.children.every(child => isNavigationNode(child, value.id, ids));
}

function isTenantContextSummary(value) {
  return isRecord(value)
    && isGuid(value.id)
    && typeof value.identifier === 'string'
    && identifierPattern.test(value.identifier)
    && isDisplayText(value.name)
    && isDisplayText(value.domain);
}

function isTenantContextDescriptor(value) {
  if (!isRecord(value)
    || !(value.tenantId === null || isGuid(value.tenantId))
    || !isDisplayText(value.identifier)
    || !isDisplayText(value.name)
    || !isDisplayText(value.scope)) {
    return false;
  }

  if (value.tenantId === null) {
    return value.identifier === 'host' && value.scope === 'host';
  }

  return identifierPattern.test(value.identifier)
    && value.scope === `tenant:${value.tenantId.replaceAll('-', '').toLowerCase()}`;
}

function isGuid(value) {
  return typeof value === 'string' && guidPattern.test(value);
}

function isDisplayText(value) {
  return typeof value === 'string' && value.trim().length > 0;
}

function isSupportedLocale(value) {
  return value === 'zh-CN' || value === 'en-US';
}

function isPositiveInteger(value) {
  return Number.isInteger(value) && value > 0;
}

function isRecord(value) {
  return typeof value === 'object' && value !== null;
}
