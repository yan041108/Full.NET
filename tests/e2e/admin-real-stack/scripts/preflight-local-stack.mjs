/**
 * 附着本机栈 L3 预检：ready、登录、导航白名单、探针菜单清理、Worker。
 * 用法：node scripts/preflight-local-stack.mjs
 * 跳过 Worker：FULLNET_E2E_SKIP_WORKER_CHECK=1
 */
import { existsSync, readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createAdminNavigationCatalog } from '../../../../packages/client-contracts/src/navigation-catalog.ts';

const rootDir = join(dirname(fileURLToPath(import.meta.url)), '..');
const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';
const origin = process.env.FULLNET_E2E_ADMIN_ORIGIN ?? 'http://localhost:25173';
const username = process.env.FULLNET_E2E_USERNAME ?? 'admin';
const password = process.env.FULLNET_E2E_PASSWORD ?? 'FullNet!2026Secure';
const catalog = createAdminNavigationCatalog();

function isProcessAlive(pid) {
  if (!Number.isSafeInteger(pid) || pid <= 0) {
    return false;
  }
  try {
    process.kill(pid, 0);
    return true;
  } catch {
    return false;
  }
}

async function loginToken() {
  const response = await fetch(`${apiBaseUrl}/api/v1/auth/login`, {
    method: 'POST',
    headers: { Origin: origin, 'Content-Type': 'application/json' },
    body: JSON.stringify({ username, password })
  });
  if (!response.ok) {
    throw new Error(`login failed: ${response.status} ${await response.text()}`);
  }
  const body = await response.json();
  return body.accessToken;
}

function walkProbeMenus(nodes, out = []) {
  for (const node of nodes ?? []) {
    const route = node.routeName ?? '';
    const title = node.title ?? '';
    if (/e2e[-_]probe/i.test(route) || /e2e_probe/i.test(title)) {
      out.push(node);
    }
    walkProbeMenus(node.children, out);
  }
  return out;
}

async function disableProbeMenus(token) {
  const headers = {
    Authorization: `Bearer ${token}`,
    Origin: origin,
    'Content-Type': 'application/json'
  };
  const all = await fetch(`${apiBaseUrl}/api/v1/identity/menus/all`, { headers });
  if (!all.ok) {
    return 0;
  }
  const menus = await all.json();
  let disabled = 0;
  for (const menu of menus) {
    if (!/e2e[-_]probe/i.test(menu.routeName ?? '') && !/e2e_probe/i.test(menu.title ?? '')) {
      continue;
    }
    if (menu.isActive === false) {
      continue;
    }
    const r = await fetch(`${apiBaseUrl}/api/v1/identity/menus/${menu.id}/disable`, {
      method: 'POST',
      headers
    });
    if (r.ok) {
      disabled += 1;
      console.log(`Disabled probe menu: ${menu.routeName}`);
    }
  }
  return disabled;
}

async function main() {
  const errors = [];

  const ready = await fetch(`${apiBaseUrl}/health/ready`);
  if (!ready.ok) {
    errors.push(
      `health/ready → ${ready.status}（附着栈：node scripts/resolve-attach-stack-env.mjs 设置 Database/Cache Redis 后重启 Host.Api）`
    );
  } else {
    console.log('OK health/ready');
  }

  let token;
  try {
    token = await loginToken();
    console.log('OK auth/login');
  } catch (error) {
    errors.push(error instanceof Error ? error.message : String(error));
  }

  if (token) {
    const disabled = await disableProbeMenus(token);
    if (disabled > 0) {
      console.log(`Cleaned ${disabled} probe menu(s)`);
    }

    const navRes = await fetch(`${apiBaseUrl}/api/v1/navigation`, {
      headers: { Authorization: `Bearer ${token}`, Origin: origin }
    });
    if (!navRes.ok) {
      errors.push(`navigation → ${navRes.status}`);
    } else {
      const tree = await navRes.json();
      const bad = walkProbeMenus(tree);
      if (bad.length > 0) {
        errors.push(`navigation still has ${bad.length} active probe menu node(s)`);
      }
      if (!catalog.isSupportedNavigationTree(tree)) {
        errors.push('navigation tree fails isSupportedNavigationTree (catalog mismatch)');
      } else {
        console.log('OK navigation catalog');
      }

      const tenantsRes = await fetch(`${apiBaseUrl}/api/v1/tenancy/available`, {
        headers: { Authorization: `Bearer ${token}`, Origin: origin }
      });
      if (!tenantsRes.ok) {
        errors.push(`tenancy/available → ${tenantsRes.status}`);
      } else {
        const tenants = await tenantsRes.json();
        const local = tenants.find(entry => entry.identifier === 'local') ?? tenants[0];
        if (!local?.id) {
          errors.push('no tenant available for tenant navigation preflight');
        } else {
          const switchRes = await fetch(`${apiBaseUrl}/api/v1/tenancy/context`, {
            method: 'PUT',
            headers: {
              Authorization: `Bearer ${token}`,
              Origin: origin,
              'Content-Type': 'application/json'
            },
            body: JSON.stringify({ tenantId: local.id })
          });
          if (!switchRes.ok) {
            errors.push(`tenancy/context → ${switchRes.status}`);
          } else {
            const switched = await switchRes.json();
            const tenantNavRes = await fetch(`${apiBaseUrl}/api/v1/navigation`, {
              headers: {
                Authorization: `Bearer ${switched.accessToken}`,
                Origin: origin
              }
            });
            if (!tenantNavRes.ok) {
              errors.push(`tenant navigation → ${tenantNavRes.status}`);
            } else {
              const tenantTree = await tenantNavRes.json();
              if (!catalog.isSupportedNavigationTree(tenantTree)) {
                errors.push(
                  'tenant navigation tree fails isSupportedNavigationTree (catalog mismatch)'
                );
              } else {
                console.log('OK tenant navigation catalog');
              }
            }
          }
        }
      }
    }
  }

  if (process.env.FULLNET_E2E_SKIP_WORKER_CHECK !== '1') {
    const statePath = join(rootDir, '.stack-state.json');
    let workerOk = false;
    if (existsSync(statePath)) {
      const state = JSON.parse(readFileSync(statePath, 'utf8'));
      if (state.workerPid && isProcessAlive(state.workerPid)) {
        workerOk = true;
      }
    }
    if (!workerOk) {
      errors.push(
        'Worker 未检测到存活进程（启动 Full.NET.Host.Worker 或设置 FULLNET_E2E_SKIP_WORKER_CHECK=1 仅跑不依赖后台的用例）'
      );
    } else {
      console.log('OK worker pid');
    }
  }

  if (errors.length > 0) {
    console.error('\nPreflight FAILED:');
    for (const e of errors) {
      console.error(`- ${e}`);
    }
    process.exitCode = 1;
    return;
  }
  console.log('\nPreflight passed.');
}

await main();
