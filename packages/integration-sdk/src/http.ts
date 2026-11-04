/** 只公开有界 HTTP 故障字段；SDK 不自动记录正文、凭据或请求对象。 */
export interface IntegrationProblem { status: number; code: string; title?: string; detail?: string; traceId?: string }
export class IntegrationHttpError extends Error {
  constructor(public readonly problem: IntegrationProblem) { super(problem.code); this.name = 'IntegrationHttpError'; }
}
export class IntegrationProtocolError extends Error {
  constructor(code: string) { super(code); this.name = 'IntegrationProtocolError'; }
}
export interface RequestOptions { headers?: HeadersInit }
export interface HttpClient {
  request<T>(path: string, init?: RequestInit, signal?: AbortSignal, options?: RequestOptions): Promise<T>;
}
/** 由调用方从同一次身份结果中提供，不能分别读取会话切换前后的令牌与企业。 */
export interface IntegrationCredentials { readonly accessToken: string; readonly tenantId: string }
export interface IntegrationClientOptions {
  baseUrl: string;
  /** 本地结果隔离核对，不作为请求企业选择器或服务端授权依据。 */
  tenantId: string;
  /** 每次实际请求重新读取当前凭据；SDK 不刷新、保存或记录凭据。 */
  getCredentials: (signal: AbortSignal) => IntegrationCredentials | Promise<IntegrationCredentials>;
  locale?: string;
  timeoutMilliseconds?: number;
  maxGetRetries?: number;
  maxResponseBytes?: number;
  /** 仅用于本机开发；生产始终使用 HTTPS。 */
  allowHttpLocalhost?: boolean;
  fetch?: typeof globalThis.fetch;
}
const guid = /^[a-f\d]{8}-[a-f\d]{4}-[a-f\d]{4}-[a-f\d]{4}-[a-f\d]{12}$/iu;
const validGuid = (value: unknown): value is string => typeof value === 'string' && guid.test(value) && value !== '00000000-0000-0000-0000-000000000000';
const invalid = (code: string): never => { throw new IntegrationProtocolError(code); };

/** 令牌提供器、退避及读取共用一次请求期限；调用方取消后不能继续发送下一次请求。 */
function withAbort<T>(value: Promise<T>, signal: AbortSignal): Promise<T> {
  if (signal.aborted) { void value.catch(() => {}); return Promise.reject(signal.reason); }
  return new Promise((resolve,reject) => {
    const abort = () => reject(signal.reason);
    signal.addEventListener('abort',abort,{once:true});
    value.then(resolve,reject).finally(() => signal.removeEventListener('abort',abort));
  });
}
async function pause(milliseconds: number, signal: AbortSignal) {
  signal.throwIfAborted();
  await new Promise<void>((resolve,reject) => {
    const abort = () => { clearTimeout(timer); reject(signal.reason); };
    const timer = setTimeout(() => { signal.removeEventListener('abort',abort); resolve(); },milliseconds);
    signal.addEventListener('abort',abort,{once:true});
  });
}
async function readJson(response: Response, maximum: number, signal: AbortSignal): Promise<unknown> {
  const reader = response.body?.getReader(); if (!reader) return undefined;
  const chunks: Uint8Array[] = []; let length = 0;
  try {
    while (true) { const item = await withAbort(reader.read(),signal); if (item.done) break; length += item.value.byteLength; if (length > maximum) invalid('sdk.response_too_large'); chunks.push(item.value); }
    const bytes = new Uint8Array(length); let offset = 0; for (const chunk of chunks) { bytes.set(chunk,offset); offset += chunk.byteLength; }
    try { return JSON.parse(new TextDecoder('utf-8',{fatal:true}).decode(bytes)); } catch { return invalid('sdk.response_json_invalid'); }
  } finally { await reader.cancel().catch(() => {}); reader.releaseLock(); }
}
function assertTenant(value: unknown, tenantId: string): void {
  const inspect = (row: unknown) => {
    if (row && typeof row === 'object' && 'tenantId' in row && (!validGuid(row.tenantId) || row.tenantId.toLowerCase() !== tenantId)) invalid('sdk.response_tenant_mismatch');
  };
  if (Array.isArray(value)) value.forEach(inspect);
  else { inspect(value); if (value && typeof value === 'object' && 'items' in value && Array.isArray(value.items)) value.items.forEach(inspect); }
}

