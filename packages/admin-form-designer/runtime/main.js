import { createApp, nextTick, ref, h } from 'vue';
import ElementPlus from 'element-plus';
import 'element-plus/dist/index.css';
import '../vendor/vform3/upstream/src/styles/index.scss';
import '../vendor/vform3/upstream/src/iconfont/iconfont.css';
import axios from 'axios';
import Draggable from 'vform-draggable';
import { registerIcon } from '../vendor/vform3/upstream/src/utils/el-icons';
import ContainerWidgets from '../vendor/vform3/upstream/src/components/form-designer/form-widget/container-widget/index';
import ContainerItems from '../vendor/vform3/upstream/src/components/form-render/container-item/index';
import { addDirective } from '../vendor/vform3/upstream/src/utils/directive';
import { loadExtension } from '../vendor/vform3/upstream/src/extension/extension-loader';
import Designer from '../vendor/vform3/upstream/src/components/form-designer/index.vue';
import Renderer from '../vendor/vform3/upstream/src/components/form-render/index.vue';
import 'virtual:fullnet-vform-icons';
import './integration.css';
import { getDefaultFormConfig } from '../vendor/vform3/upstream/src/utils/util';

// 仅固定方法的请求/回执协议；业务凭据和任意宿主请求永不进入沙箱。
const params = new URLSearchParams(location.hash.slice(1));
const channel = params.get('channel'); const parentOrigin = params.get('parent');
if (!channel || !parentOrigin || new URL(parentOrigin).origin !== parentOrigin) throw new Error('client.vform3_invalid_parent');
const send = data => parent.postMessage(JSON.parse(JSON.stringify({ protocol: 'fullnet.vform3.v1', channel, ...data })), parentOrigin);
let instance; let rootExtras = {}; const mode = ref('designer'); const renderJson = ref(); const renderData = ref({}); const generation = ref(0);
window.axios = axios;
// 预览只访问随包资源和明确的本地演示端点，禁止携带 Cookie。
axios.defaults.withCredentials = false;
// opaque Origin 没有 Cookie 访问权；演示静态请求也不能触发 Axios 默认 XSRF Cookie 读取。
axios.defaults.withXSRFToken = false;
axios.defaults.xsrfCookieName = null;
axios.interceptors.request.use(config => {
  const url = new URL(config.url, location.href);
  if (url.origin !== location.origin || !url.pathname.startsWith('/vform3/')) throw new Error('client.vform3_outbound_denied');
  config.withCredentials = false; return config;
});
const app = createApp({ setup() { return () => mode.value === 'designer'
  ? h(Designer, { key: generation.value, designerConfig: { resetFormJson: true }, ref: value => { instance = value; } })
  : renderJson.value ? h(Renderer, { key: generation.value, ref: value => { instance = value; }, formJson: renderJson.value, formData: renderData.value, onFormChange: (_field, _value, _old, values) => send({ changed: JSON.parse(JSON.stringify(values)) }) }) : null; } });
app.use(ElementPlus); registerIcon(app); app.component('draggable', Draggable);
addDirective(app); app.use(ContainerWidgets); app.use(ContainerItems); loadExtension(app);
app.mount('#app');
function connectDesign() {
  if (!instance?.designer || instance.designer.fullNetAssetConnected) return;
  const designer = instance.designer; const load = designer.loadFormJson.bind(designer);
  // 显式加载资产时不恢复另一份备份，但原版撤销栈仍须从当前空画布开始。
  if (designer.historyData.index < 0) designer.emitHistoryChange();
  designer.fullNetAssetConnected = true;
  designer.loadFormJson = json => {
    // 原版浅覆盖会遗留上一份脚本/扩展配置；原地恢复默认对象，保留 provide 的响应性。
    designer.clearSelected();
    for (const key of Object.keys(designer.formConfig)) delete designer.formConfig[key];
    Object.assign(designer.formConfig, getDefaultFormConfig(), json.formConfig);
    rootExtras = structuredClone(json); delete rootExtras.widgetList; delete rootExtras.formConfig;
    designer.fullNetAssetExtras = rootExtras; return load(json);
  };
}
connectDesign();
window.addEventListener('message', async event => {
  const message = event.data;
  if (event.source !== parent || event.origin !== parentOrigin || message?.protocol !== 'fullnet.vform3.v1' || message.channel !== channel
      || typeof message.id !== 'string' || JSON.stringify(message).length > 2_100_000) return;
  try {
    let result;
    switch (message.method) {
      case 'setFormJson': {
        const json = message.value;
        if (!json || !Array.isArray(json.widgetList) || !json.formConfig) throw new Error('client.invalid_vform_asset');
        rootExtras = structuredClone(json);
        // 以新实例清除上一次草稿状态，防止原版浅覆盖保留前一份配置。
        mode.value = 'designer'; generation.value++; await nextTick();
        connectDesign(); instance.setFormJson(structuredClone(json)); await nextTick(); result = null; break;
      }
      case 'getFormJson': result = { ...rootExtras, ...instance.getFormJson() }; break;
      case 'render': {
        const expected = message.value.json.fullNetRuntime?.rendererHash;
        if (expected && expected !== release.contentHash) throw new Error('client.vform3_runtime_mismatch');
        mode.value = 'renderer'; renderJson.value = message.value.json; renderData.value = message.value.data; generation.value++; await nextTick();
        // 挂载脚本可能改写初值；无校验读取当前实例，空必填项不能阻断用户进入表单。
        result = await instance.getFormData(false); break;
      }
      case 'getFormData': result = await instance.getFormData(); break;
      default: throw new Error('client.vform3_unknown_method');
    }
    send({ id: message.id, result });
  } catch { send({ id: message.id, error: 'client.vform3_operation_failed' }); }
});
const release = await fetch('/vform3/runtime-release.json', { credentials: 'omit', cache: 'no-store' }).then(response => { if (!response.ok) throw new Error('vform.runtime_release_missing'); return response.json(); });
await nextTick(); send({ ready: true, runtimeHash: release.contentHash });
