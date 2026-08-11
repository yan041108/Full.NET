import { describe, expect, it } from 'vitest';
import { Grid, Key } from '@element-plus/icons-vue';
import {
  isHostMenuIcon,
  isIconifyMenuIcon,
  isValidMenuIconInput,
  normalizeMenuIconInput,
  resolveMenuIconComponent
} from './host-menu-icons';
import {
  filterMenuIconGroups,
  findMenuIconGroup,
  isIconInMenuIconCatalog,
  MENU_ICON_GROUPS
} from './menu-icon-catalog';

describe('host-menu-icons', () => {
  it('resolves known icon keys to components', () => {
    expect(resolveMenuIconComponent('key')).toBe(Key);
    expect(resolveMenuIconComponent('unknown')).toBe(Grid);
  });

  it('detects iconify icon identifiers', () => {
    expect(isIconifyMenuIcon('ri:home-line')).toBe(true);
    expect(isIconifyMenuIcon('grid')).toBe(false);
  });

  it('validates host menu icon keys', () => {
    expect(isHostMenuIcon('users')).toBe(true);
    expect(isHostMenuIcon('remote')).toBe(false);
  });

  it('creates iconify wrapper components', () => {
    const component = resolveMenuIconComponent('ri:home-line');
    expect(component).not.toBe(Grid);
    expect(resolveMenuIconComponent('ri:home-line')).toBe(component);
  });

  it('normalizes and validates custom iconify input', () => {
    expect(normalizeMenuIconInput('  ri:file-excel-line  ')).toBe('ri:file-excel-line');
    expect(isValidMenuIconInput('ri:custom-icon')).toBe(true);
    expect(isValidMenuIconInput('users')).toBe(true);
    expect(isValidMenuIconInput('not-an-icon')).toBe(false);
  });
});

describe('menu-icon-catalog', () => {
  it('exposes grouped icon libraries', () => {
    expect(MENU_ICON_GROUPS.map(group => group.id)).toEqual([
      'legacy',
      'element-plus',
      'remix',
      'tabler',
      'solar'
    ]);
  });

  it('filters icons within groups', () => {
    const groups = filterMenuIconGroups('user');
    expect(groups.some(group => group.id === 'legacy')).toBe(true);
    expect(groups.some(group => group.id === 'remix')).toBe(true);
    expect(groups.every(group => group.icons.length > 0)).toBe(true);
  });

  it('finds the group for a selected icon', () => {
    expect(findMenuIconGroup('ri:home-line')?.id).toBe('remix');
    expect(findMenuIconGroup('users')?.id).toBe('legacy');
  });

  it('resolves iconify prefix for icons outside the curated list', () => {
    expect(findMenuIconGroup('ri:custom-icon')?.id).toBe('remix');
    expect(findMenuIconGroup('ep:custom-icon')?.id).toBe('element-plus');
  });

  it('exposes expanded icon packs', () => {
    expect(MENU_ICON_GROUPS.find(group => group.id === 'remix')?.icons.length)
      .toBeGreaterThan(60);
  });

  it('detects curated icons and document-related entries', () => {
    expect(isIconInMenuIconCatalog('ri:file-excel-line')).toBe(true);
    expect(isIconInMenuIconCatalog('tabler:file-import')).toBe(true);
    expect(isIconInMenuIconCatalog('ri:totally-custom-icon')).toBe(false);
  });
});
