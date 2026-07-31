import { describe, expect, it, vi } from 'vitest';
import { createOrgPositionLevelsController } from '../js/core/org-position-levels.js';

describe('Layui 职级控制器', () => {
  it('加载职级列表', async () => {
    document.body.innerHTML = `
      <div data-org-position-levels-problem hidden><strong></strong><span></span></div>
      <form data-org-position-levels-create-form></form>
      <div data-org-position-levels-directory></div>
    `;
    const request = vi.fn().mockResolvedValue({
      items: [{
        id: '01912345-6789-7abc-8def-0123456789ab',
        code: 'senior',
        name: '高级',
        displayOrder: 10,
        isActive: true,
        createdAtUtc: '2026-07-29T08:00:00.000Z',
        updatedAtUtc: null,
        version: 1
      }]
    });
    const controller = createOrgPositionLevelsController(document, {
      request,
      translation: () => ({ t: key => key })
    });

    await controller.load();

    expect(request).toHaveBeenCalledWith(
      '/api/v1/organization/position-levels?page=1&pageSize=20'
    );
    expect(document.querySelector('[data-org-position-levels-directory] strong')?.textContent)
      .toBe('高级');
    controller.dispose();
  });
});
