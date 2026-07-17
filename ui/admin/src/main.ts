import { createApp } from 'vue';
import { createPinia } from 'pinia';
import 'element-plus/theme-chalk/base.css';
import 'element-plus/theme-chalk/el-button.css';
import 'element-plus/theme-chalk/el-progress.css';
import 'element-plus/theme-chalk/el-tag.css';
import '@fullnet/design-tokens/tokens.css';
import './styles/app.css';
import App from './App.vue';
import { createAppRouter } from './router';

createApp(App)
  .use(createPinia())
  .use(createAppRouter())
  .mount('#app');
