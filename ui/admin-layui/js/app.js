import { request } from './core/http.js';
import { adminI18n } from './core/i18n.js';
import { applyLayuiLocale } from './core/layui-locale.js';
import { identitySession } from './core/session.js';
import {
  applyPermissionVisibility,
  findNavigationByPath,
  localNavigationFor,
  localViewFor,
  renderNavigation
} from './core/navigation.js';
import { createSuperAdministratorController } from './core/super-administrators.js';
import { createTenantsController } from './core/tenants.js';
import { createUsersController } from './core/users.js';
import { createRolesController } from './core/roles.js';
import { createMenusController } from './core/menus.js';
import { createOrgUnitsController } from './core/org-units.js';
import { createOrgUserUnitsController } from './core/org-user-units.js';

const hostContextValue = '__fullnet_host__';
const knownLocalPaths = new Set(['/', '/tenant-context', '/tenants', '/identity/users', '/identity/roles', '/identity/menus', '/organization/units', '/organization/user-units', '/identity/super-administrators']);
const statusRoutes = {
  '/403': {
    code: '403',
    titleKey: 'status.403.title',
    descriptionKey: 'status.403.description'
  },
  '/404': {
    code: '404',
    titleKey: 'status.404.title',
    descriptionKey: 'status.404.description'
  },
  '/500': {
    code: '500',
    titleKey: 'status.500.title',
    descriptionKey: 'status.500.description'
  }
};

/**
 * 初始化原生 Layui 管理端；返回清理函数，供自动化测试和微前端卸载移除全部监听器。
 */
