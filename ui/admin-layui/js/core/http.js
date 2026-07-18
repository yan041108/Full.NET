import { createHttpClient } from '@fullnet/client-contracts';

const apiBaseUrl = globalThis.FULLNET_CONFIG?.apiBaseUrl
  ?? import.meta.env.VITE_API_BASE_URL
  ?? '';
const http = createHttpClient(apiBaseUrl);

export const configureAuthentication = http.configureAuthentication.bind(http);
export const configureRequestLocale = http.configureRequestLocale.bind(http);
export const request = http.request.bind(http);

export { http };
