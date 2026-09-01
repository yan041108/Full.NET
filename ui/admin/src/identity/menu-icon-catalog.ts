import type { MessageKey } from '@fullnet/admin-i18n';
import { HOST_MENU_ICON_OPTIONS } from '@fullnet/client-contracts';
import {
  ELEMENT_PLUS_ICONIFY_ICONS,
  REMIX_ICONIFY_ICONS,
  SOLAR_ICONIFY_ICONS,
  TABLER_ICONIFY_ICONS
} from './menu-icon-packs';

export type MenuIconLibrary = 'legacy' | 'iconify';

/** 菜单图标分组定义，统一描述页签标题、渲染库和候选图标集合。 */
export interface MenuIconGroupDefinition {
  id: string;
  titleKey: MessageKey;
  tabTitleKey: MessageKey;
  /** Iconify 集合前缀；legacy 分组不使用 Iconify。 */
  iconifyPrefix?: string;
  library: MenuIconLibrary;
  icons: readonly string[];
}

/**
 * 菜单图标分组目录。
 *
 * Iconify 不是单独一组，而是后四组的运行时渲染引擎（`@iconify/vue`）：
 * - Element Plus → `ep:`
 * - Remix Icon → `ri:`
 * - Tabler Icons → `tabler:`
 * - Solar Icons → `solar:`
 */
export const MENU_ICON_GROUPS: readonly MenuIconGroupDefinition[] = [
  {
    id: 'legacy',
    titleKey: 'menus.iconGroupLegacy',
    tabTitleKey: 'menus.iconTabLegacy',
    library: 'legacy',
    icons: HOST_MENU_ICON_OPTIONS
  },
  {
    id: 'element-plus',
    titleKey: 'menus.iconGroupElementPlus',
    tabTitleKey: 'menus.iconTabElementPlus',
    iconifyPrefix: 'ep',
    library: 'iconify',
    icons: ELEMENT_PLUS_ICONIFY_ICONS
  },
  {
    id: 'remix',
    titleKey: 'menus.iconGroupRemix',
    tabTitleKey: 'menus.iconTabRemix',
    iconifyPrefix: 'ri',
    library: 'iconify',
    icons: REMIX_ICONIFY_ICONS
  },
  {
    id: 'tabler',
    titleKey: 'menus.iconGroupTabler',
    tabTitleKey: 'menus.iconTabTabler',
    iconifyPrefix: 'tabler',
    library: 'iconify',
    icons: TABLER_ICONIFY_ICONS
  },
  {
    id: 'solar',
    titleKey: 'menus.iconGroupSolar',
    tabTitleKey: 'menus.iconTabSolar',
    iconifyPrefix: 'solar',
    library: 'iconify',
    icons: SOLAR_ICONIFY_ICONS
  }
] as const;

export interface FilteredMenuIconGroup extends MenuIconGroupDefinition {
  icons: string[];
}

/** 按查询词过滤每个图标分组，并丢弃已经没有候选项的空分组。 */
export function filterMenuIconGroups(
  query: string,
  groups: readonly MenuIconGroupDefinition[] = MENU_ICON_GROUPS
): FilteredMenuIconGroup[] {
  const normalized = query.trim().toLowerCase();

  return groups
    .map(group => ({
      ...group,
      icons: normalized
        ? group.icons.filter(icon => icon.toLowerCase().includes(normalized))
        : [...group.icons]
    }))
    .filter(group => group.icons.length > 0);
}

/** 判断图标是否属于当前允许保存的菜单图标目录。 */
export function isIconInMenuIconCatalog(
  icon: string,
  groups: readonly MenuIconGroupDefinition[] = MENU_ICON_GROUPS
): boolean {
  return groups.some(group => group.icons.includes(icon));
}

/** 先按显式目录定位图标分组，找不到时再按 Iconify 前缀做宽松回退。 */
export function findMenuIconGroup(
  icon: string,
  groups: readonly MenuIconGroupDefinition[] = MENU_ICON_GROUPS
): MenuIconGroupDefinition | undefined {
  const curated = groups.find(group => group.icons.includes(icon));
  if (curated) {
    return curated;
  }

  const prefix = icon.split(':')[0];
  return groups.find(group => group.iconifyPrefix === prefix);
}
