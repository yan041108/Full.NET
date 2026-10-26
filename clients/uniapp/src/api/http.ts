import type { CanonicalLocale } from '../i18n/locale-adapter';
import { HttpProblem, toHttpProblem } from './problem-details';

export interface HttpClientDependencies {
  readonly request: Uni['request'];
  readonly uploadFile?: Uni['uploadFile'];
  readonly getLocale: () => CanonicalLocale;
  readonly getAccessToken?: () => string | undefined;
  readonly apiBaseUrl?: string;
}

export interface AuthenticationBridge {
  readonly getAccessToken: () => string | undefined;
  readonly refresh: () => Promise<boolean>;
}

export interface HttpRequestOptions {
  readonly path: string;
  readonly method?: 'GET' | 'POST' | 'PUT' | 'DELETE';
  readonly data?: unknown;
  readonly headers?: Readonly<Record<string, string>>;
  readonly retryUnauthorized?: boolean;
  readonly responseType?: 'arraybuffer';
}

export interface HttpClient {
  request<T>(options: HttpRequestOptions): Promise<T>;
  upload?<T>(options: HttpUploadOptions): Promise<T>;
}

/** 文件采集只接收应用内 API 路径，避免把当前凭据发送给任意地址。 */
export interface HttpUploadOptions {
  readonly path: string;
  readonly filePath: string;
  readonly retryUnauthorized?: boolean;
}

export interface ConfigurableHttpClient extends HttpClient {
  configureAuthentication(bridge?: AuthenticationBridge): void;
  upload<T>(options: HttpUploadOptions): Promise<T>;
}

/** 创建不缓存语言或认证状态的 uni.request Promise 适配器。 */
export function createHttpClient(dependencies: HttpClientDependencies): ConfigurableHttpClient {
  let authentication: AuthenticationBridge | undefined;
  let refreshInFlight: Promise<boolean> | undefined;

  function configureAuthentication(bridge?: AuthenticationBridge): void {
    authentication = bridge;
    refreshInFlight = undefined;
  }

  async function authorized<T>(operation: () => Promise<T>, retryUnauthorized = true): Promise<T> {
    try {
      return await operation();
    } catch (error: unknown) {
      const authenticationBridge = authentication;
      const shouldRetry = retryUnauthorized
        && error instanceof HttpProblem
        && error.status === 401
        && authenticationBridge !== undefined;
      if (!shouldRetry) {
        throw error;
      }

      refreshInFlight ??= Promise.resolve()
        .then(() => authenticationBridge.refresh())
        .catch(() => false)
        .finally(() => {
          refreshInFlight = undefined;
        });
      if (await refreshInFlight) {
        return await operation();
      }

      throw error;
    }
  }

  function request<T>(options: HttpRequestOptions): Promise<T> {
    return authorized(() => send<T>(options), options.retryUnauthorized !== false);
  }

  function upload<T>(options: HttpUploadOptions): Promise<T> {
    if (!options.path.startsWith('/api/') || !dependencies.uploadFile) {
      return Promise.reject(new HttpProblem({ status: 0, code: 'http.upload_unavailable', title: 'File upload unavailable.' }));
    }
    return authorized(() => new Promise<T>((resolve, reject) => {
      try {
        dependencies.uploadFile!({
          url: resolveRequestUrl(options.path, dependencies.apiBaseUrl),
          filePath: options.filePath,
          name: 'file',
          header: mergeHeaders(undefined, dependencies.getLocale(), authentication === undefined ? dependencies.getAccessToken?.() : authentication.getAccessToken()),
          success(response) {
            const body = parseResponseData(response.data);
            if (!isHttpStatus(response.statusCode)) {
              reject(new HttpProblem({ status: 0, code: 'http.unexpected_response', title: 'Request failed.' }));
            } else if (response.statusCode >= 200 && response.statusCode <= 299) resolve(body as T);
            else reject(toHttpProblem(response.statusCode, body));
          },
          fail() { reject(new HttpProblem({ status: 0, code: 'http.network_error', title: 'Network request failed.' })); }
        });
      } catch { reject(new HttpProblem({ status: 0, code: 'http.network_error', title: 'Network request failed.' })); }
    }), options.retryUnauthorized !== false);
  }

  function send<T>(options: HttpRequestOptions): Promise<T> {
    return new Promise<T>((resolve, reject) => {
      let settled = false;
      const resolveOnce = (value: T): void => {
        if (!settled) {
          settled = true;
          resolve(value);
        }
      };
      const rejectOnce = (problem: HttpProblem): void => {
        if (!settled) {
          settled = true;
          reject(problem);
        }
      };
      const rejectUnexpectedResponse = (): void => rejectOnce(new HttpProblem({
        status: 0,
        code: 'http.unexpected_response',
        title: 'Request failed.'
      }));
      const rejectNetworkFailure = (): void => rejectOnce(new HttpProblem({
        status: 0,
        code: 'http.network_error',
        title: 'Network request failed.'
      }));

      try {
        const headers = mergeHeaders(
          options.headers,
          dependencies.getLocale(),
          authentication === undefined
            ? dependencies.getAccessToken?.()
            : authentication.getAccessToken()
        );
        dependencies.request({
          url: resolveRequestUrl(options.path, dependencies.apiBaseUrl),
          method: options.method ?? 'GET',
          data: options.data as UniNamespace.RequestOptions['data'],
          header: headers,
          ...(options.responseType === 'arraybuffer' ? { responseType: 'arraybuffer' as const, dataType: 'text' } : { dataType: 'json' }),
          withCredentials: true,
          success(response) {
            try {
              if (!isHttpStatus(response?.statusCode)) {
                rejectUnexpectedResponse();
                return;
              }

              if (response.statusCode >= 200 && response.statusCode <= 299) {
                resolveOnce(response.data as T);
                return;
              }

              rejectOnce(toHttpProblem(response.statusCode, parseResponseData(response.data)));
            } catch {
              rejectUnexpectedResponse();
            }
          },
          fail() {
            rejectNetworkFailure();
          }
        });
      } catch {
        rejectNetworkFailure();
      }
    });
  }

  return {
    configureAuthentication,
    request,
    upload
  };
}

