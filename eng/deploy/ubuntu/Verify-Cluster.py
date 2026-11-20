#!/usr/bin/env python3
"""仅在本轮临时命名空间核对两个节点的 DNS 与 Pod HTTP，清理确认后才输出成功。"""
import argparse
import ipaddress
import json
import os
from pathlib import Path
import re
import select
import signal
import subprocess
import sys
import time
import uuid

RUN_LABEL = "fullnet.io/network-check-run"


class CheckFailure(Exception):
    """只输出固定阶段码，不传播 kubectl 原始输出或 kubeconfig。"""


def require(condition, code):
    if not condition:
        raise CheckFailure(code)


def command(prefix, arguments, phase, seconds=20, payload=None):
    process = subprocess.Popen([*prefix, *arguments], stdin=subprocess.PIPE if payload is not None else subprocess.DEVNULL,
                               stdout=subprocess.PIPE, stderr=subprocess.DEVNULL)
    deadline = time.monotonic() + seconds
    result = bytearray()
    try:
        if payload is not None:
            process.stdin.write(json.dumps(payload).encode())
            process.stdin.close()
        while True:
            remaining = deadline - time.monotonic()
            require(remaining > 0 and select.select([process.stdout], [], [], remaining)[0], phase + "_timeout")
            block = os.read(process.stdout.fileno(), 65536)
            if not block:
                break
            result.extend(block)
            require(len(result) <= 4 * 1024 * 1024, phase + "_output_limit")
        remaining = deadline - time.monotonic()
        require(remaining > 0 and process.wait(timeout=remaining) == 0, phase + "_failed")
        return result.decode()
    finally:
        if process.poll() is None:
            process.kill()
            process.wait(timeout=3)
        process.stdout.close()
        if process.stdin and not process.stdin.closed:
            process.stdin.close()


def node(prefix, name):
    item = json.loads(command(prefix, ["get", "node", name, "-o", "json"], "node_read"))
    require(item["metadata"]["name"] == name and item["metadata"].get("uid") and
            not item["metadata"].get("deletionTimestamp") and item["spec"].get("unschedulable") is not True and
            any(c.get("type") == "Ready" and c.get("status") == "True" for c in item["status"].get("conditions", [])),
            "node_not_ready_or_schedulable")
    return {"name": name, "uid": item["metadata"]["uid"]}


def pod(name, node_name, image, namespace, run_id):
    server = name == "server"
    container = {"name": "probe", "image": image, "imagePullPolicy": "IfNotPresent",
                 "command": ["sh", "-c", f"printf '%s\\n' '{run_id}' > /work/index.html; exec httpd -f -p 8080 -h /work"]
                            if server else ["sleep", "300"],
                 "resources": {"requests": {"cpu": "10m", "memory": "8Mi"}, "limits": {"cpu": "100m", "memory": "64Mi"}},
                 "securityContext": {"allowPrivilegeEscalation": False, "readOnlyRootFilesystem": True,
                                     "capabilities": {"drop": ["ALL"]}}}
    specification = {"nodeName": node_name, "restartPolicy": "Never", "activeDeadlineSeconds": 300,
                     "terminationGracePeriodSeconds": 1,
                     "automountServiceAccountToken": False, "enableServiceLinks": False,
                     "securityContext": {"runAsNonRoot": True, "runAsUser": 65534, "runAsGroup": 65534,
                                         "fsGroup": 65534, "seccompProfile": {"type": "RuntimeDefault"}},
                     "containers": [container]}
    if server:
        specification["volumes"] = [{"name": "work", "emptyDir": {"sizeLimit": "1Mi"}}]
        container["volumeMounts"] = [{"name": "work", "mountPath": "/work"}]
        container["readinessProbe"] = {"tcpSocket": {"port": 8080}, "periodSeconds": 1, "timeoutSeconds": 1}
    return {"apiVersion": "v1", "kind": "Pod", "metadata": {"name": name, "namespace": namespace,
            "labels": {RUN_LABEL: run_id}}, "spec": specification}


def pods(prefix, namespace, run_id, expected, image):
    items = json.loads(command(prefix, ["get", "pods", "-n", namespace, "-l", RUN_LABEL + "=" + run_id, "-o", "json"],
                               "pod_read"))["items"]
    require(len(items) == 2, "pod_count_invalid")
    result = {}
    for item in items:
        meta, spec, status = item["metadata"], item["spec"], item["status"]
        name = meta["name"]
        require(name in expected and name not in result and meta.get("uid") and
                meta.get("namespace") == namespace and meta.get("labels", {}).get(RUN_LABEL) == run_id and
                not meta.get("deletionTimestamp") and spec.get("nodeName") == expected[name] and
                spec.get("hostNetwork") is not True and status.get("phase") == "Running" and
                any(c.get("type") == "Ready" and c.get("status") == "True" for c in status.get("conditions", [])),
                "pod_identity_or_readiness_invalid")
        containers = spec["containers"]
        current = status.get("containerStatuses", [])
        require(len(containers) == len(current) == 1 and containers[0].get("image") == image and
                current[0].get("name") == "probe" and current[0].get("ready") is True and
                "running" in current[0].get("state", {}) and current[0].get("imageID") and current[0].get("containerID"),
                "pod_container_invalid")
        address = ipaddress.ip_address(status["podIP"])
        require(not (address.is_loopback or address.is_multicast or address.is_unspecified), "pod_address_invalid")
        result[name] = {"uid": meta["uid"], "node": spec["nodeName"], "address": str(address),
                        "containerId": current[0]["containerID"], "imageId": current[0]["imageID"]}
    require(result["server"]["uid"] != result["client"]["uid"] and
            result["server"]["imageId"] == result["client"]["imageId"], "pod_images_or_uids_differ")
    return result


