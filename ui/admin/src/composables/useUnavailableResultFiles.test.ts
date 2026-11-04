import { describe, expect, it } from 'vitest';
import { useUnavailableResultFiles } from './useUnavailableResultFiles';

describe('结果文件不可用状态', () => {
  it('只有明确的文件404标记对应结果，其他任务不受影响', () => {
    const files = useUnavailableResultFiles();
    expect(files.begin('a:1').markUnavailable({ title: 'missing', status: 404, code: 'files.file.not_found' })).toBe(true);
    expect(files.isUnavailable('a:1')).toBe(true);
    expect(files.isUnavailable('a:2')).toBe(false);
    expect(files.isUnavailable('b')).toBe(false);
  });
  it.each([
    { title: 'denied', status: 403, code: 'files.file.not_found' },
    { title: 'busy', status: 500, code: 'files.file.not_found' },
    { title: 'task missing', status: 404, code: 'reporting.export_task.not_found' },
    { title: 'permission', status: 403, code: 'common.permission_denied' },
    new Error('network'), undefined
  ])('权限、任务不存在和传输故障保持可重试：%s', error => {
    const files = useUnavailableResultFiles();
    expect(files.begin('a').markUnavailable(error)).toBe(false);
    expect(files.isUnavailable('a')).toBe(false);
  });
  it('刷新或身份范围变化清空旧状态，旧响应不重新污染新范围', () => {
    const files = useUnavailableResultFiles(), old = files.begin('a');
    const missing = { title: 'missing', status: 404, code: 'files.file.not_found' };
    old.markUnavailable(missing); files.reset();
    expect(files.isUnavailable('a')).toBe(false); expect(old.current()).toBe(false);
    expect(old.markUnavailable(missing)).toBe(false);
    expect(files.begin('a').markUnavailable(missing)).toBe(true);
  });
});
