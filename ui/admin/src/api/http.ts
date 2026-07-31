import { createHttpClient } from '@fullnet/client-contracts';

export const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? '';
const http = createHttpClient(apiBaseUrl);

export const configureAuthentication = http.configureAuthentication.bind(http);
export const configureRequestLocale = http.configureRequestLocale.bind(http);
export const request = http.request.bind(http);

export { http };
