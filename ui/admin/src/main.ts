import { createApp } from 'vue';
import { createPinia } from 'pinia';
import 'element-plus/theme-chalk/base.css';
import 'element-plus/theme-chalk/el-button.css';
import 'element-plus/theme-chalk/el-option.css';
import 'element-plus/theme-chalk/el-progress.css';
import 'element-plus/theme-chalk/el-select.css';
import 'element-plus/theme-chalk/el-button.css';
import 'element-plus/theme-chalk/el-menu.css';
import 'element-plus/theme-chalk/el-menu-item.css';
import 'element-plus/theme-chalk/el-dialog.css';
import 'element-plus/theme-chalk/el-drawer.css';
import 'element-plus/theme-chalk/el-input.css';
import 'element-plus/theme-chalk/el-radio-button.css';
import 'element-plus/theme-chalk/el-radio-group.css';
import 'element-plus/theme-chalk/el-scrollbar.css';
import 'element-plus/theme-chalk/el-popover.css';
import '@fullnet/design-tokens/tokens.css';
import './styles/app.css';
import App from './App.vue';
import { createAppRouter } from './router';

const pinia = createPinia();

createApp(App)
  .use(pinia)
  .use(createAppRouter(undefined, pinia))
  .mount('#app');
