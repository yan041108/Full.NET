import { isGridPreferenceResponse } from '@fullnet/client-contracts';
import { createGridPreferenceClient } from '../preferences/grid-preferences';
import { http } from './http';

/** Vue 表格偏好客户端，转发到 /api/v1/me/grid-preferences。 */

const client = createGridPreferenceClient((path, init) => http.request(path, init));

export const gridPreferencesApi = client;

export { isGridPreferenceResponse };
