import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { ElMessageBox, type MessageBoxData } from 'element-plus';
import NotificationGovernanceView from './NotificationGovernanceView.vue';
import { useSessionStore } from '../auth/session';
import * as api from '../api/notification-platform';
import { showProblem, showWarning } from '../feedback/fullNetMessage';

vi.mock('../api/notification-platform', () => ({
  listNotificationProducerScenes: vi.fn(), createNotificationProducerScene: vi.fn(),
  updateNotificationProducerScene: vi.fn(), setNotificationProducerSceneEnabled: vi.fn(),
  listNotificationQuotaPolicies: vi.fn(), getNotificationQuotaOutstanding: vi.fn(),
  createNotificationQuotaPolicy: vi.fn(), updateNotificationQuotaPolicy: vi.fn(),
  setNotificationQuotaPolicyEnabled: vi.fn()
}));
vi.mock('../feedback/fullNetMessage', () => ({
  showProblem: vi.fn(), showSuccess: vi.fn(), showWarning: vi.fn()
}));

const scene = {
  id: '019bc2b1-2a40-7cc3-8992-a80de51bf299', producerKey: 'orders', sceneKey: 'shipped',
  policyCategoryKey: 'transactional', allowedChannelKeys: ['inbox'],
  emergencyOverrideAllowed: false, isEnabled: true, version: 7,
  createdAtUtc: '2026-10-22T00:00:00Z', updatedAtUtc: '2026-10-22T00:00:00Z'
};
const quota = {
  id: scene.id, dimensionKey: 'provider', dimensionValue: '*', windowSeconds: 60,
  maxCount: 100, isEnabled: true, version: 9,
  createdAtUtc: scene.createdAtUtc, updatedAtUtc: scene.updatedAtUtc
};

function page(kind: 'scenes' | 'quotas', actions: boolean | string[] = false, read = true) {
  const pinia = createPinia();
  setActivePinia(pinia);
  const resource = kind === 'scenes' ? 'producer_scenes' : 'quotas';
  useSessionStore().currentUser = {
    id: scene.id, username: 'admin', displayName: '管理员', tenantId: null,
    actorScope: 'host', scope: 'host', isSuperAdministrator: false,
    passwordChangeRequired: false,
    permissions: [...(read ? [`notifications.${resource}.read`] : []),
      ...(actions === true ? ['create', 'update', 'set_enabled'] : actions || [])
        .map(action => `notifications.${resource}.${action}`)],
    sessionId: scene.id, preferredLocale: 'zh-CN', profileVersion: 1
  };
  return mount(NotificationGovernanceView, { props: { kind }, global: { plugins: [pinia] } });
}

