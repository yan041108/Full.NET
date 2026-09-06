import { createGridPreferenceClient } from '../preferences/grid-preferences';
import { http } from './http';

const client = createGridPreferenceClient((path, init) => http.request(path, init));

export const gridPreferencesApi = client;
