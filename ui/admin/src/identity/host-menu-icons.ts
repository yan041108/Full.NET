import { Icon } from '@iconify/vue';
import type { Component } from 'vue';
import { defineComponent, h } from 'vue';
import {
  Avatar,
  Goods,
  Grid,
  House,
  Key,
  Lock,
  Menu,
  Monitor,
  OfficeBuilding,
  Setting,
  User,
  UserFilled
} from '@element-plus/icons-vue';
import {
  HOST_MENU_ICON_OPTIONS,
  type HostMenuIcon
} from '@fullnet/client-contracts';

/** 菜单图标语义键与 Element Plus 图标的唯一映射，供侧栏与图标选择器共用。 */
const hostMenuIconComponentMap: Record<HostMenuIcon, Component> = {
  grid: Grid,
  dashboard: Grid,
  overview: House,
  users: User,
  user: UserFilled,
  menus: Menu,
  menu: Menu,
  roles: Setting,
  team: Avatar,
  monitor: Monitor,
  building: OfficeBuilding,
  key: Key,
  appstore: Goods,
  shield: Lock
};

/** 缓存 Iconify 组件实例，避免每次渲染或筛选都重复创建动态组件。 */
const iconifyComponentCache = new Map<string, Component>();

export const hostMenuIconOptions = HOST_MENU_ICON_OPTIONS;

const iconifyPattern = /^[a-z0-9][a-z0-9-]*:[a-z0-9][a-z0-9-]*$/i;

/** 判断输入是否符合 `collection:name` 形态的 Iconify 标识。 */
export function isIconifyMenuIcon(icon: string): boolean {
  return iconifyPattern.test(icon);
}

/** 规范化用户输入的菜单图标标识（去首尾空白）。 */
export function normalizeMenuIconInput(value: string): string {
  return value.trim();
}

/** 判断输入是否为可保存的菜单图标（语义键或 Iconify 标识）。 */
export function isValidMenuIconInput(value: string): boolean {
  const normalized = normalizeMenuIconInput(value);
  return isHostMenuIcon(normalized) || isIconifyMenuIcon(normalized);
}

/** 判断图标是否命中平台内建的语义键目录。 */
export function isHostMenuIcon(icon: string): icon is HostMenuIcon {
  return (HOST_MENU_ICON_OPTIONS as readonly string[]).includes(icon);
}

/** 按 Iconify 标识创建可复用组件，并以稳定名称便于调试与快照定位。 */
function createIconifyComponent(icon: string): Component {
  const cached = iconifyComponentCache.get(icon);
  if (cached) {
    return cached;
  }

  const component = defineComponent({
    name: `MenuIconify_${icon.replace(/[^a-zA-Z0-9]+/g, '_')}`,
    setup() {
      return () => h(Icon, {
        icon,
        width: '1em',
        height: '1em'
      });
    }
  });
  iconifyComponentCache.set(icon, component);
  return component;
}

/** 将菜单图标键解析为可挂载的 Vue 组件（兼容语义键与 Iconify 标识）。 */
export function resolveMenuIconComponent(icon: string): Component {
  if (isHostMenuIcon(icon)) {
    return hostMenuIconComponentMap[icon];
  }

  if (isIconifyMenuIcon(icon)) {
    return createIconifyComponent(icon);
  }

  return Grid;
}

/** @deprecated 使用 resolveMenuIconComponent */
export const resolveHostMenuIcon = resolveMenuIconComponent;

/** 按关键字过滤内建菜单图标，空查询时返回原目录副本以避免调用方误改只读常量。 */
export function filterHostMenuIcons(
  query: string,
  icons: readonly HostMenuIcon[] = HOST_MENU_ICON_OPTIONS
): HostMenuIcon[] {
  const normalized = query.trim().toLowerCase();
  if (!normalized) {
    return [...icons];
  }

  return icons.filter(icon => icon.includes(normalized));
}
