#!/usr/bin/env python3
"""只读采集部署与前端身份，全部核验后原子封存；不操作账号、数据库或网关配置。"""
import argparse
import datetime
import fcntl
import hashlib
import json
import os
from pathlib import Path
import re
import select
import signal
import shutil
import stat
import subprocess
import sys
import tempfile
import time
import urllib.request

ROLES = ("api", "worker")
LABEL = "app.kubernetes.io/component"


class InvalidEvidence(Exception):
    """只对外暴露固定错误码，避免原始命令输出包含运行秘密。"""


def require(condition, code):
    if not condition:
        raise InvalidEvidence(code)


def stable_text(value):
    return isinstance(value, str) and 0 < len(value) <= 512 and not any(ord(c) < 32 for c in value)


def snapshot(deployments, pods, names, replicas):
    result = {"deployments": [], "pods": []}
    selectors = {}
    images = {}
    for role in ROLES:
        selected = [d for d in deployments["items"] if d["metadata"]["name"] == names[role]]
        require(len(selected) == 1, "deployment_missing_or_duplicated")
        item = selected[0]
        meta, spec, status = item["metadata"], item["spec"], item["status"]
        require(not meta.get("deletionTimestamp") and meta.get("labels", {}).get(LABEL) == role,
                "deployment_identity_invalid")
        generation = meta.get("generation")
        require(type(generation) is int and generation > 0 and status.get("observedGeneration") == generation,
                "deployment_generation_not_observed")
        require(spec.get("replicas") == replicas and all(status.get(field) == replicas for field in
                ("replicas", "updatedReplicas", "readyReplicas", "availableReplicas")) and
                status.get("unavailableReplicas", 0) == 0, "deployment_rollout_incomplete")
        selector = spec["selector"]
        require(not selector.get("matchExpressions") and selector.get("matchLabels", {}).get(LABEL) == role,
                "deployment_selector_unsupported")
        selectors[role] = selector["matchLabels"]
        containers = [c for c in spec["template"]["spec"]["containers"] if c["name"] == role]
        require(len(containers) == 1 and stable_text(containers[0].get("image")), "deployment_image_invalid")
        images[role] = containers[0]["image"]
        require(stable_text(meta.get("uid")), "deployment_identity_invalid")
        result["deployments"].append({"role": role, "name": meta["name"], "uid": meta["uid"],
                                      "generation": generation, "replicas": replicas, "image": images[role]})
    for role in ROLES:
        selected = [p for p in pods["items"] if all(p["metadata"].get("labels", {}).get(k) == v
                                                   for k, v in selectors[role].items())]
        require(len(selected) == replicas, "pod_count_changed_or_terminating")
        nodes = set()
        for pod in selected:
            meta, spec, status = pod["metadata"], pod["spec"], pod["status"]
            require(not meta.get("deletionTimestamp") and status.get("phase") == "Running" and
                    any(c.get("type") == "Ready" and c.get("status") == "True"
                        for c in status.get("conditions", [])), "pod_not_ready")
            containers = [c for c in spec["containers"] if c["name"] == role]
            current = [c for c in status.get("containerStatuses", []) if c["name"] == role]
            require(len(containers) == len(current) == 1 and containers[0].get("image") == images[role] and
                    current[0].get("ready") is True and "running" in current[0].get("state", {}) and
                    stable_text(current[0].get("imageID")) and stable_text(current[0].get("containerID")),
                    "pod_image_or_container_invalid")
            require(all(stable_text(v) for v in (meta.get("name"), meta.get("uid"), spec.get("nodeName"))),
                    "pod_identity_invalid")
            nodes.add(spec["nodeName"])
            result["pods"].append({"role": role, "name": meta["name"], "uid": meta["uid"],
                                   "node": spec["nodeName"], "image": images[role], "imageId": current[0]["imageID"],
                                   "containerId": current[0]["containerID"], "restarts": current[0].get("restartCount", 0)})
        require(len(nodes) == replicas, "role_replicas_not_on_distinct_nodes")
        require(len({p["imageId"] for p in result["pods"] if p["role"] == role}) == 1,
                "role_image_ids_differ")
    require(len({p["uid"] for p in result["pods"]}) == len(result["pods"]), "pod_identity_invalid")
    result["pods"].sort(key=lambda p: (p["role"], p["name"]))
    return result


