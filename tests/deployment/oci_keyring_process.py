"""在真实角色镜像内运行独立进程，验证候选注册、共享 Key Ring 和拒绝边界。"""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import secrets
import subprocess


def verify(root):
    socket = "unix:///var/run/docker.sock"
    if os.environ.get("CI") != "true" or not Path("/var/run/docker.sock").is_socket():
        raise RuntimeError("keyring_isolated_ci_required")
    run_id = os.environ["GITHUB_RUN_ID"]
    if not run_id.isdigit():
        raise RuntimeError("keyring_run_id_invalid")

    def run(*args, timeout=60):
        return subprocess.check_output(["docker", "--host", socket, *args], text=True, timeout=timeout,
                                       stderr=subprocess.PIPE).strip()

    def image(role):
        return "fullnet-oci-import-" + role + ":" + run_id

    def name(role):
        return "fullnet-oci-import-" + role + "-" + run_id

    references = root / "keyring-references"
    references.mkdir(mode=0o700)
    # 这些名称已由导入阶段排他登记，未启动的容器只用于复制一份实际候选程序集。
    for role in ("api", "worker"):
        if run("ps", "-aq", "--filter", "name=^/" + name(role) + "$"):
            raise RuntimeError("keyring_container_already_exists")
        run("create", "--name", name(role), "--network", "none", image(role))
        run("cp", name(role) + ":/app/Full.NET.Hosting.dll", str(references / (role + ".dll")))
        run("rm", name(role))
    digests = {role: hashlib.sha256((references / (role + ".dll")).read_bytes()).hexdigest() for role in ("api", "worker")}
    if digests["api"] != digests["worker"]:
        raise RuntimeError("keyring_candidate_hosting_drift")
    (references / "Full.NET.Hosting.dll").write_bytes((references / "api.dll").read_bytes())
    output = root / "keyring-probe"
    command = ["dotnet", "publish", "tests/deployment/fixtures/keyring-process/Full.NET.CandidateKeyRingProbe.csproj",
               "-c", "Release", "--self-contained", "false", "--artifacts-path", str(root / "keyring-build"),
               "-o", str(output), "-p:CandidateReferenceDirectory=" + str(references),
               "-p:UseSharedCompilation=false", "-m:1", "-nr:false"]
    subprocess.run(command, check=True, timeout=180)
    spec = importlib.util.spec_from_file_location("keyring_assets", Path(__file__).with_name("oci_production_assets.py"))
    assets_module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(assets_module)
    password = "Fn9!" + secrets.token_hex(24)
    assets_root, wrong_root = root / "keyring-assets", root / "keyring-wrong-assets"
    for directory in (assets_root, wrong_root):
        directory.mkdir(mode=0o700)
    assets = assets_module.prepare(assets_root, "candidate-database", "candidate-redis", password)
    wrong = assets_module.prepare(wrong_root, "candidate-database", "candidate-redis", password)
    assembly = "Full.NET.CandidateKeyRingProbe"
    mounts = []
    for extension in (".dll", ".deps.json", ".runtimeconfig.json"):
        path = output / (assembly + extension)
        mounts += ["--mount", "type=bind,source=" + str(path) + ",target=/app/" + path.name + ",readonly"]
    wrong_certificate = [arg.replace(str(assets_root / "production-private" / "host"),
                                     str(wrong_root / "production-private" / "host")) for arg in assets["hostArguments"]]

    def invoke(role, mode, purpose="shared", text="", arguments=None, rejected=False):
        if run("ps", "-aq", "--filter", "name=^/" + name(role) + "$"):
            raise RuntimeError("keyring_container_already_exists")
        command = ["docker", "--host", socket, "run", "--rm", "-i", "--name", name(role), "--network", "none",
                   "--read-only", "--tmpfs", "/tmp:rw,nosuid,nodev,size=32m", "--memory", "128m", "--cpus", "1",
                   "--pids-limit", "64", "--cap-drop", "ALL", "--security-opt", "no-new-privileges",
                   "-e", "DOTNET_ENVIRONMENT=Production", "-e", "ASPNETCORE_ENVIRONMENT=Production", *mounts,
                   *(assets["hostArguments"] if arguments is None else arguments), "--entrypoint", "dotnet",
                   image(role), "/app/" + assembly + ".dll", mode]
        if mode != "inspect":
            command += [purpose]
        result = subprocess.run(command, input=text, text=True, capture_output=True, timeout=30)
        if rejected:
            if result.returncode != 3 or result.stdout or result.stderr != "keyring.unprotect.rejected":
                raise RuntimeError("keyring_rejection_not_verified")
        elif result.returncode != 0 or result.stderr:
            raise RuntimeError("keyring_probe_execution_failed")
        return result.stdout

    first, second = secrets.token_hex(32), secrets.token_hex(32)
    api_token = invoke("api", "protect", text=first)
    if invoke("worker", "unprotect", text=api_token) != first:
        raise RuntimeError("keyring_api_to_worker_failed")
    worker_token = invoke("worker", "protect", text=second)
    if invoke("api", "unprotect", text=worker_token) != second or invoke("worker", "unprotect", text=api_token) != first:
        raise RuntimeError("keyring_reverse_or_reload_failed")
    invoke("worker", "unprotect", purpose="different", text=api_token, rejected=True)
    position = len(api_token) // 2
    tampered = api_token[:position] + ("A" if api_token[position] != "A" else "B") + api_token[position + 1:]
    invoke("worker", "unprotect", text=tampered, rejected=True)
    invoke("worker", "unprotect", text=api_token, arguments=wrong_certificate, rejected=True)
    invoke("worker", "unprotect", text=api_token, arguments=wrong["hostArguments"], rejected=True)
    count = invoke("api", "inspect")
    if not count.isdigit() or int(count) < 1:
        raise RuntimeError("keyring_at_rest_encryption_missing")
    print("candidate_keyring_verified: directions=2 reload=true rejection_cases=4 encrypted_keys=" + count, flush=True)
    return {"scope": "independent-processes-in-candidate-images", "environment": "Production",
            "candidateHostingSha256": digests["api"], "apiToWorkerVerified": True, "workerToApiVerified": True,
            "freshProcessReloadVerified": True, "wrongPurposeRejected": True, "tamperedPayloadRejected": True,
            "wrongCertificateRejected": True, "unrelatedKeyRingRejected": True, "encryptedKeyCount": int(count),
            "businessHostConsumerVerified": False, "nativeAotVerified": False}
