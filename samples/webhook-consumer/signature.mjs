import { createHash, createHmac, timingSafeEqual } from 'node:crypto';
import { readFile, stat } from 'node:fs/promises';

/** 秘密通过文件引用读取；允许文本文件最后一个换行，不修剪密钥中的空格。 */
export async function readSigningSecret(file) {
  if (!file || (await stat(file)).size > 1024) throw Error('webhook_consumer.secret_file_invalid');
  const secret = new TextDecoder('utf-8', { fatal: true }).decode(await readFile(file)).replace(/\r?\n$/u, '');
  if (secret.length < 16 || secret.length > 128 || /[\r\n]/u.test(secret)) throw Error('webhook_consumer.secret_file_invalid');
  return secret;
}

const guid = value => typeof value === 'string' && /^[a-f\d]{8}-[a-f\d]{4}-[a-f\d]{4}-[a-f\d]{4}-[a-f\d]{12}$/iu.test(value) && value !== '00000000-0000-0000-0000-000000000000';
/** 元数据先规范化，避免重复头、换行、前导零或不同 UUID 表示引入签名歧义。 */
export function signatureMetadata(value) {
  if (!value || value.version !== '1' || ![value.keyId,value.eventId,value.deliveryId].every(guid)
    || typeof value.timestamp !== 'string' || !/^(?:0|[1-9]\d{0,11})$/u.test(value.timestamp)
    || typeof value.eventType !== 'string' || !/^[\x21-\x7e]{1,128}$/u.test(value.eventType)) return undefined;
  return { version:'1',keyId:value.keyId.toLowerCase(),eventId:value.eventId.toLowerCase(),deliveryId:value.deliveryId.toLowerCase(),eventType:value.eventType,timestamp:value.timestamp };
}
/** 对原文摘要和每次投递的完整身份签名，不改变正文或业务事件时间。 */
export function computeSignature(body, secret, metadata) {
  const stamp = signatureMetadata(metadata); if (!stamp) throw Error('webhook_consumer.metadata_invalid');
  const canonical = ['fullnet.webhook/1',stamp.timestamp,stamp.keyId,stamp.eventId,stamp.deliveryId,stamp.eventType,createHash('sha256').update(body).digest('hex').toUpperCase()].join('\n');
  return createHmac('sha256',secret).update(canonical,'utf8').digest('hex').toUpperCase();
}
/** 明确允许的 KeyId 与时间窗独立核对；恒定时间比较后再检查一次窗口。 */
export function verifySignature(body, signature, secret, metadata, { keyId, clock = Date.now, maxAgeSeconds = 300 } = {}) {
  const stamp = signatureMetadata(metadata), fresh = () => { const now = clock(); return Number.isFinite(now) && Math.abs(Math.floor(now/1000)-Number(stamp?.timestamp)) <= maxAgeSeconds; };
  if (!stamp || !guid(keyId) || stamp.keyId !== keyId.toLowerCase() || !Number.isInteger(maxAgeSeconds) || maxAgeSeconds < 1 || maxAgeSeconds > 3600
    || typeof signature !== 'string' || !/^[a-f\d]{64}$/iu.test(signature) || !fresh()) return false;
  const actual = Buffer.from(signature,'hex'),expected = Buffer.from(computeSignature(body,secret,stamp),'hex');
  return timingSafeEqual(actual,expected) && fresh();
}
