export interface FullNetProblemDetails {
  type?: string;
  title?: string;
  status: number;
  detail?: string;
  instance?: string;
  code: string;
  traceId?: string;
  errors?: Record<string, string[]>;
  retryAfterSeconds?: number;
}

/** 判断未知响应是否满足 Full.NET 客户端可以稳定处理的错误契约。 */
export function isFullNetProblemDetails(value: unknown): value is FullNetProblemDetails {
  if (typeof value !== 'object' || value === null) {
    return false;
  }

  const candidate = value as Record<string, unknown>;
  return typeof candidate.status === 'number'
    && Number.isInteger(candidate.status)
    && typeof candidate.code === 'string'
    && candidate.code.length > 0;
}

/**
 * 读取标准错误响应；当网关返回 HTML、空响应或损坏 JSON 时返回安全的统一错误。
 */
export async function readProblemDetails(response: Response): Promise<FullNetProblemDetails> {
  const retryAfter = response.headers.get('retry-after');
  const seconds = retryAfter && /^\d+$/.test(retryAfter.trim()) ? Number(retryAfter)
    : retryAfter ? Math.ceil((Date.parse(retryAfter) - Date.now()) / 1000) : NaN;
  const retryMetadata = Number.isFinite(seconds) && seconds >= 0
    ? { retryAfterSeconds: Math.min(3600, Math.max(1, seconds)) } : {};
  const contentType = response.headers.get('content-type')?.toLowerCase() ?? '';
  if (contentType.includes('json')) {
    try {
      const value: unknown = await response.clone().json();
      if (isFullNetProblemDetails(value)) {
        return { ...value, ...retryMetadata };
      }
    } catch {
      // 响应体属于不可信边界，损坏 JSON 必须降级为稳定错误而不是泄露解析异常。
    }
  }

  return {
    ...retryMetadata,
    status: response.status,
    code: 'http.unexpected_response',
    title: response.statusText || '请求失败'
  };
}
