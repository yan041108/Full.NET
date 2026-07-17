export interface ProblemViolation {
  readonly field: string;
  readonly code: string;
  readonly arguments: Readonly<Record<string, unknown>>;
}

export interface HttpProblemOptions {
  readonly status: number;
  readonly code?: string;
  readonly title?: string;
  readonly detail?: string;
  readonly traceId?: string;
  readonly violations?: readonly ProblemViolation[];
}

export interface ProblemPresentation {
  readonly title: string;
  readonly traceId: string | undefined;
  readonly fieldMessages: Readonly<Record<string, readonly string[]>>;
  readonly message: string | undefined;
}

export type ProblemTranslator = (
  code: string,
  arguments_?: Readonly<Record<string, unknown>>
) => string | undefined;

/** 将标准 HTTP 失败保留为可按稳定代码处理的错误对象。 */
export class HttpProblem extends Error {
  readonly status: number;
  readonly code: string;
  readonly title: string;
  readonly detail: string | undefined;
  readonly traceId: string | undefined;
  readonly violations: readonly ProblemViolation[];

  constructor(options: HttpProblemOptions) {
    const title = nonEmptyString(options.title) ?? 'Request failed.';
    super(title);
    this.name = 'HttpProblem';
    this.status = Number.isInteger(options.status) ? options.status : 0;
    this.code = nonEmptyString(options.code) ?? 'http.unexpected_response';
    this.title = title;
    this.detail = nonEmptyString(options.detail);
    this.traceId = nonEmptyString(options.traceId);
    this.violations = options.violations ?? [];
  }
}

/** 从不可信响应载荷中提取可安全展示的标准 ProblemDetails 字段。 */
export function toHttpProblem(status: number, value: unknown): HttpProblem {
  const candidate = asRecord(value);
  if (!candidate) {
    return new HttpProblem({ status });
  }

  return new HttpProblem({
    status,
    code: nonEmptyString(candidate.code),
    title: nonEmptyString(candidate.title),
    detail: nonEmptyString(candidate.detail),
    traceId: nonEmptyString(candidate.traceId),
    violations: toViolations(candidate.violations)
  });
}

/** 基于稳定业务代码生成展示模型，未知代码仅回退到安全服务端标题。 */
export function toProblemPresentation(
  problem: HttpProblem,
  translate: ProblemTranslator
): ProblemPresentation {
  const fieldMessages: Record<string, string[]> = {};
  for (const violation of problem.violations) {
    const message = translate(violation.code, violation.arguments);
    if (!message) {
      continue;
    }

    (fieldMessages[violation.field] ??= []).push(message);
  }

  const message = Object.keys(fieldMessages).length === 0
    ? translate(problem.code) ?? problem.title
    : undefined;
  return {
    title: problem.title,
    traceId: problem.traceId,
    fieldMessages,
    message
  };
}

function toViolations(value: unknown): readonly ProblemViolation[] {
  if (!Array.isArray(value)) {
    return [];
  }

  return value.flatMap(item => {
    const candidate = asRecord(item);
    const field = candidate && nonEmptyString(candidate.field);
    const code = candidate && nonEmptyString(candidate.code);
    if (!field || !code) {
      return [];
    }

    return [{
      field,
      code,
      arguments: asRecord(candidate.arguments) ?? {}
    }];
  });
}

function asRecord(value: unknown): Record<string, unknown> | undefined {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
    ? value as Record<string, unknown>
    : undefined;
}

function nonEmptyString(value: unknown): string | undefined {
  return typeof value === 'string' && value.trim().length > 0 ? value : undefined;
}
