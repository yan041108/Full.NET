import { describe, expect, it } from 'vitest';
import {
  isCurrentUserResponse,
  isLocalePreferenceResponse,
  isNavigationTree,
  isTenantContextSummaryArray,
  isTenantContextTokenResponse
} from '../js/core/contracts.js';

const tenantId = '019bc2b1-2a40-7cc3-8992-a80de51bf294';

describe('Layui 不可信响应契约', () => {
  it('要求当前用户包含演员作用域', () => {
    const user = currentUser();
    expect(isCurrentUserResponse(user)).toBe(true);
    expect(isCurrentUserResponse({ ...user, actorScope: undefined })).toBe(false);
    expect(isCurrentUserResponse({ ...user, isSuperAdministrator: undefined })).toBe(false);
    expect(isCurrentUserResponse({ ...user, preferredLocale: 'fr-FR' })).toBe(false);
  });

  it('语言偏好响应只接受规范语言与正整数版本', () => {
    expect(isLocalePreferenceResponse({
      preferredLocale: 'en-US', profileVersion: 2
    })).toBe(true);
    expect(isLocalePreferenceResponse({
      preferredLocale: 'en-GB', profileVersion: 2
    })).toBe(false);
    expect(isLocalePreferenceResponse({
      preferredLocale: 'en-US', profileVersion: 0
    })).toBe(false);
  });

  it('拒绝重复、父子不一致和不安全组件键的导航', () => {
    const node = navigationNode();
    expect(isNavigationTree([node])).toBe(true);
    expect(isNavigationTree([node, { ...node }])).toBe(false);
    expect(isNavigationTree([{
      ...node,
      children: [{ ...node, id: 'child', parentId: 'wrong' }]
    }])).toBe(false);
    expect(isNavigationTree([{ ...node, componentKey: '../script' }])).toBe(false);
    expect(isNavigationTree([{
      ...node,
      requiredPermission: 'identity.super_administrators.read'
    }])).toBe(true);
  });

  it('校验租户列表和上下文令牌的一致性', () => {
    const tenant = {
      id: tenantId,
      identifier: 'acme',
      name: 'Acme Corporation',
      domain: 'acme.localhost'
    };
    expect(isTenantContextSummaryArray([tenant])).toBe(true);
    expect(isTenantContextSummaryArray([tenant, { ...tenant }])).toBe(false);
    expect(isTenantContextTokenResponse({
      accessToken: 'token', tokenType: 'Bearer',
      expiresAtUtc: '2026-07-17T04:00:00Z',
      context: {
        tenantId,
        identifier: 'acme',
        name: 'Acme Corporation',
        scope: `tenant:${tenantId.replaceAll('-', '')}`
      }
    })).toBe(true);
    expect(isTenantContextTokenResponse({
      accessToken: 'token', tokenType: 'Bearer',
      expiresAtUtc: '2026-07-17T04:00:00Z',
      context: {
        tenantId,
        identifier: 'acme',
        name: 'Acme Corporation',
        scope: 'host'
      }
    })).toBe(false);
  });

  it('守卫不修改不可信对象', () => {
    const value = [{
      ...navigationNode(),
      children: [{ ...navigationNode(), id: 'child', parentId: 'bad' }]
    }];
    const before = structuredClone(value);

    expect(isNavigationTree(value)).toBe(false);
    expect(value).toEqual(before);
  });
});

function currentUser() {
  return {
    id: 'user-id', username: 'admin', displayName: '系统管理员',
    tenantId: null, actorScope: 'host', scope: 'host',
    isSuperAdministrator: true,
    permissions: [], sessionId: 'session-id',
    preferredLocale: 'zh-CN', profileVersion: 1
  };
}

function navigationNode() {
  return {
    id: 'overview', parentId: null, routeName: 'overview', path: '/',
    componentKey: 'overview', title: '工作台', caption: '平台运行概览',
    icon: 'dashboard', order: 10,
    requiredPermission: 'platform.dashboard.read', children: []
  };
}
