import { afterEach, describe, expect, it, vi } from 'vitest';
import { initializeAdminApp } from '../js/app.js';

afterEach(() => {
  vi.unstubAllGlobals();
  window.history.replaceState({}, '', '/');
  document.body.innerHTML = '';
});

function renderFixture() {
  document.body.innerHTML = `
    <a data-route="/" href="#/">工作台</a>
    <button data-testid="load-current-user">检查会话</button>
    <section data-contract-result hidden>
      <strong data-testid="error-code"></strong>
      <code data-testid="trace-id"></code>
    </section>
    <main data-route-view="overview">概览</main>
    <main data-route-view="status" hidden>
      <span data-status-code></span>
      <h1 data-status-title></h1>
      <p data-status-description></p>
    </main>`;
}

describe('Layui 管理端应用', () => {
  it('接口失败时展示与 Vue 一致的错误码和 TraceId', async () => {
    renderFixture();
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({
      status: 403,
      code: 'authorization.denied',
      traceId: 'trace-parity'
    }), {
      status: 403,
      headers: { 'content-type': 'application/problem+json' }
    })));

    const app = initializeAdminApp(document);
    document.querySelector('[data-testid="load-current-user"]').click();

    await vi.waitFor(() => {
      expect(document.querySelector('[data-testid="error-code"]').textContent)
        .toBe('authorization.denied');
    });
    expect(document.querySelector('[data-testid="trace-id"]').textContent)
      .toBe('trace-parity');
    app.dispose();
  });

  it('根据 Hash 路由呈现 403 状态页', () => {
    renderFixture();
    window.location.hash = '#/403';

    const app = initializeAdminApp(document);

    expect(document.querySelector('[data-route-view="overview"]').hidden).toBe(true);
    expect(document.querySelector('[data-route-view="status"]').hidden).toBe(false);
    expect(document.querySelector('[data-status-code]').textContent).toBe('403');
    expect(document.querySelector('[data-status-title]').textContent).toBe('没有访问权限');
    app.dispose();
  });
});
