/** 原生发布先核对实际 SDK；浮动标签或宿主运行时更新不能证明编译器已经修补。 */
export function verifyNativeAotSdkProbe(result, minimumVersion) {
  const stable = value => typeof value === 'string' && /^10\.\d{1,6}\.\d{1,6}$/.test(value);
  const version = typeof result?.stdout === 'string' ? result.stdout.trim() : '';
  if (result?.error || result?.status !== 0 || !stable(version) || !stable(minimumVersion)) {
    throw new Error('Native AOT SDK 探测失败或版本不受支持；保留原发布证据。');
  }
  const actual = version.split('.').map(Number);
  const minimum = minimumVersion.split('.').map(Number);
  const older = actual[1] < minimum[1] || actual[1] === minimum[1] && actual[2] < minimum[2];
  if (older) throw new Error(`Native AOT SDK ${version} 未达到 ${minimumVersion}；保留原发布证据。`);
  return version;
}

/** 使用发布的仓库目录及容器参数探测，保证 global.json 参与同一 SDK 选择。 */
export function probeNativeAotSdk({ run, repositoryRoot, dockerArgs, minimumVersion }) {
  const result = dockerArgs
    ? run('docker', [...dockerArgs, 'dotnet', '--version'], { cwd: repositoryRoot })
    : run('dotnet', ['--version'], { cwd: repositoryRoot });
  return verifyNativeAotSdkProbe(result, minimumVersion);
}
