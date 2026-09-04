# VForm3 ESM 安全子集来源

- 上游仓库：<https://github.com/vform666/variant-form3-vite>
- 上游提交：`c67479e496bab56a93a3dff168a4f529d8293c67`
- 对应版本：`3.0.10`
- 许可证：本目录 `LICENSE.txt` 中的 `Variant Form 许可条款 1.0`
- 作者声明：`src/esm/VForm3EsmDesigner.vue` 顶部保留上游要求的作者信息

## 采用边界

Full.NET 保留 VForm3 的 `widgetList/formConfig` JSON 兼容模型、三栏设计交互和字段目录概念，并针对 Vue 3.5/Vite 8 重写为仓库内原生 ESM 安全子集。该实现不等同于上游完整产品，也不得被重新声明为 Full.NET 自有 MIT 源码。

生产依赖图只包含 Workflow 当前批准的 `input`、`textarea`、`number`、`date`、`time`、`radio`、`checkbox`、`select`、`switch`。没有复制或引入上游代码生成器、SFC 生成器、脚本/CSS/HTML 编辑器、Ace、Quill、文件/图片上传、远程模板、Axios、运行时扩展加载器和示例应用。

## 更新规则

升级时必须重新固定上游提交，复核许可文本和作者声明，运行严格 CSP、危险能力扫描、真实浏览器 JSON 回读/保存及包体门禁。未经新的设计、安全和许可评估，不得扩大采用目录。
