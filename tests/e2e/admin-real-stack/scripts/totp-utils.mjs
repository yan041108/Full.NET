import { createHmac } from 'node:crypto';

const BASE32_ALPHABET = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';
const STEP_SECONDS = 30;
const DIGITS = 6;

/** 与 Identity TotpAlgorithm 对齐的 Base32 解码。 */
export function decodeBase32Secret(base32) {
  const normalized = base32.trim().replace(/=+$/, '').toUpperCase();
  const output = [];
  let buffer = 0;
  let bitsLeft = 0;

  for (const ch of normalized) {
    const value = BASE32_ALPHABET.indexOf(ch);
    if (value < 0) {
      throw new Error('TOTP shared secret is not valid Base32.');
    }

    buffer = (buffer << 5) | value;
    bitsLeft += 5;
    if (bitsLeft >= 8) {
      bitsLeft -= 8;
      output.push((buffer >> bitsLeft) & 0xff);
    }
  }

  return Buffer.from(output);
}

/** 计算 RFC 6238 TOTP（HMAC-SHA1、30 秒步长、6 位）。 */
export function computeTotpCode(key, utcMs = Date.now()) {
  const timestep = Math.floor(utcMs / 1000 / STEP_SECONDS);
  const counter = Buffer.alloc(8);
  counter.writeBigInt64BE(BigInt(timestep));
  const hmac = createHmac('sha1', key).update(counter).digest();
  const offset = hmac[hmac.length - 1] & 0x0f;
  const binary =
    ((hmac[offset] & 0x7f) << 24) |
    (hmac[offset + 1] << 16) |
    (hmac[offset + 2] << 8) |
    hmac[offset + 3];
  const otp = binary % 10 ** DIGITS;
  return otp.toString().padStart(DIGITS, '0');
}
