import type { CanonicalLocale } from '../i18n/locale-adapter';
import { HttpProblem, toHttpProblem } from './problem-details';

export interface HttpClientDependencies {
  readonly request: Uni['request'];
  readonly getLocale: () => CanonicalLocale;
  readonly getAccessToken?: () => string | undefined;
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
}

export interface HttpClient {
  request<T>(options: HttpRequestOptions): Promise<T>;
}

export interface ConfigurableHttpClient extends HttpClient {
  configureAuthentication(bridge?: AuthenticationBridge): void;
}

/** 创建不缓存语言或认证状态的 uni.request Promise 适配器。 */
export function createHttpClient(dependencies: HttpClientDependencies): ConfigurableHttpClient {
  let authentication: AuthenticationBridge | undefined;
  let refreshInFlight: Promise<boolean> | undefined;

  function configureAuthentication(bridge?: AuthenticationBridge): void {
    authentication = bridge;
    refreshInFlight = undefined;
  }

  async function request<T>(options: HttpRequestOptions): Promise<T> {
    try {
      return await send<T>(options);
    } catch (error: unknown) {
      const authenticationBridge = authentication;
      const shouldRetry = options.retryUnauthorized !== false
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
        return await request<T>({
          ...options,
          retryUnauthorized: false
        });
      }

      throw error;
    }
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
          url: options.path,
          method: options.method ?? 'GET',
          data: options.data as UniNamespace.RequestOptions['data'],
          header: headers,
          dataType: 'json',
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
    request
  };
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
  if (typeof value !== 'string') {
    return value;
  }

  try {
    return JSON.parse(value) as unknown;
  } catch {
    return undefined;
  }
}
