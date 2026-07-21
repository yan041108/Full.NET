import assert from 'node:assert/strict';
import { afterEach, test } from 'node:test';
import { provisionViewer } from './provision-viewer.mjs';

const permissions = [
  'platform.dashboard.read',
  'identity.navigation.read',
  'tenancy.tenants.read',
  'tenancy.tenants.switch'
];

const originalFetch = globalThis.fetch;

afterEach(() => {
  globalThis.fetch = originalFetch;
});

test('目标角色和用户位于第二页时复用现有记录', async () => {
  const role = createRole('role-viewer', 'e2e-host-viewer');
  const user = createUser('user-viewer', 'e2e-viewer');
  const scenario = installApiStub({
    rolePages: [fillRoles(100), [role]],
    userPages: [fillUsers(100), [user]]
  });

  const result = await provisionViewer(testEnvironment());

  assert.deepEqual(result, {
    roleId: role.id,
    userId: user.id,
    username: user.username
  });
  assert.deepEqual(scenario.createdResources, []);
  assert.deepEqual(scenario.listRequests, [
    '/api/v1/identity/roles?page=1&pageSize=100',
    '/api/v1/identity/roles?page=2&pageSize=100',
    '/api/v1/identity/users?page=1&pageSize=100',
    '/api/v1/identity/users?page=2&pageSize=100'
  ]);
});

test('跨页角色自然键重复时拒绝继续准备', async () => {
  const scenario = installApiStub({
    rolePages: [
      [createRole('role-viewer-1', 'e2e-host-viewer')],
      [createRole('role-viewer-2', 'e2e-host-viewer')]
    ],
    roleTotal: 101,
    userPages: [[]]
  });

  await assert.rejects(
    provisionViewer(testEnvironment()),
    /e2e-host-viewer.*2/);
  assert.deepEqual(scenario.createdResources, []);
});

test('跨页用户名大小写重复时拒绝继续准备', async () => {
  const role = createRole('role-viewer', 'e2e-host-viewer');
  const scenario = installApiStub({
    rolePages: [[role]],
    userPages: [
      [createUser('user-viewer-1', 'E2E-Viewer')],
      [createUser('user-viewer-2', 'e2e-viewer')]
    ],
    userTotal: 101
  });

  await assert.rejects(
    provisionViewer(testEnvironment()),
    /e2e-viewer.*2/i);
  assert.deepEqual(scenario.createdResources, []);
});

function installApiStub({ rolePages, userPages, roleTotal, userTotal }) {
  const createdResources = [];
  const listRequests = [];

  globalThis.fetch = async (input, init = {}) => {
    const url = new URL(input);
    const method = init.method ?? 'GET';
    const requestPath = `${url.pathname}${url.search}`;

    if (method === 'POST' && url.pathname === '/api/v1/auth/login') {
      return jsonResponse({ accessToken: 'test-access-token' });
    }

    if (method === 'GET' && url.pathname === '/api/v1/identity/roles') {
      listRequests.push(requestPath);
      return jsonResponse(pageResponse(rolePages, url, roleTotal));
    }

    if (method === 'POST' && url.pathname === '/api/v1/identity/roles') {
      createdResources.push('role');
      return jsonResponse(createRole('created-role', 'e2e-host-viewer'));
    }

    const roleDetail = url.pathname.match(/^\/api\/v1\/identity\/roles\/([^/]+)$/);
    if (method === 'GET' && roleDetail) {
      return jsonResponse(findById(rolePages, roleDetail[1]));
    }

    if (method === 'PUT' && url.pathname.endsWith('/permissions')) {
      const roleId = url.pathname.split('/').at(-2);
      return jsonResponse({ ...findById(rolePages, roleId), permissionCodes: permissions });
    }

    if (method === 'GET' && url.pathname === '/api/v1/identity/users') {
      listRequests.push(requestPath);
      return jsonResponse(pageResponse(userPages, url, userTotal));
    }

    if (method === 'POST' && url.pathname === '/api/v1/identity/users') {
      createdResources.push('user');
      return jsonResponse(createUser('created-user', 'e2e-viewer'));
    }

    const userRoles = url.pathname.match(/^\/api\/v1\/identity\/users\/([^/]+)\/roles$/);
    if (method === 'GET' && userRoles) {
      return jsonResponse({ userId: userRoles[1], roleIds: ['role-viewer'], version: 0 });
    }

    if (method === 'PUT' && userRoles) {
      const body = JSON.parse(init.body);
      return jsonResponse({ userId: userRoles[1], roleIds: body.roleIds, version: 1 });
    }

    throw new Error(`未处理的测试请求: ${method} ${requestPath}`);
  };

  return { createdResources, listRequests };
}

function pageResponse(pages, url, configuredTotal) {
  const page = Number(url.searchParams.get('page'));
  const pageSize = Number(url.searchParams.get('pageSize'));
  return {
    items: pages[page - 1] ?? [],
    page,
    pageSize,
    total: configuredTotal ?? pages.reduce((sum, items) => sum + items.length, 0)
  };
}

function createRole(id, code) {
  return {
    id,
    code,
    name: code,
    isSystem: false,
    isActive: true,
    isSuperAdministrator: false,
    permissionCodes: permissions,
    createdAtUtc: '2026-07-22T00:00:00Z',
    updatedAtUtc: null,
    version: 0
  };
}

function createUser(id, username) {
  return {
    id,
    username,
    displayName: username,
    isActive: true,
    createdAtUtc: '2026-07-22T00:00:00Z',
    updatedAtUtc: null,
    version: 0
  };
}

function fillRoles(count) {
  return Array.from({ length: count }, (_, index) =>
    createRole(`role-${index}`, `ordinary-role-${index}`));
}

function fillUsers(count) {
  return Array.from({ length: count }, (_, index) =>
    createUser(`user-${index}`, `ordinary-user-${index}`));
}

function findById(pages, id) {
  return pages.flat().find(item => item.id === id);
}

function jsonResponse(body, status = 200) {
  return {
    ok: status >= 200 && status < 300,
    status,
    async text() {
      return JSON.stringify(body);
    }
  };
}

function testEnvironment() {
  return {
    FULLNET_E2E_API_URL: 'http://api.test',
    FULLNET_E2E_ADMIN_ORIGIN: 'http://admin.test',
    FULLNET_E2E_USERNAME: 'admin',
    FULLNET_E2E_PASSWORD: 'admin-password',
    FULLNET_E2E_VIEWER_USERNAME: 'e2e-viewer',
    FULLNET_E2E_VIEWER_PASSWORD: 'viewer-password'
  };
}
