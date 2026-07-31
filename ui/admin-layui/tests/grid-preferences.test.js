import { describe, expect, it } from 'vitest';
import {
  applyGridPreference,
  createGridPreferenceClient
} from '../js/core/grid-preferences.js';

describe('Layui Grid 偏好适配器', () => {
  const columns = [
    { field: 'displayName', width: 180, hide: false, fixed: false },
    { field: 'username', width: 160, hide: false, fixed: false },
    { field: 'status', width: 100, hide: false, fixed: false },
    { field: 'actions', width: 220, hide: false, fixed: 'right' }
  ];

  it('恢复顺序、宽度、可见性和冻结状态且不修改源列', () => {
    const before = structuredClone(columns);
    const restored = applyGridPreference('identity.users', columns, {
      gridKey: 'identity.users',
      schemaVersion: 1,
      columns: [
        { columnKey: 'username', order: 0, width: 260, visible: true, fixed: 'left' },
        { columnKey: 'displayName', order: 1, width: 200, visible: false, fixed: null }
      ],
      version: 4
    });

    expect(restored.map(column => column.field)).toEqual([
      'username',
      'displayName',
      'status',
      'actions'
    ]);
    expect(restored[0]).toMatchObject({ width: 260, hide: false, fixed: 'left' });
    expect(restored[1]).toMatchObject({ width: 200, hide: true, fixed: false });
    expect(columns).toEqual(before);
  });

  it('SchemaVersion 变化时 load 与 apply 都安全回退', async () => {
    const restored = applyGridPreference('identity.users', columns, {
      gridKey: 'identity.users',
      schemaVersion: 2,
      columns: [
        { columnKey: 'username', order: 0, width: 999, visible: false, fixed: 'left' }
      ],
      version: 9
    });

    expect(restored).toEqual(columns);
    expect(restored).not.toBe(columns);

    const client = createGridPreferenceClient(async () => ({
      gridKey: 'identity.users',
      schemaVersion: 2,
      columns: [],
      version: 9
    }));
    await expect(client.load('identity.users')).resolves.toBeUndefined();
  });

  it('使用编码后的 GridKey 调用 GET、PUT 和 DELETE', async () => {
    const calls = [];
    const request = async (path, init) => {
      calls.push([path, init]);
      return {
        gridKey: 'identity.users',
        schemaVersion: 1,
        columns: [],
        version: init?.method === 'DELETE' ? 0 : 1
      };
    };
    const client = createGridPreferenceClient(request);

    await client.load('identity.users');
    await client.save('identity.users', {
      schemaVersion: 1,
      columns: [],
      version: 0
    });
    await client.reset('identity.users');
    await expect(client.save('identity.remote-script', {
      schemaVersion: 1,
      columns: [],
      version: 0
    })).rejects.toThrow(/unknown Grid/u);

    expect(calls.map(([path, init]) => [path, init?.method ?? 'GET'])).toEqual([
      ['/api/v1/me/grid-preferences/identity.users', 'GET'],
      ['/api/v1/me/grid-preferences/identity.users', 'PUT'],
      ['/api/v1/me/grid-preferences/identity.users', 'DELETE']
    ]);
  });
});
