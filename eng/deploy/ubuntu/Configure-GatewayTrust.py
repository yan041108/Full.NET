#!/usr/bin/env python3
"""登记唯一网关在 Flannel 跨节点 SNAT 后的主机地址，不信任整个 Pod 网段。"""
import ipaddress
import json
import os
import subprocess
import sys

assert os.geteuid() == 0
primary = str(ipaddress.IPv4Address(sys.argv[1]))
kubectl = ["k3s", "kubectl"]
nodes = json.loads(subprocess.check_output(kubectl + ["get", "nodes", "-o", "json"]))["items"]
node = [n for n in nodes if any(a["type"] == "InternalIP" and a["address"] == primary for a in n["status"]["addresses"])]
assert len(node) == 1
gateway_overlay = str(ipaddress.IPv4Network(node[0]["spec"]["podCIDR"]).network_address)
subprocess.run(kubectl + ["-n", "fullnet-production", "patch", "configmap", "fullnet-runtime", "--type=merge",
    "-p", json.dumps({"data": {"TrustedProxy__KnownProxies__0": primary, "TrustedProxy__KnownProxies__1": gateway_overlay}})], check=True)
resource = {"apiVersion": "networking.k8s.io/v1", "kind": "NetworkPolicy",
    "metadata": {"name": "fullnet-internal-edge", "namespace": "fullnet-production"},
    "spec": {"podSelector": {"matchLabels": {"app.kubernetes.io/component": "api"}}, "policyTypes": ["Ingress"],
             "ingress": [{"from": [{"ipBlock": {"cidr": address + "/32"}} for address in [primary, gateway_overlay]],
                          "ports": [{"protocol": "TCP", "port": 8080}]}]}}
subprocess.run(kubectl + ["apply", "--server-side", "--field-manager=fullnet-deployment", "-f", "-"],
               input=json.dumps(resource), text=True, check=True)
print("已精确登记主网关的 LAN 与 Flannel 主机地址；需要滚动 API 才能加载代理信任配置。")
