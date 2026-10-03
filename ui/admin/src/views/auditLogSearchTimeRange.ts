export interface AuditLogTimeInput {
  fromUtc?: string;
  toUtc?: string;
}

export type AuditLogTimeRange = {
  valid: true;
  fromUtc?: string;
  toUtc?: string;
  defaulted: boolean;
} | { valid: false };

export function resolveAuditLogSearchTimeRange(
  input: AuditLogTimeInput,
  hasContains: boolean,
  now = new Date()
): AuditLogTimeRange {
  const rawFrom = input.fromUtc?.trim();
  const rawTo = input.toUtc?.trim();
  if (hasContains && !rawFrom && !rawTo) {
    return {
      valid: true,
      fromUtc: new Date(now.getTime() - 24 * 60 * 60 * 1000).toISOString(),
      toUtc: now.toISOString(),
      defaulted: true
    };
  }

  const fromUtc = parseUtc(rawFrom);
  const toUtc = parseUtc(rawTo);
  if ((rawFrom && !fromUtc) || (rawTo && !toUtc)
    || (hasContains && (!fromUtc || !toUtc))
    || (fromUtc && toUtc && fromUtc > toUtc)) {
    return { valid: false };
  }
  return { valid: true, fromUtc, toUtc, defaulted: false };
}

function parseUtc(value: string | undefined): string | undefined {
  if (!value) return undefined;
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? undefined : date.toISOString();
}
