import type { App, Plugin } from 'vue';
import { ElAlert } from 'element-plus/es/components/alert/index.mjs';
import { ElButton, ElButtonGroup } from 'element-plus/es/components/button/index.mjs';
import { ElCard } from 'element-plus/es/components/card/index.mjs';
import { ElCollapse, ElCollapseItem } from 'element-plus/es/components/collapse/index.mjs';
import { ElAside, ElContainer, ElHeader, ElMain } from 'element-plus/es/components/container/index.mjs';
import { ElDialog } from 'element-plus/es/components/dialog/index.mjs';
import { ElDivider } from 'element-plus/es/components/divider/index.mjs';
import { ElDrawer } from 'element-plus/es/components/drawer/index.mjs';
import { ElDropdown, ElDropdownItem, ElDropdownMenu } from 'element-plus/es/components/dropdown/index.mjs';
import { ElForm, ElFormItem } from 'element-plus/es/components/form/index.mjs';
import { ElInput } from 'element-plus/es/components/input/index.mjs';
import { ElInputNumber } from 'element-plus/es/components/input-number/index.mjs';
import { ElOption, ElSelect } from 'element-plus/es/components/select/index.mjs';
import { ElPopover } from 'element-plus/es/components/popover/index.mjs';
import { ElRadio, ElRadioButton, ElRadioGroup } from 'element-plus/es/components/radio/index.mjs';
import { ElScrollbar } from 'element-plus/es/components/scrollbar/index.mjs';
import { ElTabPane, ElTabs } from 'element-plus/es/components/tabs/index.mjs';
import { ElTooltip } from 'element-plus/es/components/tooltip/index.mjs';
import { ElTree } from 'element-plus/es/components/tree/index.mjs';

const vformElementPlusComponents: Plugin[] = [
  ElAlert,
  ElAside,
  ElButton,
  ElButtonGroup,
  ElCard,
  ElCollapse,
  ElCollapseItem,
  ElContainer,
  ElDialog,
  ElDivider,
  ElDrawer,
  ElDropdown,
  ElDropdownItem,
  ElDropdownMenu,
  ElForm,
  ElFormItem,
  ElHeader,
  ElInput,
  ElInputNumber,
  ElMain,
  ElOption,
  ElPopover,
  ElRadio,
  ElRadioButton,
  ElRadioGroup,
  ElScrollbar,
  ElSelect,
  ElTabPane,
  ElTabs,
  ElTooltip,
  ElTree
];

/** 只注册 VForm3 实际引用的 Element Plus 组件，避免全量插件进入管理端初始依赖图。 */
export function installVFormElementPlusComponents(app: App): void {
  for (const component of vformElementPlusComponents) app.use(component);
}
