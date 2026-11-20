#!/usr/bin/env python3
"""验证公开对象证书并原子安装；不签发证书、不改私钥/CA，也不推断在线服务已加载。"""
import argparse
import fcntl
import grp
import hashlib
import ipaddress
import json
import os
from pathlib import Path
import re
import shutil
import signal
import ssl
import stat
import subprocess
import sys
import tempfile

DEFAULT_DIRECTORY = Path("/etc/fullnet/object-storage")


class SyncFailure(Exception):
    """固定错误码，不携带文件内容、私钥、网络响应或原始 OpenSSL 错误。"""


def require(condition, code):
    if not condition:
        raise SyncFailure(code)


def regular_file(path, optional=False, private=False):
    try:
        descriptor = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
    except FileNotFoundError:
        if optional:
            return None
        raise
    with os.fdopen(descriptor, "rb") as stream:
        info = os.fstat(stream.fileno())
        require(stat.S_ISREG(info.st_mode) and info.st_nlink == 1 and info.st_uid == os.geteuid()
                and info.st_mode & 0o022 == 0, "local_input_permissions_invalid")
        if private:
            require(info.st_mode & 0o007 == 0, "private_key_permissions_invalid")
        require(0 < info.st_size <= 65536, "local_input_size_invalid")
        content = stream.read(65537)
        require(len(content) == info.st_size, "local_inputs_changed")
    identity = (info.st_dev, info.st_ino, info.st_size, info.st_mtime_ns, info.st_ctime_ns,
                info.st_uid, info.st_gid, stat.S_IMODE(info.st_mode))
    return content, identity


def snapshot(directory):
    return {name: regular_file(directory / name, optional=name == "object.pem", private=name == "object.key")
            for name in ("server.env", "application-ca.pem", "object.key", "object.pem")}


def execute(arguments, phase, content=None, seconds=10):
    try:
        result = subprocess.run(arguments, input=content, stdin=subprocess.DEVNULL if content is None else None,
                                stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, timeout=seconds)
    except (OSError, subprocess.SubprocessError):
        raise SyncFailure(phase) from None
    require(result.returncode == 0 and len(result.stdout) <= 65536, phase)
    return result.stdout


