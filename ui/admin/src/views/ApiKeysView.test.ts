import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import ApiKeysView from './ApiKeysView.vue';
import { useSessionStore } from '../auth/session';
import {
  createHostApiKey,
  listHostApiKeys
} from '../api/api-keys';

vi.mock('../api/api-keys', () => ({
  createHostApiKey: vi.fn(),
  disableHostApiKey: vi.fn(),
  listHostApiKeys: vi.fn()
}));

const listMock = vi.mocked(listHostApiKeys);
const createMock = vi.mocked(createHostApiKey);
const userId = '019bc2b1-2a40-7cc3-8992-a80de51bf296';

describe('Vue API Key 管理页', () => {
  beforeEach(() => {
    const pinia = createPinia();
    setActivePinia(pinia);
    const session = useSessionStore();
    session.currentUser = {
      id: userId,
      username: 'admin',
      displayName: '管理员',
      tenantId: null,
      actorScope: 'host',
      scope: 'host',
      isSuperAdministrator: false,
      permissions: ['identity.api_keys.read', 'identity.api_keys.write'],
      sessionId: '019bc2b1-2a40-7cc3-8992-a80de51bf297',
      preferredLocale: 'zh-CN',
      profileVersion: 1
    };
    listMock.mockReset().mockResolvedValue({
      items: [],
      page: 1,
      pageSize: 20,
      total: 0
    });
    createMock.mockReset();
  });

  it('创建后仅在当前页面内呈现一次性明文且发送去重权限', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', {
      configurable: true,
      value: { writeText }
    });
    createMock.mockResolvedValue({
      key: {
        id: '019bc2b1-2a40-7cc3-8992-a80de51bf295',
        userId,
        username: 'automation',
        displayName: '部署流水线',
        keyPrefix: 'fn_live_abcd',
        permissions: ['platform.dashboard.read'],
        expiresAtUtc: null,
        isActive: true,
        lastUsedAtUtc: null,
        createdAtUtc: '2026-07-26T00:00:00Z'
      },
      secret: 'fn_live_once_only'
    });
    const wrapper = mount(ApiKeysView);
    await flushPromises();

    await wrapper.get('[data-testid="api-key-user-id"]').setValue(userId);
    await wrapper.get('[data-testid="api-key-display-name"]').setValue('部署流水线');
    await wrapper.get('[data-testid="api-key-permissions"]')
      .setValue('platform.dashboard.read,\nplatform.dashboard.read');
    await wrapper.get('[data-testid="api-key-create-form"]').trigger('submit');
    await flushPromises();

    expect(createMock).toHaveBeenCalledWith({
      userId,
      displayName: '部署流水线',
      permissions: ['platform.dashboard.read'],
      expiresAtUtc: null
    });
    expect(wrapper.get('[data-testid="api-key-secret"]').text())
      .toContain('fn_live_once_only');
    const copyButton = wrapper.findAll('button')
      .find(button => button.text() === '复制密钥');
    await copyButton?.trigger('click');
    await flushPromises();
    expect(writeText).toHaveBeenCalledWith('fn_live_once_only');
    expect(Object.values(localStorage)).not.toContain('fn_live_once_only');
    expect(Object.values(sessionStorage)).not.toContain('fn_live_once_only');
  });

  it('只读权限不会呈现创建表单', async () => {
    useSessionStore().currentUser!.permissions = ['identity.api_keys.read'];

    const wrapper = mount(ApiKeysView);
    await flushPromises();

    expect(wrapper.find('[data-testid="api-key-create-form"]').exists()).toBe(false);
  });
});
