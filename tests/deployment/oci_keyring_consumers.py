"""调用候选实际模块保护器，验证用途隔离及通知保护器的独立介质恢复。"""
import importlib.util
from pathlib import Path
import re
import secrets


def verify(root, assets, assets_root, invoke, generic_token):
    inventories = {}
    expected = {"Full.NET.Modules." + name + ".dll" for name in ("Identity", "Notifications", "Payments")}
    for role in ("api", "worker"):
        lines = invoke(role, "consumer-inventory").splitlines()
        values = dict(line.split("=", 1) for line in lines)
        if len(lines) != len(expected) or set(values) != expected or not all(re.fullmatch(r"[0-9a-f]{64}", sha) for sha in values.values()):
            raise RuntimeError("consumer_candidate_inventory_invalid")
        inventories[role] = values
    aliases = ("notification", "ldap", "oauth", "totp", "payment-api", "payment-private")
    tokens, plaintexts, results = {}, {}, {}
    for alias in aliases:
        first, second = secrets.token_hex(32), secrets.token_hex(32)
        token = invoke("api", "consumer-protect", purpose=alias, text=first)
        if invoke("worker", "consumer-unprotect", purpose=alias, text=token) != first:
            raise RuntimeError("consumer_forward_failed_" + alias)
        reverse = invoke("worker", "consumer-protect", purpose=alias, text=second)
        if invoke("api", "consumer-unprotect", purpose=alias, text=reverse) != second:
            raise RuntimeError("consumer_reverse_failed_" + alias)
        tokens[alias], plaintexts[alias] = token, first
        results[alias] = {"apiToWorkerVerified": True, "workerToApiVerified": True}
    # 固定相邻用途均不同；支付 API 密钥与私钥也必须相互隔离。
    for index, alias in enumerate(aliases):
        wrong = aliases[(index + 1) % len(aliases)]
        invoke("worker", "consumer-unprotect", purpose=wrong, text=tokens[alias], rejected=True)
        results[alias]["differentConsumerPurposeRejected"] = True
    invoke("worker", "consumer-unprotect", purpose="notification", text=generic_token, rejected=True)
    invoke("worker", "consumer-unprotect", purpose="payment-api", text=tokens["payment-private"], rejected=True)

    def notification_invoke(role, mode, purpose="shared", **kwargs):
        if mode in ("protect", "unprotect"):
            return invoke(role, "consumer-" + mode, purpose="notification", **kwargs)
        return invoke(role, mode, purpose=purpose, **kwargs)

    spec = importlib.util.spec_from_file_location("consumer_recovery", Path(__file__).with_name("oci_keyring_recovery.py"))
    recovery = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(recovery)
    restored = recovery.verify(root, assets, assets_root, notification_invoke,
                               tokens["notification"], plaintexts["notification"])
    print("candidate_module_protectors_verified: consumers=6 directions=2 purpose_rejections=8 notification_recovery=true", flush=True)
    return {"scope": "actual-module-protector-components-in-independent-processes", "moduleAssemblies": inventories,
            "protectors": results, "genericProbePurposeRejected": True, "paymentPurposesBothDirectionsRejected": True,
            "notificationRecipientMediaRecovery": restored,
            "moduleProtectorComponentsVerified": True, "businessHostPipelineVerified": False,
            "externalDeliveryVerified": False, "paymentChannelVerified": False, "nativeAotVerified": False}
