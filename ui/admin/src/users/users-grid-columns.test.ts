import { describe, expect, it } from 'vitest';
import type { GridPreferenceResponse } from '@fullnet/client-contracts';
import {
  applyUsersGridPreference,
  createDefaultUsersGridColumns,
  toUsersGridPreferenceColumns
} from './users-grid-columns';

describe('users grid columns', () => {
  const labelFor = (key: string) => key;
  const authorizeAll = () => true;
  const authorizePhoneOnly = (key: string) => key === 'phone';

  it('builds defaults for authorized columns only', () => {
    const columns = createDefaultUsersGridColumns(labelFor, authorizePhoneOnly);
    expect(columns).toHaveLength(1);
    expect(columns[0]?.key).toBe('phone');
  });

  it('applies persisted order visibility and fixed state', () => {
    const columns = createDefaultUsersGridColumns(labelFor, authorizeAll);
    const preference: GridPreferenceResponse = {
      gridKey: 'identity.users',
      schemaVersion: 2,
      version: 2,
      columns: [
        { columnKey: 'createdAt', order: 0, width: 200, visible: false, fixed: 'right' },
        { columnKey: 'phone', order: 1, width: 180, visible: true, fixed: 'left' }
      ]
    };

    const restored = applyUsersGridPreference(columns, preference);
    expect(restored.map(column => column.key).slice(0, 2)).toEqual(['createdAt', 'phone']);
    expect(restored.find(column => column.key === 'createdAt')).toMatchObject({
      visible: false,
      fixed: 'right',
      width: 200
    });
  });

  it('serializes only authorized columns for persistence', () => {
    const columns = createDefaultUsersGridColumns(labelFor, authorizePhoneOnly);
    const payload = toUsersGridPreferenceColumns(columns);
    expect(payload).toEqual([
      {
        columnKey: 'phone',
        order: 0,
        width: 140,
        visible: true,
        fixed: null
      }
    ]);
  });
});
