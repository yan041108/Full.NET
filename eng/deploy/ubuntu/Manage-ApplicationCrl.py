#!/usr/bin/env python3
"""发布应用 CA 的 CRL，并为现有 Web 证书接通吊销检查。"""
import argparse
import fcntl
import ipaddress
import os
from pathlib import Path
import shutil
import subprocess
import tempfile


ROOT = Path("/etc/fullnet")
PUBLIC = Path("/var/lib/fullnet-pki")


def openssl(*args):
    return subprocess.run(["openssl", *args], check=True, capture_output=True, text=True).stdout


def ensure_state(state, ca, key):
    if state.exists():
        return
    if (PUBLIC / "application.crl").exists():
        raise ValueError("已发布 CRL 但缺少吊销账本，请先恢复原账本。")
    with tempfile.TemporaryDirectory(prefix=".application-pki-", dir=state.parent) as directory:
        pending = Path(directory)
        (pending / "index.txt").touch()
        (pending / "crlnumber").write_text("1000\n", encoding="ascii")
        (pending / "ca.cnf").write_text(f"""[ca]
default_ca = fullnet
[fullnet]
database = {state / 'index.txt'}
certificate = {ca}
private_key = {key}
crlnumber = {state / 'crlnumber'}
default_md = sha256
default_crl_days = 7
""", encoding="utf-8")
        pending.rename(state)


def publish_crl(state, ca, work):
    pem = work / "application.crl.pem"
    der = work / "application.crl"
    openssl("ca", "-config", state / "ca.cnf", "-gencrl", "-out", pem)
    openssl("crl", "-in", pem, "-verify", "-CAfile", ca, "-noout")
    openssl("crl", "-in", pem, "-outform", "DER", "-out", der)
    der.chmod(0o644)
    # 在发布目录所在的文件系统原子替换，失败时旧 CRL 继续可读。
    der.replace(PUBLIC / der.name)
    return pem


def reload_nginx():
    subprocess.run(["nginx", "-t"], check=True)
    subprocess.run(["systemctl", "reload", "nginx"], check=True)


def configure_web(certificates, ca, key, crl, address):
    web = certificates / "web.pem"
    extensions = certificates / "web.extensions"
    url = f"URI:http://{address}/pki/application.crl"
    extension = f"crlDistributionPoints={url}"
    current = extensions.read_text(encoding="utf-8")
    verify = ("verify", "-CAfile", ca, "-CRLfile", crl, "-crl_check", "-purpose", "sslserver", "-verify_ip", str(address))
    # 重试沿用已签发证书，重载处理上次在文件切换后中断的情况。
    if extension in current.splitlines() and url in openssl("x509", "-in", web, "-noout", "-ext", "crlDistributionPoints").split():
        openssl(*verify, web)
        reload_nginx()
        return
    with tempfile.TemporaryDirectory(prefix=".web-crl-", dir=certificates) as directory:
        work = Path(directory)
        new_web, new_extensions = work / "web.pem", work / "web.extensions"
        lines = [line for line in current.splitlines() if line.split("=", 1)[0].strip().lower() != "crldistributionpoints"]
        new_extensions.write_text("\n".join([*lines, extension, ""]), encoding="utf-8")
        # 随机序列号与现有对象存储证书续签序列独立，复用现有 CA 和 Web 私钥。
        serial = openssl("rand", "-hex", "19").strip()
        openssl("x509", "-req", "-in", certificates / "web.csr", "-CA", ca, "-CAkey", key,
                "-set_serial", f"0x{serial}", "-days", "825", "-extfile", new_extensions, "-out", new_web)
        openssl(*verify, new_web)
        if openssl("x509", "-in", new_web, "-pubkey", "-noout") != openssl("pkey", "-in", certificates / "web.key", "-pubout"):
            raise ValueError("Web CSR 与现有私钥不匹配。")
        files = [(web, new_web), (extensions, new_extensions)]
        for old, new in files:
            previous = work / f"{old.name}.previous"
            metadata = old.stat()
            shutil.copy2(old, previous)
            os.chown(previous, metadata.st_uid, metadata.st_gid)
            new.chmod(metadata.st_mode & 0o777)
            os.chown(new, metadata.st_uid, metadata.st_gid)
        try:
            new_extensions.replace(extensions)
            new_web.replace(web)
            reload_nginx()
        except BaseException:
            for old, _ in files:
                (work / f"{old.name}.previous").replace(old)
            raise


def manage(address):
    certificates = ROOT / "certificates"
    state = ROOT / "application-pki"
    ca, key = certificates / "application-ca.pem", ROOT / "secrets/application-ca.key"
    with (ROOT / ".application-crl.lock").open("a", encoding="ascii") as lock:
        fcntl.flock(lock, fcntl.LOCK_EX)
        if address is not None:
            ensure_state(state, ca, key)
        if not state.is_dir():
            raise ValueError("请先配置应用 CA 吊销列表。")
        try:
            PUBLIC.mkdir()
        except FileExistsError:
            pass
        else:
            PUBLIC.chmod(0o755)
        with tempfile.TemporaryDirectory(prefix=".application-crl-", dir=PUBLIC) as directory:
            crl = publish_crl(state, ca, Path(directory))
            if address is not None:
                configure_web(certificates, ca, key, crl, address)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    command = parser.add_mutually_exclusive_group(required=True)
    command.add_argument("--configure", type=ipaddress.IPv4Address, metavar="GATEWAY_IP")
    command.add_argument("--refresh", action="store_true")
    options = parser.parse_args()
    if os.geteuid() != 0:
        parser.error("需要 root 权限。")
    os.umask(0o077)
    try:
        manage(options.configure)
    except subprocess.CalledProcessError as error:
        parser.exit(1, f"应用 CA 吊销配置失败（{error.cmd[0]}）。\n{error.stderr or ''}")
    except (OSError, ValueError) as error:
        parser.exit(1, f"应用 CA 吊销配置失败。{error}\n")


if __name__ == "__main__":
    main()
