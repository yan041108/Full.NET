import { request } from './core/http.js';
import { identitySession } from './core/session.js';
import {
  applyPermissionVisibility,
  findNavigationByPath,
  localViewFor,
  renderNavigation
} from './core/navigation.js';

const hostContextValue = '__fullnet_host__';
const knownLocalPaths = new Set(['/', '/tenant-context']);
const statusRoutes = {
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
  }
};

/**
 * 初始化原生 Layui 管理端；返回清理函数，供自动化测试和微前端卸载移除全部监听器。
 */
export function initializeAdminApp(root = document, options = {}) {
  const session = options.session ?? identitySession;
  const autoRestore = options.autoRestore !== false;
  const probeButton = root.querySelector('[data-testid="load-current-user"]');
  const loginForm = root.querySelector('[data-login-form]');
  const logoutButton = root.querySelector('[data-session-logout]');
  const contextSelector = root.querySelector('[data-context-select]');
  const tenantDirectory = root.querySelector('[data-tenant-directory]');
  let latestSnapshot = {
    state: 'initializing',
    currentUser: undefined,
    navigation: [],
    availableTenants: [],
    switching: false,
    currentContextName: 'Full.NET Host'
  };
  let isProbing = false;
  let isLoggingIn = false;
  let isSwitchingContext = false;

  const onRouteChange = () => renderRoute(root, latestSnapshot);
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
  const switchContext = async (value) => {
    if (isSwitchingContext || latestSnapshot.switching) {
      renderContextSelector(contextSelector, latestSnapshot);
      return;
    }

    isSwitchingContext = true;
    hideContextProblem(root);
    renderContextSelector(contextSelector, latestSnapshot);
    if (contextSelector) contextSelector.disabled = true;
    try {
      await session.switchTenant(value === hostContextValue ? null : value);
    } catch (problem) {
      showContextProblem(root, problem);
    } finally {
      isSwitchingContext = false;
      renderContextSelector(contextSelector, latestSnapshot);
    }
  };
  const onContextChange = (event) => {
    const requestedValue = event.currentTarget.value;
    // 原生 select 会先改变选中项，因此立即恢复服务端确认的旧值，禁止乐观展示。
    renderContextSelector(contextSelector, latestSnapshot);
    void switchContext(requestedValue);
  };
  const onTenantAction = (event) => {
    const target = event.target instanceof Element
      ? event.target.closest('[data-context-target]')
      : null;
    if (!target || !tenantDirectory?.contains(target)) {
      return;
    }

    void switchContext(target.dataset.contextTarget);
  };
  const unsubscribeSession = session.subscribe((snapshot) => {
    latestSnapshot = normalizeSnapshot(snapshot);
    renderSession(root, latestSnapshot);
  });

  window.addEventListener('hashchange', onRouteChange);
  probeButton?.addEventListener('click', onProbe);
  loginForm?.addEventListener('submit', onLogin);
  logoutButton?.addEventListener('click', onLogout);
  contextSelector?.addEventListener('change', onContextChange);
  tenantDirectory?.addEventListener('click', onTenantAction);
  renderRoute(root, latestSnapshot);

  const ready = autoRestore
    ? Promise.resolve(session.restore())
    : Promise.resolve();

  // Layui 只负责渐进增强；核心路由、权限与错误反馈在其全局对象缺失时仍可工作。
  globalThis.layui?.use?.(['element', 'form', 'layer'], () => {
    globalThis.layui.element?.render?.();
    globalThis.layui.form?.render?.();
  });

  return {
    ready,
    dispose() {
      window.removeEventListener('hashchange', onRouteChange);
      probeButton?.removeEventListener('click', onProbe);
      loginForm?.removeEventListener('submit', onLogin);
      logoutButton?.removeEventListener('click', onLogout);
      contextSelector?.removeEventListener('change', onContextChange);
      tenantDirectory?.removeEventListener('click', onTenantAction);
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
  root.querySelectorAll('[data-current-context]').forEach((element) => {
    element.textContent = snapshot.currentContextName;
  });
  root.querySelectorAll('[data-current-context-scope]').forEach((element) => {
    element.textContent = snapshot.currentUser?.scope ?? '';
  });

  const navigationContainer = root.querySelector('[data-navigation]');
  if (navigationContainer) {
    renderNavigation(
      navigationContainer,
      snapshot.navigation,
      currentRoute()
    );
  }

  renderContextSelector(root.querySelector('[data-context-select]'), snapshot);
  renderTenantDirectory(root.querySelector('[data-tenant-directory]'), snapshot);
  applyPermissionVisibility(root, snapshot.currentUser?.permissions ?? []);
  renderRoute(root, snapshot);
}

function renderRoute(root, snapshot) {
  const route = currentRoute();
  const navigation = findNavigationByPath(snapshot.navigation, route);
  const status = statusRoutes[route]
    ?? (!navigation
      ? statusRoutes[knownLocalPaths.has(route) ? '/403' : '/404']
      : undefined);
  const activeView = navigation
    ? localViewFor(navigation.componentKey)
    : undefined;

  root.querySelectorAll('[data-route-view]').forEach((view) => {
    const viewKey = view.dataset.routeView;
    view.hidden = status ? viewKey !== 'status' : viewKey !== activeView;
  });

  if (status) {
    setText(root, '[data-status-code]', status.code);
    setText(root, '[data-status-title]', status.title);
    setText(root, '[data-status-description]', status.description);
  }

  const navigationContainer = root.querySelector('[data-navigation]');
  if (navigationContainer) {
    renderNavigation(navigationContainer, snapshot.navigation, route);
  } else {
    root.querySelectorAll('[data-route]').forEach((link) => {
      link.classList.toggle('is-active', link.dataset.route === route);
    });
  }

  setText(
    root,
    '[data-route-title]',
    navigation?.title ?? (status?.title ?? '状态页')
  );
}

function renderContextSelector(selector, snapshot) {
  if (!selector) {
    return;
  }

  const ownerDocument = selector.ownerDocument;
  const fragment = ownerDocument.createDocumentFragment();
  fragment.append(createOption(ownerDocument, hostContextValue, 'Full.NET Host'));
  snapshot.availableTenants.forEach((tenant) => {
    fragment.append(createOption(ownerDocument, tenant.id, tenant.name));
  });
  selector.replaceChildren(fragment);
  selector.value = snapshot.currentUser?.tenantId ?? hostContextValue;
  selector.disabled = snapshot.switching
    || !snapshot.currentUser?.permissions.includes('tenancy.tenants.switch');
}

function renderTenantDirectory(container, snapshot) {
  if (!container) {
    return;
  }

  const ownerDocument = container.ownerDocument;
  const fragment = ownerDocument.createDocumentFragment();
  fragment.append(createTenantCard(ownerDocument, {
    id: null,
    identifier: 'host',
    name: 'Full.NET Host',
    domain: '宿主控制面'
  }, snapshot));
  snapshot.availableTenants.forEach((tenant) => {
    fragment.append(createTenantCard(ownerDocument, tenant, snapshot));
  });
  container.replaceChildren(fragment);
}

function createTenantCard(ownerDocument, tenant, snapshot) {
  const article = ownerDocument.createElement('article');
  const isCurrent = snapshot.currentUser?.tenantId === tenant.id;
  article.classList.toggle('is-active', isCurrent);
  if (tenant.id) article.dataset.tenantId = tenant.id;

  const code = ownerDocument.createElement('span');
  code.className = 'fn-tenant-card__code';
  code.textContent = tenant.identifier;
  const title = ownerDocument.createElement('h3');
  title.textContent = tenant.name;
  const domain = ownerDocument.createElement('p');
  domain.textContent = tenant.domain;
  const footer = ownerDocument.createElement('div');
  const state = ownerDocument.createElement('small');
  state.textContent = isCurrent ? '当前上下文' : '可进入';
  footer.append(state);

  const canSwitch = snapshot.currentUser?.permissions.includes(
    'tenancy.tenants.switch'
  ) === true;
  if (canSwitch && !isCurrent) {
    const button = ownerDocument.createElement('button');
    button.type = 'button';
    button.className = 'layui-btn layui-btn-sm';
    button.dataset.contextTarget = tenant.id ?? hostContextValue;
    button.disabled = snapshot.switching;
    button.textContent = tenant.id ? '进入租户' : '返回 Host';
    footer.append(button);
  }

  article.append(code, title, domain, footer);
  return article;
}

function createOption(ownerDocument, value, label) {
  const option = ownerDocument.createElement('option');
  option.value = value;
  option.textContent = label;
  return option;
}

function currentRoute() {
  const route = window.location.hash.replace(/^#/, '') || '/';
  return route.startsWith('/') ? route : `/${route}`;
}

function normalizeSnapshot(snapshot) {
  return {
    state: snapshot.state ?? 'anonymous',
    currentUser: snapshot.currentUser,
    navigation: snapshot.navigation ?? [],
    availableTenants: snapshot.availableTenants ?? [],
    switching: snapshot.switching === true,
    currentContextName: snapshot.currentContextName ?? 'Full.NET Host'
  };
}

function showContractResult(root, problem) {
  const panel = root.querySelector('[data-contract-result]');
  if (panel) {
    panel.hidden = false;
    panel.classList.add('is-error');
  }
  setText(root, '[data-testid="error-code"]', problem?.code ?? 'client.unexpected_error');
  setText(root, '[data-testid="trace-id"]', problem?.traceId ?? '无 TraceId');
}

function showContextProblem(root, problem) {
  const panel = root.querySelector('[data-context-problem]');
  if (panel) panel.hidden = false;
  setText(root, '[data-context-error-code]', problem?.code ?? 'client.context_switch_failed');
  setText(root, '[data-context-error-title]', problem?.title ?? '上下文切换未完成');
}

function hideContextProblem(root) {
  const panel = root.querySelector('[data-context-problem]');
  if (panel) panel.hidden = true;
}

function showLoginProblem(root, problem) {
  const panel = root.querySelector('[data-login-problem]');
  if (panel) panel.hidden = false;
  setText(root, '[data-login-error-code]', problem?.code ?? 'client.login_failed');
  setText(root, '[data-login-error-title]', problem?.title ?? '登录请求未完成');
}

function hideLoginProblem(root) {
  const panel = root.querySelector('[data-login-problem]');
  if (panel) panel.hidden = true;
}

function setText(root, selector, value) {
  const element = root.querySelector(selector);
  if (element) element.textContent = value;
}
