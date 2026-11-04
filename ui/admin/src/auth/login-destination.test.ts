import { beforeEach, expect, it, vi } from 'vitest';

beforeEach(() => { vi.restoreAllMocks(); vi.resetModules(); sessionStorage.clear(); });
it('身份中心整页往返后仅消费一次固定的企业接续目标', async () => {
  const start = await import('./login-destination'); start.rememberLoginDestination('/tenant-context');
  expect(sessionStorage.getItem('fullnet.admin.login.destination')).not.toContain('invitationToken');
  vi.resetModules(); const callback = await import('./login-destination');
  expect(callback.takeLoginDestination()).toBe('/tenant-context');
  expect(callback.takeLoginDestination()).toBeUndefined();
  expect(sessionStorage.getItem('fullnet.admin.login.destination')).toBeNull();
});
it.each(['https://evil.test', '//evil.test', '/\\evil.test', '/tenant-context?token=secret', '/tenant-context#secret', '/%2ftenant-context', ['/tenant-context'], undefined])('拒绝任意或重复跳转参数 %j', async value => {
  const intent = await import('./login-destination'); intent.rememberLoginDestination(value);
  expect(intent.resolveLoginDestination(value)).toBeUndefined(); expect(intent.takeLoginDestination()).toBeUndefined();
});
it('新登录没有接续目标时清除上次意图', async () => {
  const intent = await import('./login-destination'); intent.rememberLoginDestination('/tenant-context'); intent.rememberLoginDestination(undefined);
  expect(intent.takeLoginDestination()).toBeUndefined();
});
it.each(['broken', JSON.stringify({ path: '//evil.test', expiresAt: Date.now() + 5000 }), JSON.stringify({ path: '/tenant-context', expiresAt: 0 }), JSON.stringify({ path: '/tenant-context', expiresAt: Number.MAX_SAFE_INTEGER })])('损坏或过期持久意图 %s 不接续', async value => {
  sessionStorage.setItem('fullnet.admin.login.destination', value);
  const intent = await import('./login-destination'); expect(intent.takeLoginDestination()).toBeUndefined();
  expect(sessionStorage.getItem('fullnet.admin.login.destination')).toBeNull();
});
it('受限存储在当前应用可接续，消费后不恢复旧值', async () => {
  vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw Error('denied'); });
  vi.spyOn(Storage.prototype, 'removeItem').mockImplementation(() => { throw Error('denied'); });
  const intent = await import('./login-destination'); intent.rememberLoginDestination('/tenant-context');
  expect(intent.takeLoginDestination()).toBe('/tenant-context'); expect(intent.takeLoginDestination()).toBeUndefined();
});
