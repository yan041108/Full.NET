import { createHmac, timingSafeEqual } from 'node:crypto';
import { readFile, stat } from 'node:fs/promises';

/** 秘密通过文件引用读取；允许文本文件最后一个换行，不修剪密钥中的空格。 */
export async function readSigningSecret(file) {
  if (!file || (await stat(file)).size > 1024) throw Error('webhook_consumer.secret_file_invalid');
  const secret = new TextDecoder('utf-8', { fatal: true }).decode(await readFile(file)).replace(/\r?\n$/u, '');
  if (secret.length < 16 || secret.length > 128 || /[\r\n]/u.test(secret)) throw Error('webhook_consumer.secret_file_invalid');
  return secret;
}

/** 验证原始 UTF-8 字节，不能解析再序列化；签名比较不使用字符串相等。 */
export function verifySignature(body, signature, secret) {
  if (typeof signature !== 'string' || !/^[a-f\d]{64}$/iu.test(signature)) return false;
  const actual = Buffer.from(signature, 'hex'), expected = createHmac('sha256', secret).update(body).digest();
  return timingSafeEqual(actual, expected);
}