def command_json(command):
    # 原始输出只留进程内；限制字节与时间，失败不回显 kubeconfig 或集群报错。
    process = subprocess.Popen(command, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL)
    data = bytearray()
    deadline = time.monotonic() + 20
    try:
        while True:
            remaining = deadline - time.monotonic()
            require(remaining > 0 and select.select([process.stdout], [], [], remaining)[0], "cluster_command_timeout")
            chunk = os.read(process.stdout.fileno(), 65536)
            if not chunk:
                break
            data.extend(chunk)
            require(len(data) <= 4 * 1024 * 1024, "cluster_response_too_large")
        remaining = deadline - time.monotonic()
        require(remaining > 0 and process.wait(timeout=remaining) == 0, "cluster_command_failed")
        return json.loads(data)
    finally:
        if process.poll() is None:
            process.kill()
            process.wait(timeout=3)
        process.stdout.close()


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs):
        return None


def health_response(port):
    # Linux CLI 在主线程独占此计时器；socket timeout 不能限制持续滴流的响应头或正文。
    def expired(*_):
        raise InvalidEvidence("pod_health_timeout")

    previous = signal.signal(signal.SIGALRM, expired)
    try:
        signal.setitimer(signal.ITIMER_REAL, 3)
        opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect())
        with opener.open(f"http://127.0.0.1:{port}/health/ready", timeout=3) as response:
            body = response.read(513)
            require(response.status == 200 and len(body) <= 512 and body.strip() == b"Healthy", "pod_health_not_healthy")
    finally:
        # 先撤销本轮闹钟再还原处理器，失败也不能影响后续采集或隧道回收。
        signal.setitimer(signal.ITIMER_REAL, 0)
        signal.signal(signal.SIGALRM, previous)


def health(prefix, pod, port):
    process = subprocess.Popen([*prefix, "port-forward", "pod/" + pod["name"], f":{port}",
                                "--address=127.0.0.1"], stdout=subprocess.PIPE, stderr=subprocess.DEVNULL)
    try:
        deadline = time.monotonic() + 10
        line = bytearray()
        while b"\n" not in line:
            remaining = deadline - time.monotonic()
            require(remaining > 0 and len(line) < 1024, "health_tunnel_unavailable")
            require(select.select([process.stdout], [], [], remaining)[0], "health_tunnel_unavailable")
            chunk = os.read(process.stdout.fileno(), 1)
            require(bool(chunk), "health_tunnel_unavailable")
            line.extend(chunk)
        match = re.fullmatch(rb"Forwarding from 127\.0\.0\.1:([0-9]{1,5}) -> [0-9]+\r?\n", bytes(line))
        require(match is not None and 1 <= int(match[1]) <= 65535, "health_tunnel_unavailable")
        health_response(int(match[1]))
        require(process.poll() is None, "health_tunnel_unavailable")
        return {"uid": pod["uid"], "role": pod["role"], "status": "Healthy"}
    finally:
        # 只终止本轮隧道；即使未输出端口也必须等待回收，不修改 NetworkPolicy。
        if process.poll() is None:
            process.terminate()
            try:
                process.wait(timeout=3)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=3)
        process.stdout.close()


def digest(path):
    before = path.lstat()
    require(stat.S_ISREG(before.st_mode) and before.st_nlink == 1, "input_link_or_type_invalid")
    with path.open("rb") as stream:
        value = hashlib.file_digest(stream, "sha256").hexdigest()
    after = path.lstat()
    identity = lambda s: (s.st_dev, s.st_ino, s.st_size, s.st_mtime_ns, s.st_ctime_ns)
    require(identity(before) == identity(after), "release_inputs_changed")
    return value


def manifests(release, frontend):
    files = {}
    for path in sorted(frontend.rglob("*")):
        require(not path.is_symlink(), "input_link_or_type_invalid")
        if path.is_file():
            files[path.relative_to(frontend).as_posix()] = digest(path)
            require(len(files) <= 10000, "frontend_file_limit")
    require("index.html" in files, "frontend_index_missing")
    artifacts = {}
    for path in sorted(release.iterdir()):
        if path.name.endswith((".manifest.json", ".oci.tar")) or path.name in ("source-manifest.json", "chart.tar"):
            artifacts[path.name] = digest(path)
    require("source-manifest.json" in artifacts and "chart.tar" in artifacts, "release_artifact_missing")
    require(len(artifacts) <= 128, "release_artifact_limit")
    return {"frontend": files, "artifacts": artifacts}


