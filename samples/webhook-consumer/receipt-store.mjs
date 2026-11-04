import { open, mkdir, unlink } from 'node:fs/promises';
import path from 'node:path';

const guid = value => typeof value === 'string' && /^[a-f\d]{8}-[a-f\d]{4}-[a-f\d]{4}-[a-f\d]{4}-[a-f\d]{12}$/iu.test(value) && value !== '00000000-0000-0000-0000-000000000000';
export { guid as isGuid };

/** 单进程样例账本：独占文件锁、串行追加并 flush，完整回执持久化后才确认 HTTP。 */
export async function openReceiptStore(file, { maxReceipts = 10_000, maxBytes = 8 * 1024 * 1024 } = {}) {
  if (!path.isAbsolute(file) || !Number.isSafeInteger(maxReceipts) || maxReceipts < 1 || !Number.isSafeInteger(maxBytes) || maxBytes < 1) throw Error('webhook_consumer.store_config_invalid');
  await mkdir(path.dirname(file), { recursive: true, mode: 0o700 });
  const lock = await open(file + '.lock', 'wx', 0o600);
  let ledger;
  try {
    await lock.writeFile(String(process.pid)); await lock.sync();
    ledger = await open(file, 'a+', 0o600);
    let size = (await ledger.stat()).size;
    if (size > maxBytes) throw Error('webhook_consumer.store_full');
    const contents = await ledger.readFile('utf8'), rows = new Map();
    // 半条记录或矛盾记录须人工核对，不能截断并遗忘已确认事件。
    if (contents && !contents.endsWith('\n')) throw Error('webhook_consumer.store_corrupt');
    for (const line of contents.split('\n').filter(Boolean)) {
      const row = JSON.parse(line);
      if (row.schemaVersion !== 1 || !guid(row.eventId) || !guid(row.tenantId) || !guid(row.deliveryId) || !guid(row.result?.instanceId)
        || row.eventType !== 'fullnet.workflow.instance.completed' || !/^[a-f\d]{64}$/u.test(row.payloadDigest)
        || !Number.isFinite(Date.parse(row.receivedAtUtc)) || row.result?.status !== 'recorded' || rows.has(row.eventId.toLowerCase())) throw Error('webhook_consumer.store_corrupt');
      rows.set(row.eventId.toLowerCase(), row);
      if (rows.size > maxReceipts) throw Error('webhook_consumer.store_full');
    }
    let queue = Promise.resolve(), poisoned = false, closed = false;
    return {
      accept(receipt) {
        const operation = queue.then(async () => {
          if (closed || poisoned) throw Error('webhook_consumer.store_unavailable');
          const key = receipt.eventId.toLowerCase(), previous = rows.get(key);
          if (previous) {
            if (previous.tenantId !== receipt.tenantId || previous.eventType !== receipt.eventType || previous.payloadDigest !== receipt.payloadDigest) return { conflict: true };
            return { receipt: previous, duplicate: true };
          }
          const bytes = Buffer.from(JSON.stringify(receipt) + '\n');
          if (rows.size >= maxReceipts || size + bytes.length > maxBytes) throw Error('webhook_consumer.store_full');
          try {
            let offset = 0;
            while (offset < bytes.length) { const written = await ledger.write(bytes, offset, bytes.length - offset); if (!written.bytesWritten) throw Error(); offset += written.bytesWritten; }
            await ledger.sync();
          } catch { poisoned = true; throw Error('webhook_consumer.store_unavailable'); }
          size += bytes.length; rows.set(key, receipt);
          return { receipt, duplicate: false };
        });
        queue = operation.catch(() => {});
        return operation;
      },
      async close() { if (closed) return; closed = true; await queue; await ledger.close(); await lock.close(); await unlink(file + '.lock'); }
    };
  } catch (error) { await ledger?.close(); await lock.close(); await unlink(file + '.lock'); throw error; }
}
