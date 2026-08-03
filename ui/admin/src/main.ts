import { createApp } from 'vue';
import { createPinia } from 'pinia';
import 'element-plus/theme-chalk/base.css';
import 'element-plus/theme-chalk/el-icon.css';
import 'element-plus/theme-chalk/el-button.css';
import 'element-plus/theme-chalk/el-option.css';
import 'element-plus/theme-chalk/el-progress.css';
import 'element-plus/theme-chalk/el-select.css';
import 'element-plus/theme-chalk/el-button.css';
import 'element-plus/theme-chalk/el-menu.css';
import 'element-plus/theme-chalk/el-menu-item.css';
import 'element-plus/theme-chalk/el-sub-menu.css';
import 'element-plus/theme-chalk/el-dialog.css';
import 'element-plus/theme-chalk/el-drawer.css';
import 'element-plus/theme-chalk/el-input.css';
import 'element-plus/theme-chalk/el-tabs.css';
import 'element-plus/theme-chalk/el-tab-pane.css';
import 'element-plus/theme-chalk/el-transfer.css';
import 'element-plus/theme-chalk/el-pagination.css';
import 'element-plus/theme-chalk/el-tree.css';
import 'element-plus/theme-chalk/el-form.css';
import 'element-plus/theme-chalk/el-form-item.css';
import 'element-plus/theme-chalk/el-row.css';
import 'element-plus/theme-chalk/el-col.css';
import 'element-plus/theme-chalk/el-tag.css';
import 'element-plus/theme-chalk/el-table.css';
import 'element-plus/theme-chalk/el-table-column.css';
import 'element-plus/theme-chalk/el-loading.css';
import 'element-plus/theme-chalk/el-message-box.css';
import 'element-plus/theme-chalk/el-checkbox.css';
import 'element-plus/theme-chalk/el-overlay.css';
import 'element-plus/theme-chalk/el-radio.css';
import 'element-plus/theme-chalk/el-date-picker.css';
import 'element-plus/theme-chalk/el-tooltip.css';
import 'element-plus/theme-chalk/el-radio-button.css';
import 'element-plus/theme-chalk/el-radio-group.css';
import 'element-plus/theme-chalk/el-scrollbar.css';
import 'element-plus/theme-chalk/el-popover.css';
import 'element-plus/theme-chalk/el-popper.css';
import 'element-plus/theme-chalk/el-dropdown.css';
import 'element-plus/theme-chalk/el-dropdown-menu.css';
import 'element-plus/theme-chalk/el-dropdown-item.css';
import 'element-plus/theme-chalk/el-card.css';
import 'element-plus/theme-chalk/dark/css-vars.css';
import '@fullnet/design-tokens/tokens.css';
import './styles/app.css';
import App from './App.vue';
import { createAppRouter } from './router';

const pinia = createPinia();

createApp(App)
  .use(pinia)
  .use(createAppRouter(undefined, pinia))
  .mount('#app');
