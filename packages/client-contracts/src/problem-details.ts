export interface FullNetProblemDetails {
  type?: string;
  title?: string;
  status: number;
  detail?: string;
  instance?: string;
  code: string;
  traceId?: string;
  errors?: Record<string, string[]>;
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
  const contentType = response.headers.get('content-type')?.toLowerCase() ?? '';
  if (contentType.includes('json')) {
    try {
      const value: unknown = await response.clone().json();
      if (isFullNetProblemDetails(value)) {
        return value;
      }
    } catch {
      // 响应体属于不可信边界，损坏 JSON 必须降级为稳定错误而不是泄露解析异常。
    }
  }

  return {
    status: response.status,
    code: 'http.unexpected_response',
    title: response.statusText || '请求失败'
  };
}
