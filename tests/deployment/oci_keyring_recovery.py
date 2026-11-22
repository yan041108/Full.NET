"""只对本轮夹具备份介质验证独立恢复、完整性拒绝和时间点边界。"""
import base64
import hashlib
import json
import secrets
import stat
import uuid
import zipfile


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def restore(backup, manifest, target):
    # 先在有界内存中核对全部输入，拒绝后不得创建目标目录或部分恢复文件。
    if target.exists():
        raise RuntimeError("recovery_target_exists")
    if {p.name for p in backup.iterdir()} != {"keyring.zip", "certificate.pfx", "manifest.json"}:
        raise RuntimeError("recovery_backup_files_mismatch")
    for name in ("keyring.zip", "certificate.pfx", "manifest.json"):
        path = backup / name
        metadata = path.lstat()
        if not stat.S_ISREG(metadata.st_mode) or metadata.st_nlink != 1 or metadata.st_size > 2 * 1024 * 1024:
            raise RuntimeError("recovery_backup_entry_invalid")
    if json.loads((backup / "manifest.json").read_text()) != manifest:
        raise RuntimeError("recovery_manifest_mismatch")
    for name, expected in manifest["files"].items():
        if digest(backup / name) != expected:
            raise RuntimeError("recovery_backup_digest_mismatch")
    contents = {}
    with zipfile.ZipFile(backup / "keyring.zip") as archive:
        entries = archive.infolist()
        if not 1 <= len(entries) <= 32 or len({e.filename for e in entries}) != len(entries):
            raise RuntimeError("recovery_zip_entries_invalid")
        for entry in entries:
            name = entry.filename
            if not name.startswith("key-") or not name.endswith(".xml") or name != "key-" + str(uuid.UUID(name[4:-4])) + ".xml":
                raise RuntimeError("recovery_zip_name_invalid")
            if entry.is_dir() or stat.S_ISLNK(entry.external_attr >> 16) or not 1 <= entry.file_size <= 65536:
                raise RuntimeError("recovery_zip_entry_invalid")
            contents[name] = archive.read(entry)
    if {name: hashlib.sha256(value).hexdigest() for name, value in contents.items()} != manifest["keys"]:
        raise RuntimeError("recovery_key_manifest_mismatch")
    target.mkdir(mode=0o700)
    keyring, pki = target / "keyring", target / "pki"
    keyring.mkdir(mode=0o770)
    keyring.chmod(0o770)
    pki.mkdir(mode=0o750)
    pki.chmod(0o750)
    for name, value in contents.items():
        path = keyring / name
        path.write_bytes(value)
        path.chmod(0o640)
    certificate = pki / "protection.pfx"
    certificate.write_bytes((backup / "certificate.pfx").read_bytes())
    certificate.chmod(0o440)
    return keyring, pki