describe('通知治理控制面', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(api.listNotificationProducerScenes).mockResolvedValue({ items: [scene], page: 1, pageSize: 20, total: 1 });
    vi.mocked(api.listNotificationQuotaPolicies).mockResolvedValue({ items: [quota], page: 1, pageSize: 20, total: 1 });
    vi.mocked(api.getNotificationQuotaOutstanding).mockResolvedValue([{ dimensionKey: 'provider', stateKey: 'pending_reconciliation', count: 4 }]);
    vi.mocked(api.updateNotificationProducerScene).mockResolvedValue(scene);
    vi.mocked(api.updateNotificationQuotaPolicy).mockResolvedValue(quota);
    vi.restoreAllMocks();
  });

  it.each(['scenes', 'quotas'] as const)('无读取权限的 %s 页面不请求也不渲染管理入口', async kind => {
    const wrapper = page(kind, true, false);
    await flushPromises();
    expect(api.listNotificationProducerScenes).not.toHaveBeenCalled();
    expect(api.listNotificationQuotaPolicies).not.toHaveBeenCalled();
    expect(api.getNotificationQuotaOutstanding).not.toHaveBeenCalled();
    expect(wrapper.find('[data-testid="governance-create"]').exists()).toBe(false);
  });

  it.each(['scenes', 'quotas'] as const)('只读 %s 页面没有写按钮', async kind => {
    const wrapper = page(kind);
    await flushPromises();
    expect(wrapper.find('[data-testid="governance-create"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="governance-edit"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="governance-toggle"]').exists()).toBe(false);
  });

  it.each([
    ['scenes', 'create'], ['scenes', 'update'], ['scenes', 'set_enabled'],
    ['quotas', 'create'], ['quotas', 'update'], ['quotas', 'set_enabled']
  ] as const)('%s 的 %s 授权只创建对应操作入口', async (kind, action) => {
    const wrapper = page(kind, [action]);
    await flushPromises();
    expect(wrapper.find('[data-testid="governance-create"]').exists()).toBe(action === 'create');
    expect(wrapper.find('[data-testid="governance-edit"]').exists()).toBe(action === 'update');
    expect(wrapper.find('[data-testid="governance-toggle"]').exists()).toBe(action === 'set_enabled');
  });

  it.each(['scenes', 'quotas'] as const)('编辑期间撤销 %s 更新权限后不能保存', async kind => {
    const wrapper = page(kind, ['update']);
    await flushPromises();
    await wrapper.get('[data-testid="governance-edit"]').trigger('click');
    const resource = kind === 'scenes' ? 'producer_scenes' : 'quotas';
    useSessionStore().currentUser!.permissions = [`notifications.${resource}.read`];
    await flushPromises();
    expect(wrapper.find('[data-testid="governance-save"]').exists()).toBe(false);
    await wrapper.findComponent({ name: 'ElForm' }).trigger('submit');
    expect(api.updateNotificationProducerScene).not.toHaveBeenCalled();
    expect(api.updateNotificationQuotaPolicy).not.toHaveBeenCalled();
  });

  it('场景编辑携带原版本，不改名业务标识', async () => {
    const wrapper = page('scenes', true);
    await flushPromises();
    await wrapper.get('[data-testid="governance-edit"]').trigger('click');
    await wrapper.get('[data-testid="governance-channels"]').setValue('inbox, email');
    await wrapper.get('[data-testid="governance-save"]').trigger('click');
    await flushPromises();
    expect(api.updateNotificationProducerScene).toHaveBeenCalledWith(scene.id, {
      policyCategoryKey: 'transactional', allowedChannelKeys: ['inbox', 'email'],
      emergencyOverrideAllowed: false, version: 7
    });
    expect(api.createNotificationProducerScene).not.toHaveBeenCalled();
  });

  it('重复渠道在发送管理命令前拒绝', async () => {
    const wrapper = page('scenes', true);
    await flushPromises();
    await wrapper.get('[data-testid="governance-edit"]').trigger('click');
    await wrapper.get('[data-testid="governance-channels"]').setValue('inbox, inbox');
    await wrapper.get('[data-testid="governance-save"]').trigger('click');
    expect(api.updateNotificationProducerScene).not.toHaveBeenCalled();
    expect(showWarning).toHaveBeenCalled();
  });

  it('配额编辑只发送额度与版本，台账明确不表示剩余额度', async () => {
    const wrapper = page('quotas', true);
    await flushPromises();
    expect(wrapper.text()).toContain('不是当前窗口剩余额度');
    expect(wrapper.text()).toContain('pending_reconciliation: 4');
    await wrapper.get('[data-testid="governance-edit"]').trigger('click');
    await wrapper.get('[data-testid="governance-save"]').trigger('click');
    await flushPromises();
    expect(api.updateNotificationQuotaPolicy).toHaveBeenCalledWith(quota.id, {
      windowSeconds: 60, maxCount: 100, version: 9
    });
  });

  it('版本冲突统一反馈并保留编辑窗口', async () => {
    const problem = { status: 409, title: '政策已变更' };
    vi.mocked(api.updateNotificationProducerScene).mockRejectedValueOnce(problem);
    const wrapper = page('scenes', true);
    await flushPromises();
    await wrapper.get('[data-testid="governance-edit"]').trigger('click');
    await wrapper.get('[data-testid="governance-save"]').trigger('click');
    await flushPromises();
    expect(showProblem).toHaveBeenCalledWith(problem, '治理配置保存未完成。');
    expect(wrapper.findComponent({ name: 'ElDialog' }).props('modelValue')).toBe(true);
  });

  it('确认窗口期间撤销权限后不发送启停命令', async () => {
    let confirm!: () => void;
    vi.spyOn(ElMessageBox, 'confirm').mockImplementation(() => new Promise(resolve => {
      confirm = () => resolve('confirm' as MessageBoxData);
    }));
    const wrapper = page('scenes', true);
    await flushPromises();
    await wrapper.get('[data-testid="governance-toggle"]').trigger('click');
    useSessionStore().currentUser!.permissions = ['notifications.producer_scenes.read'];
    confirm();
    await flushPromises();
    expect(api.setNotificationProducerSceneEnabled).not.toHaveBeenCalled();
  });

  it('启停确认期间切换路由不把场景标识提交到配额入口', async () => {
    let confirm!: () => void;
    vi.spyOn(ElMessageBox, 'confirm').mockImplementation(() => new Promise(resolve => {
      confirm = () => resolve('confirm' as MessageBoxData);
    }));
    const wrapper = page('scenes', ['set_enabled']);
    await flushPromises();
    await wrapper.get('[data-testid="governance-toggle"]').trigger('click');
    useSessionStore().currentUser!.permissions = ['notifications.quotas.read', 'notifications.quotas.set_enabled'];
    await wrapper.setProps({ kind: 'quotas' });
    confirm();
    await flushPromises();
    expect(api.setNotificationProducerSceneEnabled).not.toHaveBeenCalled();
    expect(api.setNotificationQuotaPolicyEnabled).not.toHaveBeenCalled();
  });

  it('台账读取失败清除列表并保持可刷新错误状态', async () => {
    vi.mocked(api.getNotificationQuotaOutstanding).mockRejectedValueOnce(Error('offline'));
    const wrapper = page('quotas', true);
    await flushPromises();
    expect(wrapper.text()).toContain('无法加载治理配置');
    expect(wrapper.findComponent({ name: 'ElTable' }).props('data')).toEqual([]);
  });

  it('路由类型切换重新读取配额而不复用场景行', async () => {
    const wrapper = page('scenes', true);
    await flushPromises();
    useSessionStore().currentUser!.permissions = ['notifications.quotas.read'];
    await wrapper.setProps({ kind: 'quotas' });
    await flushPromises();
    expect(api.listNotificationQuotaPolicies).toHaveBeenCalledWith(1, 20);
    expect(wrapper.findComponent({ name: 'ElTable' }).props('data')).toEqual([quota]);
  });
});
