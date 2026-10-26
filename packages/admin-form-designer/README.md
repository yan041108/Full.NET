# Full.NET Admin Form Designer

`@fullnet/admin-form-designer` 提供固定 VForm 3.0.10 完整源码的隔离设计器和原版 Renderer。全部 27 个原版/内置扩展添加入口、布局、属性、预览和工具栏保留；来源、许可、最小适配与完整性清单见 [PROVENANCE](vendor/vform3/PROVENANCE.md)。

宿主接口 `setFormJson/getFormJson/render/getFormData` 为异步 Promise，通过固定消息协议调用无同源权限的 iframe。载入以新实例清除上一份状态，卸载拒绝未完成请求。原版 JSON 不经过业务 Schema 回显。

`render` 完成后返回当前模型的对象快照，包含原版挂载脚本同步设置的初值；读取不触发表单校验，空必填项不会阻止进入编辑。调用方仍须检查装载代次、值协议与字段权限，再形成业务 Patch。显式调用 `getFormData` 保留原版校验行为。

宿主只在 Ready 运行摘要核验后转交变更。接收消息先检查来源、通道和 JSON 可序列化性，消息封装与调用回执最多为 2,100,000 个序列化字符，变更值最多为 2,000,000 个序列化字符；循环引用、BigInt 和超限负载不能进入业务回调。异常调用回执会拒绝对应 Promise 并清除定时器，后续合法请求仍可使用。该通信长度限制与服务端设计资产的 UTF-8 字节限制分别执行。

Workflow 使用独立设计资产 API/数据库持久化完整 JSON，并保存修订与摘要；发布原子固定完整快照和业务校验投影。待办仅获得授权渲染投影与值；未支持的生产业务绑定在发布时诊断，不裁剪原设计。

## 构建和部署

管理端 Vite 插件自动构建 `/vform3/`，开发端口分别保存产物；生产构建输出在 `ui/admin/dist/vform3/`，包括 Ace、示例、原版许可及全部实际打包依赖 Notices。可单独执行 `pnpm --filter @fullnet/admin-form-designer build:runtime`。完整运行包固定于 `contracts/workflow/vform3-runtime-release.json`：只有明确修改受审产物时执行 `node scripts/workflow/vform-runtime-release.mjs --update`；构建/预览自动核对，部署前执行 `node scripts/workflow/vform-runtime-release.mjs --verify <实际 vform3 目录>`，文件增删或字节变化都会拒绝。前后端必须作为同一发布输入一起构建。

静态服务器须保留 `/vform3/` 路径及 JS/CSS/字体正确 MIME，为 opaque-origin 模块资源返回 `Access-Control-Allow-Origin: *`（不允许 credentials）与 `Cross-Origin-Resource-Policy: cross-origin`；该规则仅用于此目录，不能套用业务 API。宿主安装只收紧父文档 `frame-src 'self'`，不放宽宿主 script-src；帧保留独立 CSP。生产业务 API 不允许 Origin `null` 的凭据访问。

官方预览的上传/远程配置使用受控演示端点；真实 Files、目录和新增业务规则在原版对齐后扩展。现有业务接续、绑定清单/Web 目标与实际产物核验已按[实施计划 VF-F4](../../docs/superpowers/plans/2026-10-23-vform3-full-source-migration.md)实施，验收和未验证边界仍独立记录。H5 预览不代表小程序/Flutter 完成。

## 针对性验证

`pnpm --filter @fullnet/admin-form-designer test -- src/VForm3DesignerHost.test.ts` 验证协议/生命周期；`node --test packages/admin-form-designer/tests/source-inventory.test.mjs` 验证全部上游文件。管理端 `tests/vform3-full-source.spec.mjs` 验证真实控件、属性、工具栏、可编译 SFC、隔离和填报；API/双库保存与发布验证由 Workflow 负责。完整证据见[本轮验证记录](../../docs/verification/2026-10-24-vform3-full-source-migration.md)。
