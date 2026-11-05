import { describe, expect, it } from 'vitest';
import { formatPaymentAmount, parsePaymentAmount } from './payment-amount';
describe('完整 Int64 支付字符串的精确元分转换', () => {
  it.each([['0.01', '1'], ['0.29', '29'], ['17.29', '1729'], ['1', '100'], ['1.2', '120'], [' 1.23 ', '123'],
    ['90071992547409.91', '9007199254740991'], ['90071992547409.93', '9007199254740993'], ['92233720368547758.07', '9223372036854775807']] as const)
  ('%s 精确转换为 %s 分', (text, expected) => { expect(parsePaymentAmount(text)).toBe(expected); });
  it.each(['', '0', '0.00', '-1', '+1', '01.00', '.1', '1.', '1abc', '1e3', '1.001', '2.675', 'NaN', 'Infinity',
    '92233720368547758.08', '92233720368547758.071', '9'.repeat(1000)])('非法或越界文本 %s 拒绝提交', text => { expect(parsePaymentAmount(text)).toBeUndefined(); });
  it.each([['0', '0.00'], ['1', '0.01'], ['29', '0.29'], ['1729', '17.29'], ['9007199254740990', '90071992547409.90'],
    ['9007199254740993', '90071992547409.93'], ['9223372036854775806', '92233720368547758.06'], ['9223372036854775807', '92233720368547758.07']] as const)
  ('最小单位 %s 精确展示为 %s', (minor, expected) => { expect(formatPaymentAmount(minor)).toBe(expected); });
  it.each([undefined, null, 0, 29, -1, 0.1, '01', '1e3', '-1', '9223372036854775808', Number.NaN, Number.POSITIVE_INFINITY])
  ('错误线格式 %s 不显示舍入金额', value => { expect(formatPaymentAmount(value)).toBeUndefined(); });
});
