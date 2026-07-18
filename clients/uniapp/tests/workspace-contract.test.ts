import { readFile } from 'node:fs/promises';
import { describe, expect, it } from 'vitest';

const dcloudPackages = [
  '@dcloudio/uni-app',
  '@dcloudio/uni-components',
  '@dcloudio/uni-h5',
  '@dcloudio/uni-mp-alipay',
  '@dcloudio/uni-mp-weixin',
  '@dcloudio/uni-cli-shared',
  '@dcloudio/vite-plugin-uni'
];

type PackageDefinition = {
  dependencies: Record<string, string>;
  devDependencies: Record<string, string>;
  scripts: Record<string, string>;
};

async function readPackageDefinition(): Promise<PackageDefinition> {
  return JSON.parse(
    await readFile(new URL('../package.json', import.meta.url), 'utf8')
  ) as PackageDefinition;
}

describe('uni-app workspace contract', () => {
  it('pins all DCloud packages to one exact version', async () => {
    const packageDefinition = await readPackageDefinition();

    const packageVersions = dcloudPackages.map(packageName =>
      packageDefinition.dependencies[packageName] ?? packageDefinition.devDependencies[packageName]
    );

    expect(packageVersions).toHaveLength(dcloudPackages.length);
    expect(new Set(packageVersions).size).toBe(1);
    expect(packageVersions[0]).toBe('3.0.0-5010520260709002');
    expect(packageVersions.every(version => !/[~^*]|latest/.test(version))).toBe(true);
  });

  it('pins every direct dependency without a range or latest tag', async () => {
    const packageDefinition = await readPackageDefinition();
    const directDependencies = {
      ...packageDefinition.dependencies,
      ...packageDefinition.devDependencies
    };

    expect(Object.keys(directDependencies)).not.toHaveLength(0);
    expect(
      Object.values(directDependencies).every(version => !/[~^*]|latest/i.test(version))
    ).toBe(true);
  });

  it('exposes every supported target build script', async () => {
    const packageDefinition = await readPackageDefinition();

    expect(packageDefinition.scripts['build:h5']).toBe('uni build -p h5');
    expect(packageDefinition.scripts['build:mp-weixin']).toBe('uni build -p mp-weixin');
    expect(packageDefinition.scripts['build:mp-alipay']).toBe('uni build -p mp-alipay');
  });

  it('uses only the official uni Vite plugin for application builds', async () => {
    const viteConfig = await readFile(new URL('../vite.config.ts', import.meta.url), 'utf8');

    expect(viteConfig).toMatch(/@dcloudio\/vite-plugin-uni/);
    expect(viteConfig).toMatch(/plugins:\s*mode === 'test' \? \[\] : \[uni\(\)\]/);
  });

  it('includes routed Vue SFC files in the standard typecheck project', async () => {
    const tsconfig = JSON.parse(
      await readFile(new URL('../tsconfig.json', import.meta.url), 'utf8')
    ) as { readonly include?: readonly string[] };

    expect(tsconfig.include).toContain('src/**/*.vue');
  });

  it('records the required runtime licenses', async () => {
    const notices = await readFile(
      new URL('../../../THIRD-PARTY-NOTICES', import.meta.url),
      'utf8'
    );

    expect(notices).toMatch(/uni-app\/DCloud[^\n]*Apache-2\.0/i);
    expect(notices).toMatch(/Vue I18n[^\n]*MIT/i);
  });

  it('pins a Vue 3.4-compatible VueUse resolution and records its license', async () => {
    const [packageDefinition, resolvedVueUse, notices] = await Promise.all([
      readPackageDefinition(),
      readFile(new URL('../node_modules/@vueuse/core/package.json', import.meta.url), 'utf8'),
      readFile(new URL('../../../THIRD-PARTY-NOTICES', import.meta.url), 'utf8')
    ]);

    expect(packageDefinition.devDependencies['@vueuse/core']).toBe('11.3.0');
    expect(JSON.parse(resolvedVueUse).version).toBe('11.3.0');
    expect(JSON.parse(resolvedVueUse).version).not.toBe('14.3.0');
    expect(notices).toMatch(/VueUse[^\n]*MIT/i);
  });
});
