#!/usr/bin/env python3
"""精确登记单个内网网关的两个代理地址，条件写入并复核，不滚动业务宿主。"""
import argparse
import ipaddress
import json
import os
import subprocess
import sys
import uuid

NAMESPACE = "fullnet-production"
CONFIGURATION = "fullnet-runtime"
POLICY = "fullnet-internal-edge"
OPERATION = "fullnet.io/gateway-trust-operation"


class TrustFailure(Exception):
    """固定阶段码；凭据、命令参数、资源正文与原始响应不进入错误输出。"""


def require(condition, code):
    if not condition:
        raise TrustFailure(code)


def ipv4(value):
    address = ipaddress.IPv4Address(value)
    require(not (address.is_loopback or address.is_link_local or address.is_multicast or address.is_unspecified
                 or int(address) == 0xFFFFFFFF), "gateway_address_invalid")
    return str(address)


class Client:
    def __init__(self, prefix):
        self.prefix = prefix

    def command(self, arguments, payload=None):
        try:
            result = subprocess.run([*self.prefix, *arguments],
                input=json.dumps(payload) if payload is not None else None,
                stdin=subprocess.DEVNULL if payload is None else None,
                capture_output=True, text=True, timeout=20)
        except (OSError, subprocess.SubprocessError):
            raise TrustFailure("kubernetes_command_failed") from None
        require(result.returncode == 0 and len(result.stdout) <= 1048576, "kubernetes_command_failed")
        return json.loads(result.stdout) if result.stdout.strip() else None

    def get(self, kind, name):
        return self.command(["-n", NAMESPACE, "get", kind, name, "--ignore-not-found", "-o", "json"])


def metadata(resource, kind, name):
    require(isinstance(resource, dict) and resource.get("kind") == kind, "resource_identity_invalid")
    meta = resource["metadata"]
    require(meta.get("name") == name and meta.get("namespace") == NAMESPACE and
            bool(meta.get("uid")) and bool(meta.get("resourceVersion")) and not meta.get("deletionTimestamp"),
            "resource_identity_invalid")
    return meta


def gateway_node(client, primary):
    payload = client.command(["get", "nodes", "-o", "json"])
    nodes = [node for node in payload["items"] if any(
        address.get("type") == "InternalIP" and address.get("address") == primary
        for address in node.get("status", {}).get("addresses", []))]
    require(len(nodes) == 1, "gateway_node_not_unique")
    node = nodes[0]
    require(node["metadata"].get("uid") and not node["metadata"].get("deletionTimestamp") and
            any(condition.get("type") == "Ready" and condition.get("status") == "True"
                for condition in node["status"].get("conditions", [])), "gateway_node_not_ready")
    network = ipaddress.IPv4Network(node["spec"]["podCIDR"])
    require(16 <= network.prefixlen <= 30 and network.is_private and
            ipaddress.IPv4Address(primary) not in network, "gateway_pod_cidr_invalid")
    overlay = ipv4(str(network.network_address))
    return {"uid": node["metadata"]["uid"], "name": node["metadata"]["name"], "overlay": overlay}


def trust_key(key):
    return key.casefold() == "trustedproxy" or key.casefold().startswith("trustedproxy__")


def desired_data(resource, primary, overlay):
    data = resource.get("data", {})
    require(isinstance(data, dict) and all(isinstance(key, str) and isinstance(value, str)
            for key, value in data.items()), "runtime_configuration_invalid")
    require(not any(trust_key(key) for key in resource.get("binaryData", {})), "binary_trust_configuration_invalid")
    desired = {key: value for key, value in data.items() if not trust_key(key)}
    desired.update({"TrustedProxy__Enabled": "true", "TrustedProxy__ForwardLimit": "1",
                    "TrustedProxy__KnownProxies__0": primary, "TrustedProxy__KnownProxies__1": overlay})
    require(not resource.get("immutable") or desired == data, "runtime_configuration_immutable")
    return desired


def desired_policy(primary, overlay):
    return {"podSelector": {"matchLabels": {"app.kubernetes.io/component": "api"}},
            "policyTypes": ["Ingress"], "ingress": [{
                "from": [{"ipBlock": {"cidr": address + "/32"}} for address in (primary, overlay)],
                "ports": [{"protocol": "TCP", "port": 8080}]}]}


