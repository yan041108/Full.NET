import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import assert from 'node:assert/strict';
import { renderGeneratedFiles } from './generate-fullnet-client.mjs';
import { validateClientGenerationReadiness } from './validate-client-generation-readiness.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
/** 以原模块契约及事件 Schema 为权威，不复制管理端实现或改写生成器的共享默认输出。 */
export async function generateIntegrationSdk({ check = false } = {}) {
  const document = JSON.parse(await readFile(path.join(root, 'contracts/openapi/webhook-subscriptions-v1.json'), 'utf8'));
  for (const item of Object.values(document.paths)) for (const operation of Object.values(item)) operation.tags = ['Webhooks'];
  const event = JSON.parse(await readFile(path.join(root, 'contracts/webhooks/workflow-instance-completed-v1.schema.json'), 'utf8'));
  document.components.schemas.WorkflowCompletedWebhookData = event.$defs.WorkflowCompletedWebhookData;
  const envelope = structuredClone(event); delete envelope.$defs; delete envelope.$schema; delete envelope.$id;
  envelope.properties.Data = { $ref: '#/components/schemas/WorkflowCompletedWebhookData' };
  document.components.schemas.WorkflowCompletedWebhookEvent = envelope;
  const violations = validateClientGenerationReadiness(document, { publicOperationIds: [] });
  if (violations.length) throw Error(violations.join('\n'));
  const files = renderGeneratedFiles(document), directory = path.join(root, 'packages/integration-sdk/src/generated');
  if (!check) await mkdir(directory, { recursive: true });
  for (const [name, contents] of Object.entries(files)) {
    const file = path.join(directory, name);
    if (check) assert.equal((await readFile(file, 'utf8')).replaceAll('\r\n','\n'), contents.replaceAll('\r\n','\n'), `Generated SDK drift: ${name}`);
    else await writeFile(file, contents);
  }
  return Object.keys(files);
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  await generateIntegrationSdk({ check: process.argv.includes('--check') }); console.log('Integration SDK generation verified.');
}
