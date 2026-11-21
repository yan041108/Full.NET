#!/usr/bin/env python3
"""对象容量专用 Node Exporter 首次安装；默认预检，显式 --apply 才写入并启动服务。"""
import argparse
import fcntl
import hashlib
import io
import ipaddress
import json
import os
from pathlib import Path
import platform
import re
import shutil
import signal
import socket
import stat
import subprocess
import sys
import tarfile
import tempfile

VERSION = '1.13.1'
ARCHIVE_SHA256 = '5c00c5234f1ee45900b25539a5a0ab82146a098c15c16a4ec3bc855b597a6477'
UNIT = 'fullnet-object-capacity-exporter.service'
METRICS = '/var/lib/fullnet-object-capacity'


class InstallFailure(Exception):
    """只输出稳定错误码，不回显凭据、路径内容或命令错误。"""


def require(condition, code):
    if not condition:
        raise InstallFailure(code)


def execute(arguments, content=None):
    try:
        result = subprocess.run([str(value) for value in arguments], input=content,
            stdin=subprocess.DEVNULL if content is None else None, capture_output=True, timeout=15)
    except (OSError, subprocess.SubprocessError):
        raise InstallFailure('command_failed') from None
    require(result.returncode == 0 and len(result.stdout) <= 65536, 'command_failed')
    return result.stdout


def read_input(path, maximum=65536, private=False):
    descriptor = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
    with os.fdopen(descriptor, 'rb') as stream:
        info = os.fstat(stream.fileno())
        require(stat.S_ISREG(info.st_mode) and info.st_nlink == 1 and info.st_uid == os.geteuid()
            and info.st_mode & (0o077 if private else 0o022) == 0, 'input_permissions_invalid')
        require(0 < info.st_size <= maximum, 'input_size_invalid')
        content = stream.read(maximum + 1)
        after = os.fstat(stream.fileno())
        require(len(content) == info.st_size and (info.st_size, info.st_mtime_ns, info.st_ctime_ns)
            == (after.st_size, after.st_mtime_ns, after.st_ctime_ns), 'input_changed')
        return content


def unpack_archive(content):
    require(hashlib.sha256(content).hexdigest() == ARCHIVE_SHA256, 'archive_digest_invalid')
    prefix = f'node_exporter-{VERSION}.linux-amd64'
    expected = {prefix + '/' + name: name for name in ('node_exporter', 'LICENSE', 'NOTICE')}
    output = {}
    # 不调用 extract/extractall；只读取经摘要冻结的三个精确普通文件，拒绝链接和额外路径。
    with tarfile.open(fileobj=io.BytesIO(content), mode='r:gz') as archive:
        for index, member in enumerate(archive):
            require(index < 8, 'archive_entries_invalid')
            if member.name.rstrip('/') == prefix and member.isdir():
                continue
            require(member.name in expected and member.isfile(), 'archive_entry_invalid')
            name = expected[member.name]
            require(name not in output and 0 < member.size <= (64 * 1024**2 if name == 'node_exporter' else 512 * 1024),
                'archive_entry_invalid')
            output[name] = archive.extractfile(member).read(member.size + 1)
            require(len(output[name]) == member.size, 'archive_entry_invalid')
    require(set(output) == {'node_exporter', 'LICENSE', 'NOTICE'}, 'archive_entries_invalid')
    binary = output['node_exporter']
    require(binary[:6] == b'\x7fELF\x02\x01' and binary[18:20] == b'\x3e\x00', 'archive_architecture_invalid')
    return output


def validate_credentials(credentials, address):
    with tempfile.TemporaryDirectory(prefix='fullnet-exporter-pki-') as temporary:
        directory = Path(temporary)
        for name, content in credentials.items():
            path = directory / name
            path.write_bytes(content)
            path.chmod(0o600)
        execute(['openssl', 'verify', '-purpose', 'sslserver', '-verify_ip', address,
            '-CAfile', directory / 'server-ca.pem', directory / 'server.crt'])
        cert_public = execute(['openssl', 'x509', '-in', directory / 'server.crt', '-pubkey', '-noout'])
        cert_public = execute(['openssl', 'pkey', '-pubin', '-outform', 'DER'], cert_public)
        key_public = execute(['openssl', 'pkey', '-in', directory / 'server.key', '-passin', 'pass:', '-pubout', '-outform', 'DER'])
        require(cert_public == key_public, 'server_key_mismatch')
        require(credentials['client-ca.pem'].count(b'-----BEGIN CERTIFICATE-----') == 1, 'client_ca_invalid')
        constraint = execute(['openssl', 'x509', '-in', directory / 'client-ca.pem', '-noout', '-ext', 'basicConstraints'])
        require(b'CA:TRUE' in constraint, 'client_ca_invalid')
        execute(['openssl', 'verify', '-partial_chain', '-CAfile', directory / 'client-ca.pem', directory / 'client-ca.pem'])


