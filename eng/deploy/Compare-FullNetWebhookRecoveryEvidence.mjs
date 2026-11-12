import { lstatSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { pathToFileURL } from 'node:url';

const uuid = /^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/iu;
const sha256 = /^[a-f0-9]{64}$/iu;
const identityFields = ['tenantId', 'subscriptionId', 'eventId', 'deliveryId'];
const allowedReceiptStates = new Set(['applied', 'unknown']);

function readEvidence(path, label) {
  const info = lstatSync(path);
  if (!info.isFile() || info.size > 4 * 1024 * 1024) throw new Error(`${label} must be a regular JSON file of at most 4 MiB`);
  try { return JSON.parse(readFileSync(path, 'utf8')); }
  catch { throw new Error(`${label} must contain valid JSON`); }
}

function indexEvidence(records, label) {
  if (!Array.isArray(records)) throw new Error(`${label} must be an array`);
  const index = new Map();
  for (const [position, record] of records.entries()) {
    if (!record || !identityFields.every(field => typeof record[field] === 'string' && uuid.test(record[field])
      && record[field] !== '00000000-0000-0000-0000-000000000000')
      || typeof record.payloadSha256 !== 'string' || !sha256.test(record.payloadSha256)) {
      throw new Error(`${label} row ${position + 1} has an invalid identity or payload hash`);
    }
    if (label === 'Receiver receipts' && !allowedReceiptStates.has(record.outcome)) {
      throw new Error(`${label} row ${position + 1} has an invalid outcome`);
    }
    const identity = Object.fromEntries(identityFields.map(field => [field, record[field].toLowerCase()]));
    if (index.has(identity.deliveryId)) throw new Error(`${label} contains a duplicate delivery ID`);
    index.set(identity.deliveryId, { ...identity, payloadSha256: record.payloadSha256.toLowerCase(), outcome: record.outcome });
  }
  return index;
}

/** 只对账已知范围。缺少回执时不能推断接收方未提交。 */
export function compareFullNetWebhookRecoveryEvidence(deliveries, receipts) {
  const restored = indexEvidence(deliveries, 'Restored deliveries');
  const receiver = indexEvidence(receipts, 'Receiver receipts');
  const confirmedApplied = [];
  const unresolved = [];
  for (const delivery of restored.values()) {
    const receipt = receiver.get(delivery.deliveryId);
    const scope = Object.fromEntries(identityFields.map(field => [field, delivery[field]]));
    if (!receipt) unresolved.push({ ...scope, reason: 'receipt-missing' });
    else if (identityFields.some(field => delivery[field] !== receipt[field]) || delivery.payloadSha256 !== receipt.payloadSha256) {
      unresolved.push({ ...scope, reason: 'identity-or-payload-mismatch' });
    } else if (receipt.outcome === 'unknown') unresolved.push({ ...scope, reason: 'receiver-outcome-unknown' });
    else confirmedApplied.push(scope);
  }
  // 接收方独有的回执可能代表恢复点之后已提交的外部效果，同样需要人工核对。
  for (const receipt of receiver.values()) {
    if (!restored.has(receipt.deliveryId)) {
      const scope = Object.fromEntries(identityFields.map(field => [field, receipt[field]]));
      unresolved.push({ ...scope, reason: 'restored-delivery-missing' });
    }
  }
  return { confirmedApplied, unresolved, unresolvedExternalOutcomes: unresolved.length };
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  try {
    if (process.argv.length !== 4) throw new Error('Usage: node Compare-FullNetWebhookRecoveryEvidence.mjs <restored-deliveries.json> <receiver-receipts.json>');
    const deliveries = readEvidence(process.argv[2], 'Restored deliveries');
    const receipts = readEvidence(process.argv[3], 'Receiver receipts');
    const result = compareFullNetWebhookRecoveryEvidence(deliveries, receipts);
    console.log(JSON.stringify(result));
    if (result.unresolvedExternalOutcomes !== 0) process.exitCode = 1;
  } catch (error) {
    console.error(error.message);
    process.exitCode = 1;
  }
}
