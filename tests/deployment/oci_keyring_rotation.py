"""分别隔离活动证书、历史解密证书和新密钥身份，验证候选轮换与配置回退。"""
import base64
import hashlib
import json
import secrets
import uuid


def verify(root, assets, assets_root, new_root, invoke, old_token, old_plaintext):
    original_pki = assets_root / "production-private" / "host"
    original_config = assets_root / "production-private" / "appsettings.Production.json"
    pki = root / "keyring-rotation-pki"
    pki.mkdir(mode=0o755)
    old_pfx, new_pfx = original_pki / "protection.pfx", new_root / "production-private" / "host" / "protection.pfx"
    for name, source in (("historical.pfx", old_pfx), ("active.pfx", new_pfx)):
        target = pki / name
        target.write_bytes(source.read_bytes())
        target.chmod(0o444)
    template = json.loads(original_config.read_text())
    password = template["DataProtection"]["CertificatePassword"]

    def configuration(name, active, historical):
        value = json.loads(original_config.read_text())
        value["DataProtection"].update({
            "CertificatePath": "/var/fullnet/pki/" + active,
            "HistoricalCertificatePaths": ["/var/fullnet/pki/" + item for item in historical],
            "HistoricalCertificatePasswords": [password] * len(historical),
        })
        path = root / ("keyring-" + name + ".json")
        path.write_text(json.dumps(value))
        path.chmod(0o444)
        return [argument.replace(str(original_pki), str(pki)).replace(str(original_config), str(path))
                for argument in assets["hostArguments"]]

    rotated = configuration("rotated", "active.pfx", ["historical.pfx"])
    active_only = configuration("active-only", "active.pfx", [])
    rollback = configuration("rollback", "historical.pfx", ["active.pfx"])

    def key_id(token):
        # 按 ASP.NET Core 10 的公开载荷格式读取 KeyId，只用于验收，不解析业务令牌。
        data = base64.b64decode(token + "=" * (-len(token) % 4), altchars=b"-_", validate=True)
        if len(data) < 20 or data[:4] != bytes.fromhex("09f0c9f0"):
            raise RuntimeError("rotation_payload_format_invalid")
        return str(uuid.UUID(bytes_le=data[4:20]))

    invoke("worker", "unprotect", text=old_token, arguments=active_only, rejected=True)
    if invoke("worker", "unprotect", text=old_token, arguments=rotated) != old_plaintext:
        raise RuntimeError("rotation_historical_decryption_failed")
    created_id = invoke("api", "rotate", arguments=rotated)
    if str(uuid.UUID(created_id)) != created_id or created_id == key_id(old_token):
        raise RuntimeError("rotation_new_key_not_created")
    plaintext = secrets.token_hex(32)
    new_token = invoke("worker", "protect", text=plaintext, arguments=rotated)
    if key_id(new_token) != created_id:
        raise RuntimeError("rotation_new_key_not_used")
    for token, expected in ((old_token, old_plaintext), (new_token, plaintext)):
        if invoke("api", "unprotect", text=token, arguments=rotated) != expected:
            raise RuntimeError("rotation_mixed_generation_failed")
    if invoke("worker", "unprotect", text=new_token, arguments=active_only) != plaintext:
        raise RuntimeError("rotation_active_certificate_cannot_decrypt_new_key")
    invoke("worker", "unprotect", text=old_token, arguments=active_only, rejected=True)
    invoke("api", "unprotect", text=new_token, arguments=assets["hostArguments"], rejected=True)
    for token, expected in ((old_token, old_plaintext), (new_token, plaintext)):
        if invoke("worker", "unprotect", text=token, arguments=rollback) != expected:
            raise RuntimeError("rotation_rollback_history_failed")
    count = invoke("api", "inspect", arguments=rotated)
    if not count.isdigit() or int(count) < 2:
        raise RuntimeError("rotation_encrypted_key_generations_missing")
    print("candidate_keyring_rotation_verified: new_key_used=true historical=true rollback=true encrypted_keys=" + count, flush=True)
    return {"oldKeyId": key_id(old_token), "newKeyId": created_id, "newKeyUsedVerified": True,
            "historicalCertificateDecryptionVerified": True, "activeOnlyNewPayloadVerified": True,
            "activeOnlyOldPayloadRejected": True, "oldCertificateOnlyNewPayloadRejected": True,
            "rollbackWithNewCertificateHistoryVerified": True, "encryptedKeyCount": int(count),
            "oldCertificateSha256": hashlib.sha256(old_pfx.read_bytes()).hexdigest(),
            "newCertificateSha256": hashlib.sha256(new_pfx.read_bytes()).hexdigest(),
            "businessHostConsumerVerified": False, "productionRotationVerified": False}
