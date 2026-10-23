import { HubConnectionBuilder } from '@microsoft/signalr';
import type {
  NotificationsHubConnection,
  NotificationsHubConnectionOptions
} from './notifications-realtime.js';

/** 按需创建实际传输；令牌始终在连接时读取，不在延迟模块中缓存身份。 */
export function createNotificationsSignalRConnection(
  options: NotificationsHubConnectionOptions
): NotificationsHubConnection {
  return new HubConnectionBuilder()
    .withUrl(options.hubPath, {
      accessTokenFactory: async () => options.accessTokenFactory() ?? ''
    })
    .withAutomaticReconnect([...options.reconnectDelays])
    .build();
}
