import { afterEach, describe, expect, it, vi } from 'vitest';
import { createHttpClient } from '../src/http';
import {
  aiUploadKnowledgeVersion, aiDownloadKnowledgeSource, aiDeleteKnowledgeDocument,
  aiRetryKnowledgeImport, aiListKnowledgeVersions, aiListKnowledgeOrganizationCandidates, aiListKnowledgeDocumentOrganizationCandidates, aiListKnowledgeRoleCandidates, aiListKnowledgeDocumentRoleCandidates,
  aiGetKnowledgeOrganizations, aiSetKnowledgeOrganizations, aiGetKnowledgeDocumentOrganizations, aiSetKnowledgeDocumentOrganizations,
  aiGetKnowledgeRoles, aiSetKnowledgeRoles, aiGetKnowledgeDocumentRoles, aiSetKnowledgeDocumentRoles
} from '../src/ai-knowledge';

const id = '00000000-0000-0000-0000-000000000001';
const resource = { knowledgeBaseId: id, documentId: id };
const imported = {
  id, documentId: id, versionId: id, state: 'pending_confirmation', failureCode: null,
  createdAtUtc: '2026-10-03T00:00:00Z', updatedAtUtc: '2026-10-03T00:00:00Z'
};
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), {
  status, headers: { 'content-type': 'application/json' }
});

afterEach(() => vi.unstubAllGlobals());