/** 面向外部 Bearer 调用；仅 GET 的暂时故障自动重试，所有 POST 均由调用方明确处理。 */
export function createIntegrationClient(options: IntegrationClientOptions): HttpClient {
  let base: URL; try { base = new URL(options.baseUrl); } catch { return invalid('sdk.configuration_invalid'); }
  const timeout = options.timeoutMilliseconds ?? 15_000, retries = options.maxGetRetries ?? 2, maximum = options.maxResponseBytes ?? 4_194_304;
  const local = ['localhost','127.0.0.1','[::1]'].includes(base.hostname);
  if ((base.protocol !== 'https:' && !(base.protocol === 'http:' && local && options.allowHttpLocalhost)) || base.username || base.password || base.search || base.hash || base.pathname !== '/' || !validGuid(options.tenantId)
    || typeof options.getCredentials !== 'function' || !Number.isInteger(timeout) || timeout < 1 || timeout > 120_000 || !Number.isInteger(retries) || retries < 0 || retries > 3 || !Number.isSafeInteger(maximum) || maximum < 1 || maximum > 16_777_216) invalid('sdk.configuration_invalid');
  const tenantId = options.tenantId.toLowerCase(), fetcher = options.fetch ?? globalThis.fetch;
  let locale: string; try { locale = Intl.getCanonicalLocales(options.locale ?? 'zh-CN')[0]; if (!locale) return invalid('sdk.configuration_invalid'); } catch { return invalid('sdk.configuration_invalid'); }
  return { async request<T>(resource: string, init: RequestInit = {}, caller?: AbortSignal, requestOptions: RequestOptions = {}): Promise<T> {
    if (!resource.startsWith('/api/v1/') || resource.includes('\\') || resource.includes('#')) invalid('sdk.request_path_invalid');
    const target = new URL(resource,base); if (target.origin !== base.origin || !target.pathname.startsWith('/api/v1/')) invalid('sdk.request_path_invalid');
    const deadline = new AbortController(), timer = setTimeout(() => deadline.abort(new DOMException('SDK request timed out.','TimeoutError')),timeout);
    const signal = AbortSignal.any([...(caller ? [caller] : []),...(init.signal ? [init.signal] : []),deadline.signal]), method = (init.method ?? 'GET').toUpperCase();
    try {
    for (let attempt = 0; ; attempt++) {
      signal.throwIfAborted();
      const credentials = await withAbort(Promise.resolve(options.getCredentials(signal)),signal);
      if (!credentials || !validGuid(credentials.tenantId) || credentials.tenantId.toLowerCase() !== tenantId) invalid('sdk.credential_scope_changed');
      const token = credentials.accessToken;
      if (typeof token !== 'string' || !token || token.length > 16_384 || !/^[\x21-\x7e]+$/u.test(token)) invalid('sdk.credential_invalid');
      const headers = new Headers(init.headers); new Headers(requestOptions.headers).forEach((value,key) => { if (!headers.has(key)) headers.set(key,value); });
      headers.set('Authorization','Bearer '+token); headers.set('Accept','application/json'); headers.set('Accept-Language',locale);
      if (typeof init.body === 'string' && !headers.has('Content-Type')) headers.set('Content-Type','application/json');
      let response: Response;
      try { response = await fetcher(target,{...init,method,headers,signal,credentials:'omit',redirect:'error'}); }
      catch { signal.throwIfAborted(); if (method !== 'GET' || attempt >= retries) return invalid('sdk.transport_failure'); await pause(200 * 2 ** attempt,signal); continue; }
      if (method === 'GET' && attempt < retries && [429,502,503,504].includes(response.status)) {
        const header = response.headers.get('Retry-After'), seconds = header === null ? NaN : Number(header);
        const delay = Number.isFinite(seconds) ? seconds * 1000 : header && Number.isFinite(Date.parse(header)) ? Date.parse(header) - Date.now() : 200 * 2 ** attempt;
        await response.body?.cancel().catch(() => {}); await pause(Math.max(0,Math.min(30_000,delay)),signal); continue;
      }
      if (!response.ok) {
        let body: unknown; try { body = await readJson(response,65_536,signal); } catch { signal.throwIfAborted(); }
        const record = body && typeof body === 'object' ? body as Record<string,unknown> : {}, code = typeof record.code === 'string' && /^[a-z][a-z\d_.-]{0,127}$/u.test(record.code) ? record.code : 'http.'+response.status;
        const text = (key: string, limit: number) => typeof record[key] === 'string' ? (record[key] as string).slice(0,limit) : undefined;
        throw new IntegrationHttpError({status:response.status,code,title:text('title',256),detail:text('detail',2048),traceId:text('traceId',128)});
      }
      if (response.status === 204) return undefined as T;
      let value: unknown;
      try { value = await readJson(response,maximum,signal); }
      catch (error) {
        signal.throwIfAborted(); if (error instanceof IntegrationProtocolError) throw error;
        if (method !== 'GET' || attempt >= retries) return invalid('sdk.transport_failure');
        await pause(200 * 2 ** attempt,signal); continue;
      }
      assertTenant(value,tenantId); return value as T;
    }
    } finally { clearTimeout(timer); }
  } };
}
