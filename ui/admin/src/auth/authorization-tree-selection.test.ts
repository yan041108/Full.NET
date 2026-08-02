import { describe, expect, it } from 'vitest';
import {
  applyPermissionNodeCheck,
  buildPermissionTreeNodes,
  permissionCodesToCheckedNodeIds
} from './authorization-tree-selection';
import type { PermissionTreeNode } from './authorization-tree-selection';

const sampleModules = [
  {
    id: 'identity',
    title: '\u8eab\u4efd\u4e0e\u6743\u9650',
    order: 10,
    pages: [
      {
        id: 'users',
        title: '\u7528\u6237\u7ba1\u7406',
        permissionCode: 'identity.users.read',
        order: 10,
        actions: [
          {
            id: 'identity.users.create',
            name: '\u521b\u5efa\u7528\u6237',
            permissionCode: 'identity.users.create',
            order: 10
          },
          {
            id: 'identity.users.reset-password',
            name: '\u91cd\u7f6e\u5bc6\u7801',
            permissionCode: 'identity.users.reset_password',
            order: 50
          }
        ],
        children: []
      }
    ]
  }
];

describe('\u6a21\u5757\u7ea7\u6388\u6743\u6811\u9009\u62e9\u89c4\u5219', () => {
  const nodes = buildPermissionTreeNodes(sampleModules);
  const moduleNode = nodes[0]!;
  const pageNode = moduleNode.children?.[0]!;
  const createAction = pageNode.children?.[0]!;
  const resetAction = pageNode.children?.[1]!;

  it('\u52fe\u9009\u6a21\u5757\u4f1a\u9009\u4e2d\u5168\u90e8\u9875\u9762\u4e0e\u64cd\u4f5c\u6743\u9650', () => {
    const next = applyPermissionNodeCheck(new Set<string>(), moduleNode, true);
    expect([...next].sort()).toEqual([
      'identity.users.create',
      'identity.users.read',
      'identity.users.reset_password'
    ]);
  });

  it('\u53d6\u6d88\u6a21\u5757\u4f1a\u6e05\u9664\u5168\u90e8\u540e\u4ee3\u6743\u9650', () => {
    const selected = new Set([
      'identity.users.read',
      'identity.users.create',
      'identity.users.reset_password'
    ]);
    const next = applyPermissionNodeCheck(selected, moduleNode, false);
    expect([...next]).toEqual([]);
  });

  it('\u6a21\u5757\u8282\u70b9\u4e0d\u4f1a\u5199\u5165\u6743\u9650\u96c6\u5408', () => {
    const next = applyPermissionNodeCheck(new Set<string>(), moduleNode, true);
    expect([...next].some(code => code.includes('module'))).toBe(false);
  });

  it('\u6a21\u5757\u8282\u70b9\u5728\u5168\u90e8\u540e\u4ee3\u9009\u4e2d\u65f6\u6620\u5c04\u4e3a\u52fe\u9009\u952e', () => {
    const checked = permissionCodesToCheckedNodeIds(
      new Set([
        'identity.users.read',
        'identity.users.create',
        'identity.users.reset_password'
      ]),
      nodes
    );
    expect(checked).toContain('module:identity');
    expect(checked).toContain('page:users');
    expect(checked).toContain('action:identity.users.create');
  });

  it('\u4ec5\u52fe\u9009\u9875\u9762\u65f6\u4e0d\u4f1a\u52fe\u9009\u6a21\u5757\u8282\u70b9', () => {
    const checked = permissionCodesToCheckedNodeIds(
      new Set(['identity.users.read']),
      nodes
    );
    expect(checked).not.toContain('module:identity');
    expect(checked).toContain('page:users');
  });

  it('\u4fdd\u7559\u9875\u9762\u4e0e\u64cd\u4f5c\u65e2\u6709\u52fe\u9009\u8bed\u4e49', () => {
    const next = applyPermissionNodeCheck(new Set<string>(), createAction, true);
    expect([...next].sort()).toEqual([
      'identity.users.create',
      'identity.users.read'
    ]);
    const cleared = applyPermissionNodeCheck(
      new Set(['identity.users.read', 'identity.users.create', 'identity.users.reset_password']),
      pageNode,
      false
    );
    expect([...cleared]).toEqual([]);
    expect(resetAction).toBeDefined();
  });
});

function collectPermissionCodes(node: PermissionTreeNode): string[] {
  const codes = node.permissionCode ? [node.permissionCode] : [];
  for (const child of node.children ?? []) {
    codes.push(...collectPermissionCodes(child));
  }
  return codes;
}

describe('\u6a21\u5757\u8282\u70b9\u5143\u6570\u636e', () => {
  it('\u6a21\u5757\u8282\u70b9\u4ec5\u7528\u4e8e\u5206\u7ec4\u5c55\u793a', () => {
    const moduleNode = buildPermissionTreeNodes(sampleModules)[0]!;
    expect(moduleNode.kind).toBe('module');
    expect(moduleNode.permissionCode).toBe('');
    expect(collectPermissionCodes(moduleNode).length).toBeGreaterThan(0);
  });
});