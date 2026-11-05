import { describe, expect, it } from 'vitest';
import { formatPaymentAmount, parsePaymentAmount } from './payment-amount';

describe('当前数字支付契约的精确元分转换', () => {
  it.each([['0.01', 1], ['0.29', 29], ['17.29', 1729], ['1', 100], ['1.2', 120], [' 1.23 ', 123],
    ['90071992547409.91', Number.MAX_SAFE_INTEGER]] as const)('%s 精确转换为 %s 分', (text, expected) => {
    expect(parsePaymentAmount(text)).toBe(expected);
  });
  it.each(['', '0', '0.00', '-1', '+1', '01.00', '.1', '1.', '1abc', '1e3', '1.001', '2.675', 'NaN', 'Infinity',
    '90071992547409.92', '9'.repeat(1000)])('非法或超范围文本 %s 拒绝提交', text => {
    expect(parsePaymentAmount(text)).toBeUndefined();
  });
  it.each([[0, '0.00'], [1, '0.01'], [29, '0.29'], [1729, '17.29'], [Number.MAX_SAFE_INTEGER - 1, '90071992547409.90'], [Number.MAX_SAFE_INTEGER, '90071992547409.91']] as const)
  ('最小单位 %s 精确展示为 %s', (minor, expected) => { expect(formatPaymentAmount(minor)).toBe(expected); });
  it.each([undefined, null, '100', -1, 0.1, Number.MAX_SAFE_INTEGER + 1, Number.NaN, Number.POSITIVE_INFINITY])
  ('失真响应 %s 不显示舍入金额', value => { expect(formatPaymentAmount(value)).toBeUndefined(); });
});
