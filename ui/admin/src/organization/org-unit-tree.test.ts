import { describe, expect, it } from 'vitest';
import type { OrganizationUnit } from '@fullnet/client-contracts';
import {
  buildOrganizationUnitParentOptions,
  buildOrganizationUnitTree,
  collectDescendantIds,
  filterOrganizationUnitsForTree,
  wouldCreateOrganizationUnitCycle
} from './org-unit-tree';

function unit(
  id: string,
  code: string,
  parentId: string | null,
  displayOrder = 10
): OrganizationUnit {
  return {
    id,
    parentId,
    code,
    name: code,
    displayOrder,
    isActive: true,
    createdAtUtc: '2026-08-04T00:00:00Z',
    updatedAtUtc: null,
    version: 1
  };
}

describe('organization unit tree helpers', () => {
  const root = unit('root', 'company-a', null, 1);
  const child = unit('child', 'dept-b', 'root', 2);
  const leaf = unit('leaf', 'team-c', 'child', 3);

  it('builds nested tree sorted by displayOrder', () => {
    const tree = buildOrganizationUnitTree([leaf, root, child]);
    expect(tree).toHaveLength(1);
    expect(tree[0]?.id).toBe('root');
    expect(tree[0]?.children).toHaveLength(1);
    expect(tree[0]?.children[0]?.id).toBe('child');
    expect(tree[0]?.children[0]?.children[0]?.id).toBe('leaf');
  });

  it('promotes cyclic graphs into a finite tree without repeating nodes', () => {
    const a = unit('a', 'a', 'b');
    const b = unit('b', 'b', 'a');
    const tree = buildOrganizationUnitTree([a, b]);
    const ids: string[] = [];
    const walk = (nodes: typeof tree): void => {
      for (const node of nodes) {
        ids.push(node.id);
        walk(node.children);
      }
    };
    walk(tree);
    expect(ids.sort()).toEqual(['a', 'b']);
    expect(new Set(ids).size).toBe(ids.length);
  });

  it('detects parent cycles including self and descendants', () => {
    const units = [root, child, leaf];
    expect(wouldCreateOrganizationUnitCycle(units, 'root', 'root')).toBe(true);
    expect(wouldCreateOrganizationUnitCycle(units, 'root', 'leaf')).toBe(true);
    expect(wouldCreateOrganizationUnitCycle(units, 'leaf', 'root')).toBe(false);
    expect(wouldCreateOrganizationUnitCycle(units, 'leaf', null)).toBe(false);
  });

  it('disables self and descendants in parent options', () => {
    const options = buildOrganizationUnitParentOptions([root, child, leaf], 'root');
    expect(options.find(option => option.value === 'root')?.disabled).toBe(true);
    expect(options.find(option => option.value === 'child')?.disabled).toBe(true);
    expect(options.find(option => option.value === 'leaf')?.disabled).toBe(true);
  });

  it('keeps ancestors when filtering for tree display', () => {
    const filtered = filterOrganizationUnitsForTree(
      [root, child, leaf],
      unitRow => unitRow.id === 'leaf'
    );
    expect(filtered.map(item => item.id).sort()).toEqual(['child', 'leaf', 'root']);
    expect(collectDescendantIds([root, child, leaf], 'root')).toEqual(
      new Set(['child', 'leaf'])
    );
  });
});
