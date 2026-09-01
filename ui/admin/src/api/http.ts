import { createHttpClient } from '@fullnet/client-contracts';

/** 管理端 API 基地址；未配置时默认与当前站点同源。 */
export const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? '';

/** 所有 API 模块共享的 HTTP 客户端，统一承载认证、语言和错误处理约定。 */
const http = createHttpClient(apiBaseUrl);

/** 配置认证读取器，供登录恢复和令牌轮换链路复用。 */
export const configureAuthentication = http.configureAuthentication.bind(http);
/** 配置请求语言提供器，确保每次请求都携带当前活动语言。 */
export const configureRequestLocale = http.configureRequestLocale.bind(http);
/** 发送标准 JSON/文本请求。 */
export const request = http.request.bind(http);
/** 发送二进制请求，供文件下载与预览场景复用。 */
export const requestBlob = http.requestBlob.bind(http);

/** 导出底层 HTTP 客户端实例，供共享测试替身和少量高级封装复用；普通 API 模块优先使用上面的稳定包装器。 */
export { http };