export function initializeAdminApp(root = document, options = {}) {
  const session = options.session ?? identitySession;
  const i18n = options.i18n ?? adminI18n;
  const autoRestore = options.autoRestore !== false;
  const probeButton = root.querySelector('[data-testid="load-current-user"]');
  const loginForm = root.querySelector('[data-login-form]');
  const logoutButton = root.querySelector('[data-session-logout]');
  const skipLink = root.querySelector('[data-skip-link]');
  const contextSelector = root.querySelector('[data-context-select]');
  const localeSelectors = root.querySelectorAll('[data-locale-select]');
  const tenantDirectory = root.querySelector('[data-tenant-directory]');
  let translation = i18n.snapshot();
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
  let componentLocaleGeneration = 0;
  const superAdministrators = createSuperAdministratorController(root, {
    request,
    translation: () => translation
  });
  const tenants = createTenantsController(root, {
    request,
    translation: () => translation
  });
  const users = createUsersController(root, {
    request,
    translation: () => translation
  });
  const roles = createRolesController(root, {
    request,
    translation: () => translation
  });
  const menus = createMenusController(root, {
    request,
    translation: () => translation
  });
  const orgUnits = createOrgUnitsController(root, {
    request,
    translation: () => translation
  });
  const orgUserUnits = createOrgUserUnitsController(root, {
    request,
    translation: () => translation
  });

  const onRouteChange = () => {
    renderRoute(root, latestSnapshot, translation, { focusHeading: true });
    if (latestSnapshot.state === 'authenticated'
      && currentRoute() === '/identity/super-administrators') {
      void superAdministrators.load();
    }
    if (latestSnapshot.state === 'authenticated'
      && currentRoute() === '/tenants') {
      void tenants.load();
    }
    if (latestSnapshot.state === 'authenticated'
      && currentRoute() === '/identity/users') {
      void users.load();
    }
    if (latestSnapshot.state === 'authenticated'
      && currentRoute() === '/identity/roles') {
      void roles.load();
    }
    if (latestSnapshot.state === 'authenticated'
      && currentRoute() === '/identity/menus') {
      void menus.load();
    }
    if (latestSnapshot.state === 'authenticated'
      && currentRoute() === '/organization/units') {
      void orgUnits.load();
    }
    if (latestSnapshot.state === 'authenticated'
      && currentRoute() === '/organization/user-units') {
      void orgUserUnits.load();
    }
  };
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
      if (code) {
        code.textContent = translation.t('overview.connectedUser', {
          name: currentUser?.displayName
            ?? translation.t('overview.currentUserFallback')
        });
      }
      if (traceId) traceId.textContent = currentUser?.id ?? '';
    } catch (problem) {
      showContractResult(root, problem, translation);
      globalThis.layui?.layer?.msg?.(translation.t('overview.clientFailure'), { icon: 2 });
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
    if (submitButton) {
      submitButton.disabled = true;
      submitButton.setAttribute('aria-busy', 'true');
      setText(
        submitButton,
        '[data-i18n="auth.submit"]',
        translation.t('auth.submitting')
      );
    }
    hideLoginProblem(root);
    try {
      const formData = new FormData(loginForm);
      await session.login(
        String(formData.get('username') ?? '').trim(),
        String(formData.get('password') ?? '')
      );
    } catch (problem) {
      showLoginProblem(root, problem, translation);
      globalThis.layui?.layer?.msg?.(translation.t('auth.loginFailed'), { icon: 2 });
    } finally {
      isLoggingIn = false;
      if (submitButton) {
        submitButton.disabled = false;
        submitButton.removeAttribute('aria-busy');
        setText(
          submitButton,
          '[data-i18n="auth.submit"]',
          translation.t('auth.submit')
        );
      }
    }
  };
  const onLogout = () => {
    void session.logout();
  };
  const onSkipToMain = (event) => {
    // Hash 路由会把 #main-content 当作业务路由，因此在原地完成焦点跳转。
    event.preventDefault();
    root.querySelector('#main-content')?.focus();
  };
  const switchContext = async (value) => {
    if (isSwitchingContext || latestSnapshot.switching) {
      renderContextSelector(contextSelector, latestSnapshot, translation);
      return;
    }

    isSwitchingContext = true;
    hideContextProblem(root);
    renderContextSelector(contextSelector, latestSnapshot, translation);
    if (contextSelector) contextSelector.disabled = true;
    try {
      await session.switchTenant(value === hostContextValue ? null : value);
    } catch (problem) {
      showContextProblem(root, problem, translation);
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
  const onLocaleChange = async (event) => {
    const requestedLocale = event.currentTarget.value;
    localeSelectors.forEach(selector => {
      selector.value = translation.locale;
      selector.disabled = true;
      selector.setAttribute('aria-busy', 'true');
    });
    hideLocaleProblem(root);
    try {
      await session.changeLocale(requestedLocale);
    } catch {
      showLocaleProblem(root, translation);
      globalThis.layui?.layer?.msg?.(
        translation.t('locale.saveFailed'),
        { icon: 2 }
      );
    } finally {
      localeSelectors.forEach(selector => {
        selector.value = translation.locale;
        selector.disabled = latestSnapshot.savingLocale === true;
        selector.removeAttribute('aria-busy');
      });
    }
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
    renderSession(root, latestSnapshot, translation);
    if (latestSnapshot.state === 'authenticated'
      && currentRoute() === '/identity/super-administrators') {
      void superAdministrators.load();
    }
    if (latestSnapshot.state === 'authenticated'
      && currentRoute() === '/tenants') {
      void tenants.load();
    }
    if (latestSnapshot.state === 'authenticated'
      && currentRoute() === '/identity/users') {
      void users.load();
    }
    if (latestSnapshot.state === 'authenticated'
      && currentRoute() === '/identity/roles') {
      void roles.load();
    }
    if (latestSnapshot.state === 'authenticated'
      && currentRoute() === '/identity/menus') {
      void menus.load();
    }
    if (latestSnapshot.state === 'authenticated'
      && currentRoute() === '/organization/units') {
      void orgUnits.load();
    }
    if (latestSnapshot.state === 'authenticated'
      && currentRoute() === '/organization/user-units') {
      void orgUserUnits.load();
    }
  });
  const unsubscribeI18n = i18n.subscribe((snapshot) => {
    translation = snapshot;
    // 组件消息必须先更新，随后业务绑定和组件 render 才能读取同一语言。
    applyLayuiLocale(globalThis.layui, translation.locale);
    const componentLocaleOperation = ++componentLocaleGeneration;
    renderComponentLocaleFixture(
      root,
      translation.locale,
      () => componentLocaleOperation === componentLocaleGeneration
    );
    i18n.applyBindings(root);
    if (isLoggingIn) {
      setText(
        root,
        '[data-i18n="auth.submit"]',
        translation.t('auth.submitting')
      );
    }
    renderSession(root, latestSnapshot, translation);
  });

  window.addEventListener('hashchange', onRouteChange);
  probeButton?.addEventListener('click', onProbe);
  loginForm?.addEventListener('submit', onLogin);
  logoutButton?.addEventListener('click', onLogout);
  skipLink?.addEventListener('click', onSkipToMain);
  contextSelector?.addEventListener('change', onContextChange);
  localeSelectors.forEach(selector => {
    selector.addEventListener('change', onLocaleChange);
  });
  tenantDirectory?.addEventListener('click', onTenantAction);
  i18n.applyBindings(root);
  renderRoute(root, latestSnapshot, translation);

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
      skipLink?.removeEventListener('click', onSkipToMain);
      contextSelector?.removeEventListener('change', onContextChange);
      localeSelectors.forEach(selector => {
        selector.removeEventListener('change', onLocaleChange);
      });
      tenantDirectory?.removeEventListener('click', onTenantAction);
      superAdministrators.dispose();
      tenants.dispose();
      users.dispose();
      roles.dispose();
      menus.dispose();
      orgUnits.dispose();
      orgUserUnits.dispose();
      unsubscribeSession();
      unsubscribeI18n();
    }
  };
}

