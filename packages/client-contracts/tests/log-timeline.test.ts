import { describe, expect, it, vi } from 'vitest';
import { isLogTimelineResponse, queryLogTimeline, type LogTimelineResponse } from '../src/log-timeline';
import type { HttpClient } from '../src/http';

const sample = (): LogTimelineResponse => ({
  fromUtc: '2026-10-16T00:00:00Z', toUtc: '2026-10-16T01:00:00Z', partial: true,
  sources: [{ source: 'runtime.local_file', status: 'bounded_scan' }],
  entries: [{ id: 'event-1', source: 'runtime.local_file', occurredAtUtc: '2026-10-16T00:30:00Z', summary: 'diagnostic',
    correlation: { traceId: 'a'.repeat(32), spanId: null, parentSpanId: null, requestId: null, userId: null,
      tenantId: null, sessionId: null, centerSessionId: null, applicationSessionId: null, clientIpFingerprint: null } }]
});

describe('日志时间线客户端契约', () => {
  it('保留缺失关联与来源覆盖状态', () => expect(isLogTimelineResponse(sample())).toBe(true));
  it('接受服务端省略空关联字段，拒绝畸形关联对象和过量结果', () => {
    expect(isLogTimelineResponse({ ...sample(), entries: [{ ...sample().entries[0], correlation: {} }] })).toBe(true);
    expect(isLogTimelineResponse({ ...sample(), entries: [{ ...sample().entries[0], correlation: null }] })).toBe(false);
    expect(isLogTimelineResponse({ ...sample(), entries: [{ ...sample().entries[0], correlation: { traceId: 123 } }] })).toBe(false);
    expect(isLogTimelineResponse({ ...sample(), entries: Array(201).fill(sample().entries[0]) })).toBe(false);
  });
  it('保留所有精确过滤器与取消令牌', async () => {
    const request = vi.fn().mockResolvedValue(sample());
    const signal = new AbortController().signal;
    await queryLogTimeline({ request } as unknown as HttpClient, {
      fromUtc: sample().fromUtc, toUtc: sample().toUtc, sessionId: 'session', userId: 'user', clientIpFingerprint: 'c'.repeat(64)
    }, signal);
    const url = new URL(request.mock.calls[0]![0], 'https://localhost');
    expect(url.searchParams.get('sessionId')).toBe('session');
    expect(url.searchParams.get('userId')).toBe('user');
    expect(url.searchParams.get('clientIpFingerprint')).toBe('c'.repeat(64));
    expect(request.mock.calls[0]![2]).toBe(signal);
  });
  it('拒绝后端返回的畸形响应', async () => {
    await expect(queryLogTimeline({ request: vi.fn().mockResolvedValue({}) } as unknown as HttpClient,
      { fromUtc: sample().fromUtc, toUtc: sample().toUtc })).rejects.toThrow('client.invalid_log_timeline');
  });
});
