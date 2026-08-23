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
  executableName: 'Full.NET.Host.Api',
  runtimeIdentifier: 'linux-x64',
  sdkImage: 'mcr.microsoft.com/dotnet/sdk:10.0',
  sdkImageLabel: 'mcr.microsoft.com/dotnet/sdk:10.0 (Debian-based official SDK)',
  minimumExecutableBytes: 8_000_000,
  publishMsBuildProperties: {
    Configuration: 'Release',
    RuntimeIdentifier: 'linux-x64',
    SelfContained: 'true',
    FullNetPublishMode: 'NativeAot',
  },
};

export function resolveRepositoryPath(relativePath) {
  return path.join(apiNativeAotPublishContract.repositoryRoot, relativePath);
}
