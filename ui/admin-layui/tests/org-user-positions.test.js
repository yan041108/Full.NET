import { describe, expect, it, vi } from 'vitest';
import { createOrgUserPositionsController } from '../js/core/org-user-positions.js';

describe('Layui 用户职位隶属控制器', () => {
  it('只读账号不加载候选，候选端点 403 时降级为空列表', async () => {
    document.body.innerHTML = `
      <form data-org-user-positions-create-form>
        <select name="userId" data-org-user-positions-user></select>
        <select name="positionId" data-org-user-positions-position></select>
      </form>
      <div data-org-user-positions-problem hidden><strong></strong><span></span></div>
      <div data-org-user-positions-directory></div>`;

    const calls = [];
    const controller = createOrgUserPositionsController(document, {
      request: async url => {
        calls.push(url);
        if (url.includes('/user-positions?page=')) {
          return { items: [], page: 1, pageSize: 20, total: 0 };
        }
        if (url.includes('/positions?page=')) {
          return { items: [], page: 1, pageSize: 20, total: 0 };
        }
        return {};
      },
      hasPermission: () => false,
      translation: () => ({
        t: key => key,
        locale: 'zh-CN'
      })
    });

    await controller.load();

    const userSelect = document.querySelector('[data-org-user-positions-user]');
    expect(calls).not.toContain(
      '/api/v1/organization/user-positions/assignable-users?page=1&pageSize=100'
    );
    expect(calls.some(url =>
      url.includes('/identity/users') || url === '/api/v1/me'
    )).toBe(false);
    expect(userSelect.options).toHaveLength(1);
    controller.dispose();

    calls.length = 0;
    const forbiddenController = createOrgUserPositionsController(document, {
      request: async url => {
        calls.push(url);
        if (url.includes('/user-positions?page=')
          || url.includes('/positions?page=')) {
          return { items: [], page: 1, pageSize: 20, total: 0 };
        }
        if (url.includes('/assignable-users')) {
          throw { status: 403, code: 'authorization.permission_denied' };
        }
        return {};
      },
      hasPermission: permission => permission === 'organization.user_positions.write',
      translation: () => ({
        t: key => key,
        locale: 'zh-CN'
      })
    });

    await forbiddenController.load();
    expect(calls).toContain(
      '/api/v1/organization/user-positions/assignable-users?page=1&pageSize=100'
    );
    expect(userSelect.options).toHaveLength(1);
    forbiddenController.dispose();
  });

  it('加载列表、创建隶属并取消', async () => {
    document.body.innerHTML = `
      <form data-org-user-positions-create-form>
        <select name="userId" data-org-user-positions-user></select>
        <select name="positionId" data-org-user-positions-position></select>
      </form>
      <div data-org-user-positions-problem hidden><strong></strong><span></span></div>
      <div data-org-user-positions-directory></div>`;

    const calls = [];
    const controller = createOrgUserPositionsController(document, {
      request: async (url, options) => {
        calls.push({ url, options });
        if (url.includes('/user-positions?page=')) {
          return calls.filter(call => call.url === '/api/v1/organization/user-positions'
            && call.options?.method === 'POST').length > 0
            ? {
              items: [{
                id: 'assignment-id',
                userId: 'user-id',
                username: 'admin',
                displayName: '系统管理员',
                positionId: 'position-id',
                positionCode: 'engineer',
                positionName: '工程师',
                isPrimary: false,
                isActive: true,
                createdAtUtc: '2026-07-25T00:00:00Z',
                updatedAtUtc: null,
                version: 1
              }],
              page: 1,
              pageSize: 20,
              total: 1
            }
            : { items: [], page: 1, pageSize: 20, total: 0 };
        }
        if (url.includes('/positions?page=')) {
          return {
            items: [{
              id: 'position-id',
              code: 'engineer',
              name: '工程师',
              displayOrder: 10,
              isActive: true,
              createdAtUtc: '2026-07-25T00:00:00Z',
              updatedAtUtc: null,
              version: 1
            }],
            page: 1,
            pageSize: 20,
            total: 1
          };
        }
        if (url.includes('/assignable-users')) {
          return {
            items: [{
              id: 'user-id',
              username: 'admin',
              displayName: '系统管理员',
              isActive: true,
              createdAtUtc: '2026-07-25T00:00:00Z',
              updatedAtUtc: null,
              version: 1
            }],
            page: 1,
            pageSize: 20,
            total: 1
          };
        }
        return {};
      },
      hasPermission: permission => permission === 'organization.user_positions.write',
      translation: () => ({
        t: key => key,
        locale: 'zh-CN'
      })
    });

    await controller.load();
    expect(calls[0].url).toBe('/api/v1/organization/user-positions?page=1&pageSize=20');

    const userSelect = document.querySelector('[data-org-user-positions-user]');
    const positionSelect = document.querySelector('[data-org-user-positions-position]');
    userSelect.value = 'user-id';
    positionSelect.value = 'position-id';
    document.querySelector('[data-org-user-positions-create-form]')
      .dispatchEvent(new Event('submit', { cancelable: true }));

    await new Promise(resolve => setTimeout(resolve, 0));
    expect(calls.some(call =>
      call.url === '/api/v1/organization/user-positions'
      && call.options?.method === 'POST')).toBe(true);
    await controller.load();
    expect(calls.filter(call => call.url.includes('/user-positions?page=')).length)
      .toBeGreaterThan(1);
    controller.dispose();
  });

  it('按需加载下一页可分配用户并保留已有选项', async () => {
    document.body.innerHTML = `
      <form data-org-user-positions-create-form>
        <select name="userId" data-org-user-positions-user></select>
        <select name="positionId" data-org-user-positions-position></select>
        <button type="button" data-org-user-positions-load-more-users hidden></button>
      </form>
      <div data-org-user-positions-problem hidden><strong></strong><span></span></div>
      <div data-org-user-positions-directory></div>`;
    const calls = [];
    const controller = createOrgUserPositionsController(document, {
      request: async url => {
        calls.push(url);
        if (url.includes('/user-positions?page=') || url.includes('/positions?page=')) {
          return { items: [], page: 1, pageSize: 20, total: 0 };
        }
        if (url.includes('page=2')) {
          return {
            items: [{ id: 'user-2', username: 'operator', displayName: '操作员' }],
            page: 2,
            pageSize: 100,
            total: 101
          };
        }
        return {
          items: [{ id: 'user-1', username: 'admin', displayName: '管理员' }],
          page: 1,
          pageSize: 100,
          total: 101
        };
      },
      hasPermission: permission => permission === 'organization.user_positions.write',
      translation: () => ({ locale: 'zh-CN', t: key => key })
    });

    await controller.load();
    const loadMore = document.querySelector('[data-org-user-positions-load-more-users]');
    expect(loadMore.hidden).toBe(false);
    loadMore.click();

    await vi.waitFor(() => expect(calls).toContain(
      '/api/v1/organization/user-positions/assignable-users?page=2&pageSize=100'
    ));
    await vi.waitFor(() => expect(
      Array.from(document.querySelector('[data-org-user-positions-user]').options)
        .map(option => option.value)
    ).toEqual(['', 'user-1', 'user-2']));
    controller.dispose();
  });
});
