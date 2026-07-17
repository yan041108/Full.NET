import { afterEach, describe, expect, it, vi } from 'vitest';
import { initializeAdminApp } from '../js/app.js';

const tenantId = '019bc2b1-2a40-7cc3-8992-a80de51bf294';

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
  it('通过安全 DOM API 呈现动态导航和租户上下文', () => {
    renderDynamicFixture();
    const snapshot = authorizedSnapshot();
    snapshot.navigation[0].title = '<img src=x onerror=alert(1)>';
    const session = createSessionStub(snapshot);

    const app = initializeAdminApp(document, { session, autoRestore: false });

    expect(document.querySelector('[data-navigation]').textContent)
      .toContain('<img src=x onerror=alert(1)>');
    expect(document.querySelector('[data-navigation] img')).toBeNull();
    expect(document.querySelector('[data-current-context]').textContent)
      .toBe('Full.NET Host');
    expect(document.querySelector('[data-context-select] option:last-child').textContent)
      .toBe('Acme Corporation');
    expect(document.querySelector('[data-route-view="overview"]').hidden).toBe(false);
    app.dispose();
  });

  it('切换失败立即恢复旧选择并显示 ProblemDetails', async () => {
    renderDynamicFixture();
    const session = createSessionStub(authorizedSnapshot());
    session.switchTenant.mockRejectedValue({
      status: 404,
      code: 'tenancy.context_not_found',
      title: '租户不存在'
    });
    const app = initializeAdminApp(document, { session, autoRestore: false });
    const selector = document.querySelector('[data-context-select]');

    selector.value = tenantId;
    selector.dispatchEvent(new Event('change', { bubbles: true }));

    await vi.waitFor(() => expect(
      document.querySelector('[data-context-error-code]').textContent
    ).toBe('tenancy.context_not_found'));
    expect(selector.value).toBe('__fullnet_host__');
    expect(session.switchTenant).toHaveBeenCalledWith(tenantId);
    app.dispose();
  });

  it('无切换权限时隐藏声明式操作且不创建租户按钮', () => {
    renderDynamicFixture();
    const snapshot = authorizedSnapshot();
    snapshot.currentUser.permissions = ['tenancy.tenants.read'];
    const session = createSessionStub(snapshot);

    const app = initializeAdminApp(document, { session, autoRestore: false });

    expect(document.querySelector('[data-context-selector]').hidden).toBe(true);
    expect(document.querySelector('[data-tenant-directory] button')).toBeNull();
    app.dispose();
  });

  it('卸载时移除路由、选择器和租户目录监听器', () => {
    renderDynamicFixture();
    const session = createSessionStub(authorizedSnapshot());
    const selector = document.querySelector('[data-context-select]');
    const directory = document.querySelector('[data-tenant-directory]');
    const removeWindowListener = vi.spyOn(window, 'removeEventListener');
    const removeSelectorListener = vi.spyOn(selector, 'removeEventListener');
    const removeDirectoryListener = vi.spyOn(directory, 'removeEventListener');
    const app = initializeAdminApp(document, { session, autoRestore: false });

    app.dispose();

    expect(removeWindowListener).toHaveBeenCalledWith(
      'hashchange',
      expect.any(Function)
    );
    expect(removeSelectorListener).toHaveBeenCalledWith(
      'change',
      expect.any(Function)
    );
    expect(removeDirectoryListener).toHaveBeenCalledWith(
      'click',
      expect.any(Function)
    );
  });

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
      currentUser: {
        username: 'admin', displayName: '系统管理员', tenantId: null,
        scope: 'host', permissions: []
      },
      navigation: [],
      availableTenants: [],
      switching: false,
      currentContextName: 'Full.NET Host'
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

function renderDynamicFixture() {
  document.body.innerHTML = `
    <section data-session-boot></section>
    <section data-login-view hidden></section>
    <div data-session-shell hidden>
      <strong data-current-user></strong>
      <small data-current-scope></small>
      <strong data-current-context></strong>
      <small data-current-context-scope></small>
      <nav data-navigation></nav>
      <label data-context-selector data-permission="tenancy.tenants.switch">
        <select data-context-select></select>
      </label>
      <div data-context-problem hidden>
        <strong data-context-error-code></strong>
        <span data-context-error-title></span>
      </div>
      <div data-tenant-directory></div>
      <button data-session-logout></button>
      <main data-route-view="overview">工作台</main>
      <main data-route-view="tenant-context" hidden>租户上下文</main>
      <main data-route-view="status" hidden>
        <span data-status-code></span>
        <h1 data-status-title></h1>
        <p data-status-description></p>
      </main>
    </div>`;
}

function authorizedSnapshot() {
  return {
    state: 'authenticated',
    currentUser: {
      id: 'user-id', username: 'admin', displayName: '系统管理员',
      tenantId: null, actorScope: 'host', scope: 'host',
      permissions: [
        'platform.dashboard.read',
        'tenancy.tenants.read',
        'tenancy.tenants.switch'
      ],
      sessionId: 'session-id'
    },
    navigation: [
      navigationNode('overview'),
      navigationNode('tenant-context')
    ],
    availableTenants: [{
      id: tenantId, identifier: 'acme', name: 'Acme Corporation',
      domain: 'acme.localhost'
    }],
    switching: false,
    currentContextName: 'Full.NET Host'
  };
}

function navigationNode(componentKey) {
  const tenant = componentKey === 'tenant-context';
  return {
    id: componentKey, parentId: null, routeName: componentKey,
    path: tenant ? '/tenant-context' : '/', componentKey,
    title: tenant ? '租户上下文' : '工作台',
    caption: tenant ? '进入租户或返回 Host' : '平台运行概览',
    icon: tenant ? 'building' : 'dashboard', order: tenant ? 20 : 10,
    requiredPermission: tenant
      ? 'tenancy.tenants.read'
      : 'platform.dashboard.read',
    children: []
  };
}

function createSessionStub(snapshot) {
  return {
    subscribe: vi.fn((listener) => {
      listener(snapshot);
      return vi.fn();
    }),
    login: vi.fn(),
    restore: vi.fn(),
    switchTenant: vi.fn(),
    logout: vi.fn()
  };
}