function renderSession(root, snapshot, translation) {
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
    currentScope.textContent = snapshot.currentUser?.isSuperAdministrator
      ? translation.t('shell.superAdministrator')
      : snapshot.currentUser?.scope === 'host'
        ? translation.t('shell.hostAdmin')
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
      currentRoute(),
      translation.t
    );
  }

  renderContextSelector(
    root.querySelector('[data-context-select]'),
    snapshot,
    translation
  );
  renderTenantDirectory(
    root.querySelector('[data-tenant-directory]'),
    snapshot,
    translation
  );
  applyPermissionVisibility(root, snapshot.currentUser?.permissions ?? []);
  renderRoute(root, snapshot, translation);
}

function renderRoute(root, snapshot, translation, options = {}) {
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
    setText(root, '[data-status-title]', translation.t(status.titleKey));
    setText(
      root,
      '[data-status-description]',
      translation.t(status.descriptionKey)
    );
  }

  const navigationContainer = root.querySelector('[data-navigation]');
  if (navigationContainer) {
    renderNavigation(
      navigationContainer,
      snapshot.navigation,
      route,
      translation.t
    );
  } else {
    root.querySelectorAll('[data-route]').forEach((link) => {
      link.classList.toggle('is-active', link.dataset.route === route);
    });
  }

  const local = navigation
    ? localNavigationFor(navigation.componentKey)
    : undefined;
  setText(
    root,
    '[data-route-title]',
    local
      ? translation.t(local.titleKey)
      : translation.t(status?.titleKey ?? 'navigation.status.title')
  );

  const titleKey = local
    ? local.titleKey
    : status?.titleKey ?? 'navigation.status.title';
  translation.setPageTitle(titleKey);

  if (options.focusHeading) {
    root.querySelector(
      '[data-route-view]:not([hidden]) [data-route-heading]'
    )?.focus();
  }
}

function renderContextSelector(selector, snapshot, translation) {
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

function renderTenantDirectory(container, snapshot, translation) {
  if (!container) {
    return;
  }

  const ownerDocument = container.ownerDocument;
  const fragment = ownerDocument.createDocumentFragment();
  fragment.append(createTenantCard(ownerDocument, {
    id: null,
    identifier: 'host',
    name: 'Full.NET Host',
    domain: translation.t('tenant.hostDomain')
  }, snapshot, translation));
  snapshot.availableTenants.forEach((tenant) => {
    fragment.append(createTenantCard(
      ownerDocument,
      tenant,
      snapshot,
      translation
    ));
  });
  if (snapshot.availableTenants.length === 0) {
    const empty = ownerDocument.createElement('p');
    empty.className = 'fn-tenant-grid__empty';
    empty.textContent = translation.t('tenant.directoryEmpty');
    fragment.append(empty);
  }
  container.replaceChildren(fragment);
}

function createTenantCard(ownerDocument, tenant, snapshot, translation) {
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
  state.textContent = isCurrent
    ? translation.t('tenant.current')
    : translation.t('tenant.available');
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
    button.textContent = tenant.id
      ? translation.t('tenant.enter')
      : translation.t('tenant.returnHost');
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
    savingLocale: snapshot.savingLocale === true,
    currentContextName: snapshot.currentContextName ?? 'Full.NET Host'
  };
}

