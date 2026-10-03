import assert from 'node:assert/strict';
import { writeFileSync } from 'node:fs';

// 复用真实授权的 Host 会话切入 local 租户；不伪造主体，也不在业务请求提供 TenantId。
export async function verifyApplicationCrudTenantHttp(baseUrl, { hostAccessToken, logPath, request = fetch, onCreatedProduct }) {
  assert.ok(typeof hostAccessToken === 'string' && hostAccessToken.trim(), 'authenticated Host token is required');
  const secrets = [hostAccessToken];
  const redact = (value) => secrets.reduce((text, secret) => text.replaceAll(secret, '[REDACTED]'), String(value));
  const evidence = { completed: false, responses: [] };
  const execute = async (stage, path, method, token, body, status, code, credentialResponse = false) => {
    const entry = { stage, method, url: baseUrl + path };
    evidence.responses.push(entry);
    try {
      const response = await request(entry.url, {
        method, headers: { Authorization: `Bearer ${token}`, Origin: 'http://localhost', 'Content-Type': 'application/json' },
        body: body === undefined ? undefined : JSON.stringify(body),
        redirect: 'error', signal: AbortSignal.timeout(15_000),
      });
      entry.status = response.status;
      entry.contentType = response.headers.get('content-type');
      const text = await response.text();
      // 签发响应可能包含未知的新令牌，整个正文都不写日志，也不放入失败断言。
      entry.body = credentialResponse ? '[credential response omitted]' : redact(text);
      assert.equal(entry.status, status, `${stage}: ${entry.body}`);
      let result;
      try {
        result = JSON.parse(text);
      } catch (error) {
        // JSON 解析器可能摘录原文；签发响应中的未知凭据不能进入异常消息。
        if (credentialResponse) throw new Error('invalid context credential response JSON');
        throw error;
      }
      if (code) {
        assert.match(entry.contentType ?? '', /^application\/problem\+json(?:;|$)/iu);
        assert.equal(result.status, status);
        assert.equal(result.code, code);
      }
      return result;
    } catch (error) {
      entry.error = redact(error instanceof Error ? error.message : error);
      throw new Error(entry.error);
    }
  };
  try {
    const tenants = await execute('available', '/api/v1/tenancy/available', 'GET', hostAccessToken, undefined, 200);
    assert.ok(Array.isArray(tenants), 'available tenants must be an array');
    const local = tenants.filter((tenant) => tenant.identifier === 'local');
    assert.equal(local.length, 1, 'expected exactly one Development local tenant');
    const tenantId = local[0].id;
    assert.match(tenantId, /^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/iu, 'invalid tenant UUID v7');
    const switched = await execute('context', '/api/v1/tenancy/context', 'PUT', hostAccessToken, { tenantId }, 200, undefined, true);
    assert.ok(typeof switched.accessToken === 'string' && switched.accessToken.trim(), 'context response missing token');
    const tenantToken = switched.accessToken;
    secrets.push(tenantToken);
    assert.equal(switched.context?.tenantId, tenantId, 'context response selected a different tenant');
    assert.equal(switched.context?.identifier, 'local');
    assert.equal(switched.context?.scope, `tenant:${tenantId.replaceAll('-', '')}`, 'context response is not tenant scope');
    const base = '/api/v1/catalog/products';
    const originalName = 'Application tenant CRUD probe';
    const updatedName = 'Updated application tenant CRUD probe';
    const created = await execute('create', base + '/', 'POST', tenantToken, { name: originalName }, 201);
    assert.match(created.id, /^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/iu, 'generated Id must be UUID v7');
    const item = base + '/' + created.id;
    const check = (product, name, version) => {
      assert.equal(product.id, created.id);
      assert.equal(product.tenantId, tenantId, 'business row has unexpected tenant');
      assert.equal(product.name, name, 'business row changed unexpectedly');
      assert.equal(product.version, version, 'version must retain the generated string contract');
    };
    check(created, originalName, '1');
    // 创建结果完成租户与版本核对后，才移交可信上下文给生成客户端读取。
    if (onCreatedProduct) await onCreatedProduct({ tenantAccessToken: tenantToken,
      product: { id: created.id, tenantId, name: originalName, version: '1' } });
    check(await execute('read', item, 'GET', tenantToken, undefined, 200), originalName, '1');
    const listed = await execute('list', base + '/?page=1&pageSize=5', 'GET', tenantToken, undefined, 200);
    assert.ok(Array.isArray(listed.items), 'list response missing items');
    const matches = listed.items.filter((product) => product.id === created.id);
    assert.equal(matches.length, 1, 'created product not found exactly once');
    check(matches[0], originalName, '1');
    check(await execute('update', item, 'PUT', tenantToken, { name: updatedName, version: '1' }, 200), updatedName, '2');
    // 409 本身不能证明乐观锁保护：冲突后读取必须确认已提交版本和内容未变。
    await execute('stale-update', item, 'PUT', tenantToken, { name: 'Stale update must be rejected', version: '1' }, 409, 'catalog.products.version_conflict');
    check(await execute('read-after-stale-update', item, 'GET', tenantToken, undefined, 200), updatedName, '2');
    await execute('stale-delete', item + '/delete', 'POST', tenantToken, { version: '1' }, 409, 'catalog.products.version_conflict');
    check(await execute('read-after-stale-delete', item, 'GET', tenantToken, undefined, 200), updatedName, '2');
    check(await execute('delete', item + '/delete', 'POST', tenantToken, { version: '2' }, 200), updatedName, '2');
    await execute('read-deleted', item, 'GET', tenantToken, undefined, 404, 'catalog.products.not_found');
    const afterDelete = await execute('list-after-delete', base + '/?page=1&pageSize=5', 'GET', tenantToken, undefined, 200);
    assert.ok(Array.isArray(afterDelete.items), 'post-delete list response missing items');
    assert.equal(afterDelete.items.some((product) => product.id === created.id), false, 'deleted product remains in list');
    evidence.completed = true;
    evidence.tenantId = tenantId;
    // 只在内存移交最新会话给下一验收阶段；最终报告不序列化此返回对象。
    return { businessRequests: 11, versionConflicts: 2, deleted: true, tenantId, tenantAccessToken: tenantToken };
  } catch (error) {
    evidence.error = redact(error instanceof Error ? error.message : error);
    throw new Error(evidence.error);
  } finally {
    writeFileSync(logPath, redact(JSON.stringify(evidence, null, 2)));
  }
}
