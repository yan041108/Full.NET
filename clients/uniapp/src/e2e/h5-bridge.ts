import { createHttpClient, type HttpRequestOptions } from '../api/http';
import { hydrateAuthenticatedLocale, localeController } from '../i18n';
import type { AccountLocaleSnapshot, LocaleSnapshot } from '../i18n/locale-controller';

const bridgeName = '__FULLNET_UNIAPP_E2E__';
const fixtureMarker = 'fullnet-uniapp-e2e-fixture';

interface H5E2EBridge {
  readonly marker: typeof fixtureMarker;
  hydrateAuthenticated(snapshot: AccountLocaleSnapshot): LocaleSnapshot;
  request<T>(options: HttpRequestOptions): Promise<T>;
}

function createApplicationHttpClient() {
  return createHttpClient({
    request: uni.request,
    getLocale: () => localeController.initialize().preferredLocale
  });
}

/**
 * 为 H5 开发构建安装最小测试端口；它复用正式控制器与 HTTP 客户端，不创建登录或 Token。
 */
export function installH5E2EBridge(): void {
  const bridge: H5E2EBridge = {
    marker: fixtureMarker,
    hydrateAuthenticated(snapshot) {
      return hydrateAuthenticatedLocale(snapshot, createApplicationHttpClient());
    },
    request<T>(options: HttpRequestOptions) {
      return createApplicationHttpClient().request<T>(options);
    }
  };

  Object.defineProperty(globalThis, bridgeName, {
    configurable: true,
    enumerable: false,
    value: bridge
  });
}
