import { describe, expect, it } from 'vitest';
import {
  createGridPreferenceRequest,
  gridPreferenceDefinitionFor,
  isGridPreferenceResponse
} from '../src/grid-preferences';

describe('grid preference contracts', () => {
  it('uses a local trusted grid and column catalog', () => {
    const definition = gridPreferenceDefinitionFor('identity.users');

    expect(definition).toMatchObject({
      gridKey: 'identity.users',
      schemaVersion: 2
    });
    expect(gridPreferenceDefinitionFor('identity.remote-script')).toBeUndefined();
    expect(() => createGridPreferenceRequest(
      definition!,
      [{ columnKey: 'remoteScript', order: 0, width: 120, visible: true, fixed: null }],
      0
    )).toThrow(/unknown column/u);
  });

  it('rejects duplicate columns and normalizes persisted order', () => {
    const definition = gridPreferenceDefinitionFor('identity.users')!;

    expect(() => createGridPreferenceRequest(
      definition,
      [
        { columnKey: 'phone', order: 1, width: 120, visible: true, fixed: null },
        { columnKey: 'phone', order: 2, width: 180, visible: false, fixed: 'left' }
      ],
      0
    )).toThrow(/duplicate column/u);

    expect(createGridPreferenceRequest(
      definition,
      [
        { columnKey: 'createdAt', order: 2, width: 140, visible: false, fixed: 'right' },
        { columnKey: 'phone', order: 0, width: 240, visible: true, fixed: 'left' }
      ],
      3
    )).toEqual({
      schemaVersion: 2,
      columns: [
        { columnKey: 'phone', order: 0, width: 240, visible: true, fixed: 'left' },
        { columnKey: 'createdAt', order: 2, width: 140, visible: false, fixed: 'right' }
      ],
      version: 3
    });
  });

  it('validates the stable response shape', () => {
    expect(isGridPreferenceResponse({
      gridKey: 'identity.users',
      schemaVersion: 2,
      columns: [
        { columnKey: 'phone', order: 0, width: 240, visible: true, fixed: null }
      ],
      version: 1
    })).toBe(true);
    expect(isGridPreferenceResponse({
      gridKey: 'identity.users',
      schemaVersion: 2,
      columns: [
        { columnKey: 'phone', order: 0, width: -1, visible: true, fixed: 'center' }
      ],
      version: 1
    })).toBe(false);
  });
});
