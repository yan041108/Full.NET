# 客户端工具链精确补丁

`@dcloudio__uni-mp-weixin@3.0.0-5010520260709002.patch` 针对该精确 Apache-2.0 发行，仅将 `lib/uni.automator.js` 的一处 `require("jimp").read(t)` 更新为 `require("jimp").Jimp.read(t)`，适配根 override 固定的 Jimp 1.6.1。没有扩展自动化能力或替换其他平台代码。

旧 Jimp 0.10 使用带漏洞的 phin；中间版 0.22 又引入存在漏洞的 file-type。直接升级拥有者依赖并调整真实消费 API，使新版本使用已修复的 file-type；不跨主版本强塞不兼容解析器，也不新增安全例外。Jimp 与原发行的许可证说明保留在 THIRD-PARTY-NOTICES。

补丁通过 pnpm patchedDependencies 与锁文件 hash 生效，不依赖手改 node_modules。升级 DCloud 时重新核查唯一调用点；上游采用新 API 后删除补丁和对应精确登记。`pnpm test:workspace` 覆盖实际 Weixin 依赖链下的 PNG / HTTP 图片解码；三目标构建与 H5 浏览器覆盖其余工具链消费行为。
