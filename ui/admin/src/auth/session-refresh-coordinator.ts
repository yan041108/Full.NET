import { createSessionRefreshCoordinator } from '@fullnet/client-contracts';

/** 统一串行化会话刷新请求，避免多个并发 401 恢复链路重复刷新令牌。 */
export const sessionRefreshCoordinator = createSessionRefreshCoordinator();
