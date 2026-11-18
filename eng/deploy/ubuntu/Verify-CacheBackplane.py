#!/usr/bin/env python3
"""显式核对两个不同节点的缓存进程；不改变业务配置，只操作随机运行键和本轮临时目录。"""
import argparse
import concurrent.futures
import json
import math
import os
import pathlib
import re
import subprocess
import sys
import uuid
from dataclasses import dataclass


class VerificationFailure(Exception):
    """对外仅使用固定阶段说明，不传播命令输出中的连接参数。"""


@dataclass(frozen=True)
class Pod:
    name: str
    uid: str
    node: str


def require(condition, message):
    if not condition:
        raise VerificationFailure(message)


def command(arguments, phase, seconds=20):
    try:
        result = subprocess.run(arguments, capture_output=True, text=True, timeout=seconds)
    except (OSError, subprocess.TimeoutExpired) as error:
        raise VerificationFailure(phase + "未完成") from error
    require(result.returncode == 0, phase + "失败")
    return result.stdout


def parse_json(value, phase):
    try:
        return json.loads(value)
    except (ValueError, TypeError) as error:
        raise VerificationFailure(phase + "不是有效 JSON") from error


def pod_identity(item, release, container, require_ready=True):
    require(isinstance(item, dict), "Pod 清单格式错误")
    metadata, spec, status = (item.get(key, {}) for key in ("metadata", "spec", "status"))
    require(all(isinstance(part, dict) for part in (metadata, spec, status)), "Pod 清单格式错误")
    labels = metadata.get("labels", {})
    require(isinstance(labels, dict) and labels.get("app.kubernetes.io/instance") == release
            and labels.get("app.kubernetes.io/component") == "api", "Pod 不属于选定 API 发布")
    conditions, containers = status.get("conditions", []), spec.get("containers", [])
    require(isinstance(conditions, list) and isinstance(containers, list), "Pod 清单格式错误")
    if require_ready:
        require(not metadata.get("deletionTimestamp") and status.get("phase") == "Running"
                and any(row.get("type") == "Ready" and row.get("status") == "True"
                        for row in conditions if isinstance(row, dict)), "API Pod 尚未稳定就绪")
    require(any(row.get("name") == container for row in containers if isinstance(row, dict)), "API 容器不存在")
    values = [metadata.get("name"), metadata.get("uid"), spec.get("nodeName")]
    require(all(isinstance(value, str) and value and len(value) <= 253 for value in values), "Pod 身份不完整")
    require(all(re.fullmatch(r"[a-z0-9](?:[-a-z0-9.]*[a-z0-9])?", value) for value in (values[0], values[2])), "Pod 或节点名称无效")
    return Pod(*values)


def inspect_pods(kubectl, options):
    selector = "app.kubernetes.io/instance=" + options.release + ",app.kubernetes.io/component=api"
    inventory = parse_json(command(kubectl + ["get", "pods", "--selector", selector, "-o", "json"], "读取 Pod 清单"), "Pod 清单")
    require(isinstance(inventory, dict) and isinstance(inventory.get("items"), list), "Pod 清单格式错误")
    require(len(inventory["items"]) == 2, "要求选定发布恰有两个稳定 API Pod")
    pods = sorted((pod_identity(item, options.release, options.container) for item in inventory["items"]), key=lambda pod: pod.name)
    require(len({pod.name for pod in pods}) == 2 and len({pod.uid for pod in pods}) == 2
            and len({pod.node for pod in pods}) == 2, "要求两个不同 Pod 分布在不同节点")
    return pods


def receipt(output, role, run, pod):
    record = parse_json(output, "进程回执")
    require(isinstance(record, dict), "进程回执格式错误")
    require(record.get("role") == role and record.get("run") == run, "进程回执不属于本轮角色")
    require(type(record.get("processId")) is int and record["processId"] > 0, "进程回执缺少有效 PID")
    instance = record.get("instance")
    require(isinstance(instance, str) and 0 < len(instance) <= 256 and instance.isprintable(), "缓存实例身份无效")
    require(type(record.get("l1TtlSeconds")) is int and record["l1TtlSeconds"] == 120, "L1 到期对照不匹配")
    require(record.get("l2Removal") is True and record.get("authoritativeRefill") is True
            and record.get("health") == "Healthy", "缓存删除、回填或健康核对未通过")
    samples = record.get("receivedInvalidationMilliseconds")
    valid_time = lambda value, limit: type(value) in (int, float) and 0 <= value < limit and math.isfinite(value)
    require(isinstance(samples, list) and len(samples) == 1 and valid_time(samples[0], 5000), "缺少有效双向失效观察")
    missed = record.get("missedNotificationConvergenceMilliseconds")
    require(missed is None if role == "a" else valid_time(missed, 4000) and missed > 0, "丢通知收敛观察无效")
    # 不透传未知字段；节点和 Pod 身份只能来自本轮 Kubernetes 清单。
    return {"role": role, "run": run, "processId": record["processId"], "instance": instance,
            "l1TtlSeconds": 120, "l2Removal": True, "authoritativeRefill": True, "health": "Healthy",
            "receivedInvalidationMilliseconds": samples, "missedNotificationConvergenceMilliseconds": missed,
            "pod": pod.name, "podUid": pod.uid, "node": pod.node}


def remote(kubectl, options, pod, arguments):
    return kubectl + ["exec", pod.name, "--container", options.container, "--"] + arguments


