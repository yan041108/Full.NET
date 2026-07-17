import { request } from './core/http.js';
import { identitySession } from './core/session.js';

const STATUS_ROUTES = {
  '/403': {
    code: '403',
    title: '没有访问权限',
    description: '当前账号缺少访问此功能所需的权限，请联系管理员完成授权。'
  },
  '/404': {
    code: '404',
    title: '页面没有找到',
    description: '目标地址可能已调整，请从左侧导航重新进入功能。'
  },
  '/500': {
    code: '500',
    title: '服务暂时不可用',
    description: '系统已记录本次异常，请稍后重试并向运维人员提供 TraceId。'
  },
  '/identity': {
    code: 'ID',
    title: '身份与权限',
    description: '身份、角色、权限策略与数据范围功能将在业务模块阶段持续交付。'
  },
  '/organization': {
    code: 'ORG',
    title: '组织与租户',
    description: '组织架构、租户生命周期与跨租户隔离能力将在后续模块中实现。'
  },
  '/settings': {
    code: 'CFG',
    title: '系统设置',
    description: '配置中心、字典、参数与审计能力将在后续模块中实现。'
  }
};

function currentRoute() {
  const route = window.location.hash.replace(/^#/, '') || '/';
  return route.startsWith('/') ? route : `/${route}`;
}

function renderRoute(root) {
  const route = currentRoute();
  const overview = root.querySelector('[data-route-view="overview"]');
  const status = root.querySelector('[data-route-view="status"]');
  const definition = STATUS_ROUTES[route] ?? (route === '/' ? undefined : STATUS_ROUTES['/404']);

  if (overview) {
    overview.hidden = Boolean(definition);
  }
  if (status) {
    status.hidden = !definition;
  }

  if (definition) {
    const code = root.querySelector('[data-status-code]');
    const title = root.querySelector('[data-status-title]');
    const description = root.querySelector('[data-status-description]');
    if (code) code.textContent = definition.code;
    if (title) title.textContent = definition.title;
    if (description) description.textContent = definition.description;
  }

  root.querySelectorAll('[data-route]').forEach((link) => {
    link.classList.toggle('is-active', link.getAttribute('data-route') === route);
  });
}

function showContractResult(root, problem) {
  const panel = root.querySelector('[data-contract-result]');
  const code = root.querySelector('[data-testid="error-code"]');
  const traceId = root.querySelector('[data-testid="trace-id"]');
  if (panel) {
    panel.hidden = false;
    panel.classList.add('is-error');
  }
  if (code) code.textContent = problem?.code ?? 'client.unexpected_error';
  if (traceId) traceId.textContent = problem?.traceId ?? '无 TraceId';
}

/**
 * 初始化 Layui 管理端的交互行为。
 * 返回显式清理函数，确保自动化测试和微前端卸载时不会遗留全局事件。
 */
export function initializeAdminApp(root = document, options = {}) {
  const session = options.session ?? identitySession;
  const autoRestore = options.autoRestore !== false;
  const probeButton = root.querySelector('[data-testid="load-current-user"]');
  const loginForm = root.querySelector('[data-login-form]');
  const logoutButton = root.querySelector('[data-session-logout]');
  let isProbing = false;
  let isLoggingIn = false;

  const onRouteChange = () => renderRoute(root);
  const onProbe = async () => {
    if (isProbing) {
      return;
    }

    isProbing = true;
    if (probeButton) probeButton.disabled = true;
    probeButton?.setAttribute('aria-busy', 'true');
    probeButton?.classList.add('layui-btn-disabled');
    try {
      const currentUser = await request('/api/v1/me');
      const panel = root.querySelector('[data-contract-result]');
      const code = root.querySelector('[data-testid="error-code"]');
      const traceId = root.querySelector('[data-testid="trace-id"]');
      if (panel) {
        panel.hidden = false;
        panel.classList.remove('is-error');
      }
      if (code) code.textContent = `已连接：${currentUser?.displayName ?? '当前用户'}`;
      if (traceId) traceId.textContent = currentUser?.id ?? '';
    } catch (problem) {
      showContractResult(root, problem);
      globalThis.layui?.layer?.msg?.('会话检查失败，请查看错误信息', { icon: 2 });
    } finally {
      isProbing = false;
      if (probeButton) probeButton.disabled = false;
      probeButton?.removeAttribute('aria-busy');
      probeButton?.classList.remove('layui-btn-disabled');
    }
  };

  const onLogin = async (event) => {
    event.preventDefault();
    if (isLoggingIn || !loginForm) {
      return;
    }

    isLoggingIn = true;
    const submitButton = loginForm.querySelector('[type="submit"]');
    if (submitButton) submitButton.disabled = true;
    hideLoginProblem(root);
    try {
      const formData = new FormData(loginForm);
      await session.login(
        String(formData.get('username') ?? '').trim(),
        String(formData.get('password') ?? '')
      );
    } catch (problem) {
      showLoginProblem(root, problem);
      globalThis.layui?.layer?.msg?.('登录失败，请核对错误信息', { icon: 2 });
    } finally {
      isLoggingIn = false;
      if (submitButton) submitButton.disabled = false;
    }
  };
  const onLogout = () => {
    void session.logout();
  };
  const unsubscribeSession = session.subscribe((snapshot) => {
    renderSession(root, snapshot);
  });

  window.addEventListener('hashchange', onRouteChange);
  probeButton?.addEventListener('click', onProbe);
  loginForm?.addEventListener('submit', onLogin);
  logoutButton?.addEventListener('click', onLogout);
  renderRoute(root);

  const ready = autoRestore
    ? Promise.resolve(session.restore())
    : Promise.resolve();

  // Layui 是渐进增强层；核心路由和错误展示不依赖其全局对象，便于测试与降级。
  globalThis.layui?.use?.(['element', 'layer'], () => {
    globalThis.layui.element?.render?.();
  });

  return {
    ready,
    dispose() {
      window.removeEventListener('hashchange', onRouteChange);
      probeButton?.removeEventListener('click', onProbe);
      loginForm?.removeEventListener('submit', onLogin);
      logoutButton?.removeEventListener('click', onLogout);
      unsubscribeSession();
    }
  };
}

function renderSession(root, snapshot) {
  const boot = root.querySelector('[data-session-boot]');
  const login = root.querySelector('[data-login-view]');
  const shell = root.querySelector('[data-session-shell]');
  if (boot) boot.hidden = snapshot.state !== 'initializing';
  if (login) login.hidden = snapshot.state !== 'anonymous';
  if (shell) shell.hidden = snapshot.state !== 'authenticated';

  const currentUser = root.querySelector('[data-current-user]');
  const currentScope = root.querySelector('[data-current-scope]');
  if (currentUser) currentUser.textContent = snapshot.currentUser?.displayName ?? '';
  if (currentScope) {
    currentScope.textContent = snapshot.currentUser?.scope === 'host'
      ? 'Host Admin'
      : snapshot.currentUser?.username ?? '';
  }
}

function showLoginProblem(root, problem) {
  const panel = root.querySelector('[data-login-problem]');
  const code = root.querySelector('[data-login-error-code]');
  const title = root.querySelector('[data-login-error-title]');
  if (panel) panel.hidden = false;
  if (code) code.textContent = problem?.code ?? 'client.login_failed';
  if (title) title.textContent = problem?.title ?? '登录请求未完成';
}

function hideLoginProblem(root) {
  const panel = root.querySelector('[data-login-problem]');
  if (panel) panel.hidden = true;
}
