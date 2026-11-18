#!/usr/bin/env python3
"""只读核对基础设施备份的角色、文件清单、私有权限和摘要，不执行恢复或信任恢复标记。"""
import argparse
import hashlib
import json
import pathlib
import re
import stat
import sys


class InvalidBackup(Exception):
    """对外只报告固定原因，不输出归档内容或秘密材料。"""


SHARED = {"ROLE", "redis.rdb", "redis-check.txt", "configuration.tar.gz", "packages.tsv"}
PRIMARY = {"mysql.sql.gz", "mysql-configuration.tar.gz"}
OPTIONAL_PRIMARY = {"data-protection.tar.gz", "k3s-state.db", "k3s-recovery.tar.gz"}
MARKERS = {"COMPLETE", "RESTORE-VERIFIED", "OBJECT-RESTORE-VERIFIED"}


def require(condition, message):
    if not condition:
        raise InvalidBackup(message)


def verify(directory, role, staging=False):
    require(role in ("primary", "secondary"), "备份角色无效")
    require(not directory.is_symlink() and directory.is_dir(), "备份目录无效")
    require(stat.S_IMODE(directory.stat().st_mode) & 0o077 == 0, "备份目录权限过宽")
    items = list(directory.iterdir())
    require(0 < len(items) <= 16, "备份文件数量无效")
    names = {item.name for item in items}
    required = SHARED | (PRIMARY if role == "primary" else {"object-storage.tar.gz"})
    allowed = required | {"SHA256SUMS"} | MARKERS | (OPTIONAL_PRIMARY if role == "primary" else set())
    require(required | {"SHA256SUMS"} <= names and names <= allowed, "备份缺少必需文件或包含未知文件")
    require(("k3s-state.db" in names) == ("k3s-recovery.tar.gz" in names), "K3s 数据与密钥归档必须成对存在")
    require("COMPLETE" not in names if staging else "COMPLETE" in names, "备份完成状态不匹配")
    if staging:
        require(not names & (MARKERS - {"COMPLETE"}), "暂存备份不能带有历史恢复标记")
    for item in items:
        info = item.lstat()
        require(stat.S_ISREG(info.st_mode) and info.st_nlink == 1, "备份不能包含目录、链接或其他特殊文件")
        if item.name not in MARKERS:
            require(stat.S_IMODE(info.st_mode) & 0o077 == 0 and info.st_size > 0, "备份文件为空或权限过宽")
    role_file = directory / "ROLE"
    require(role_file.stat().st_size <= 32 and role_file.read_text(encoding="ascii").strip() == role, "备份角色不匹配")
    checksum_file = directory / "SHA256SUMS"
    require(checksum_file.stat().st_size <= 16 * 256, "摘要清单过大")
    expected = {}
    for line in checksum_file.read_text(encoding="ascii").splitlines():
        match = re.fullmatch(r"([a-fA-F0-9]{64})  \./([A-Za-z0-9][A-Za-z0-9._-]*)", line)
        require(match is not None, "摘要清单格式或路径无效")
        digest, name = match.groups()
        require(name not in expected and name in names and name not in MARKERS | {"SHA256SUMS"}, "摘要清单包含重复或非法文件")
        expected[name] = digest.lower()
    require(set(expected) == names - MARKERS - {"SHA256SUMS"}, "摘要清单没有精确覆盖备份文件")
    for name, expected_digest in expected.items():
        digest = hashlib.sha256()
        with (directory / name).open("rb") as stream:
            for block in iter(lambda: stream.read(1024 * 1024), b""):
                digest.update(block)
        require(digest.hexdigest() == expected_digest, "备份文件摘要不匹配")
    return {"schemaVersion": 1, "role": role, "state": "staged" if staging else "complete",
            "verifiedFiles": len(expected), "restoreVerified": False}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=pathlib.Path)
    parser.add_argument("--role", required=True, choices=("primary", "secondary"))
    parser.add_argument("--staging", action="store_true", help="仅供生产者在生成 COMPLETE 之前核对暂存输出")
    options = parser.parse_args(argv)
    try:
        result = verify(options.directory, options.role, options.staging)
    except (InvalidBackup, OSError, UnicodeError) as error:
        message = str(error) if isinstance(error, InvalidBackup) else "备份文件读取失败"
        print("备份完整性校验失败：" + message, file=sys.stderr)
        return 1
    print(json.dumps(result, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    sys.exit(main())