def cleanup(kubectl, options, prepared, started, temporary, run):
    current, failed = [], False
    for pod, copied in prepared:
        try:
            raw = command(kubectl + ["get", "pod", pod.name, "-o", "json", "--ignore-not-found"], "核对清理目标")
            require(raw.strip(), "清理目标 Pod 已退出")
            actual = pod_identity(parse_json(raw, "清理目标"), options.release, options.container, require_ready=False)
            require(actual == pod, "清理目标 Pod 已更换")
            current.append((pod, copied))
        except VerificationFailure:
            failed = True
    if started:
        targets = [pod for pod, copied in current if copied]
        if not targets:
            failed = True
        else:
            try:
                command(remote(kubectl, options, targets[0], ["timeout", "--kill-after=2s", "12s", "dotnet",
                    temporary + "/Full.NET.CacheBackplaneProbe.dll", run, "cleanup"]), "清理协调键")
            except VerificationFailure:
                failed = True
    # 一个清理失败不能阻止其余本轮目录的清理；替换后的 Pod 不接受旧身份的写操作。
    for pod, _ in current:
        try:
            command(remote(kubectl, options, pod, ["rm", "-rf", "--", temporary]), "清理临时目录")
        except VerificationFailure:
            failed = True
    return not failed


def verify(options):
    publish = options.publish.resolve(strict=True)
    require(publish.is_dir() and all((publish / name).is_file() for name in (
        "Full.NET.CacheBackplaneProbe.dll", "Full.NET.CacheBackplaneProbe.deps.json",
        "Full.NET.CacheBackplaneProbe.runtimeconfig.json")), "缺少完整探针发布目录")
    require(not any(path.is_symlink() for path in publish.rglob("*")), "探针发布目录不得包含符号链接")
    require(not options.output.exists() and not options.output.is_symlink(), "回执目标已存在，拒绝覆盖")
    kubectl = (["k3s", "kubectl"] if options.kubectl == "k3s" else ["kubectl"])
    if options.context:
        kubectl += ["--context", options.context]
    kubectl += ["--namespace", options.namespace]
    pods = inspect_pods(kubectl, options)
    run = uuid.uuid4().hex
    temporary = "/tmp/fullnet-cache-verification-" + run
    prepared, results, failure, started = [], [], None, False
    try:
        for pod in pods:
            # 远端硬期限早于 kubectl 等待期限；停止本地客户端不等同于停止 Pod 中的进程。
            command(remote(kubectl, options, pod, ["timeout", "--version"]), "核对远端期限工具")
            command(remote(kubectl, options, pod, ["mkdir", "-m", "700", "--", temporary]), "创建临时目录")
            prepared.append((pod, False))
            command(kubectl + ["cp", "--container", options.container, str(publish) + "/.",
                              pod.name + ":" + temporary], "复制探针")
            prepared[-1] = (pod, True)
        started = True
        def execute(pair):
            pod, role = pair
            value = command(remote(kubectl, options, pod, ["timeout", "--kill-after=5s", "70s", "dotnet",
                temporary + "/Full.NET.CacheBackplaneProbe.dll", run, role]), "执行缓存角色 " + role, 85)
            return receipt(value, role, run, pod)
        with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
            results = list(pool.map(execute, zip(pods, ("a", "b"))))
        require(len({result["instance"] for result in results}) == 2, "两个缓存实例身份相同")
        require(inspect_pods(kubectl, options) == pods, "验收期间 Pod 或节点已变化")
    except VerificationFailure as error:
        failure = error
    finally:
        cleaned = cleanup(kubectl, options, prepared, started, temporary, run)
    if failure:
        raise failure
    require(cleaned, "验收清理未完成，不签发成功回执")
    report = {"schemaVersion": 1, "outcome": "passed", "run": run, "namespace": options.namespace,
              "release": options.release, "scope": "两个不同节点 API Pod 内的独立 SDK 进程；不代表常驻宿主或容量", "results": results}
    options.output.parent.mkdir(parents=True, exist_ok=True)
    # 独占创建防止运行期间出现的文件被覆盖；只有全部业务断言和清理完成后才签发成功。
    descriptor = os.open(options.output, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    with os.fdopen(descriptor, "w", encoding="utf-8") as stream:
        json.dump(report, stream, ensure_ascii=False, allow_nan=False, indent=2)
    return report


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("publish", type=pathlib.Path)
    parser.add_argument("output", type=pathlib.Path)
    parser.add_argument("--namespace", required=True)
    parser.add_argument("--release", default="fullnet-api")
    parser.add_argument("--container", default="api")
    parser.add_argument("--kubectl", choices=("k3s", "kubectl"), default="k3s")
    parser.add_argument("--context")
    options = parser.parse_args(argv)
    label = re.compile(r"[a-z0-9](?:[-a-z0-9]*[a-z0-9])?\Z")
    if any(not label.fullmatch(value) or len(value) > maximum for value, maximum in (
        (options.namespace, 63), (options.release, 53), (options.container, 63))):
        parser.error("命名空间、发布或容器名称无效")
    if options.context is not None and (not options.context.strip() or options.context.startswith("-")):
        parser.error("集群上下文无效")
    try:
        report = verify(options)
    except (VerificationFailure, OSError) as error:
        # 文件系统及 kubectl 错误正文可能携带内部路径或连接配置，不向终端透传。
        message = str(error) if isinstance(error, VerificationFailure) else "文件操作失败"
        print("缓存双节点验收失败：" + message, file=sys.stderr)
        return 1
    print(json.dumps({"outcome": report["outcome"], "run": report["run"], "results": len(report["results"])}, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    sys.exit(main())
