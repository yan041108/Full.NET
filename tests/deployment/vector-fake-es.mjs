import http from 'node:http';

let itemStatus = 429;
let bulkRequests = 0;
const responses = [];

http.createServer(async (request, response) => {
  if (request.url === '/probe') {
    response.setHeader('content-type', 'application/json');
    response.end(JSON.stringify({ itemStatus, bulkRequests, responses }));
    return;
  }
  if (request.url?.startsWith('/status/')) {
    const nextStatus = Number(request.url.slice('/status/'.length));
    if (![201, 400, 429].includes(nextStatus)) {
      response.writeHead(400).end();
      return;
    }
    itemStatus = nextStatus;
    response.writeHead(204).end();
    return;
  }
  if (request.url?.includes('_bulk')) {
    let body = '';
    for await (const chunk of request) body += chunk;
    bulkRequests++;
    // 400 阶段仅固定坏记录失败，后续正常记录可成功，用于检查连续 Offset 是否越过缺口。
    const eventId = body.match(/0199aabb-ccdd-7000-8000-00000000008[123]/)?.[0] ?? 'unknown';
    const responseStatus = itemStatus === 400 && eventId !== '0199aabb-ccdd-7000-8000-000000000082' ? 201 : itemStatus;
    responses.push({ eventId, status: responseStatus });
    const item = { index: { _index: 'fullnet-logs-probe', _id: 'probe', status: responseStatus } };
    if (responseStatus !== 201) {
      item.index.error = { type: responseStatus === 429 ? 'es_rejected_execution_exception' : 'mapper_parsing_exception', reason: 'probe' };
    }
    response.writeHead(200, { 'content-type': 'application/json', 'x-elastic-product': 'Elasticsearch' });
    response.end(JSON.stringify({ took: 1, errors: responseStatus !== 201, items: [item] }));
    return;
  }
  response.writeHead(200, { 'content-type': 'application/json', 'x-elastic-product': 'Elasticsearch' });
  response.end(JSON.stringify({ name: 'fake-es', version: { number: '8.17.0' } }));
}).listen(9200, '0.0.0.0');
