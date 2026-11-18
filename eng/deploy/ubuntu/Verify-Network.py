#!/usr/bin/env python3
"""只读网络准入：匿名 Redis PING 必须拒绝，MySQL TLS 必须验证 CA 与目标身份。"""
import argparse
import json
import math
import socket
import ssl
import struct
import sys
import time

PROTOCOL_41 = 512
CLIENT_SSL = 2048
SECURE_CONNECTION = 32768


class ProbeFailure(Exception):
    """仅携带稳定机器码，避免远端响应及连接秘密进入诊断。"""


def receive(connection, count, deadline):
    remaining = deadline - time.monotonic()
    if remaining <= 0:
        raise ProbeFailure("network_timeout")
    connection.settimeout(remaining)
    data = connection.recv(count)
    if not data:
        raise ProbeFailure("protocol_truncated")
    return data


def receive_exact(connection, count, deadline):
    data = bytearray()
    while len(data) < count:
        data.extend(receive(connection, count - len(data), deadline))
    return bytes(data)


def verify_redis(host, port, timeout):
    with socket.create_connection((host, port), timeout=timeout) as connection:
        deadline = time.monotonic() + timeout
        connection.sendall(b"*1\r\n$4\r\nPING\r\n")
        response = bytearray()
        # RESP 简单错误以 CRLF 结束；不能将一次 recv 当作完整消息。
        while b"\r\n" not in response:
            if len(response) >= 1024:
                raise ProbeFailure("redis_reply_too_large")
            response.extend(receive(connection, 1024 - len(response), deadline))
        line, _, tail = response.partition(b"\r\n")
        if tail or not (line == b"-NOAUTH" or line.startswith(b"-NOAUTH ")):
            raise ProbeFailure("redis_anonymous_ping_not_denied")
    return {"anonymousPingRejected": True}


def mysql_capabilities(payload):
    if not payload or payload[0] != 10:
        raise ProbeFailure("mysql_protocol_unsupported")
    version_end = payload.find(b"\0", 1)
    # 版本之后必须有连接号、随机数、填充、双段能力及协议 4.1 固定字段。
    start = version_end + 1
    if version_end <= 1 or len(payload) < start + 31 or payload[start + 12] != 0:
        raise ProbeFailure("mysql_greeting_invalid")
    low = int.from_bytes(payload[start + 13:start + 15], "little")
    high = int.from_bytes(payload[start + 18:start + 20], "little")
    capabilities = low | (high << 16)
    if capabilities & (PROTOCOL_41 | CLIENT_SSL) != (PROTOCOL_41 | CLIENT_SSL):
        raise ProbeFailure("mysql_tls_not_advertised")
    return capabilities


def verify_mysql(host, port, timeout, context):
    with socket.create_connection((host, port), timeout=timeout) as connection:
        deadline = time.monotonic() + timeout
        header = receive_exact(connection, 4, deadline)
        size = int.from_bytes(header[:3], "little")
        if header[3] != 0 or not 1 <= size <= 65536:
            raise ProbeFailure("mysql_frame_invalid")
        capabilities = mysql_capabilities(receive_exact(connection, size, deadline))
        flags = PROTOCOL_41 | CLIENT_SSL | (capabilities & SECURE_CONNECTION)
        request = struct.pack("<IIB23s", flags, 16777216, 255, b"\0" * 23)
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            raise ProbeFailure("network_timeout")
        connection.settimeout(remaining)
        connection.sendall(len(request).to_bytes(3, "little") + b"\1" + request)
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            raise ProbeFailure("network_timeout")
        connection.settimeout(remaining)
        # SSLRequest 后仅执行 TLS；不发送数据库用户名、密码或业务 SQL。
        with context.wrap_socket(connection, server_hostname=host) as tls:
            return {"tlsVersion": tls.version(), "certificateIdentityVerified": True}


def host_value(value):
    if not value or len(value) > 253 or any(c.isspace() or ord(c) < 33 or ord(c) == 127 for c in value) or "://" in value:
        raise argparse.ArgumentTypeError("invalid_host")
    return value


def port_value(value):
    try:
        port = int(value)
    except ValueError:
        raise argparse.ArgumentTypeError("invalid_port") from None
    if not 1 <= port <= 65535:
        raise argparse.ArgumentTypeError("invalid_port")
    return port


def timeout_value(value):
    try:
        seconds = float(value)
    except ValueError:
        raise argparse.ArgumentTypeError("invalid_timeout") from None
    if not math.isfinite(seconds) or not 0 < seconds <= 30:
        raise argparse.ArgumentTypeError("invalid_timeout")
    return seconds


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--redis", required=True, type=host_value)
    parser.add_argument("--redis-port", type=port_value, default=6379)
    parser.add_argument("--mysql", type=host_value)
    parser.add_argument("--mysql-port", type=port_value)
    parser.add_argument("--ca")
    parser.add_argument("--timeout", type=timeout_value, default=5)
    args = parser.parse_args(argv)
    if (args.mysql and not args.ca) or (not args.mysql and (args.ca is not None or args.mysql_port is not None)):
        parser.error("mysql_and_ca_must_be_paired")
    try:
        context = None
        if args.mysql:
            context = ssl.create_default_context(cafile=args.ca)
            context.minimum_version = ssl.TLSVersion.TLSv1_2
        report = {"redis": verify_redis(args.redis, args.redis_port, args.timeout)}
        if args.mysql:
            report["mysql"] = verify_mysql(args.mysql, args.mysql_port or 3306, args.timeout, context)
    except ssl.SSLCertVerificationError:
        code = "mysql_certificate_verification_failed"
    except ssl.SSLError:
        code = "tls_failed"
    except (TimeoutError, socket.timeout):
        code = "network_timeout"
    except ProbeFailure as error:
        code = str(error)
    except (OSError, ValueError):
        code = "network_or_ca_unavailable"
    else:
        # 全部选定检查完成后一次签发，避免后续失败遗留局部成功回执。
        print(json.dumps({"status": "passed", **report}, separators=(",", ":")))
        return 0
    print(json.dumps({"status": "failed", "code": code}, separators=(",", ":")), file=sys.stderr)
    return 1


if __name__ == "__main__":
    sys.exit(main())
