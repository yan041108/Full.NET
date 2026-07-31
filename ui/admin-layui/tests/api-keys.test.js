import { describe, expect, it, vi } from 'vitest';
import { createApiKeysController } from '../js/core/api-keys.js';

describe('Layui Host API Key 控制器', () => {
  it('创建后展示并复制一次性明文，再禁用列表项', async () => {
    document.body.innerHTML = `
      <div data-api-keys-problem hidden><strong></strong><span></span></div>
      <form data-api-keys-form>
        <input name="userId">
        <input name="displayName">
        <textarea name="permissions"></textarea>
        <input name="expiresAtUtc">
      </form>
      <section data-api-keys-secret hidden><strong></strong><code></code>
        <button type="button" data-api-keys-copy></button>
      </section>
      <div data-api-keys-directory></div>`;
    const key = {
      id: '019bc2b1-2a40-7cc3-8992-a80de51bf295',
      userId: '019bc2b1-2a40-7cc3-8992-a80de51bf296',
      username: '<automation>',
      displayName: '<部署流水线>',
      keyPrefix: 'fn_live_abcd',
      permissions: ['platform.dashboard.read'],
      expiresAtUtc: null,
      isActive: true,
      lastUsedAtUtc: null,
      createdAtUtc: '2026-07-26T00:00:00Z'
    };
    const request = vi.fn()
      .mockResolvedValueOnce({ items: [key], page: 1, pageSize: 20, total: 1 })
      .mockResolvedValueOnce({ key, secret: 'fn_live_once_only' })
      .mockResolvedValueOnce({ items: [key], page: 1, pageSize: 20, total: 1 })
      .mockResolvedValueOnce({ ...key, isActive: false })
      .mockResolvedValueOnce({ items: [{ ...key, isActive: false }], page: 1, pageSize: 20, total: 1 });
    const clipboard = { writeText: vi.fn().mockResolvedValue(undefined) };
    const controller = createApiKeysController(document, {
      request,
      translation: () => ({ locale: 'zh-CN', t: key => key }),
      canWrite: () => true,
      clipboard,
      confirm: async () => true
    });

    await controller.load();
    expect(document.querySelector('[data-api-keys-directory] strong')?.textContent)
      .toBe('<部署流水线>');
    expect(document.querySelector('[data-api-keys-directory] strong')?.innerHTML)
      .toBe('&lt;部署流水线&gt;');

    const form = document.querySelector('[data-api-keys-form]');
    form.elements.userId.value = key.userId;
    form.elements.displayName.value = '部署流水线';
    form.elements.permissions.value = 'platform.dashboard.read,\nplatform.dashboard.read';
    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    await vi.waitFor(() => expect(request).toHaveBeenCalledTimes(3));

    expect(request).toHaveBeenNthCalledWith(
      2,
      '/api/v1/identity/api-keys',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({
          userId: key.userId,
          displayName: '部署流水线',
          permissions: ['platform.dashboard.read'],
          expiresAtUtc: null
        })
      })
    );
    expect(document.querySelector('[data-api-keys-secret] code')?.textContent)
      .toBe('fn_live_once_only');

    document.querySelector('[data-api-keys-copy]')
      ?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    await vi.waitFor(() => expect(clipboard.writeText).toHaveBeenCalledWith('fn_live_once_only'));

    document.querySelector('[data-api-keys-disable]')
      ?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    await vi.waitFor(() => expect(request).toHaveBeenCalledTimes(5));
    expect(request).toHaveBeenNthCalledWith(
      4,
      `/api/v1/identity/api-keys/${key.id}/disable`,
      { method: 'POST' }
    );
    controller.dispose();
  });

  it('只读用户不会呈现动态禁用按钮', async () => {
    document.body.innerHTML = `
      <form data-api-keys-form></form>
      <section data-api-keys-secret hidden><code></code><button data-api-keys-copy></button></section>
      <button type="button" data-api-keys-refresh></button>
      <div data-api-keys-directory></div>`;
    const controller = createApiKeysController(document, {
      request: vi.fn().mockResolvedValue({
        items: [{
          id: 'key-1',
          displayName: 'readonly',
          username: 'readonly',
          keyPrefix: 'fn',
          permissions: [],
          isActive: true
        }]
      }),
      translation: () => ({ locale: 'zh-CN', t: key => key }),
      canWrite: () => false
    });

    await controller.load();

    expect(document.querySelector('[data-api-keys-disable]')).toBeNull();
    controller.dispose();
  });

  it('刷新列表会重新请求并格式化最后使用时间', async () => {
    document.body.innerHTML = `
      <button type="button" data-api-keys-refresh></button>
      <div data-api-keys-directory></div>`;
    const request = vi.fn()
      .mockResolvedValueOnce({
        items: [{
          id: 'key-1',
          displayName: '流水线',
          username: 'automation',
          keyPrefix: 'fn_live',
          permissions: ['identity.users.read'],
          expiresAtUtc: null,
          isActive: true,
          lastUsedAtUtc: '2026-07-26T12:00:00Z'
        }],
        page: 1,
        pageSize: 20,
        total: 1
      })
      .mockResolvedValueOnce({
        items: [{
          id: 'key-1',
          displayName: '流水线',
          username: 'automation',
          keyPrefix: 'fn_live',
          permissions: ['identity.users.read'],
          expiresAtUtc: null,
          isActive: true,
          lastUsedAtUtc: '2026-07-26T12:00:00Z'
        }],
        page: 1,
        pageSize: 20,
        total: 1
      });
    const controller = createApiKeysController(document, {
      request,
      translation: () => ({ locale: 'zh-CN', t: key => key }),
      canWrite: () => false
    });

    await controller.load();
    const directoryText = document.querySelector('[data-api-keys-directory]')?.textContent ?? '';
    expect(directoryText).toContain('apiKeys.lastUsedAt');
    expect(directoryText).not.toContain('apiKeys.never');

    document.querySelector('[data-api-keys-refresh]')
      ?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    await vi.waitFor(() => expect(request).toHaveBeenCalledTimes(2));
    controller.dispose();
  });
});
