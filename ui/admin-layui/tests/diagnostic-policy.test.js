import { describe, expect, it, vi } from 'vitest';
import { createDiagnosticPolicyController } from '../js/core/diagnostic-policy.js';

function mountFixture() {
  document.body.innerHTML = `
    <button data-diagnostic-policy-restore type="button">restore</button>
    <div data-diagnostic-policy-problem hidden><strong></strong><span></span></div>
    <div data-diagnostic-policy-directory></div>
  `;
}

describe('Layui 限时诊断策略控制器', () => {
  it('加载默认策略并显示恢复按钮', async () => {
    mountFixture();
    const request = vi.fn().mockResolvedValue({
      version: 0,
      pressureState: 'Normal',
      isDefault: true,
      loadedAtUtc: '2026-08-01T00:00:00.000Z',
      activeRules: [],
      configEntryVersion: 0
    });

    const controller = createDiagnosticPolicyController(document.body, {
      request,
      translation: () => ({
        t: (key, params = {}) => `${key}:${JSON.stringify(params)}`
      }),
      hasPermission: code => code === 'settings.diagnostic_policy.restore'
    });

    await vi.waitFor(() => {
      expect(request).toHaveBeenCalledWith('/api/v1/settings/diagnostic-policy');
    });

    const restore = document.querySelector('[data-diagnostic-policy-restore]');
    expect(restore.hidden).toBe(false);
    expect(restore.disabled).toBe(false);
    expect(document.querySelector('[data-diagnostic-policy-directory]')?.textContent)
      .toContain('diagnosticPolicy.pressureLabel');
    expect(typeof controller.destroy).toBe('function');
  });
});