def verify(root, assets, assets_root, invoke, token, plaintext):
    backup = root / "keyring-backup"
    backup.mkdir(mode=0o770)
    backup.chmod(0o770)
    original_pki = assets_root / "production-private" / "host"
    original_ring = assets_root / "production-private" / "keyring"
    original_config = assets_root / "production-private" / "appsettings.Production.json"
    if invoke("api", "inspect") != "1":
        raise RuntimeError("recovery_initial_encrypted_key_missing")
    count = invoke("api", "backup", arguments=[*assets["hostArguments"], "--mount",
                   "type=bind,source=" + str(backup) + ",target=/var/fullnet/recovery"])
    if count != "1":
        raise RuntimeError("recovery_backup_key_count_invalid")
    certificate = backup / "certificate.pfx"
    certificate.write_bytes((original_pki / "protection.pfx").read_bytes())
    certificate.chmod(0o600)
    with zipfile.ZipFile(backup / "keyring.zip") as archive:
        keys = {item.filename: hashlib.sha256(archive.read(item)).hexdigest() for item in archive.infolist()}
    manifest = {"schemaVersion": 1, "files": {name: digest(backup / name) for name in ("keyring.zip", "certificate.pfx")},
                "keys": keys}
    (backup / "manifest.json").write_text(json.dumps(manifest, sort_keys=True))
    manifest_digest = digest(backup / "manifest.json")

    def reject_variant(name, missing=False):
        variant, target = root / ("keyring-backup-" + name), root / ("keyring-rejected-" + name)
        variant.mkdir(mode=0o700)
        for item in backup.iterdir():
            if not (missing and item.name == "certificate.pfx"):
                (variant / item.name).write_bytes(item.read_bytes())
        if not missing:
            data = bytearray((variant / "keyring.zip").read_bytes())
            data[len(data) // 2] ^= 1
            (variant / "keyring.zip").write_bytes(data)
        expected = "recovery_backup_files_mismatch" if missing else "recovery_backup_digest_mismatch"
        try:
            restore(variant, manifest, target)
        except RuntimeError as error:
            if str(error) == expected and not target.exists():
                return
            raise
        raise RuntimeError("recovery_invalid_backup_accepted")

    reject_variant("corrupted")
    reject_variant("missing-certificate", missing=True)
    target = root / "keyring-restored"
    keyring, pki = restore(backup, manifest, target)
    configuration = root / "keyring-restored.json"
    settings = {"DataProtection": json.loads(original_config.read_text())["DataProtection"]}
    configuration.write_text(json.dumps(settings))
    configuration.chmod(0o444)
    arguments = [arg.replace(str(original_ring), str(keyring)).replace(str(original_pki), str(pki))
                 .replace(str(original_config), str(configuration)) for arg in assets["hostArguments"]]
    if invoke("worker", "unprotect", text=token, arguments=arguments) != plaintext:
        raise RuntimeError("recovery_original_payload_failed")
    new_plaintext = secrets.token_hex(32)
    new_token = invoke("worker", "protect", text=new_plaintext, arguments=arguments)
    if invoke("api", "unprotect", text=new_token, arguments=arguments) != new_plaintext:
        raise RuntimeError("recovery_reverse_process_failed")
    # 备份后只在原介质创建新密钥，恢复副本不得通过原目录或旧进程缓存读取它。
    created = invoke("api", "rotate")
    later_token = invoke("api", "protect", text=secrets.token_hex(32))
    payload = base64.b64decode(later_token + "=" * (-len(later_token) % 4), altchars=b"-_", validate=True)
    if len(payload) < 20 or payload[:4] != bytes.fromhex("09f0c9f0") or str(uuid.UUID(bytes_le=payload[4:20])) != created:
        raise RuntimeError("recovery_post_backup_key_not_used")
    invoke("worker", "unprotect", text=later_token, arguments=arguments, rejected=True)
    if invoke("api", "unprotect", text=token, arguments=arguments) != plaintext or invoke("api", "inspect", arguments=arguments) != "1":
        raise RuntimeError("recovery_snapshot_changed")
    for name, expected in manifest["files"].items():
        if digest(backup / name) != expected:
            raise RuntimeError("recovery_source_backup_changed")
    if digest(backup / "manifest.json") != manifest_digest:
        raise RuntimeError("recovery_source_manifest_changed")
    print("candidate_keyring_recovery_verified: restored=true corruption_rejected=true missing_certificate_rejected=true later_key_rejected=true", flush=True)
    return {"backupManifestSha256": manifest_digest, "backupArchiveSha256": manifest["files"]["keyring.zip"],
            "certificateSha256": manifest["files"]["certificate.pfx"], "restoredEncryptedKeyCount": 1,
            "originalPayloadRestoredVerified": True, "restoredRoleRoundTripVerified": True,
            "corruptedBackupRejectedBeforeWrites": True, "missingCertificateRejectedBeforeWrites": True,
            "postBackupKeyPayloadRejected": True, "sourceBackupUnchanged": True,
            "certificatePasswordIncludedInBackup": False, "businessHostConsumerVerified": False,
            "productionRecoveryVerified": False}
