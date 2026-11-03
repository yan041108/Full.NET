import type {
  CreateEnterpriseRequestRequest, EnterpriseRequestAggregateResponse, EnterpriseRequestDetailInput,
  EnterpriseRequestDetailResponse, UpdateEnterpriseRequestAggregateRequest
} from '../../api/enterprise-request-client/index.generated';

type Mutable<T> = { -readonly [K in keyof T]: T[K] };
export interface EditableRequestLine extends Mutable<EnterpriseRequestDetailInput> { key: string; id?: string }
export interface EditableRequestMaster extends Mutable<CreateEnterpriseRequestRequest> { organizationUnitId: string }

/** 金额保持十进制字符串；仅核对 precision/scale，禁止经 JS Number 丢失精度。 */
export function isRequestDecimal(value: string, scale: 2 | 4): boolean {
  return new RegExp(`^-?\\d{1,${18 - scale}}(?:\\.\\d{1,${scale}})?$`).test(value);
}
export function validRequestEditor(master: EditableRequestMaster, rows: EditableRequestLine[]): boolean {
  const uuid = (value: string) => /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value)
    && value !== '00000000-0000-0000-0000-000000000000';
  return uuid(master.organizationUnitId) && uuid(master.applicantUserId) && !!master.requestNumber.trim() && master.requestNumber.length <= 64
    && !!master.title.trim() && master.title.length <= 200 && master.status === 'Draft' && isRequestDecimal(master.totalAmount, 2)
    && rows.length <= 128 && new Set(rows.map(row => row.lineNumber)).size === rows.length
    && rows.every(row => Number.isInteger(row.lineNumber) && row.lineNumber > 0 && row.lineNumber <= 2147483647
      && !!row.itemDescription.trim() && row.itemDescription.length <= 200 && isRequestDecimal(row.quantity, 4)
      && isRequestDecimal(row.unitPrice, 2) && isRequestDecimal(row.lineAmount, 2));
}
export function requestLineInput(row: EnterpriseRequestDetailInput): EnterpriseRequestDetailInput {
  return { lineNumber: row.lineNumber, itemDescription: row.itemDescription, quantity: row.quantity, unitPrice: row.unitPrice, lineAmount: row.lineAmount };
}
export function editRequestLine(row: EnterpriseRequestDetailResponse): EditableRequestLine {
  return { ...requestLineInput(row), key: row.id, id: row.id };
}

/** 主从版本均来自已加载快照；只发送变动行，父单 ID 由服务端聚合操作赋值。 */
export function buildRequestAggregateUpdate(master: EditableRequestMaster, original: EnterpriseRequestAggregateResponse,
  rows: EditableRequestLine[]): UpdateEnterpriseRequestAggregateRequest {
  const originals = new Map(original.details.map(row => [row.id, row]));
  const ids = rows.flatMap(row => row.id ? [row.id] : []);
  if (new Set(ids).size !== ids.length || ids.some(id => !originals.has(id))) throw new Error('client.invalid_request_lines');
  return {
    master: { requestNumber: master.requestNumber, title: master.title, status: 'Draft', totalAmount: master.totalAmount,
      applicantUserId: master.applicantUserId, version: original.master.version },
    added: rows.filter(row => !row.id).map(requestLineInput),
    updated: rows.filter(row => row.id && JSON.stringify(requestLineInput(row)) !== JSON.stringify(requestLineInput(originals.get(row.id)!)))
      .map(row => ({ id: row.id!, version: originals.get(row.id!)!.version, input: requestLineInput(row) })),
    removed: original.details.filter(row => !ids.includes(row.id)).map(row => ({ id: row.id, version: row.version }))
  };
}
