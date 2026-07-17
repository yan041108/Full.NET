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

    const app = initializeAdminApp(document, { autoRestore: false });
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

    const app = initializeAdminApp(document, { autoRestore: false });

    expect(document.querySelector('[data-route-view="overview"]').hidden).toBe(true);
    expect(document.querySelector('[data-route-view="status"]').hidden).toBe(false);
    expect(document.querySelector('[data-status-code]').textContent).toBe('403');
    expect(document.querySelector('[data-status-title]').textContent).toBe('没有访问权限');
    app.dispose();
  });

  it('会话检查进行中时拒绝重复提交', async () => {
    renderFixture();
    let resolveRequest;
    const pendingResponse = new Promise((resolve) => {
      resolveRequest = resolve;
    });
    const fetchMock = vi.fn().mockReturnValue(pendingResponse);
    vi.stubGlobal('fetch', fetchMock);
    const app = initializeAdminApp(document, { autoRestore: false });
    const button = document.querySelector('[data-testid="load-current-user"]');

    button.click();
    button.click();

    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(button.disabled).toBe(true);
    resolveRequest(new Response(null, { status: 204 }));
    await vi.waitFor(() => expect(button.disabled).toBe(false));
    app.dispose();
  });

  it('匿名状态显示登录门，登录成功后切换控制台并显示当前用户', async () => {
    renderSessionFixture();
    let subscriber;
    const authenticated = {
      state: 'authenticated',
      currentUser: { username: 'admin', displayName: '系统管理员', scope: 'host' }
    };
    const session = {
      subscribe: vi.fn((listener) => {
        subscriber = listener;
        listener({ state: 'anonymous', currentUser: undefined });
        return vi.fn();
      }),
      login: vi.fn(async () => subscriber(authenticated)),
      restore: vi.fn(),
      logout: vi.fn()
    };
    const app = initializeAdminApp(document, { session, autoRestore: false });

    expect(document.querySelector('[data-login-view]').hidden).toBe(false);
    expect(document.querySelector('[data-session-shell]').hidden).toBe(true);
    document.querySelector('[name="username"]').value = 'admin';
    document.querySelector('[name="password"]').value = 'FullNet!2026Secure';
    document.querySelector('[data-login-form]').dispatchEvent(new Event('submit', {
      bubbles: true,
      cancelable: true
    }));

    await vi.waitFor(() => expect(session.login).toHaveBeenCalledWith(
      'admin',
      'FullNet!2026Secure'
    ));
    expect(document.querySelector('[data-login-view]').hidden).toBe(true);
    expect(document.querySelector('[data-session-shell]').hidden).toBe(false);
    expect(document.querySelector('[data-current-user]').textContent).toBe('系统管理员');
    app.dispose();
  });

  it('登录失败时分别呈现稳定错误码和标题', async () => {
    renderSessionFixture();
    const session = {
      subscribe: vi.fn((listener) => {
        listener({ state: 'anonymous', currentUser: undefined });
        return vi.fn();
      }),
      login: vi.fn().mockRejectedValue({
        status: 401,
        code: 'identity.invalid_credentials',
        title: '用户名或密码错误'
      }),
      restore: vi.fn(),
      logout: vi.fn()
    };
    const app = initializeAdminApp(document, { session, autoRestore: false });

    document.querySelector('[data-login-form]').dispatchEvent(new Event('submit', {
      bubbles: true,
      cancelable: true
    }));

    await vi.waitFor(() => {
      expect(document.querySelector('[data-login-error-code]').textContent)
        .toBe('identity.invalid_credentials');
    });
    expect(document.querySelector('[data-login-error-title]').textContent)
      .toBe('用户名或密码错误');
    app.dispose();
  });
});

function renderSessionFixture() {
  document.body.innerHTML = `
    <section data-session-boot></section>
    <section data-login-view hidden>
      <form data-login-form>
        <input name="username">
        <input name="password" type="password">
        <button type="submit">登录</button>
        <div data-login-problem hidden>
          <strong data-login-error-code></strong>
          <span data-login-error-title></span>
        </div>
      </form>
    </section>
    <div data-session-shell hidden>
      <strong data-current-user></strong>
      <small data-current-scope></small>
      <button data-session-logout type="button">退出</button>
      <main data-route-view="overview"></main>
      <main data-route-view="status" hidden>
        <span data-status-code></span>
        <h1 data-status-title></h1>
        <p data-status-description></p>
      </main>
    </div>`;
}
