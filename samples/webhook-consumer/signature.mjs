import { createHash, createHmac, timingSafeEqual } from 'node:crypto';
import { readFile, stat } from 'node:fs/promises';
import path from 'node:path';

/** 秘密通过文件引用读取；允许文本文件最后一个换行，不修剪密钥中的空格。 */
export async function readSigningSecret(file) {
  if (!file || (await stat(file)).size > 1024) throw Error('webhook_consumer.secret_file_invalid');
  const secret = new TextDecoder('utf-8', { fatal: true }).decode(await readFile(file)).replace(/\r?\n$/u, '');
  if (secret.length < 16 || secret.length > 128 || /[\r\n]/u.test(secret)) throw Error('webhook_consumer.secret_file_invalid');
  return secret;
}

/** 轮换配置只保存秘密文件引用；最多两个允许键，旧键必须有明确且最多一小时的剩余接收期限。 */
export async function readSigningKeys(file, clock = Date.now) {
  if (!file || (await stat(file)).size > 4096) throw Error('webhook_consumer.keys_file_invalid');
  const value = JSON.parse(new TextDecoder('utf-8',{fatal:true}).decode(await readFile(file)));
  if (!value || !Array.isArray(value.keys) || Object.keys(value).some(key=>key!=='keys') || value.keys.length<1 || value.keys.length>2) throw Error('webhook_consumer.keys_file_invalid');
  const entries=[];
  for(const item of value.keys){
    if (!item || Object.keys(item).some(key=>!['keyId','secretFile','acceptUntilUtc'].includes(key)) || typeof item.secretFile!=='string' || !path.isAbsolute(item.secretFile)) throw Error('webhook_consumer.keys_file_invalid');
    entries.push({keyId:item.keyId,secret:await readSigningSecret(item.secretFile),acceptUntilUtc:item.acceptUntilUtc});
  }
  return normalizeSigningKeys(entries,clock);
}

/** 启动时复制受信配置，外部对象后续变化不能隐式延长旧键期限。 */
export function normalizeSigningKeys(entries,clock=Date.now){
  const now=clock();if(!Number.isFinite(now)||!Array.isArray(entries)||entries.length<1||entries.length>2)throw Error('webhook_consumer.keys_config_invalid');
  const keys=entries.map(item=>{
    const until=item?.acceptUntilUtc===null?Infinity:typeof item?.acceptUntilUtc==='string'&&/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,3})?Z$/u.test(item.acceptUntilUtc)?Date.parse(item.acceptUntilUtc):NaN;
    if(!guid(item?.keyId)||typeof item.secret!=='string'||item.secret.length<16||item.secret.length>128||until!==Infinity&&(!Number.isFinite(until)||until>now+3_600_000))throw Error('webhook_consumer.keys_config_invalid');
    return Object.freeze({keyId:item.keyId.toLowerCase(),secret:item.secret,acceptUntilUtc:item.acceptUntilUtc,acceptUntilMilliseconds:until});
  });
  if(new Set(keys.map(key=>key.keyId)).size!==keys.length||keys.filter(key=>key.acceptUntilMilliseconds===Infinity).length!==1)throw Error('webhook_consumer.keys_config_invalid');
  return Object.freeze(keys);
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