function showContractResult(root, problem, translation) {
  const panel = root.querySelector('[data-contract-result]');
  if (panel) {
    panel.hidden = false;
    panel.classList.add('is-error');
  }
  setText(root, '[data-testid="error-code"]', problem?.code ?? 'client.unexpected_error');
  setText(
    root,
    '[data-testid="trace-id"]',
    problem?.traceId ?? translation.t('overview.noTraceId')
  );
}

function showContextProblem(root, problem, translation) {
  const panel = root.querySelector('[data-context-problem]');
  if (panel) panel.hidden = false;
  setText(root, '[data-context-error-code]', problem?.code ?? 'client.context_switch_failed');
  setText(
    root,
    '[data-context-error-title]',
    problem?.title ?? translation.t('shell.contextSwitchFailed')
  );
}

function hideContextProblem(root) {
  const panel = root.querySelector('[data-context-problem]');
  if (panel) panel.hidden = true;
}

function showLoginProblem(root, problem, translation) {
  const panel = root.querySelector('[data-login-problem]');
  if (panel) panel.hidden = false;
  setText(root, '[data-login-error-code]', problem?.code ?? 'client.login_failed');
  setText(
    root,
    '[data-login-error-title]',
    problem?.title ?? translation.t('auth.loginFailed')
  );
}

function hideLoginProblem(root) {
  const panel = root.querySelector('[data-login-problem]');
  if (panel) panel.hidden = true;
}

function showLocaleProblem(root, translation) {
  const panel = root.querySelector('[data-locale-problem]');
  if (!panel) {
    return;
  }

  panel.hidden = false;
  panel.textContent = translation.t('locale.saveFailed');
}

function hideLocaleProblem(root) {
  const panel = root.querySelector('[data-locale-problem]');
  if (panel) panel.hidden = true;
}

/**
 * 仅在明确的 E2E 查询参数下渲染真实 Layui 组件，验证公开 i18n 配置被组件实际消费。
 */
function renderComponentLocaleFixture(root, locale, isCurrent) {
  const fixture = root.querySelector('[data-component-locale-fixture]');
  const enabled = import.meta.env.DEV
    && new URLSearchParams(globalThis.location?.search ?? '')
    .has('component-locale-fixture');
  if (!fixture || !enabled) {
    return;
  }

  fixture.hidden = false;
  const layui = globalThis.layui;
  if (typeof layui?.use !== 'function') {
    return;
  }

  layui.use(['laypage', 'laydate'], (laypage, laydate) => {
    if (!isCurrent()) {
      return;
    }

    const previousDateInput = fixture.querySelector('[data-component-locale-date]');
    const dateInput = previousDateInput.cloneNode();
    // cloneNode 会保留 Laydate 实例键，必须移除才会按新语言创建实例。
    dateInput.removeAttribute('lay-laydate-id');
    previousDateInput.replaceWith(dateInput);
    laypage.render({
      elem: fixture.querySelector('[data-component-locale-pagination]'),
      count: 30,
      limit: 10,
      layout: ['prev', 'page', 'next']
    });
    laydate.render({
      elem: dateInput,
      type: 'date',
      // Laydate 保留独立 lang 选项；显式传入后才会从已设置的公开 i18n 消息中取对应语言。
      lang: locale === 'en-US' ? 'en' : 'cn'
    });
  });
}

function setText(root, selector, value) {
  const element = root.querySelector(selector);
  if (element) element.textContent = value;
}