def paths(root=Path('/')):
    return {'config': root / 'etc/fullnet/object-capacity-exporter',
        'binary': root / 'usr/local/lib/fullnet/object-capacity-exporter',
        'unit': root / 'etc/systemd/system' / UNIT,
        'metrics': root / METRICS.lstrip('/'), 'lock': root / 'run' / (UNIT + '.lock')}


def trusted_directory(path):
    for parent in (path, *path.parents):
        info = parent.lstat()
        require(stat.S_ISDIR(info.st_mode) and info.st_uid == 0 and info.st_mode & 0o022 == 0,
            'installation_parent_invalid')


def inspect_targets(targets):
    for name in ('config', 'binary', 'unit'):
        require(not os.path.lexists(targets[name]), 'existing_installation')
        trusted_directory(targets[name].parent)
    trusted_directory(targets['metrics'])
    for directory in (targets['metrics'], targets['binary'].parent):
        require(all(parent.stat().st_mode & 0o005 == 0o005 for parent in (directory, *directory.parents)),
            'metrics_or_binary_directory_unreadable')


def service_unit(address, targets):
    credential_lines = '\n'.join('LoadCredential=' + name + ':' + str(targets['config'] / name)
        for name in ('server.crt', 'server.key', 'client-ca.pem', 'web.json'))
    return f'''[Unit]
Description=Full.NET object capacity textfile exporter
After=network-online.target
Wants=network-online.target
[Service]
Type=exec
DynamicUser=yes
{credential_lines}
ExecStart={targets['binary']}/node_exporter --web.listen-address={address}:9100 --web.config.file=%d/web.json --collector.disable-defaults --collector.textfile --collector.textfile.directory={targets['metrics']}
Restart=on-failure
RestartSec=5
TimeoutStartSec=30
TimeoutStopSec=15
UMask=0077
NoNewPrivileges=yes
ProtectSystem=strict
ProtectHome=yes
PrivateTmp=yes
PrivateDevices=yes
ProtectKernelTunables=yes
ProtectKernelModules=yes
ProtectControlGroups=yes
RestrictSUIDSGID=yes
RestrictAddressFamilies=AF_INET AF_INET6 AF_UNIX
CapabilityBoundingSet=
AmbientCapabilities=
ReadOnlyPaths={targets['metrics']}
MemoryMax=256M
CPUQuota=50%
TasksMax=128
[Install]
WantedBy=multi-user.target
'''


def prepare(options, targets):
    require(sys.platform == 'linux' and platform.machine() == 'x86_64', 'linux_amd64_required')
    address = ipaddress.IPv4Address(options.bind_address)
    require(any(address in ipaddress.IPv4Network(network) for network in ('10.0.0.0/8', '172.16.0.0/12', '192.168.0.0/16')),
        'private_bind_address_required')
    require(re.fullmatch(r'(?=.{1,253}\Z)[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?)*', options.client_san)
        is not None, 'client_san_invalid')
    inspect_targets(targets)
    binary = unpack_archive(read_input(options.archive, maximum=64 * 1024**2))
    credentials = {name: read_input(path, private=name == 'server.key') for name, path in (
        ('server.crt', options.server_certificate), ('server.key', options.server_key),
        ('server-ca.pem', options.server_ca), ('client-ca.pem', options.client_ca))}
    validate_credentials(credentials, str(address))
    credentials.pop('server-ca.pem')
    credentials['web.json'] = json.dumps({'tls_server_config': {'cert_file': 'server.crt', 'key_file': 'server.key',
        'client_ca_file': 'client-ca.pem', 'client_auth_type': 'RequireAndVerifyClientCert', 'min_version': 'TLS12',
        'client_allowed_sans': [options.client_san]}}).encode()
    return binary, credentials, service_unit(str(address), targets)