function resolveRequestUrl(path: string, apiBaseUrl: string | undefined): string {
  if (/^[a-z][a-z0-9+.-]*:\/\//i.test(path)) {
    return path;
  }

  const normalizedBaseUrl = apiBaseUrl?.trim().replace(/\/+$/u, '') ?? '';
  if (!normalizedBaseUrl) {
    return path;
  }

  return `${normalizedBaseUrl}/${path.replace(/^\/+/, '')}`;
}

function mergeHeaders(
  headers: Readonly<Record<string, string>> | undefined,
  locale: CanonicalLocale,
  token: string | undefined
): Record<string, string> {
  const merged: Record<string, string> = {};
  for (const [name, value] of Object.entries(headers ?? {})) {
    const normalizedName = name.toLowerCase();
    if (normalizedName !== 'accept-language' && normalizedName !== 'authorization') {
      merged[name] = value;
    }
  }

  merged['Accept-Language'] = locale;
  const trimmedToken = token?.trim();
  if (trimmedToken) {
    merged.Authorization = `Bearer ${trimmedToken}`;
  }
  return merged;
}

function isHttpStatus(value: unknown): value is number {
  return typeof value === 'number'
    && Number.isInteger(value)
    && value >= 100
    && value <= 599;
}

function parseResponseData(value: unknown): unknown {
  if (value instanceof ArrayBuffer) {
    try { value = new TextDecoder('utf-8', { fatal: true }).decode(value); } catch { return undefined; }
  }
  if (typeof value !== 'string') {
    return value;
  }

  try {
    return JSON.parse(value) as unknown;
  } catch {
    return undefined;
  }
}
