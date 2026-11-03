/**
 * Host.Api linux-x64 Native AOT 发布契约的单一权威源。
 * 治理测试、架构测试与 publish 脚本必须与此保持一致。
 */
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  '../..'
);

export const apiNativeAotPublishContract = {
  repositoryRoot,
  projectRelativePath: 'src/Hosts/Full.NET.Host.Api/Full.NET.Host.Api.csproj',
  outputRelativeDir: 'artifacts/native-aot/linux-x64/publish',
  manifestRelativePath: 'artifacts/native-aot/linux-x64/publish-manifest.json',
  publishLogRelativePath: 'artifacts/native-aot/linux-x64/publish.log',
  executableName: 'Full.NET.Host.Api',
  runtimeIdentifier: 'linux-x64',
  minimumSdkVersion: '10.0.402',
  sdkImage: 'mcr.microsoft.com/dotnet/sdk:10.0.402',
  publishSdkImage: 'fullnet-native-aot-publish-sdk:10.0.402',
  sdkImageLabel: 'fullnet-native-aot-publish-sdk:10.0.402 (Debian SDK 10.0.402 + clang/zlib for Native AOT)',
  minimumExecutableBytes: 8_000_000,
  // API 可达闭包的实际 ILC 在 12 GiB 容器内内存不足；扩容仍经过正式资源准入。
  buildBudget: Object.freeze({ cpu: 2, memoryGiB: 16 }),
  publishMsBuildProperties: {
    Configuration: 'Release',
    RuntimeIdentifier: 'linux-x64',
    SelfContained: 'true',
    FullNetPublishMode: 'NativeAot',
    RuntimeFrameworkVersion: '10.0.13',
  },
};

export const workerNativeAotPublishContract = {
  ...apiNativeAotPublishContract,
  buildBudget: Object.freeze({ cpu: 2, memoryGiB: 12 }),
  projectRelativePath: 'src/Hosts/Full.NET.Host.Worker/Full.NET.Host.Worker.csproj',
  outputRelativeDir: 'artifacts/native-aot/worker/linux-x64/publish',
  manifestRelativePath: 'artifacts/native-aot/worker/linux-x64/publish-manifest.json',
  publishLogRelativePath: 'artifacts/native-aot/worker/linux-x64/publish.log',
  executableName: 'Full.NET.Host.Worker',
};

export const migratorNativeAotPublishContract = {
  ...apiNativeAotPublishContract,
  // 最小迁移闭包单独限额；内存不足必须保留失败，不能缩减迁移或播种功能。
  buildBudget: Object.freeze({ cpu: 2, memoryGiB: 8 }),
  projectRelativePath: 'src/Hosts/Full.NET.Host.Migrator/Full.NET.Host.Migrator.csproj',
  outputRelativeDir: 'artifacts/native-aot/migrator/linux-x64/publish',
  manifestRelativePath: 'artifacts/native-aot/migrator/linux-x64/publish-manifest.json',
  publishLogRelativePath: 'artifacts/native-aot/migrator/linux-x64/publish.log',
  executableName: 'Full.NET.Host.Migrator',
};

export const logConsumerNativeAotPublishContract = {
  ...apiNativeAotPublishContract,
  buildBudget: Object.freeze({ cpu: 2, memoryGiB: 12 }),
  projectRelativePath: 'src/Hosts/Full.NET.Host.LogConsumer/Full.NET.Host.LogConsumer.csproj',
  outputRelativeDir: 'artifacts/native-aot/log-consumer/linux-x64/publish',
  manifestRelativePath: 'artifacts/native-aot/log-consumer/linux-x64/publish-manifest.json',
  publishLogRelativePath: 'artifacts/native-aot/log-consumer/linux-x64/publish.log',
  executableName: 'Full.NET.Host.LogConsumer',
};

/** 角色必须显式选择；未知值失败关闭，不能误用 API 的发布目录或证明。 */
export function nativeAotPublishContract(host = 'api') {
  const contracts = { api: apiNativeAotPublishContract, worker: workerNativeAotPublishContract,
    migrator: migratorNativeAotPublishContract, 'log-consumer': logConsumerNativeAotPublishContract };
  if (!Object.hasOwn(contracts, host)) throw new Error(`未知 Native 宿主：${host}`);
  return contracts[host];
}

export function resolveRepositoryPath(relativePath) {
  return path.join(apiNativeAotPublishContract.repositoryRoot, relativePath);
}