def update(client, kind, name, field, desired, previous, operation, state, key, recovered):
    if previous is not None and previous.get(field, {}) == desired:
        state[key] = "verified"
        return previous
    previous_meta = metadata(previous, kind, name) if previous is not None else None
    annotations = {**(previous_meta.get("annotations", {}) if previous_meta else {}), OPERATION: operation}
    # 先记录未知结果；响应丢失时只有本轮标记、身份和完整内容均吻合才接受为已写入。
    state[key] = "unknown"
    failed_response = False
    try:
        if previous is None:
            client.command(["-n", NAMESPACE, "create", "-f", "-", "-o", "json"], {
                "apiVersion": "networking.k8s.io/v1", "kind": kind,
                "metadata": {"name": name, "namespace": NAMESPACE, "annotations": annotations}, field: desired})
        else:
            patch = [{"op": "test", "path": "/metadata/uid", "value": previous_meta["uid"]},
                     {"op": "test", "path": "/metadata/resourceVersion", "value": previous_meta["resourceVersion"]},
                     {"op": "add", "path": "/metadata/annotations", "value": annotations},
                     {"op": "add", "path": "/" + field, "value": desired}]
            client.command(["-n", NAMESPACE, "patch", kind, name, "--type=json", "--patch-file=/dev/stdin", "-o", "json"], patch)
    except TrustFailure:
        failed_response = True
    observed = client.get(kind, name)
    meta = metadata(observed, kind, name)
    if (previous_meta and meta["uid"] != previous_meta["uid"] or
            meta.get("annotations", {}).get(OPERATION) != operation or observed.get(field) != desired):
        state[key] = "not_verified"
        raise TrustFailure("resource_write_not_verified")
    state[key] = "verified"
    if failed_response:
        recovered.append(key)
    return observed


def configure(client, primary, state):
    node = gateway_node(client, primary)
    configuration = client.get("ConfigMap", CONFIGURATION)
    metadata(configuration, "ConfigMap", CONFIGURATION)
    policy = client.get("NetworkPolicy", POLICY)
    if policy is not None:
        metadata(policy, "NetworkPolicy", POLICY)
    data = desired_data(configuration, primary, node["overlay"])
    spec = desired_policy(primary, node["overlay"])
    require(gateway_node(client, primary) == node, "gateway_node_changed")
    operation, recovered = str(uuid.uuid4()), []
    # 跨资源没有事务；先登记精确策略，后续配置失败不自动恢复旧的宽信任/宽策略。
    policy = update(client, "NetworkPolicy", POLICY, "spec", spec, policy, operation, state, "networkPolicy", recovered)
    configuration = update(client, "ConfigMap", CONFIGURATION, "data", data, configuration, operation, state, "runtimeConfig", recovered)
    require(gateway_node(client, primary) == node, "gateway_node_changed")
    for kind, name, field, expected, desired, key in (
        ("NetworkPolicy", POLICY, "spec", policy, spec, "networkPolicy"),
        ("ConfigMap", CONFIGURATION, "data", configuration, data, "runtimeConfig")):
        state[key] = "unknown"
        observed = client.get(kind, name)
        meta = metadata(observed, kind, name)
        if meta["uid"] != expected["metadata"]["uid"] or observed.get(field) != desired:
            state[key] = "not_verified"
            raise TrustFailure("resource_changed_after_write")
        state[key] = "verified"
    return {"status": "configured", **state, "gateway": primary, "overlay": node["overlay"],
            "writeResponsesRecovered": recovered, "apiRolloutRequired": True, "runtimeActivationVerified": False}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("primary")
    parser.add_argument("--kubectl")
    parser.add_argument("--context")
    options = parser.parse_args(argv)
    state = {"networkPolicy": "not_attempted", "runtimeConfig": "not_attempted"}
    try:
        primary = ipv4(options.primary)
        if options.kubectl:
            require(bool(options.context), "explicit_context_required")
            prefix = [options.kubectl, "--context", options.context]
        else:
            require(os.geteuid() == 0, "root_required")
            prefix = ["k3s", "kubectl", "--kubeconfig", "/etc/rancher/k3s/k3s.yaml"]
            if options.context:
                prefix += ["--context", options.context]
        result = configure(Client([*prefix, "--request-timeout=15s"]), primary, state)
    except TrustFailure as error:
        code = str(error)
    except (OSError, ValueError, KeyError, TypeError, AttributeError):
        code = "gateway_trust_failed"
    else:
        print(json.dumps(result, separators=(",", ":")))
        return 0
    print(json.dumps({"status": "failed", "code": code, **state}, separators=(",", ":")), file=sys.stderr)
    return 1


if __name__ == "__main__":
    sys.exit(main())