describe('知识库共享生成客户端', () => {
  it('四个机构授予操作使用各自资源路径并传递版本、认证、语言与取消', async () => {
    const documentId = '00000000-0000-0000-0000-000000000002';
    const documentResource = { knowledgeBaseId: id, documentId };
    const granted = { resourceId: id, organizationUnitIds: [id], version: 4 };
    const documentGranted = { ...granted, resourceId: documentId };
    const fetchMock = vi.fn().mockImplementation(async (url: string) =>
      json({ ...(url.includes('/documents/') ? documentGranted : granted), version: '4' }));
    vi.stubGlobal('fetch', fetchMock);
    const http = createHttpClient();
    http.configureRequestLocale(() => 'zh-CN');
    http.configureAuthentication({ getAccessToken: () => 'access', refresh: async () => false });
    const signal = new AbortController().signal;
    const body = { organizationUnitIds: [id], version: 3 };
    expect(await aiGetKnowledgeOrganizations(http, { knowledgeBaseId: id }, signal)).toEqual(granted);
    expect(await aiSetKnowledgeOrganizations(http, { knowledgeBaseId: id, body }, signal)).toEqual(granted);
    expect(await aiGetKnowledgeDocumentOrganizations(http, documentResource, signal)).toEqual(documentGranted);
    expect(await aiSetKnowledgeDocumentOrganizations(http, { ...documentResource, body }, signal)).toEqual(documentGranted);
    for (const [index, [url, request]] of fetchMock.mock.calls.entries()) {
      expect(url).toBe(`/api/v1/ai/knowledge-bases/${id}${index >= 2 ? `/documents/${documentId}` : ''}/organizations`);
      expect(request.method).toBe(index % 2 === 0 ? 'GET' : 'PUT');
      expect(request.body === undefined ? undefined : JSON.parse(request.body)).toEqual(index % 2 === 0 ? undefined : body);
      expect(request.signal).toBe(signal);
      expect(new Headers(request.headers).get('authorization')).toBe('Bearer access');
      expect(new Headers(request.headers).get('accept-language')).toBe('zh-CN');
    }
  });

  it('机构授予刷新只重试一次且保留清空机构的原始并发版本', async () => {
    const granted = { resourceId: id, organizationUnitIds: [], version: 4 };
    const fetchMock = vi.fn().mockResolvedValueOnce(new Response(null, { status: 401 })).mockResolvedValueOnce(json(granted));
    vi.stubGlobal('fetch', fetchMock);
    const http = createHttpClient();
    let token = 'old';
    const refresh = vi.fn(async () => { token = 'new'; return true; });
    http.configureAuthentication({ getAccessToken: () => token, refresh });
    expect(await aiSetKnowledgeDocumentOrganizations(http, { ...resource, body: { organizationUnitIds: [], version: 3 } })).toEqual(granted);
    expect(refresh).toHaveBeenCalledTimes(1);
    expect(fetchMock).toHaveBeenCalledTimes(2);
    for (const [, request] of fetchMock.mock.calls) expect(JSON.parse(request.body)).toEqual({ organizationUnitIds: [], version: 3 });
    expect(new Headers(fetchMock.mock.calls[1][1].headers).get('authorization')).toBe('Bearer new');
  });

  it.each([
    { status: 403, code: 'authorization.permission_denied' },
    { status: 409, code: 'ai.knowledge.version_conflict' }
  ])('机构授予保留 $status 的 ProblemDetails', async ({ status, code }) => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json({ status, code, title: '拒绝' }, status)));
    await expect(aiSetKnowledgeOrganizations(createHttpClient(), { knowledgeBaseId: id, body: { organizationUnitIds: [], version: 3 } }))
      .rejects.toMatchObject({ status, code });
  });

  it.each([
    { resourceId: 'invalid', organizationUnitIds: [id], version: 4 },
    { resourceId: id, organizationUnitIds: ['invalid'], version: 4 },
    { resourceId: id, organizationUnitIds: null, version: 4 },
    { resourceId: id, organizationUnitIds: [id], version: 4.5 },
    { resourceId: id, organizationUnitIds: [id], version: '9007199254740993' }
  ])('机构授予拒绝损坏的资源、机构集合或并发版本 %j', async granted => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json(granted)));
    await expect(aiGetKnowledgeOrganizations(createHttpClient(), { knowledgeBaseId: id })).rejects.toThrow(/invalid_ai_knowledge_organization_grants_response/);
  });

  it('Host 空机构授予读取保持资源标识与版本', async () => {
    const granted = { resourceId: id, organizationUnitIds: [], version: 1 };
    vi.stubGlobal('fetch', vi.fn().mockImplementation(async () => json(granted)));
    expect(await aiGetKnowledgeOrganizations(createHttpClient(), { knowledgeBaseId: id })).toEqual(granted);
    expect(await aiGetKnowledgeDocumentOrganizations(createHttpClient(), resource)).toEqual(granted);
  });


  it('四个角色授予操作使用各自资源路径并传递版本、认证、语言与取消', async () => {
    const granted = { resourceId: id, roleIds: [id], version: 4 };
    const fetchMock = vi.fn().mockImplementation(async () => json({ ...granted, version: '4' }));
    vi.stubGlobal('fetch', fetchMock);
    const http = createHttpClient();
    http.configureRequestLocale(() => 'zh-CN');
    http.configureAuthentication({ getAccessToken: () => 'access', refresh: async () => false });
    const signal = new AbortController().signal;
    const body = { roleIds: [id], version: 3 };
    expect(await aiGetKnowledgeRoles(http, { knowledgeBaseId: id }, signal)).toEqual(granted);
    expect(await aiSetKnowledgeRoles(http, { knowledgeBaseId: id, body }, signal)).toEqual(granted);
    expect(await aiGetKnowledgeDocumentRoles(http, resource, signal)).toEqual(granted);
    expect(await aiSetKnowledgeDocumentRoles(http, { ...resource, body }, signal)).toEqual(granted);
    for (const [index, [url, request]] of fetchMock.mock.calls.entries()) {
      expect(url).toBe(`/api/v1/ai/knowledge-bases/${id}${index >= 2 ? `/documents/${id}` : ''}/roles`);
      expect(request.method).toBe(index % 2 === 0 ? 'GET' : 'PUT');
      expect(request.body === undefined ? undefined : JSON.parse(request.body)).toEqual(index % 2 === 0 ? undefined : body);
      expect(request.signal).toBe(signal);
      expect(new Headers(request.headers).get('authorization')).toBe('Bearer access');
      expect(new Headers(request.headers).get('accept-language')).toBe('zh-CN');
    }
  });

  it('角色授予刷新只重试一次且保留清空角色的原始并发版本', async () => {
    const granted = { resourceId: id, roleIds: [], version: 4 };
    const fetchMock = vi.fn().mockResolvedValueOnce(new Response(null, { status: 401 })).mockResolvedValueOnce(json(granted));
    vi.stubGlobal('fetch', fetchMock);
    const http = createHttpClient();
    let token = 'old';
    const refresh = vi.fn(async () => { token = 'new'; return true; });
    http.configureAuthentication({ getAccessToken: () => token, refresh });
    expect(await aiSetKnowledgeDocumentRoles(http, { ...resource, body: { roleIds: [], version: 3 } })).toEqual(granted);
    expect(refresh).toHaveBeenCalledTimes(1);
    expect(fetchMock).toHaveBeenCalledTimes(2);
    for (const [, request] of fetchMock.mock.calls) expect(JSON.parse(request.body)).toEqual({ roleIds: [], version: 3 });
    expect(new Headers(fetchMock.mock.calls[1][1].headers).get('authorization')).toBe('Bearer new');
  });

  it.each([
    { status: 403, code: 'authorization.permission_denied' },
    { status: 409, code: 'ai.knowledge.version_conflict' }
  ])('角色授予保留 $status 的 ProblemDetails', async ({ status, code }) => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json({ status, code, title: '拒绝' }, status)));
    await expect(aiSetKnowledgeRoles(createHttpClient(), { knowledgeBaseId: id, body: { roleIds: [], version: 3 } }))
      .rejects.toMatchObject({ status, code });
  });

  it.each([
    { resourceId: 'invalid', roleIds: [id], version: 4 },
    { resourceId: id, roleIds: ['invalid'], version: 4 },
    { resourceId: id, roleIds: null, version: 4 },
    { resourceId: id, roleIds: [id], version: 4.5 },
    { resourceId: id, roleIds: [id], version: '9007199254740993' }
  ])('角色授予拒绝损坏的资源、角色集合或并发版本 %j', async granted => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json(granted)));
    await expect(aiGetKnowledgeRoles(createHttpClient(), { knowledgeBaseId: id })).rejects.toThrow(/invalid_ai_knowledge_role_grants_response/);
  });

  it('两个角色候选操作传递分页、认证、语言与取消且只返回最小角色投影', async () => {
    const page = { items: [{ id, code: 'reader', name: '阅读角色' }], page: 2, pageSize: 10, total: '21' };
    const fetchMock = vi.fn().mockImplementation(async () => json(page));
    vi.stubGlobal('fetch', fetchMock);
    const http = createHttpClient();
    http.configureRequestLocale(() => 'zh-CN');
    http.configureAuthentication({ getAccessToken: () => 'access', refresh: async () => false });
    const signal = new AbortController().signal;
    expect(await aiListKnowledgeRoleCandidates(http, { knowledgeBaseId: id, page: 2, pageSize: 10 }, signal)).toEqual({ ...page, total: 21 });
    expect(await aiListKnowledgeDocumentRoleCandidates(http, { ...resource, page: 2, pageSize: 10 }, signal)).toEqual({ ...page, total: 21 });
    for (const [index, [url, request]] of fetchMock.mock.calls.entries()) {
      const parsed = new URL(url, 'https://example.test');
      expect(parsed.pathname).toBe(`/api/v1/ai/knowledge-bases/${id}${index === 1 ? `/documents/${id}` : ''}/role-candidates`);
      expect([...parsed.searchParams.entries()]).toEqual([['page', '2'], ['pageSize', '10']]);
      expect(request.signal).toBe(signal);
      expect(new Headers(request.headers).get('authorization')).toBe('Bearer access');
      expect(new Headers(request.headers).get('accept-language')).toBe('zh-CN');
      expect(request.body).toBeUndefined();
    }
  });

  it('角色候选拒绝损坏 UUID 并传播权威撤权 ProblemDetails', async () => {
    const fetchMock = vi.fn().mockResolvedValueOnce(json({ items: [{ id: 'invalid', code: 'role', name: '角色' }], page: 1, pageSize: 20, total: '1' }))
      .mockResolvedValueOnce(json({ status: 403, code: 'authorization.permission_denied', title: 'Forbidden' }, 403));
    vi.stubGlobal('fetch', fetchMock);
    const http = createHttpClient();
    await expect(aiListKnowledgeRoleCandidates(http, { knowledgeBaseId: id })).rejects.toThrow(/invalid_paged_result_of_ai_knowledge_role_candidate_response/);
    await expect(aiListKnowledgeDocumentRoleCandidates(http, resource)).rejects.toMatchObject({ status: 403, code: 'authorization.permission_denied' });
  });

  it('文件版本分页读取整数线编码并拒绝精度丢失和损坏行', async () => {
    const version = {
      id, documentId: id, versionNumber: 1, originalFileName: '来源.txt', contentType: 'text/plain',
      contentHash: 'A'.repeat(64), sizeBytes: '8', processingStatus: 'pending_processing',
      createdAtUtc: '2026-10-03T00:00:00Z'
    };
    const page = { items: [version], page: 1, pageSize: 20, total: '1' };
    const fetchMock = vi.fn().mockResolvedValueOnce(json(page))
      .mockResolvedValueOnce(json({ ...page, items: [{ ...version, sizeBytes: '9007199254740993' }] }));
    vi.stubGlobal('fetch', fetchMock);
    const http = createHttpClient();
    expect(await aiListKnowledgeVersions(http, resource)).toEqual({
      ...page, total: 1, items: [{ ...version, sizeBytes: 8 }]
    });
    await expect(aiListKnowledgeVersions(http, resource)).rejects.toThrow(/invalid_paged_result_of_ai_knowledge_version_response/);
  });

  it('认证刷新重试保留原始文件、媒体类型和幂等键，并透传取消及语言', async () => {
    const body = new Blob([new Uint8Array([0, 255, 42])]);
    const signal = new AbortController().signal;
    const fetchMock = vi.fn().mockResolvedValueOnce(new Response(null, { status: 401 }))
      .mockResolvedValueOnce(json(imported, 202));
    vi.stubGlobal('fetch', fetchMock);
    const http = createHttpClient();
    let token = 'old';
    http.configureAuthentication({ getAccessToken: () => token, refresh: async () => { token = 'new'; return true; } });
    http.configureRequestLocale(() => 'zh-CN');
    expect(await aiUploadKnowledgeVersion(http, {
      ...resource, version: 3, requestKey: id, fileName: '来源 +.pdf', contentType: 'application/pdf', body
    }, signal)).toEqual(imported);
    expect(fetchMock).toHaveBeenCalledTimes(2);
    const [url, request] = fetchMock.mock.calls[1];
    const query = new URL(url, 'https://example.test').searchParams;
    expect(query.get('version')).toBe('3');
    expect(query.get('requestKey')).toBe(id);
    expect(query.get('fileName')).toBe('来源 +.pdf');
    expect([...query.keys()].sort()).toEqual(['fileName', 'requestKey', 'version']);
    expect(request.body).toBe(body);
    expect(request.signal).toBe(signal);
    expect(new Headers(request.headers).get('content-type')).toBe('application/pdf');
    expect(new Headers(request.headers).get('authorization')).toBe('Bearer new');
    expect(new Headers(request.headers).get('accept-language')).toBe('zh-CN');
  });

  it('来源走认证 Blob 响应；撤权 ProblemDetails 原样交给共享错误边界', async () => {
    const fetchMock = vi.fn().mockResolvedValueOnce(new Response('source bytes', {
      headers: { 'content-type': 'application/octet-stream' }
    })).mockResolvedValueOnce(json({ status: 403, code: 'authorization.permission_denied', title: 'Forbidden' }, 403));
    vi.stubGlobal('fetch', fetchMock);
    const http = createHttpClient();
    http.configureAuthentication({ getAccessToken: () => 'access', refresh: async () => false });
    const parameters = { ...resource, versionId: id };
    expect(await (await aiDownloadKnowledgeSource(http, parameters)).text()).toBe('source bytes');
    expect(new Headers(fetchMock.mock.calls[0][1].headers).get('authorization')).toBe('Bearer access');
    await expect(aiDownloadKnowledgeSource(http, parameters)).rejects.toMatchObject({
      status: 403, code: 'authorization.permission_denied'
    });
  });

  it('文档删除接受 204，导入重试拒绝不符合 DTO 的 JSON', async () => {
    const fetchMock = vi.fn().mockResolvedValueOnce(new Response(null, { status: 204 }))
      .mockResolvedValueOnce(json({ ...imported, versionId: 'invalid-guid' }));
    vi.stubGlobal('fetch', fetchMock);
    const http = createHttpClient();
    await expect(aiDeleteKnowledgeDocument(http, { ...resource, body: { version: 3 } })).resolves.toBeUndefined();
    expect(JSON.parse(fetchMock.mock.calls[0][1].body)).toEqual({ version: 3 });
    await expect(aiRetryKnowledgeImport(http, { ...resource, operationId: id })).rejects.toThrow(/invalid_ai_knowledge_import_response/);
  });
  it('两条组织候选操作携带分页、身份、语言与取消，只返回最小机构投影', async () => {
    const page = { items: [{ id, code: 'department', name: '研发部' }], page: 2, pageSize: 10, total: '21' };
    const fetchMock = vi.fn().mockImplementation(async () => json(page));
    vi.stubGlobal('fetch', fetchMock);
    const http = createHttpClient();
    http.configureRequestLocale(() => 'zh-CN');
    http.configureAuthentication({ getAccessToken: () => 'access', refresh: async () => false });
    const signal = new AbortController().signal;
    expect(await aiListKnowledgeOrganizationCandidates(http, { knowledgeBaseId: id, page: 2, pageSize: 10 }, signal)).toEqual({ ...page, total: 21 });
    expect(await aiListKnowledgeDocumentOrganizationCandidates(http, { ...resource, page: 2, pageSize: 10 }, signal)).toEqual({ ...page, total: 21 });
    for (const [index, [url, request]] of fetchMock.mock.calls.entries()) {
      const parsed = new URL(url, 'https://example.test');
      expect(parsed.pathname).toBe(`/api/v1/ai/knowledge-bases/${id}${index === 1 ? `/documents/${id}` : ''}/organization-candidates`);
      expect([...parsed.searchParams.entries()]).toEqual([['page', '2'], ['pageSize', '10']]);
      expect(request.signal).toBe(signal);
      expect(new Headers(request.headers).get('authorization')).toBe('Bearer access');
      expect(new Headers(request.headers).get('accept-language')).toBe('zh-CN');
      expect(request.body).toBeUndefined();
    }
  });

  it('Host 空候选接受整数字符串分页元数据', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json({ items: [], page: 1, pageSize: 20, total: '0' })));
    expect(await aiListKnowledgeOrganizationCandidates(createHttpClient(), { knowledgeBaseId: id }))
      .toEqual({ items: [], page: 1, pageSize: 20, total: 0 });
  });

  it('组织候选拒绝损坏的机构 UUID', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json({ items: [{ id: 'invalid', code: 'department', name: '研发部' }], page: 1, pageSize: 20, total: 1 })));
    await expect(aiListKnowledgeDocumentOrganizationCandidates(createHttpClient(), resource))
      .rejects.toThrow(/invalid_paged_result_of_ai_knowledge_organization_candidate_response/);
  });

  it.each([400, 403, 404])('组织候选保留 %s ProblemDetails', async status => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json({ status, code: 'authorization.permission_denied', title: '拒绝' }, status)));
    await expect(aiListKnowledgeOrganizationCandidates(createHttpClient(), { knowledgeBaseId: id }))
      .rejects.toMatchObject({ status, code: 'authorization.permission_denied' });
  });

  it('组织候选的 401 刷新只重试一次并保留分页和取消', async () => {
    const fetchMock = vi.fn().mockResolvedValueOnce(new Response(null, { status: 401 }))
      .mockResolvedValueOnce(json({ items: [], page: 3, pageSize: 7, total: 0 }));
    vi.stubGlobal('fetch', fetchMock);
    const http = createHttpClient();
    let token = 'old';
    http.configureAuthentication({ getAccessToken: () => token, refresh: async () => { token = 'new'; return true; } });
    const signal = new AbortController().signal;
    await aiListKnowledgeDocumentOrganizationCandidates(http, { ...resource, page: 3, pageSize: 7 }, signal);
    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(fetchMock.mock.calls[0][0]).toBe(fetchMock.mock.calls[1][0]);
    expect(fetchMock.mock.calls[1][1].signal).toBe(signal);
    expect(new Headers(fetchMock.mock.calls[1][1].headers).get('authorization')).toBe('Bearer new');
  });

});
