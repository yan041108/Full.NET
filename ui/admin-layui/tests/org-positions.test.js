import { describe, expect, it, vi } from 'vitest';
import { createOrgPositionsController } from '../js/core/org-positions.js';

describe('Layui 职位控制器', () => {
  it('加载职位列表', async () => {
    document.body.innerHTML = `
      <div data-org-positions-problem hidden><strong></strong><span></span></div>
      <form data-org-positions-create-form></form>
      <div data-org-positions-directory></div>
    `;
    const request = vi.fn().mockResolvedValueOnce({
      items: [{
        id: '01912345-6789-7abc-8def-0123456789ab',
        code: 'engineer',
        name: '工程师',
        displayOrder: 10,
        isActive: true,
        createdAtUtc: '2026-07-25T08:00:00.000Z',
        updatedAtUtc: null,
        version: 1
      }],
      page: 1,
      pageSize: 20,
      total: 1
    });

    const controller = createOrgPositionsController(document, {
      request,
      translation: () => ({
        t: key => key
      })
    });

    await controller.load();
    expect(request).toHaveBeenCalledWith('/api/v1/organization/positions?page=1&pageSize=20');
    expect(document.querySelector('[data-org-positions-directory] strong')?.textContent)
      .toBe('工程师');
    controller.dispose();
  });
});
