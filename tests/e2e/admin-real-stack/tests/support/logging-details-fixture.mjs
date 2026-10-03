import { execFile } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import { promisify } from 'node:util';

const exec = promisify(execFile);

// 只向本次 Development Testcontainers 栈写入独立场景数据，不改变生产播种或采集开关。
export async function createLoggingDetailsFixture() {
  const state = JSON.parse(await readFile(new URL('../../.stack-state.json', import.meta.url), 'utf8'));
  if (state.stackProfile !== 'development' || !/^[a-f0-9]{64}$/.test(state.containerId)
      || !['SqlServer', 'MySql'].includes(state.databaseProvider)) throw new Error('需要隔离的 Development 测试栈');
  const inspected = await exec('docker', ['inspect', state.containerId]);
  const container = JSON.parse(inspected.stdout)[0];
  if (container.Config.Labels?.['org.testcontainers'] !== 'true') throw new Error('拒绝写入非 Testcontainers 数据库');
  const mysql = state.databaseProvider === 'MySql';
  const expectedImage = mysql ? 'mysql:8.0' : 'mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04';
  if (container.Config.Image !== expectedImage || !['localhost', '127.0.0.1'].includes(new URL(state.apiUrl).hostname)) {
    throw new Error('拒绝写入非本地真实栈数据库');
  }
  async function sql(statement) {
    const args = mysql
      ? ['exec', '-e', 'MYSQL_PWD=FullNet_Test!123', state.containerId, 'mysql', '--user=fullnet', '--database=fullnet', '--execute', statement]
      : ['exec', '-e', 'SQLCMDPASSWORD=FullNet_Test!123', state.containerId, '/opt/mssql-tools18/bin/sqlcmd', '-C', '-b', '-I', '-U', 'sa', '-d', 'master', '-Q', statement];
    try {
      await exec('docker', args, { timeout: 20000, maxBuffer: 65536 });
    } catch (error) {
      // 不输出包含测试凭据和完整详情 SQL 的进程命令。
      throw new Error(`隔离日志场景 SQL 失败：${String(error.stdout || error.stderr || error.code).slice(0, 1500)}`);
    }
  }
  function id() {
    const bytes = randomBytes(16);
    bytes.writeUIntBE(Date.now(), 0, 6);
    bytes[6] = (bytes[6] & 15) | 112;
    bytes[8] = (bytes[8] & 63) | 128;
    const h = bytes.toString('hex');
    return `${h.slice(0, 8)}-${h.slice(8, 12)}-${h.slice(12, 16)}-${h.slice(16, 20)}-${h.slice(20)}`;
  }
  const ids = Array.from({ length: 23 }, id);
  const prefix = `/api/e2e/logging-${ids[0]}`;
  const actionPrefix = `e2e.logging.${ids[0]}`;
  const key = value => mysql ? `UNHEX('${value.replaceAll('-', '')}')` : `'${value}'`;
  const date = value => `'${mysql ? value.toISOString().replace('T', ' ').slice(0, 23) : value.toISOString()}'`;
  const context = {
    schemaVersion: 1, clientIp: '192.0.2.19', clientPort: 43210, serverIp: '192.0.2.20', serverPort: 5149,
    requestCaptureState: 'captured', requestSummary: { fromUtc: new Date(Date.now() - 3600000).toISOString(), toUtc: new Date().toISOString(), statusCode: 200, succeeded: true },
    responseCaptureState: 'captured', responseSummary: { rowCount: 37, truncated: false, includesSensitiveFields: false }
  };
  const encoded = Buffer.from(JSON.stringify(context), mysql ? 'utf8' : 'utf16le').toString('hex');
  const json = mysql ? `CONVERT(UNHEX('${encoded}') USING utf8mb4)` : `CONVERT(nvarchar(max),0x${encoded})`;
  const values = ids.map((value, index) => `(${key(value)},${date(new Date(Date.now() - index * 1000))},'${actionPrefix}.${index}','POST','${prefix}/${index}',200,3,1,${index === 2 ? 'NULL' : json},${index === 2 ? 'NULL' : date(new Date(Date.now() + (index === 1 ? -60000 : 900000)))})`);
  try {
    // 限制每条 CLI 命令长度，避免 Windows 进程参数上限截断场景 SQL。
    for (let offset = 0; offset < values.length; offset += 5) {
      await sql(`INSERT INTO fn_auditing_operation_log (Id,OccurredAtUtc,ActionKey,HttpMethod,RequestPath,StatusCode,DurationMs,Succeeded,ContextJson,DetailsExpiresAtUtc) VALUES ${values.slice(offset, offset + 5).join(',')};`);
    }
  } catch (error) {
    await sql(`DELETE FROM fn_auditing_operation_log WHERE Id IN (${ids.map(key).join(',')});`).catch(() => {});
    throw error;
  }
  return { ids, prefix, actionPrefix, async dispose() { await sql(`DELETE FROM fn_auditing_operation_log WHERE Id IN (${ids.map(key).join(',')});`); } };
}