def receive(options, state):
    directory = options.directory
    require(not directory.is_symlink() and directory.is_dir(), "certificate_directory_invalid")
    info = directory.stat()
    require(info.st_uid == os.geteuid() and info.st_mode & 0o022 == 0, "certificate_directory_permissions_invalid")
    if directory.resolve() == DEFAULT_DIRECTORY:
        require(os.geteuid() == 0, "root_required")
    group = int(options.group) if options.group.isdecimal() else grp.getgrnam(options.group).gr_gid
    require(0 <= group <= 0xFFFFFFFE, "group_invalid")
    descriptor = os.open(directory / ".certificate-sync.lock", os.O_RDWR | os.O_CREAT | os.O_NOFOLLOW | os.O_NONBLOCK, 0o600)
    try:
        lock_info = os.fstat(descriptor)
        require(stat.S_ISREG(lock_info.st_mode) and lock_info.st_uid == os.geteuid() and
                lock_info.st_nlink == 1 and lock_info.st_mode & 0o077 == 0, "certificate_lock_invalid")
        try:
            fcntl.flock(descriptor, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            raise SyncFailure("certificate_sync_busy") from None
        original = snapshot(directory)
        environment = original["server.env"][0].decode("ascii").strip()
        match = re.fullmatch(r"OBJECT_BIND_ADDRESS=([0-9.]+)", environment)
        require(match is not None, "object_address_invalid")
        address = str(ipaddress.IPv4Address(match[1]))
        work = Path(tempfile.mkdtemp(prefix=".certificate-", dir=directory))
        try:
            candidate = work / "object.pem"
            status = execute(["curl", "-q", "-4", "-fsS", "--noproxy", "*", "--proto", "=http",
                              "--connect-timeout", "5", "--max-time", "20", "--max-filesize", "32768",
                              "--max-redirs", "0", "--output", str(candidate), "--write-out", "%{http_code}",
                              f"http://{options.primary}:{options.port}/pki/object-server.pem"],
                             "certificate_download_failed", seconds=25)
            require(status == b"200", "certificate_http_status_invalid")
            content = candidate.read_bytes()
            require(0 < len(content) <= 32768, "certificate_download_size_invalid")
            require(re.fullmatch(rb"\s*-----BEGIN CERTIFICATE-----\r?\n[A-Za-z0-9+/=\r\n]+-----END CERTIFICATE-----\s*", content),
                    "certificate_pem_invalid")
            ca = work / "application-ca.pem"
            ca.write_bytes(original["application-ca.pem"][0])
            execute(["openssl", "verify", "-purpose", "sslserver", "-verify_ip", address,
                     "-CAfile", str(ca), str(candidate)], "certificate_trust_or_identity_invalid")
            certificate_public = execute(["openssl", "x509", "-in", str(candidate), "-pubkey", "-noout"],
                                         "certificate_public_key_invalid")
            certificate_der = execute(["openssl", "pkey", "-pubin", "-outform", "DER"], "certificate_public_key_invalid", certificate_public)
            # 私钥只从受控文件读入内存，再经 stdin 提取公钥；不写新私钥副本，不出现在 argv/日志。
            private_public = execute(["openssl", "pkey", "-pubout", "-outform", "DER"],
                                     "local_private_key_invalid", original["object.key"][0])
            require(certificate_der == private_public, "certificate_private_key_mismatch")
            require(snapshot(directory) == original, "local_inputs_changed")
            fingerprint = hashlib.sha256(ssl.PEM_cert_to_DER_cert(content.decode("ascii").strip())).hexdigest()
            old = original["object.pem"]
            unchanged = old is not None and old[0] == content and old[1][-2:] == (group, 0o640)
            if not unchanged:
                os.chown(candidate, os.geteuid(), group, follow_symlinks=False)
                os.chmod(candidate, 0o640, follow_symlinks=False)
                with candidate.open("rb") as stream:
                    os.fsync(stream.fileno())
                # 提交与结果标记处于同一个信号屏蔽区，避免替换已完成却回报“未安装”。
                blocked = signal.pthread_sigmask(signal.SIG_BLOCK, {signal.SIGTERM, signal.SIGINT})
                try:
                    os.replace(candidate, directory / "object.pem")
                    state["certificateInstalled"] = True
                finally:
                    signal.pthread_sigmask(signal.SIG_SETMASK, blocked)
            return {"status": "unchanged" if unchanged else "updated", "certificateInstalled": state["certificateInstalled"],
                    "sha256Fingerprint": fingerprint, "serviceActivationVerified": False}
        finally:
            try:
                shutil.rmtree(work)
            except OSError:
                raise SyncFailure("certificate_temporary_cleanup_failed") from None
    finally:
        os.close(descriptor)


def address_value(value):
    try:
        return str(ipaddress.IPv4Address(value))
    except ipaddress.AddressValueError:
        raise argparse.ArgumentTypeError("invalid_primary_ipv4") from None


def port_value(value):
    try:
        port = int(value)
    except ValueError:
        raise argparse.ArgumentTypeError("invalid_source_port") from None
    if not 1 <= port <= 65535:
        raise argparse.ArgumentTypeError("invalid_source_port")
    return port


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("primary", type=address_value)
    parser.add_argument("--directory", type=Path, default=DEFAULT_DIRECTORY)
    parser.add_argument("--port", type=port_value, default=80)
    parser.add_argument("--group", default="fullnet-storage")
    options = parser.parse_args(argv)
    state = {"certificateInstalled": False}
    def interrupted(*_):
        raise SyncFailure("certificate_sync_interrupted")
    previous = {number: signal.signal(number, interrupted) for number in (signal.SIGTERM, signal.SIGINT)}
    try:
        try:
            result = receive(options, state)
        except SyncFailure as error:
            code = str(error)
        except (OSError, ValueError, KeyError, UnicodeError):
            code = "certificate_sync_failed"
        else:
            print(json.dumps(result, separators=(",", ":")))
            return 0
        print(json.dumps({"status": "failed", "code": code, **state}, separators=(",", ":")), file=sys.stderr)
        return 1
    finally:
        for number, handler in previous.items():
            signal.signal(number, handler)


if __name__ == "__main__":
    sys.exit(main())
