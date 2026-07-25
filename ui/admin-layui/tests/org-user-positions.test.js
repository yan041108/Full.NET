import { describe, expect, it } from 'vitest';
import { createOrgUserPositionsController } from '../js/core/org-user-positions.js';

describe('Layui 用户职位隶属控制器', () => {
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
        if (url.includes('/identity/users')) {
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
});
