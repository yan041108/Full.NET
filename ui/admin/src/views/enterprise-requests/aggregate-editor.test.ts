import { expect, it } from 'vitest';
import type { EnterpriseRequestAggregateResponse } from '../../api/enterprise-request-client/index.generated';
import { buildRequestAggregateUpdate, isRequestDecimal, validRequestEditor, type EditableRequestLine } from './aggregate-editor';

const id = '019bc2b1-2a40-7cc3-8992-a80de51bf330';
const master = { organizationUnitId: id, applicantUserId: id, requestNumber: 'REQ', title: 'Request', totalAmount: '9007199254740993.12', status: 'Draft' };
const input = { lineNumber: 1, itemDescription: 'Item', quantity: '1.0001', unitPrice: '1.00', lineAmount: '1.00' };
it('大金额及版本按字符串保存，拒绝超出精度而不经浮点数舍入', () => {
  expect(isRequestDecimal('9007199254740993.12', 2)).toBe(true);
  expect(isRequestDecimal('1.234', 2)).toBe(false); expect(isRequestDecimal('1.12345', 4)).toBe(false);
  expect(isRequestDecimal('1e4', 2)).toBe(false); expect(isRequestDecimal('10000000000000000', 2)).toBe(false);
  expect(validRequestEditor(master, [{ ...input, key: 'new' }])).toBe(true);
  expect(validRequestEditor(master, [{ ...input, key: 'a' }, { ...input, key: 'b' }])).toBe(false);
});
it('按原始主从版本只生成实际变更的新增、修改和删除集合', () => {
  const original = { master: { ...master, version: '9007199254740993' }, details: [
    { ...input, id: 'a', version: '9007199254740995' }, { ...input, id: 'b', version: '3' }
  ] } as unknown as EnterpriseRequestAggregateResponse;
  const rows: EditableRequestLine[] = [{ ...input, key: 'a', id: 'a', itemDescription: 'Changed' }, { ...input, key: 'c', lineNumber: 3 }];
  const payload = buildRequestAggregateUpdate(master, original, rows);
  expect(payload.master.version).toBe('9007199254740993'); expect(payload.master.totalAmount).toBe(master.totalAmount);
  expect(payload.updated).toEqual([{ id: 'a', version: '9007199254740995', input: { ...input, itemDescription: 'Changed' } }]);
  expect(payload.removed).toEqual([{ id: 'b', version: '3' }]); expect(payload.added).toEqual([{ ...input, lineNumber: 3 }]);
  expect(JSON.stringify(payload)).not.toContain('organizationUnitId'); expect(JSON.stringify(payload)).not.toContain('requestId');
});
it('重复或不属于原始快照的明细身份拒绝生成请求', () => {
  const original = { master: { ...master, version: '1' }, details: [{ ...input, id: 'a', version: '1' }] } as unknown as EnterpriseRequestAggregateResponse;
  expect(() => buildRequestAggregateUpdate(master, original, [{ ...input, key: 'x', id: 'x' }])).toThrow('client.invalid_request_lines');
  expect(() => buildRequestAggregateUpdate(master, original, [{ ...input, key: 'a', id: 'a' }, { ...input, key: 'b', id: 'a' }])).toThrow();
});
