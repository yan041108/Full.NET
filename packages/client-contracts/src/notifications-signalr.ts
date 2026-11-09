import { DefaultHttpClient, HttpError, HttpResponse, HubConnectionBuilder, NullLogger, type HttpRequest } from '@microsoft/signalr';
import { readRetryAfterSeconds } from './problem-details.js';
import { NotificationsHubHttpFailure } from './notifications-realtime.js';
import type {
  NotificationsHubConnection,
  NotificationsHubConnectionOptions
} from './notifications-realtime.js';

/** 按需创建实际传输；令牌始终在连接时读取，不在延迟模块中缓存身份。 */
export function createNotificationsSignalRConnection(
  options: NotificationsHubConnectionOptions
): NotificationsHubConnection {
  const httpClient = new NotificationsHttpClient();
  const connection = new HubConnectionBuilder()
    .withUrl(options.hubPath, {
      httpClient,
      headers: options.headers,
      accessTokenFactory: async () => options.accessTokenFactory() ?? ''
    })
    .build();
  // 协商错误由 SDK 包装；在连接边界恢复结构化状态，交给同一个控制器处理首次连接和断线。
  return {
    async start() {
      httpClient.negotiationFailure = undefined;
      try {
        await connection.start();
      } catch (error) {
        throw httpClient.negotiationFailure ?? error;
      }
    },
    stop: () => connection.stop(),
    on: (name, handler) => connection.on(name, handler),
    off: (name, handler) => connection.off(name, handler),
    onreconnected: handler => connection.onreconnected(handler),
    onclose: handler => connection.onclose(handler)
  };
}

class NotificationsHttpClient extends DefaultHttpClient {
  negotiationFailure?: NotificationsHubHttpFailure;

  constructor() {
    super(NullLogger.instance);
  }

  override async send(request: HttpRequest): Promise<HttpResponse> {
    const url = request.url;
    if (!url || request.method !== 'POST' || !new URL(url).pathname.endsWith('/negotiate')) {
      try {
        return await super.send(request);
      } catch (error) {
        if (error instanceof HttpError && isRetryStatus(error.statusCode)) {
          throw new NotificationsHubHttpFailure(error.statusCode);
        }
        throw error;
      }
    }

    this.negotiationFailure = undefined;
    // 仅接管 SDK 的空正文协商请求以读取响应头；保持 SDK 提供的授权、CSRF、Cookie 和超时。
    const response = await fetch(url, {
      method: request.method,
      cache: 'no-cache',
      credentials: request.withCredentials ? 'include' : 'same-origin',
      headers: { 'X-Requested-With': 'XMLHttpRequest', ...request.headers },
      signal: request.timeout ? AbortSignal.timeout(request.timeout) : undefined
    });
    if (isRetryStatus(response.status)) {
      this.negotiationFailure = new NotificationsHubHttpFailure(response.status, readRetryAfterSeconds(response.headers));
      void response.body?.cancel().catch(() => undefined);
      throw this.negotiationFailure;
    }
    if (!response.ok) {
      void response.body?.cancel().catch(() => undefined);
      throw new HttpError('Notifications Hub negotiation failed', response.status);
    }
    return new HttpResponse(response.status, response.statusText, await response.text());
  }
}

function isRetryStatus(status: number): boolean {
  return status === 401 || status === 403 || status === 429;
}
