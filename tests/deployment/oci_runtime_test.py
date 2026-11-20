"""正式 OCI 打包入口的真实 MCR/Docker 验收，仅在显式授权的隔离 Linux 执行机运行。"""
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tarfile
import tempfile
import unittest
import uuid

REPOSITORY = Path(__file__).resolve().parents[2]
PACKER = REPOSITORY / "eng/deploy/ubuntu/Pack-OciImage.py"
PROJECT = REPOSITORY / "tests/deployment/fixtures/oci-runtime/Full.NET.OciRuntimeFixture.csproj"


def run(command, seconds=180, environment=None, check=True):
    result = subprocess.run(["timeout", "--signal=TERM", "--kill-after=5s", str(seconds) + "s", *map(str, command)],
                            cwd=REPOSITORY, env=environment, capture_output=True, text=True, timeout=seconds + 15)
    if check and result.returncode:
        raise RuntimeError(f"{command[0]} failed ({result.returncode}): " + (result.stdout + result.stderr)[-6000:])
    return result


class OciRuntimeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if not sys.platform.startswith("linux") or os.environ.get("FULLNET_OCI_RUNTIME") != "isolated":
            raise RuntimeError("真实镜像验收需要明确的隔离 Linux 执行入口")
        if os.environ.get("DOCKER_HOST", "unix:///var/run/docker.sock").split(":", 1)[0] != "unix":
            raise RuntimeError("拒绝操作远程 Docker")
        context = json.loads(run(["docker", "context", "inspect"]).stdout)[0]
        if not context["Endpoints"]["docker"]["Host"].startswith("unix://"):
            raise RuntimeError("拒绝操作远程 Docker context")
        cls.temporary = tempfile.TemporaryDirectory(prefix="fullnet-oci-runtime-")
        cls.root = Path(cls.temporary.name)
        cls.run_id = uuid.uuid4().hex
        cls.references = []
        cls.container_names = set()
        cls.counter = 0
        cls.addClassCleanup(cls.clean)
        cls.environment = {**os.environ, "DOTNET_CLI_HOME": str(cls.root / "dotnet-home"),
                           "NUGET_PACKAGES": str(cls.root / "nuget/packages"),
                           "NUGET_HTTP_CACHE_PATH": str(cls.root / "nuget/http"),
                           "DOTNET_CLI_TELEMETRY_OPTOUT": "1", "MSBUILDDISABLENODEREUSE": "1"}
        cls.archives, cls.reports, cls.publishes, cls.images = {}, {}, {}, {}
        for role in ("api", "worker", "migrator"):
            publish = cls.root / ("publish-" + role)
            print("oci_runtime_stage: publishing " + role, flush=True)
            result = run(["dotnet", "publish", PROJECT, "-c", "Release", "-r", "linux-x64",
                          "--self-contained", "false", "--artifacts-path", cls.root / "build" / role,
                          "--output", publish, "--disable-build-servers", "--nologo",
                          "-p:AssemblyName=Full.NET.Host." + role.capitalize(), "-p:UseSharedCompilation=false",
                          "-m:2"], seconds=240, environment=cls.environment)
            print(result.stdout[-1500:], flush=True)
            phase = "legacy" if role == "api" else "current"
            (publish / "candidate.txt").write_text(phase)
            (publish / ".runtime-dotfile").write_text("preserved")
            if phase == "legacy":
                (publish / "old-only.txt").write_text("must disappear after update")
            cls.publishes[role] = publish
            archive, report = cls.pack(role, phase, None if role == "api" else cls.archives["api"])
            cls.archives[role], cls.reports[role] = archive, report
            cls.images[role] = cls.import_image(archive, role + "-" + phase)

    @classmethod
    def clean(cls):
        failures = []
        try:
            # 只回收本轮唯一标签/名称的容器和已排他登记的镜像，不执行 prune。
            existing = run(["docker", "ps", "-aq", "--filter", "label=fullnet.oci-runtime-run=" + cls.run_id]).stdout.split()
            for container in existing:
                name = json.loads(run(["docker", "container", "inspect", container]).stdout)[0]["Name"].lstrip("/")
                if name not in cls.container_names:
                    raise RuntimeError("出现非本轮登记的容器名称")
                run(["docker", "rm", "-f", container], seconds=30)
            for reference in reversed(cls.references):
                run(["docker", "image", "rm", "--force", reference], seconds=60)
            remaining = run(["docker", "ps", "-aq", "--filter", "label=fullnet.oci-runtime-run=" + cls.run_id]).stdout.strip()
            if remaining:
                failures.append("本轮容器未回收")
        except Exception as error:
            failures.append(str(error))
        finally:
            cls.temporary.cleanup()
        if failures:
            raise RuntimeError("; ".join(failures))
        print("oci_runtime_cleanup: own_containers=0 own_image_tags=0 temporary_files=removed", flush=True)

    @classmethod
    def pack(cls, role, phase, base):
        source = cls.root / f"{role}-{phase}-publish.tar"
        with tarfile.open(source, "w") as archive:
            for path in sorted(cls.publishes[role].iterdir()):
                archive.add(path, arcname=path.name)
        destination = cls.root / f"{role}-{phase}.oci.tar"
        command = [sys.executable, "-O", PACKER, "--publish", source, "--role", role,
                   "--tag", cls.run_id + "-" + phase, "--output", destination, "--cache", cls.root / "oci-cache"]
        if base:
            with tarfile.open(base) as archive:
                index = json.load(archive.extractfile("index.json"))
            command.extend(["--base-oci", base, "--base-digest", index["manifests"][0]["digest"]])
        print("oci_runtime_stage: packing " + role + "-" + phase, flush=True)
        report = json.loads(run(command, seconds=420).stdout)
        with destination.open("rb") as stream:
            actual = "sha256:" + hashlib.file_digest(stream, "sha256").hexdigest()
        if report["status"] != "packed" or report["archiveSha256"] != actual:
            raise RuntimeError("打包回执与实际归档摘要不符")
        print("oci_runtime_artifact: " + json.dumps(report, sort_keys=True), flush=True)
        return destination, report

    @classmethod
    def import_image(cls, archive, suffix):
        reference = "fullnet-oci-runtime-" + suffix + ":" + cls.run_id
        # 导入前确认名称未被占用，避免覆盖或回收既有镜像。
        listed = run(["docker", "image", "ls", "--format", "{{.Repository}}:{{.Tag}}", "--filter", "reference=" + reference]).stdout.strip()
        if listed:
            raise RuntimeError("镜像名称已存在")
        cls.references.append(reference)
        print("oci_runtime_stage: importing " + suffix, flush=True)
        run(["skopeo", "copy", "--format", "v2s2", "oci-archive:" + str(archive), "docker-daemon:" + reference], seconds=180)
        with tarfile.open(archive) as source:
            index = json.load(source.extractfile("index.json"))
            manifest = json.load(source.extractfile("blobs/sha256/" + index["manifests"][0]["digest"][7:]))
            configuration = json.load(source.extractfile("blobs/sha256/" + manifest["config"]["digest"][7:]))
        image = json.loads(run(["docker", "image", "inspect", reference]).stdout)[0]
        if image["Config"]["User"] != "1654" or image["Config"]["WorkingDir"] != "/app":
            raise RuntimeError("Docker 未保留镜像进程约束")
        if image["Config"]["Entrypoint"] != configuration["config"]["Entrypoint"] or image["RootFS"]["Layers"] != configuration["rootfs"]["diff_ids"]:
            raise RuntimeError("Docker 实际层或入口与 OCI 不符")
        print("oci_runtime_import: " + json.dumps({"role": suffix, "imageId": image["Id"], "layers": len(image["RootFS"]["Layers"])}), flush=True)
        return reference

    @classmethod
    def start(cls, image, role, phase, expected_success=True):
        cls.counter += 1
        name = f"fullnet-oci-runtime-{cls.run_id}-{cls.counter}"
        cls.container_names.add(name)
        result = run(["docker", "run", "--rm", "--name", name, "--label", "fullnet.oci-runtime-run=" + cls.run_id,
                      "--network", "none", "--read-only", "--memory", "128m", "--cpus", "1", "--pids-limit", "64",
                      "--cap-drop", "ALL", "--security-opt", "no-new-privileges", image, phase], seconds=30, check=False)
        if expected_success:
            expected = f"fullnet_oci_runtime:Full.NET.Host.{role.capitalize()}:{phase}:1654:"
            if result.returncode != 0 or not re.fullmatch(re.escape(expected) + r"10\.[0-9]+\.[0-9]+\s*", result.stdout):
                raise RuntimeError("真实容器入口失败: " + (result.stdout + result.stderr)[-2000:])
            print(result.stdout.strip(), flush=True)
        elif result.returncode != 3 or "fullnet_oci_runtime:" in result.stdout:
            raise RuntimeError("错误的应用内容没有被真实容器拒绝")
        return result

    def test_three_role_archives_import_and_start_without_network(self):
        for role in ("api", "worker", "migrator"):
            with self.subTest(role=role):
                self.start(self.images[role], role, "legacy" if role == "api" else "current")

    def test_offline_api_update_removes_old_files_and_keeps_runtime_layers(self):
        publish = self.publishes["api"]
        (publish / "candidate.txt").write_text("current")
        (publish / "old-only.txt").unlink()
        archive, report = self.pack("api", "current", self.archives["api"])
        image = self.import_image(archive, "api-current")
        self.assertEqual(report["baseManifestDigest"], self.reports["api"]["manifestDigest"])
        with tarfile.open(archive) as current, tarfile.open(self.archives["api"]) as previous:
            def manifest(source):
                index = json.load(source.extractfile("index.json"))
                return json.load(source.extractfile("blobs/sha256/" + index["manifests"][0]["digest"][7:]))
            newer, older = manifest(current), manifest(previous)
            self.assertEqual(newer["layers"][:-1], older["layers"][:-1])
            self.assertEqual(len(newer["layers"]), len(older["layers"]))
        self.start(image, "api", "current")

    def test_wrong_generation_payload_fails_inside_real_container(self):
        self.start(self.images["api"], "api", "current", expected_success=False)


if __name__ == "__main__":
    unittest.main()
