import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import test from 'node:test';
import { verifyCreatedApp } from '../../scripts/templates/verify-created-app.mjs';

function configuration(preset = 'minimal', provider = 'mysql') {
  return { Database: { Provider: provider }, FullNet: { Modules: { Preset: preset } } };
}

function fixture() {
  const root = mkdtempSync(join(tmpdir(), 'fullnet-created-app-check-'));
  // 此夹具只满足结构校验，不替代真实创建和 .NET 构建。
  const files = {
    'framework-manifest.json': JSON.stringify({ schemaVersion: 1, frameworkVersion: '1.0.0', managedFiles: {} }),
    'appsettings.json': JSON.stringify(configuration()),
    'fullnet-app.json': JSON.stringify({ ownerKey: 'acme', preset: 'minimal', databaseProvider: 'mysql' }),
    'pnpm-lock.yaml': '',
    'ui/admin/package.json': '{}',
    'packages/client-contracts/package.json': '{}',
    'packages/admin-i18n/package.json': '{}',
    'packages/admin-form-designer/package.json': '{}',
    'packages/design-tokens/package.json': '{}',
    'src/Demo.Host.Api/Program.cs': '',
    'src/Demo.Host.Api/Demo.Host.Api.csproj': '<Project />',
    'src/Demo.Host.Api/appsettings.json': JSON.stringify(configuration()),
    'src/Demo.Composition/Demo.Composition.csproj': '<Project />',
    'src/Demo.Composition/ApplicationModuleCatalog.cs': '',
  };
  for (const [path, content] of Object.entries(files)) {
    mkdirSync(dirname(join(root, path)), { recursive: true });
    writeFileSync(join(root, path), content);
  }
  return { root, files };
}

for (const name of ['Demo.Composition.csproj', 'ApplicationModuleCatalog.cs']) {
  for (const replacement of ['missing', 'directory']) {
    test(`created app rejects ${replacement} matching composition ${name}`, () => {
      const { root } = fixture();
      try {
        const path = join(root, 'src/Demo.Composition', name);
        rmSync(path);
        if (replacement === 'directory') mkdirSync(path);
        const result = verifyCreatedApp(root);
        assert.equal(result.ok, false);
        assert.ok(result.errors.some((error) => error.includes(name)), result.errors.join('; '));
      } finally {
        rmSync(root, { recursive: true, force: true });
      }
    });
  }
}

