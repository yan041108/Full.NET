// 这些文件仅供仓库治理/客户端生成；其他 contracts 文件可能是嵌入资源，必须继续冻结。
export const buildOnlyContractIndexes = Object.freeze([
  'contracts/architecture/global-sql-statements.json',
  'contracts/openapi/client-generation-manifest-v1.json',
  'contracts/openapi/fullnet-client-v1.openapi.json',
  'contracts/database/object-comments.json',
]);

// 机器码阶段使用冻结 IL；工具索引仍登记于完整源码清单和变化报告。
export function nativeSourceInputs(entries, frozenManagedResources = [], executableName = 'Full.NET.Host.Api') {
  validateManagedResourceScope(frozenManagedResources);
  if (!['Full.NET.Host.Api', 'Full.NET.Host.Worker', 'Full.NET.Host.Migrator', 'Full.NET.Host.LogConsumer'].includes(executableName))
    throw new Error('未知宿主不能复用 Native 输入策略。');
  // 新角色的专属源码不能反向扩大已有 API/Worker 任务的冻结范围；其他 C# 仍由真实托管 IL 摘要约束。
  const roleSource = executableName === 'Full.NET.Host.Migrator'
    ? /Full\.NET\.(?:Seeding|Hosting\.Migrator|Migrations\.DbUp)|src\/Hosts\/Full\.NET\.Host\.Migrator\/|Full\.NET\.Modules\.(?:Identity|Tenancy|Settings|Regions)\/(?:\w+Module\.cs|Persistence\/\w*DapperAotMaterializerContributor(?:\.Migrator)?\.cs)/u
    : executableName === 'Full.NET.Host.LogConsumer' ? /Full\.NET\.LogConsumer\/|src\/Hosts\/Full\.NET\.Host\.LogConsumer\//u : null;
  return entries.filter(entry => !buildOnlyContractIndexes.includes(entry.file)
    && !frozenManagedResources.includes(entry.file)
    && (!entry.file.endsWith('.cs') || /Messaging|Full\.NET\.Data\.(?:Dapper|Abstractions)|src\/Hosts\/Full\.NET\.Host\.(?:Api|Worker)\//u.test(entry.file)
      || roleSource?.test(entry.file)));
}

// 只允许本任务范围之外的AI资源；调用方必须提供实际EmbeddedResource及冻结DLL证据，默认不排除任何资源。
function validateManagedResourceScope(files) {
  if (!Array.isArray(files) || files.some(file => typeof file !== 'string'
      || !/^src\/Modules\/Full\.NET\.Modules\.Ai\/[^\r\n]*\.resx$/u.test(file)
      || file.split('/').some(segment => segment === '..' || segment === '.')))
    throw new Error('冻结托管资源范围不合法，不能排除Workflow、共享或其它资源。');
}

// 同时检查ILC参数和实际发布内容；一旦资源作为原始内容使用，即使Link改名也不能作为已冻结IL处理。
export function assertManagedResourcesNotRuntimeInputs(ilcResponse, publishedFiles, frozenManagedResources, executableName = 'Full.NET.Host.Api') {
  validateManagedResourceScope(frozenManagedResources);
  assertToolIndexesNotRuntimeInputs(ilcResponse, publishedFiles, executableName);
  const inputs = [ilcResponse, JSON.stringify(publishedFiles)].join('\n').replaceAll('\\', '/').toLowerCase();
  for (const file of frozenManagedResources)
    if (inputs.includes(file.toLowerCase()) || inputs.includes(file.split('/').at(-1).toLowerCase()))
      throw new Error(`冻结托管资源已进入实际原生/发布输入，必须重新评估：${file}`);
}

// 精确排除必须由实际 ILC 参数与发布内容共同确认；未来变为运行时内容时拒绝复用。
export function assertToolIndexesNotRuntimeInputs(ilcResponse, publishedFiles, executableName = 'Full.NET.Host.Api') {
  if (!['Full.NET.Host.Api', 'Full.NET.Host.Worker', 'Full.NET.Host.Migrator', 'Full.NET.Host.LogConsumer'].includes(executableName) || !ilcResponse.includes(`${executableName}.dll`) || !Array.isArray(publishedFiles) || publishedFiles.length === 0)
    throw new Error('缺少实际 ILC 或 ResolvedFileToPublish 清单，不能排除工具索引。');
  const inputs = [ilcResponse, JSON.stringify(publishedFiles)].join('\n').replaceAll('\\', '/').toLowerCase();
  for (const file of buildOnlyContractIndexes)
    if (inputs.includes(file.toLowerCase()) || inputs.includes(file.split('/').at(-1).toLowerCase()))
      throw new Error(`工具索引已进入实际原生/发布输入，必须重新评估：${file}`);
}