def check(options):
    require(options.primary != options.secondary, "distinct_nodes_required")
    prefix = [options.kubectl]
    if Path(options.kubectl).name == "k3s":
        prefix.append("kubectl")
    if options.context:
        prefix.extend(["--context", options.context])
    prefix.append("--request-timeout=15s")
    nodes = [node(prefix, name) for name in (options.primary, options.secondary)]
    run_id = uuid.uuid4().hex
    namespace = "fullnet-infra-check-" + run_id[:16]
    namespace_uid = None
    created = False
    failure = None
    result = None
    try:
        # 先记录创建意图；响应丢失时仍按本轮随机标签核对归属并清理。
        created = True
        resource = {"apiVersion": "v1", "kind": "Namespace", "metadata": {"name": namespace,
                    "labels": {RUN_LABEL: run_id, "pod-security.kubernetes.io/enforce": "restricted"}}}
        receipt = json.loads(command(prefix, ["create", "-f", "-", "-o", "json"], "namespace_create", payload=resource))
        require(receipt["metadata"]["name"] == namespace and receipt["metadata"].get("uid") and
                receipt["metadata"].get("labels", {}).get(RUN_LABEL) == run_id, "namespace_identity_invalid")
        namespace_uid = receipt["metadata"]["uid"]
        expected = {"server": options.primary, "client": options.secondary}
        for name, node_name in expected.items():
            command(prefix, ["create", "-f", "-"], "pod_create",
                    payload=pod(name, node_name, options.image, namespace, run_id))
        command(prefix, ["wait", "-n", namespace, "--for=condition=Ready", "pod/server", "pod/client", "--timeout=180s"],
                "pod_ready", seconds=195)
        before = pods(prefix, namespace, run_id, expected, options.image)
        command(prefix, ["exec", "-n", namespace, "client", "-c", "probe", "--",
                         "nslookup", "kubernetes.default.svc.cluster.local"], "cluster_dns", seconds=30)
        address = before["server"]["address"]
        if ":" in address:
            address = "[" + address + "]"
        response = command(prefix, ["exec", "-n", namespace, "client", "-c", "probe", "--",
                                    "wget", "-qO-", "-T", "10", "http://" + address + ":8080"], "cross_node_http", seconds=25)
        require(response.strip() == run_id, "cross_node_http_payload_invalid")
        require(pods(prefix, namespace, run_id, expected, options.image) == before, "pod_changed_during_check")
        require([node(prefix, name) for name in (options.primary, options.secondary)] == nodes, "node_changed_during_check")
        result = {"status": "passed", "runId": run_id, "nodes": nodes, "pods": before,
                  "dnsResolved": True, "crossNodeHttp": True, "capacity": "Capacity-not-verified"}
    except (CheckFailure, OSError, ValueError, KeyError, TypeError, subprocess.SubprocessError) as error:
        failure = str(error) if isinstance(error, CheckFailure) else "cluster_check_failed"
    finally:
        if created:
            try:
                raw = command(prefix, ["get", "namespace", namespace, "--ignore-not-found", "-o", "json"], "namespace_cleanup_read")
                if raw.strip():
                    current = json.loads(raw)["metadata"]
                    require(current.get("labels", {}).get(RUN_LABEL) == run_id and
                            (namespace_uid is None or current.get("uid") == namespace_uid), "namespace_cleanup_owner_changed")
                    command(prefix, ["delete", "namespace", namespace, "--wait=true", "--timeout=60s"],
                            "namespace_delete", seconds=75)
                    require(not command(prefix, ["get", "namespace", namespace, "--ignore-not-found", "-o", "json"],
                                        "namespace_delete_confirm").strip(), "namespace_delete_not_confirmed")
            except (CheckFailure, OSError, ValueError, KeyError, TypeError, subprocess.SubprocessError):
                failure = "namespace_cleanup_not_confirmed"
    if failure:
        raise CheckFailure(failure + ":" + namespace)
    result["namespaceDeleted"] = True
    return result


def name_value(value):
    if not re.fullmatch(r"[a-z0-9](?:[a-z0-9.-]{0,251}[a-z0-9])?", value):
        raise argparse.ArgumentTypeError("invalid_node_name")
    return value


def image_value(value):
    if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._:/@+-]{0,511}", value):
        raise argparse.ArgumentTypeError("invalid_image")
    return value


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("primary", type=name_value)
    parser.add_argument("secondary", type=name_value)
    parser.add_argument("--kubectl", default="k3s")
    parser.add_argument("--context")
    parser.add_argument("--image", type=image_value, default="rancher/mirrored-library-busybox:1.37.0")
    options = parser.parse_args(argv)
    try:
        result = check(options)
    except (CheckFailure, OSError, ValueError, KeyError, TypeError, subprocess.SubprocessError) as error:
        code = str(error) if isinstance(error, CheckFailure) else "cluster_check_failed"
        print(json.dumps({"status": "failed", "code": code}), file=sys.stderr)
        return 1
    print(json.dumps(result, separators=(",", ":")))
    return 0


if __name__ == "__main__":
    def interrupted(*_):
        raise CheckFailure("cluster_check_interrupted")
    signal.signal(signal.SIGTERM, interrupted)
    signal.signal(signal.SIGINT, interrupted)
    sys.exit(main())
