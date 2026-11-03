import { afterEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import * as api from './ai-knowledge';
vi.mock('./http', () => ({ http: { request: vi.fn() } }));
const id = (n: number) => '01900000-0000-7000-8000-' + String(n).padStart(12, '0');
const params = { knowledgeBaseId: id(1), documentId: id(2), versionId: id(3) };
const root = `/api/v1/ai/knowledge-bases/${id(1)}/documents/${id(2)}/versions/${id(3)}/corrections`;
const task = () => ({ id: id(4), versionId: id(3), documentVersion: 2, state: 'queued', attemptCount: 0, sourceId: null, failureCode: null, createdAtUtc: '2026-10-28T00:00:00Z', updatedAtUtc: '2026-10-28T00:00:00Z' });
afterEach(() => vi.clearAllMocks());
describe('校对工作台生成 HTTP 消费者', () => {
  it('编辑和恢复通过生成操作发送冻结身份，历史游标及历史正文选择保持原样', async () => {
    const expected = { documentVersion: 2, sourceId: id(6), revisionId: null, headVersion: 0, sourceHash: 'a'.repeat(64), payloadHash: 'b'.repeat(64) };
    const row = { id: id(7), versionId: id(3), sourceId: id(6), parentId: null, headVersion: 1, documentVersion: 3, payloadHash: 'c'.repeat(64), reason: '说明', createdAtUtc: '2026-10-28T00:00:00Z' };
    const body = { requestKey: id(9), expected, reason: '说明', operation: { kind: 'replace', blockId: id(10), partId: id(11), text: '新文', offset: null, excluded: null } };
    const signal = new AbortController().signal; vi.mocked(http.request).mockResolvedValue(row);
    await api.editKnowledgeCorrection({ ...params, body }, signal);
    expect(http.request).toHaveBeenLastCalledWith(root + '/revisions', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) }, signal);
    const restore = { requestKey: id(12), expected, reason: '说明', targetSourceId: id(6), targetRevisionId: null };
    await api.restoreKnowledgeCorrection({ ...params, body: restore }, signal);
    expect(http.request).toHaveBeenLastCalledWith(root + '/restore', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(restore) }, signal);
    vi.mocked(http.request).mockResolvedValue({ items: [row], nextHeadVersion: null });
    await api.listKnowledgeCorrectionRevisions({ ...params, beforeHeadVersion: 5 }, signal);
    expect(http.request).toHaveBeenLastCalledWith(root + '/revisions?beforeHeadVersion=5', { method: 'GET' }, signal);
  });
  it('准备发送精确原请求，状态和最新任务透传取消信号', async () => {
    const signal = new AbortController().signal; const body = { requestKey: id(5), documentVersion: 2 }; vi.mocked(http.request).mockResolvedValue(task());
    await api.prepareKnowledgeCorrection({ ...params, body }, signal);
    expect(http.request).toHaveBeenLastCalledWith(root + '/prepare', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) }, signal);
    await api.getKnowledgeCorrectionPreparation({ ...params, preparationId: id(4) }, signal);
    expect(http.request).toHaveBeenLastCalledWith(root + '/preparations/' + id(4), { method: 'GET' }, signal);
    await api.getLatestKnowledgeCorrectionPreparation(params, signal);
    expect(http.request).toHaveBeenLastCalledWith(root + '/preparations/latest', { method: 'GET' }, signal);
  });
  it('不接收另一任务或另一文档版本的接受回执', async () => {
    vi.mocked(http.request).mockResolvedValue(task());
    await expect(api.getKnowledgeCorrectionPreparation({ ...params, preparationId: id(9) })).rejects.toThrow('client.invalid_knowledge_correction');
    await expect(api.prepareKnowledgeCorrection({ ...params, body: { requestKey: id(5), documentVersion: 3 } })).rejects.toThrow('client.invalid_knowledge_correction');
  });
  it('逐块来源查询只发送选择身份并拒绝伪装原文的补写响应', async () => {
    const response = { versionId: id(3), documentVersion: 2, sourceId: id(6), revisionId: null, headVersion: 0, sourceHash: 'a'.repeat(64), payloadHash: 'b'.repeat(64),
      parserRuntimeId: 'managed:utf8-text-v1', contentType: 'text/plain', originalFileName: 'source.txt', blockIndex: 1, totalBlocks: 2, isSourceView: true,
      block: { id: id(7), excluded: false, separator: '', parts: [{ id: id(8), text: '补写', prefix: '', contentKind: 'supplement', anchorId: null, originalText: null, sourceOffset: null, sourceLength: null, sourceLocation: null }] } };
    vi.mocked(http.request).mockResolvedValue(response); const signal = new AbortController().signal;
    expect((await api.getKnowledgeCorrectionWorkspace({ ...params, sourceId: id(6), block: 1 }, signal)).block.parts[0]?.contentKind).toBe('supplement');
    expect(http.request).toHaveBeenLastCalledWith(root + '/workspace?sourceId=' + id(6) + '&block=1', { method: 'GET' }, signal);
    vi.mocked(http.request).mockResolvedValue({ ...response, block: { ...response.block, parts: [{ ...response.block.parts[0]!, sourceOffset: 0 }] } });
    await expect(api.getKnowledgeCorrectionWorkspace({ ...params, sourceId: id(6), block: 1 })).rejects.toThrow('client.invalid_knowledge_correction');
  });
});
