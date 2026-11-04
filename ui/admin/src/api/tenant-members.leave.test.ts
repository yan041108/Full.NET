import { afterEach, expect, it, vi } from 'vitest';
import { configureAuthentication } from './http';
import { leaveCurrentTenantMember, removeTenantMember, revokeTenantInvitation } from './tenant-members';

afterEach(() => { vi.unstubAllGlobals(); configureAuthentication(); });
it('退出遇到 401 不刷新认证或自动重放，原并发版本和取消信号随请求发送', async () => {
  const refresh = vi.fn().mockResolvedValue(true), controller = new AbortController();
  configureAuthentication({ getAccessToken: () => 'original-session', refresh });
  const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify({ status: 401, code: 'identity.session_not_active' }),
    { status: 401, headers: { 'content-type': 'application/problem+json' } }));
  vi.stubGlobal('fetch', fetch);
  await expect(leaveCurrentTenantMember(4, controller.signal)).rejects.toMatchObject({ status: 401 });
  expect(refresh).not.toHaveBeenCalled(); expect(fetch).toHaveBeenCalledTimes(1);
  const [path, init] = fetch.mock.calls[0]! as unknown as [string, RequestInit];
  expect(path).toBe('/api/v1/identity/tenant-members/me/leave'); expect(init.method).toBe('POST');
  expect(init.signal).toBe(controller.signal); expect(JSON.parse(init.body as string)).toEqual({ version: 4 });
});

it('移除遇到 401 不刷新或自动重放，精确成员和版本随 DELETE 发送', async () => {
  const refresh = vi.fn().mockResolvedValue(true), controller = new AbortController();
  configureAuthentication({ getAccessToken: () => 'original-session', refresh });
  const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify({ status: 401, code: 'identity.session_not_active' }),
    { status: 401, headers: { 'content-type': 'application/problem+json' } }));
  vi.stubGlobal('fetch', fetch);
  await expect(removeTenantMember('01981a3f-00c0-7000-8000-000000000010', 4, controller.signal)).rejects.toMatchObject({ status: 401 });
  expect(refresh).not.toHaveBeenCalled(); expect(fetch).toHaveBeenCalledTimes(1);
  const [path, init] = fetch.mock.calls[0]! as unknown as [string, RequestInit];
  expect(path).toBe('/api/v1/identity/tenant-members/01981a3f-00c0-7000-8000-000000000010?version=4');
  expect(init.method).toBe('DELETE'); expect(init.signal).toBe(controller.signal);
});

it('邀请撤销遇到401不刷新或自动重放，原邀请和取消信号随请求发送', async () => {
  const refresh = vi.fn().mockResolvedValue(true), controller = new AbortController();
  configureAuthentication({ getAccessToken: () => 'original-session', refresh });
  const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify({ status: 401, code: 'identity.session_not_active' }),
    { status: 401, headers: { 'content-type': 'application/problem+json' } })); vi.stubGlobal('fetch', fetch);
  await expect(revokeTenantInvitation('01981a3f-00c0-7000-8000-000000000010', controller.signal)).rejects.toMatchObject({ status: 401 });
  expect(refresh).not.toHaveBeenCalled(); expect(fetch).toHaveBeenCalledTimes(1);
  const [path, init] = fetch.mock.calls[0]! as unknown as [string, RequestInit];
  expect(path).toBe('/api/v1/identity/tenant-members/invitations/01981a3f-00c0-7000-8000-000000000010/revoke');
  expect(init.method).toBe('POST'); expect(init.signal).toBe(controller.signal);
});
