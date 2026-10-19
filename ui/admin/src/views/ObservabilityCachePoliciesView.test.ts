import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import ObservabilityCachePoliciesView from './ObservabilityCachePoliciesView.vue';
import { useSessionStore } from '../auth/session';
import { invalidateObservabilityCachePolicy, listObservabilityCachePolicies } from '../api/observability-cache-policies';

vi.mock('../api/observability-cache-policies', () => ({
  listObservabilityCachePolicies: vi.fn(),
  invalidateObservabilityCachePolicy: vi.fn()
}));

const listMock = vi.mocked(listObservabilityCachePolicies);
const invalidateMock = vi.mocked(invalidateObservabilityCachePolicy);

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>(complete => { resolve = complete; });
  return { promise, resolve };
}

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
    passwordChangeRequired: false,
    permissions,
    sessionId: '019bc2b1-2a40-7cc3-8992-a80de51bf297',
    preferredLocale: 'zh-CN',
    profileVersion: 1
  };
  return mount(ObservabilityCachePoliciesView, { global: { plugins: [pinia] } });
}

describe('缓存管理页', () => {
  beforeEach(() => {
    invalidateMock.mockReset();
    listMock.mockReset().mockResolvedValue([
      {
        jitterSeconds: 0, negativeDurationSeconds: null, failSafeEnabled: false, failSafeMaxDurationSeconds: null,
        requiresVersionRecheck: false, maxSerializedBytes: 65536, runtimeStatus: 'unknown',
        snapshotAgeSeconds: null, snapshotMaxAgeSeconds: null,
        entryName: 'settings.grid-preference',
        ownerModule: 'settings',
        consistencyClass: 's1',
        accessKind: 'use_cache',
        l1DurationSeconds: 300,
        l2DurationSeconds: 300,
        requiresDirectInvalidation: true,
        canInvalidate: true,
        invalidationOperations: [
          {
            operationKey: 'by-user-grid',
            displayName: '按用户失效展示偏好',
            parameters: [
              { name: 'userId', valueType: 'uuid', required: true },
              { name: 'gridKey', valueType: 'grid_key', required: true }
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
    expect(wrapper.text()).toContain('settings.grid-preference');
  });

  it('显示完整有效策略，保留亚秒精度，并将缺失运行遥测显示为未知', async () => {
    const previous = await listMock();
    listMock.mockClear();
    listMock.mockResolvedValue([Object.assign({}, previous[0], {
      jitterSeconds: 1.25, negativeDurationSeconds: 0.75, failSafeEnabled: true,
      failSafeMaxDurationSeconds: 90, requiresVersionRecheck: false,
      maxSerializedBytes: 12345, runtimeStatus: 'unknown'
    })]);
    const wrapper = mountView(['observability.cache_policies.read']);
    await flushPromises();
    expect(wrapper.find('[data-policy-field="jitter"]').text()).toBe('1.25s');
    expect(wrapper.find('[data-policy-field="negative-duration"]').text()).toBe('0.75s');
    expect(wrapper.find('[data-policy-field="fail-safe"]').text()).toBe('是');
    expect(wrapper.find('[data-policy-field="fail-safe-retention"]').text()).toBe('90s');
    expect(wrapper.find('[data-policy-field="version-recheck"]').text()).toBe('否');
    expect(wrapper.find('[data-policy-field="max-bytes"]').text()).toBe('12345 B');
    expect(wrapper.find('[data-policy-field="runtime-status"]').text()).toBe('未知');
    wrapper.unmount();
  });

  it('诊断快照显示真实年龄并仅提交本机刷新', async () => {
    const previous = await listMock();
    listMock.mockClear();
    listMock.mockResolvedValue([{ ...previous[0], entryName: 'settings.diagnostic-policy',
      accessKind: 'local_snapshot', consistencyClass: 'n0', l1DurationSeconds: null, l2DurationSeconds: null,
      snapshotAgeSeconds: 2.5, snapshotMaxAgeSeconds: 60, runtimeStatus: 'snapshot_loaded',
      invalidationOperations: [{ operationKey: 'global', displayName: '刷新本机快照', parameters: [] }]
    }]);
    invalidateMock.mockResolvedValue({ entryName: 'settings.diagnostic-policy', operationKey: 'global',
      scope: 'current_node_only', invalidatedTargets: [] });
    const wrapper = mountView(['observability.cache_policies.read', 'observability.cache_policies.invalidate']);
    await flushPromises();
    expect(wrapper.find('[data-policy-field="snapshot-age"]').text()).toBe('2.5s');
    expect(wrapper.text()).toContain('已加载本机快照');
    await wrapper.find('.observability-cache-policies__form button').trigger('click');
    await flushPromises();
    expect(invalidateMock).toHaveBeenCalledWith('settings.diagnostic-policy',
      { operationKey: 'global', parameters: {}, scope: 'current_node_only' });
    wrapper.unmount();
  });

  it('明确的空缓存预算显示不适用而不是未知', async () => {
    const previous = await listMock();
    listMock.mockClear();
    listMock.mockResolvedValue([Object.assign({}, previous[0], {
      accessKind: 'authority_read', jitterSeconds: null, negativeDurationSeconds: null,
      failSafeEnabled: false, failSafeMaxDurationSeconds: null,
      requiresVersionRecheck: false, maxSerializedBytes: null, runtimeStatus: 'unknown'
    })]);
    const wrapper = mountView(['observability.cache_policies.read']);
    await flushPromises();
    expect(wrapper.find('[data-policy-field="jitter"]').text()).toBe('不适用');
    expect(wrapper.find('[data-policy-field="max-bytes"]').text()).toBe('不适用');
    expect(wrapper.find('[data-policy-field="fail-safe"]').text()).toBe('否');
    wrapper.unmount();
  });

  it('兼容契约允许的数字字符串，并拒绝非有限时长', async () => {
    const previous = await listMock();
    listMock.mockClear();
    listMock.mockResolvedValue([Object.assign({}, previous[0], {
      jitterSeconds: '1.25', negativeDurationSeconds: 'Infinity', failSafeMaxDurationSeconds: ' '
    })]);
    const wrapper = mountView(['observability.cache_policies.read']);
    await flushPromises();
    expect(wrapper.find('[data-policy-field="jitter"]').text()).toBe('1.25s');
    expect(wrapper.find('[data-policy-field="negative-duration"]').text()).toBe('未知');
    expect(wrapper.find('[data-policy-field="fail-safe-retention"]').text()).toBe('未知');
    wrapper.unmount();
  });

  it('刷新目录后使用最新策略并移除已经关闭的失效入口', async () => {
    const wrapper = mountView(['observability.cache_policies.read', 'observability.cache_policies.invalidate']);
    await flushPromises();
    expect(wrapper.find('.observability-cache-policies__form').exists()).toBe(true);
    const previous = await listMock.mock.results[0].value;
    listMock.mockResolvedValue([{ ...previous[0], canInvalidate: false, l1DurationSeconds: 60 }]);

    await wrapper.find('.observability-cache-policies__toolbar button').trigger('click');
    await flushPromises();

    expect(wrapper.find('.observability-cache-policies__form').exists()).toBe(false);
    expect(wrapper.text()).toContain('1m');
    wrapper.unmount();
  });

  it.each(['observing', 'not_applicable', 'future-status'])('运行状态 %s 不推断为缓存健康或数据最新', async status => {
    const catalog = await listMock();
    listMock.mockClear();
    listMock.mockResolvedValue([{ ...catalog[0], runtimeStatus: status }]);
    const wrapper = mountView(['observability.cache_policies.read']);
    await flushPromises();
    const actual = wrapper.find('[data-policy-field="runtime-status"]').text();
    expect(actual).toBe(status === 'observing' ? '当前节点已接入事件观察' : status === 'not_applicable' ? '不适用' : '未知');
    wrapper.unmount();
  });

  it('撤销读取权限后丢弃在途目录，恢复权限只接受新请求结果', async () => {
    const catalog = await listMock();
    const pending = deferred<Awaited<ReturnType<typeof listObservabilityCachePolicies>>>();
    listMock.mockClear();
    listMock.mockReturnValueOnce(pending.promise).mockResolvedValue([{ ...catalog[0], entryName: 'fresh-entry' }]);
    const wrapper = mountView(['observability.cache_policies.read']);
    await flushPromises();
    const session = useSessionStore();
    session.currentUser!.permissions = [];
    await flushPromises();
    session.currentUser!.permissions = ['observability.cache_policies.read'];
    await flushPromises();
    expect(wrapper.text()).toContain('fresh-entry');
    pending.resolve(catalog);
    await flushPromises();
    expect(wrapper.text()).toContain('fresh-entry');
    expect(wrapper.text()).not.toContain('settings.grid-preference');
    wrapper.unmount();
  });

  it('失效执行期间禁止重复提交和刷新，撤销权限后不显示迟到结果', async () => {
    const pending = deferred<Awaited<ReturnType<typeof invalidateObservabilityCachePolicy>>>();
    invalidateMock.mockReturnValue(pending.promise);
    const wrapper = mountView(['observability.cache_policies.read', 'observability.cache_policies.invalidate']);
    await flushPromises();
    const submit = wrapper.find('.observability-cache-policies__form button');
    await submit.trigger('click');
    await submit.trigger('click');
    expect(invalidateMock).toHaveBeenCalledTimes(1);
    const refresh = wrapper.find('.observability-cache-policies__toolbar button');
    expect((refresh.element as HTMLButtonElement).disabled).toBe(true);
    await refresh.trigger('click');
    expect(listMock).toHaveBeenCalledTimes(1);
    useSessionStore().currentUser!.permissions = [];
    await flushPromises();
    pending.resolve({ entryName: 'settings.grid-preference', operationKey: 'by-user-grid', scope: 'all_layers_synchronous', invalidatedTargets: ['private-late-target'] });
    await flushPromises();
    expect(wrapper.text()).not.toContain('private-late-target');
    expect(wrapper.find('.observability-cache-policies__form').exists()).toBe(false);
    wrapper.unmount();
  });

  it('没有读取权限时不创建刷新按钮或请求目录', async () => {
    const wrapper = mountView([]);
    await flushPromises();
    expect(listMock).not.toHaveBeenCalled();
    expect(wrapper.find('.observability-cache-policies__toolbar button').exists()).toBe(false);
    wrapper.unmount();
  });
});
