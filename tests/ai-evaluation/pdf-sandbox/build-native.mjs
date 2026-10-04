import assert from 'node:assert/strict';
import { spawn, execFileSync } from 'node:child_process';
import { randomUUID, createHash } from 'node:crypto';
import { mkdirSync, readFileSync, writeFileSync, copyFileSync, existsSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
const output = path.join(root, 'artifacts/ai-evaluation/r05e2');
const sdk = 'fullnet-native-aot-publish-sdk@sha256:963c5de8c62d5c1cf3bf3c38ade4ef44fec541b7cbc735dbcec0ba9425ad0fc3';
mkdirSync(output, { recursive: true });
const docker = args => execFileSync('docker', args, { windowsHide: true, encoding: 'utf8', timeout: 30000, maxBuffer: 65536 });

async function publish(role, project) {
  const name = `fullnet-pdf-build-${randomUUID()}`;
  const directory = path.dirname(project);
  const lock = path.join(root, directory, 'linux.packages.lock.json');
  const evidenceLock = path.join(output, `${role}.linux.packages.lock.json`);
  if (existsSync(lock)) copyFileSync(lock, evidenceLock);
  const props = `-p:UseArtifactsOutput=true -p:ArtifactsPath=/tmp/pdf-build -p:NuGetLockFilePath=/evidence/${role}.linux.packages.lock.json`;
  const command = `set -euo pipefail; dotnet --version; dotnet restore /src/${project} -r linux-x64 -p:PublishAot=true ${props} ${existsSync(lock) ? '--locked-mode' : ''} --nologo; dotnet restore /src/${project} -r linux-x64 -p:PublishAot=true ${props} --locked-mode --nologo; dotnet publish /src/${project} -c Release -r linux-x64 -p:PublishAot=true -p:StripSymbols=true ${props} -o /evidence/${role} --no-restore --nologo`;
  const log = await new Promise((resolve, reject) => {
    const child = spawn('docker', ['run', '--rm', '--name', name, '--label', `fullnet.ai.pdf.build=${name}`,
      '--cpus', '2', '--memory', '2g', '--memory-swap', '2g', '-e', 'DOTNET_PROCESSOR_COUNT=2',
      '-v', `${root}:/src:ro`, '-v', `${output}:/evidence`,
      '-v', `${path.join(process.env.USERPROFILE || process.env.HOME, '.nuget/packages')}:/root/.nuget/packages`,
      sdk, 'bash', '-c', command], { windowsHide: true });
    let text = '';
    const timer = setTimeout(() => {
      try {
        assert.equal(docker(['inspect', name, '--format', '{{index .Config.Labels "fullnet.ai.pdf.build"}}']).trim(), name);
        docker(['kill', name]);
      } catch { }
      child.kill();
    }, 600000);
    child.stdout.on('data', data => { text += data; });
    child.stderr.on('data', data => { text += data; });
    child.on('error', reject);
    child.on('close', code => {
      clearTimeout(timer);
      writeFileSync(path.join(output, `${role}-publish.log`), text);
      if (code !== 0) reject(new Error(`${role} native publish failed (${code}); inspect ${role}-publish.log`));
      else resolve(text);
    });
  });
  assert.doesNotMatch(log, /\bwarning\b/i, '独立工具不继承 Host 的告警例外');
  copyFileSync(evidenceLock, lock);
  const imageFile = path.join(output, `${role}-image-id.txt`);
  docker(['build', '--network=none', '--iidfile', imageFile, '-f', path.join(root, directory, 'Dockerfile'), path.join(output, role)]);
  return { imageId: readFileSync(imageFile, 'utf8').trim(), artifactSha256: createHash('sha256').update(readFileSync(path.join(output, role,
    role === 'parser' ? 'Full.NET.Ai.PdfParser' : 'Full.NET.PdfSandbox.Probe'))).digest('hex') };
}

const parser = await publish('parser', 'src/Tools/Full.NET.Ai.PdfParser/Full.NET.Ai.PdfParser.csproj');
console.log(`parser native image ${parser.imageId}`);
const probe = await publish('probe', 'tests/ai-evaluation/pdf-sandbox/probe/Full.NET.PdfSandbox.Probe.csproj');
writeFileSync(path.join(output, 'images.json'), JSON.stringify({ sdk, parser, probe }, null, 2) + '\n');
console.log(`probe native image ${probe.imageId}`);
