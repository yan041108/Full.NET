import { createApp } from 'vue';
import { createPinia } from 'pinia';
import 'element-plus/theme-chalk/base.css';
import 'element-plus/theme-chalk/el-button.css';
import 'element-plus/theme-chalk/el-option.css';
import 'element-plus/theme-chalk/el-progress.css';
import 'element-plus/theme-chalk/el-select.css';
import 'element-plus/theme-chalk/el-tag.css';
import '@fullnet/design-tokens/tokens.css';
import './styles/app.css';
import App from './App.vue';
import { createAppRouter } from './router';

const pinia = createPinia();

createApp(App)
  .use(pinia)
  .use(createAppRouter(undefined, pinia))
  .mount('#app');
