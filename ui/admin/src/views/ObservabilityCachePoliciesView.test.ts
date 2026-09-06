import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import ObservabilityCachePoliciesView from './ObservabilityCachePoliciesView.vue';
import { useSessionStore } from '../auth/session';
import { listObservabilityCachePolicies } from '../api/observability-cache-policies';

vi.mock('../api/observability-cache-policies', () => ({
  listObservabilityCachePolicies: vi.fn(),
  invalidateObservabilityCachePolicy: vi.fn()
}));

const listMock = vi.mocked(listObservabilityCachePolicies);

function mountView(permissions: string[]) {
  const pinia = createPinia();
  setActivePinia(pinia);
  const session = useSessionStore();
  session.currentUser = {
    id: '019bc2b1-2a40-7cc3-8992-a80de51bf296',
    username: 'admin',
    displayName: '管理员',
    tenantId: null,
    actorScope: 'host',
    scope: 'host',
    isSuperAdministrator: false,
    permissions,
    sessionId: '019bc2b1-2a40-7cc3-8992-a80de51bf297',
    preferredLocale: 'zh-CN',
    profileVersion: 1
  };
  return mount(ObservabilityCachePoliciesView, { global: { plugins: [pinia] } });
}

describe('缓存管理页', () => {
  beforeEach(() => {
    listMock.mockReset().mockResolvedValue([
      {
        entryName: 'tenancy.tenant-resolution',
        ownerModule: 'tenancy',
        consistencyClass: 's1',
        accessKind: 'use_cache',
        l1DurationSeconds: 300,
        l2DurationSeconds: 300,
        requiresDirectInvalidation: true,
        canInvalidate: true,
        invalidationOperations: [
          {
            operationKey: 'by-tenant',
            displayName: '按租户失效解析缓存',
            parameters: [
              { name: 'tenantId', valueType: 'uuid', required: true },
              { name: 'domain', valueType: 'domain', required: true }
            ]
          }
        ]
      }
    ]);
  });

  it('读取权限会加载已登记缓存策略目录', async () => {
    const wrapper = mountView(['observability.cache_policies.read']);
    await flushPromises();

    expect(listMock).toHaveBeenCalledTimes(1);
    expect(wrapper.text()).toContain('tenancy.tenant-resolution');
  });
});
