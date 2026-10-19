import fs from 'node:fs';
import {createHash} from 'node:crypto';

/** 分块核对完整原始字节与精确公告行；其他日志行再长也不积累整行字符串。 */
export function logEvidence(file, announcement, chunkBytes = 64 * 1024) {
  if (!Number.isSafeInteger(chunkBytes) || chunkBytes < 1 || chunkBytes > 1024 * 1024)
    throw Error('Invalid bounded log chunk size');
  if (announcement !== undefined && (typeof announcement !== 'string' || !announcement || /[\r\n]/u.test(announcement)))
    throw Error('Expected one exact nonempty announcement line');
  const expected = announcement === undefined ? null : Buffer.from(announcement, 'utf8');
  const buffer = Buffer.allocUnsafe(chunkBytes), hash = createHash('sha256');
  let bytes = 0, occurrences = 0, lineLength = 0, matches = true, lastByte;
  function segment(value) {
    if (matches) {
      if (lineLength + value.length > expected.length + 1) matches = false;
      else for (let i = 0; i < value.length; i++) {
        const index = lineLength + i;
        if (index < expected.length ? value[i] !== expected[index] : index !== expected.length || value[i] !== 13) {
          matches = false; break;
        }
      }
    }
    lineLength += value.length;
    if (value.length) lastByte = value[value.length - 1];
  }
  function finishLine(newline) {
    if (matches && (lineLength === expected.length || newline && lineLength === expected.length + 1 && lastByte === 13)) occurrences++;
    lineLength = 0; matches = true; lastByte = undefined;
  }
  const fd = fs.openSync(file, 'r');
  try {
    while (true) {
      const length = fs.readSync(fd, buffer, 0, buffer.length, null);
      if (!length) break;
      const chunk = buffer.subarray(0, length); hash.update(chunk); bytes += length;
      if (!expected) continue;
      let start = 0;
      while (start < chunk.length) {
        const newline = chunk.indexOf(10, start);
        const end = newline < 0 ? chunk.length : newline;
        segment(chunk.subarray(start, end));
        if (newline < 0) break;
        finishLine(true); start = newline + 1;
      }
    }
    if (expected) finishLine(false);
  } finally { fs.closeSync(fd); }
  return {sha256: hash.digest('hex'), occurrences, bytes};
}