test('another application composition cannot replace the API host matching project', () => {
  const { root } = fixture();
  try {
    rmSync(join(root, 'src/Demo.Composition'), { recursive: true });
    mkdirSync(join(root, 'src/Other.Composition'));
    writeFileSync(join(root, 'src/Other.Composition/Other.Composition.csproj'), '<Project />');
    writeFileSync(join(root, 'src/Other.Composition/ApplicationModuleCatalog.cs'), '');
    assert.equal(verifyCreatedApp(root).ok, false);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('created app rejects a directory occupying a required API program', () => {
  const { root } = fixture();
  try {
    rmSync(join(root, 'src/Demo.Host.Api/Program.cs'));
    mkdirSync(join(root, 'src/Demo.Host.Api/Program.cs'));
    assert.equal(verifyCreatedApp(root).ok, false);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

for (const configPath of ['appsettings.json', 'src/Demo.Host.Api/appsettings.json']) {
  for (const preset of ['platform', undefined]) {
    test(`created app rejects ${preset ?? 'missing'} preset in ${configPath}`, () => {
      const { root } = fixture();
      try {
        const config = configuration();
        config.FullNet.Modules.Preset = preset;
        writeFileSync(join(root, configPath), JSON.stringify(config));
        const result = verifyCreatedApp(root);
        assert.equal(result.ok, false);
        assert.ok(result.errors.some((error) => error.includes(configPath) && error.includes('preset')),
          result.errors.join('; '));
      } finally {
        rmSync(root, { recursive: true, force: true });
      }
    });
  }
}

for (const content of ['{', 'null', '[]']) {
  test(`created app rejects invalid API configuration ${content}`, () => {
    const { root } = fixture();
    try {
      writeFileSync(join(root, 'src/Demo.Host.Api/appsettings.json'), content);
      assert.equal(verifyCreatedApp(root).ok, false);
    } finally {
      rmSync(root, { recursive: true, force: true });
    }
  });
}

for (const preset of ['minimal', 'platform', 'saas', 'enterprise']) {
  test(`matching ${preset} configuration passes for both application config files`, () => {
    const { root } = fixture();
    try {
      writeFileSync(join(root, 'fullnet-app.json'), JSON.stringify({ ownerKey: 'acme', preset, databaseProvider: 'mysql' }));
      for (const path of ['appsettings.json', 'src/Demo.Host.Api/appsettings.json']) {
        writeFileSync(join(root, path), JSON.stringify(configuration(preset)));
      }
      const result = verifyCreatedApp(root);
      assert.equal(result.ok, true, result.errors.join('; '));
    } finally {
      rmSync(root, { recursive: true, force: true });
    }
  });
}

for (const configPath of ['appsettings.json', 'src/Demo.Host.Api/appsettings.json']) {
  for (const provider of ['sqlserver', undefined, 'postgres']) {
    test(`created app rejects ${provider ?? 'missing'} provider in ${configPath}`, () => {
      const { root } = fixture();
      try {
        const config = configuration();
        config.Database.Provider = provider;
        writeFileSync(join(root, configPath), JSON.stringify(config));
        const result = verifyCreatedApp(root);
        assert.equal(result.ok, false);
        assert.ok(result.errors.some((error) => error.includes(configPath) && error.includes('provider')),
          result.errors.join('; '));
      } finally {
        rmSync(root, { recursive: true, force: true });
      }
    });
  }
}

for (const provider of ['sqlserver', 'mysql']) {
  test(`matching ${provider} configuration passes without changing the application`, () => {
    const { root } = fixture();
    try {
      const files = {
        'fullnet-app.json': JSON.stringify({ ownerKey: 'acme', preset: 'minimal', databaseProvider: provider }),
        'appsettings.json': JSON.stringify(configuration('minimal', provider)),
        'src/Demo.Host.Api/appsettings.json': JSON.stringify(configuration('minimal', provider)),
      };
      for (const [path, content] of Object.entries(files)) writeFileSync(join(root, path), content);
      const result = verifyCreatedApp(root);
      assert.equal(result.ok, true, result.errors.join('; '));
      for (const [path, content] of Object.entries(files)) assert.equal(readFileSync(join(root, path), 'utf8'), content);
    } finally {
      rmSync(root, { recursive: true, force: true });
    }
  });
}

test('source path occupied by a file returns diagnostics without changing the file', () => {
  const { root } = fixture();
  try {
    const sourceRoot = join(root, 'src');
    rmSync(sourceRoot, { recursive: true });
    writeFileSync(sourceRoot, 'manual source placeholder');
    let result;
    assert.doesNotThrow(() => { result = verifyCreatedApp(root); });
    assert.equal(result.ok, false);
    assert.ok(result.errors.some((error) => error.includes('source directory') && error.includes('src')));
    assert.equal(readFileSync(sourceRoot, 'utf8'), 'manual source placeholder');
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('CLI reports a source directory error without an unhandled stack trace', () => {
  const { root } = fixture();
  try {
    rmSync(join(root, 'src'), { recursive: true });
    writeFileSync(join(root, 'src'), 'manual source placeholder');
    const result = spawnSync(process.execPath, [resolve('scripts/templates/verify-created-app.mjs'), root],
      { encoding: 'utf8', timeout: 10_000, windowsHide: true });
    assert.equal(result.error, undefined);
    assert.equal(result.status, 1);
    assert.match(result.stderr, /source directory.*src/);
    assert.doesNotMatch(result.stderr, /at verifyCreatedApp|node:fs:/);
    assert.equal(result.stdout, '');
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

for (const state of ['missing', 'empty', 'multiple']) {
  test(`${state} source hosts return an invalid result`, () => {
    const { root } = fixture();
    try {
      if (state === 'multiple') {
        mkdirSync(join(root, 'src/Other.Host.Api'));
      } else {
        rmSync(join(root, 'src'), { recursive: true });
        if (state === 'empty') mkdirSync(join(root, 'src'));
      }
      const result = verifyCreatedApp(root);
      assert.equal(result.ok, false);
      assert.ok(result.errors.includes('Created app must contain exactly one application API host'));
    } finally {
      rmSync(root, { recursive: true, force: true });
    }
  });
}

test('complete application structure passes without changing its files', () => {
  const { root, files } = fixture();
  try {
    const result = verifyCreatedApp(root);
    assert.equal(result.ok, true, result.errors.join('; '));
    for (const [path, original] of Object.entries(files)) {
      assert.equal(readFileSync(join(root, path), 'utf8'), original);
    }
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});