def write_exclusive(path, content, mode):
    descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, mode)
    identity = os.fstat(descriptor).st_ino
    try:
        with os.fdopen(descriptor, 'wb') as stream:
            stream.write(content)
            stream.flush()
            os.fchmod(stream.fileno(), mode)
            os.fsync(stream.fileno())
    except BaseException:
        require(path.lstat().st_ino == identity and not path.is_symlink(), 'partial_file_identity_changed')
        path.unlink()
        raise


def install(prepared, targets):
    require(os.geteuid() == 0, 'root_required')
    trusted_directory(targets['lock'].parent)
    descriptor = os.open(targets['lock'], os.O_RDWR | os.O_CREAT | os.O_NOFOLLOW | os.O_NONBLOCK, 0o600)
    with os.fdopen(descriptor, 'rb') as lock:
        info = os.fstat(lock.fileno())
        require(stat.S_ISREG(info.st_mode) and info.st_uid == 0 and info.st_nlink == 1
            and info.st_mode & 0o077 == 0, 'installation_lock_invalid')
        try:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            raise InstallFailure('installation_busy') from None
        inspect_targets(targets)
        require(execute(['systemctl', 'show', '-p', 'LoadState', '--value', UNIT]).strip() == b'not-found',
            'existing_systemd_unit')
        binary, credentials, unit = prepared
        created = []
        activation_attempted = False
        try:
            for name, content, mode in (('binary', binary, 0o755), ('config', credentials, 0o700)):
                targets[name].mkdir(mode=mode)
                created.append((targets[name], targets[name].stat().st_ino, True))
                targets[name].chmod(mode)
                for filename, data in content.items():
                    write_exclusive(targets[name] / filename, data, 0o755 if filename == 'node_exporter' else 0o600)
            write_exclusive(targets['unit'], unit.encode(), 0o644)
            created.append((targets['unit'], targets['unit'].stat().st_ino, False))
            execute(['systemd-analyze', 'verify', targets['unit']])
            execute(['systemctl', 'daemon-reload'])
            # 启动响应丢失也必须先停止并复核，不能直接删掉仍在运行的配置。
            activation_attempted = True
            execute(['systemctl', 'enable', '--now', UNIT])
            require(execute(['systemctl', 'show', '-p', 'ActiveState', '--value', UNIT]).strip() == b'active', 'service_not_active')
            require(execute(['systemctl', 'is-enabled', UNIT]).strip() == b'enabled', 'service_not_enabled')
        except BaseException:
            try:
                if activation_attempted:
                    execute(['systemctl', 'disable', '--now', UNIT])
                    require(execute(['systemctl', 'show', '-p', 'ActiveState', '--value', UNIT]).strip()
                        in (b'inactive', b'failed'), 'service_stop_unconfirmed')
                for path, inode, directory in reversed(created):
                    require(not path.is_symlink() and path.stat().st_ino == inode, 'installation_identity_changed')
                    shutil.rmtree(path) if directory else path.unlink()
                execute(['systemctl', 'daemon-reload'])
            except BaseException:
                raise InstallFailure('rollback_incomplete_preserved') from None
            raise


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for flag in ('archive', 'server-certificate', 'server-key', 'server-ca', 'client-ca'):
        parser.add_argument('--' + flag, required=True, type=Path)
    parser.add_argument('--bind-address', required=True)
    parser.add_argument('--client-san', required=True)
    parser.add_argument('--apply', action='store_true', help='显式首次安装并启用；默认只预检。')
    options = parser.parse_args()
    handlers = {}
    def interrupted(_number, _frame):
        raise InstallFailure('installation_interrupted')
    try:
        for number in (signal.SIGINT, signal.SIGTERM, signal.SIGHUP):
            handlers[number] = signal.signal(number, interrupted)
        target = paths()
        prepared = prepare(options, target)
        if options.apply:
            # 地址须已属于本机且端口可用；不自动改防火墙或以全接口监听兜底。
            with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as listener:
                listener.bind((options.bind_address, 9100))
            install(prepared, target)
        print(json.dumps({'state': 'installed' if options.apply else 'plan_validated', 'version': VERSION,
            'service': UNIT, 'notificationVerified': False, 'scrapeVerified': False}))
        return 0
    except (InstallFailure, OSError, ValueError, tarfile.TarError):
        error = sys.exc_info()[1]
        print(json.dumps({'state': 'installation_failed', 'code': str(error) if isinstance(error, InstallFailure)
            else 'local_validation_failed'}), file=sys.stderr)
        return 2
    finally:
        for number, handler in handlers.items():
            signal.signal(number, handler)


if __name__ == '__main__':
    sys.exit(main())
