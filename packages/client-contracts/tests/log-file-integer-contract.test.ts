import { describe, expect, it } from 'vitest';
import { readLogFileList } from '../src/generated/guards.generated.js';

describe('日志文件扫描数量的整数传输契约', () => {
  const payload = { items: [], isTruncated: false };

  it('保留已经是整数的 scannedEntries', () => {
    expect(readLogFileList({ ...payload, scannedEntries: 17 }).scannedEntries).toBe(17);
  });

  it('将 scannedEntries 的数字字符串规范化为 number', () => {
    expect(readLogFileList({ ...payload, scannedEntries: '17' }).scannedEntries).toBe(17);
  });

  it.each(['1.5', '1e3', 'bad', '9007199254740992'])('拒绝无效或不安全的扫描数量 %s', value => {
    expect(() => readLogFileList({ ...payload, scannedEntries: value })).toThrow();
  });
});