def finalize(args):
    root = args.release_root.resolve(strict=True)
    release = args.release.resolve(strict=True)
    require(release != root and release.is_relative_to(root) and release.is_dir(), "release_path_invalid")
    frontend = (args.frontend or release / "frontend").resolve(strict=True)
    require(frontend != root and frontend.is_relative_to(root) and frontend.is_dir(), "frontend_path_invalid")
    destination = release / "runtime-evidence"
    require(not destination.exists() and not destination.is_symlink(), "evidence_already_exists")
    # 与正式发布共用锁，禁止采集时本入口或角色发布同时改变候选。
    with args.lock.open("a") as lock:
        try:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            raise InvalidEvidence("release_in_progress") from None
        prefix = [args.kubectl]
        if Path(args.kubectl).name == "k3s":
            prefix.append("kubectl")
        prefix.extend(["--namespace", args.namespace, "--request-timeout=15s"])
        names = {"api": args.api_deployment, "worker": args.worker_deployment}
        read = lambda kind: command_json([*prefix, "get", kind, "-o", "json"])
        before = snapshot(read("deployments"), read("pods"), names, args.replicas)
        expected = {"api": args.api_image, "worker": args.worker_image}
        require(all(d["image"] == expected[d["role"]] for d in before["deployments"]), "candidate_image_mismatch")
        source = manifests(release, frontend)
        reports = [health(prefix, pod, args.health_port) for pod in before["pods"]]
        after = snapshot(read("deployments"), read("pods"), names, args.replicas)
        require(before == after, "runtime_identity_changed")
        require(source == manifests(release, frontend), "release_inputs_changed")
        evidence = {"schemaVersion": 1, "releaseTag": release.name, "namespace": args.namespace,
                    "frontendReleasePath": frontend.relative_to(root).as_posix(),
                    "capturedAtUtc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
                    "runtime": before, "health": reports, "capacity": "Capacity-not-verified"}
        temporary = Path(tempfile.mkdtemp(prefix=".runtime-evidence-", dir=release))
        try:
            for name, content in (("runtime.json", evidence), ("inputs.json", source)):
                path = temporary / name
                with path.open("x", encoding="utf-8") as stream:
                    json.dump(content, stream, ensure_ascii=False, sort_keys=True)
                    stream.write("\n")
                path.chmod(0o600)
            sums = "".join(f"{digest(temporary / name)}  {name}\n" for name in ("inputs.json", "runtime.json"))
            (temporary / "SHA256SUMS").write_text(sums, encoding="utf-8")
            (temporary / "SHA256SUMS").chmod(0o600)
            require(not destination.exists() and not destination.is_symlink(), "evidence_already_exists")
            # 同文件系统原子发布目录；旧证据从不更新，不写全局 COMPLETE 或恢复成功标记。
            temporary.rename(destination)
        finally:
            if temporary.exists():
                shutil.rmtree(temporary)
    return {"status": "sealed", "releaseTag": release.name, "podsVerified": len(reports),
            "capacity": "Capacity-not-verified"}


def dns_name(value):
    if not re.fullmatch(r"[a-z0-9](?:[a-z0-9.-]{0,251}[a-z0-9])?", value):
        raise argparse.ArgumentTypeError("invalid_kubernetes_name")
    return value


def port_value(value):
    try:
        port = int(value)
    except ValueError:
        raise argparse.ArgumentTypeError("invalid_health_port") from None
    if not 1 <= port <= 65535:
        raise argparse.ArgumentTypeError("invalid_health_port")
    return port


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("release", type=Path)
    parser.add_argument("--release-root", type=Path, default=Path("/srv/fullnet/releases"))
    parser.add_argument("--frontend", type=Path)
    parser.add_argument("--namespace", type=dns_name, default="fullnet-production")
    parser.add_argument("--api-deployment", type=dns_name, default="fullnet-api")
    parser.add_argument("--worker-deployment", type=dns_name, default="fullnet-worker")
    parser.add_argument("--api-image", required=True)
    parser.add_argument("--worker-image", required=True)
    parser.add_argument("--kubectl", default="k3s")
    parser.add_argument("--lock", type=Path, default=Path("/run/fullnet-release.lock"))
    parser.add_argument("--replicas", type=int, choices=range(1, 5), default=2)
    parser.add_argument("--health-port", type=port_value, default=8080)
    args = parser.parse_args(argv)
    try:
        result = finalize(args)
    except InvalidEvidence as error:
        code = str(error)
    except (OSError, ValueError, KeyError, TypeError, subprocess.SubprocessError):
        code = "evidence_capture_failed"
    else:
        print(json.dumps(result, separators=(",", ":")))
        return 0
    print(json.dumps({"status": "failed", "code": code}, separators=(",", ":")), file=sys.stderr)
    return 1


if __name__ == "__main__":
    sys.exit(main())
