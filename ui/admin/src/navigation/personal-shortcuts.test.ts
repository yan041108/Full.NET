import { describe, expect, it } from 'vitest';
import { personalShortcutKey, readPersonalShortcuts, savePersonalShortcuts } from './personal-shortcuts';

describe('个人常用入口存储', () => {
  const first = { userId: 'one', tenantId: 'a' };
  it('账号、Host 和每个租户使用独立键，刷新恢复稳定路由名', () => {
    savePersonalShortcuts(localStorage, first, ['enterprise-requests'], new Set(['enterprise-requests']));
    expect(readPersonalShortcuts(localStorage, first)).toEqual(['enterprise-requests']);
    expect(readPersonalShortcuts(localStorage, { userId: 'two', tenantId: 'a' })).toEqual([]);
    expect(readPersonalShortcuts(localStorage, { userId: 'one', tenantId: 'b' })).toEqual([]);
    expect(readPersonalShortcuts(localStorage, { userId: 'one' })).toEqual([]);
    expect(localStorage.getItem(personalShortcutKey(first))).not.toContain('/api/');
  });
  it('拒绝任意路径、未授权路由、重复项和超限数量', () => {
    for (const routes of [['/external/path'], ['unknown'], ['known', 'known'], Array(13).fill('known')])
      expect(() => savePersonalShortcuts(localStorage, first, routes, new Set(['known']))).toThrow();
    expect(readPersonalShortcuts(localStorage, first)).toEqual([]);
  });
  it('损坏、旧版本或过大的偏好不进入导航', () => {
    for (const raw of ['bad', JSON.stringify({ schemaVersion: 0, routes: ['known'] }), JSON.stringify({ schemaVersion: 1, routes: ['https://example.com'] }), ' '.repeat(3000)]) {
      localStorage.setItem(personalShortcutKey(first), raw); expect(readPersonalShortcuts(localStorage, first)).toEqual([]);
    }
  });
});
