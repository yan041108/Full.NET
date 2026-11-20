#!/usr/bin/env python3
"""从已校验的 OCI 基础层和 Linux JIT publish 包生成镜像；不启动 Docker 或应用进程。"""
import argparse
import copy
import gzip
import hashlib
import io
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import stat
import subprocess
import sys
import tarfile
import tempfile

REPOSITORY = "https://mcr.microsoft.com/v2/dotnet/aspnet"
MARKER = "Full.NET reviewed Linux publish artifact"
OCI = "application/vnd.oci.image."
MANIFEST_TYPES = {OCI + "manifest.v1+json", "application/vnd.docker.distribution.manifest.v2+json"}
LAYER_TYPES = {OCI + "layer.v1.tar", OCI + "layer.v1.tar+gzip",
               "application/vnd.docker.image.rootfs.diff.tar.gzip"}
MAX_BLOB = 4 * 1024**3


class InvalidImage(Exception):
    """对外仅提供固定失败原因，不输出归档内容或原始下载错误。"""


def require(condition, code):
    if not condition:
        raise InvalidImage(code)


def encode(value):
    return json.dumps(value, sort_keys=True, separators=(",", ":")).encode()


def diff_digest(stream):
    digest = hashlib.sha256()
    size = 0
    while chunk := stream.read(65536):
        size += len(chunk)
        require(size <= MAX_BLOB, "archive_size_limit")
        digest.update(chunk)
    return "sha256:" + digest.hexdigest()


def digest_file(path):
    status = path.lstat()
    require(stat.S_ISREG(status.st_mode) and status.st_nlink == 1, "cache_file_invalid")
    with path.open("rb") as stream:
        return "sha256:" + hashlib.file_digest(stream, "sha256").hexdigest()


def descriptor_path(descriptor, cache):
    digest, size = descriptor["digest"], descriptor["size"]
    require(isinstance(digest, str) and re.fullmatch(r"sha256:[0-9a-f]{64}", digest), "descriptor_digest_invalid")
    require(type(size) is int and 0 <= size <= MAX_BLOB, "descriptor_size_invalid")
    return cache / digest[7:]


def verify_blob(descriptor, cache):
    path = descriptor_path(descriptor, cache)
    status = path.lstat()
    require(stat.S_ISREG(status.st_mode) and status.st_nlink == 1, "cache_file_invalid")
    require(status.st_size == descriptor["size"], "blob_size_mismatch")
    require(digest_file(path) == descriptor["digest"], "blob_digest_mismatch")
    return path


def write_json(value, cache, media_type):
    content = encode(value)
    descriptor = {"mediaType": media_type, "digest": "sha256:" + hashlib.sha256(content).hexdigest(), "size": len(content)}
    path = descriptor_path(descriptor, cache)
    if not path.exists() and not path.is_symlink():
        with path.open("xb") as stream:
            stream.write(content)
    verify_blob(descriptor, cache)
    return descriptor


def fetch_json(url):
    accept = ",".join([OCI + "index.v1+json", "application/vnd.docker.distribution.manifest.list.v2+json",
                       *sorted(MANIFEST_TYPES)])
    content = subprocess.check_output(["curl", "-4", "-fsSL", "--connect-timeout", "10", "--max-time", "120",
                                       "--max-filesize", "4194304", "-H", "Accept: " + accept, url],
                                      stderr=subprocess.DEVNULL, timeout=125)
    require(len(content) <= 4194304, "metadata_too_large")
    return content, json.loads(content)


def online_base(cache):
    content, manifest = fetch_json(REPOSITORY + "/manifests/10.0")
    if "manifests" in manifest:
        platforms = [d for d in manifest["manifests"] if d.get("platform", {}).get("os") == "linux"
                     and d.get("platform", {}).get("architecture") == "amd64"]
        require(len(platforms) == 1, "base_platform_invalid")
        selected = platforms[0]
        descriptor_path(selected, cache)
        content, manifest = fetch_json(REPOSITORY + "/manifests/" + selected["digest"])
        require(len(content) == selected["size"] and "sha256:" + hashlib.sha256(content).hexdigest() == selected["digest"],
                "base_manifest_mismatch")

    def load(descriptor):
        path = descriptor_path(descriptor, cache)
        if not path.exists() and not path.is_symlink():
            temporary = None
            try:
                handle, name = tempfile.mkstemp(prefix=".download-", dir=cache)
                os.close(handle)
                temporary = Path(name)
                # 旧版 curl 也可能用大小预算检查重定向响应；小 blob 仍需容纳有界的协议开销。
                # 接收后继续按 descriptor 精确核对大小与摘要，传输预算不能成为内容放行条件。
                transport_limit = max(65536, descriptor["size"])
                subprocess.run(["curl", "-4", "-fsSL", "--connect-timeout", "10", "--max-time", "300",
                                "--max-filesize", str(transport_limit), "--proto", "=https", "--proto-redir", "=https",
                                "--max-redirs", "5", REPOSITORY + "/blobs/" + descriptor["digest"],
                                "-o", str(temporary)], check=True, stderr=subprocess.DEVNULL, timeout=305)
                require(temporary.stat().st_size == descriptor["size"] and digest_file(temporary) == descriptor["digest"],
                        "download_integrity_failed")
                temporary.replace(path)
            finally:
                if temporary is not None:
                    temporary.unlink(missing_ok=True)
        return verify_blob(descriptor, cache)
    return manifest, "sha256:" + hashlib.sha256(content).hexdigest(), load, None


def archive_members(archive):
    members = {}
    for item in archive:
        name = item.name
        require(not name.startswith("/") and "\\" not in name and not any(ord(c) < 32 for c in name)
                and ".." not in PurePosixPath(name).parts, "archive_path_invalid")
        name = str(PurePosixPath(name))
        if name == "." and item.isdir():
            continue
        require(name != "." and name not in members and item.type in (tarfile.REGTYPE, tarfile.AREGTYPE, tarfile.DIRTYPE),
                "archive_entry_invalid")
        require(0 <= item.size <= MAX_BLOB and item.offset + 512 + item.size <= MAX_BLOB, "archive_size_limit")
        members[name] = item
        require(len(members) <= 50000, "archive_entry_limit")
    return members


def offline_base(path, cache):
    archive = tarfile.open(path, "r:*")
    try:
        members = archive_members(archive)
        def metadata(name):
            item = members[name]
            require(item.isfile() and item.size <= 4194304, "metadata_too_large")
            return json.load(archive.extractfile(item))
        require(metadata("oci-layout") == {"imageLayoutVersion": "1.0.0"}, "oci_layout_invalid")
        index = metadata("index.json")
        require(index.get("schemaVersion") == 2 and len(index["manifests"]) == 1, "base_index_invalid")

        def load(descriptor):
            target = descriptor_path(descriptor, cache)
            item = members["blobs/sha256/" + descriptor["digest"][7:]]
            require(item.isfile() and item.size == descriptor["size"], "blob_size_mismatch")
            # 即使缓存已有内容，也读取并核对输入包，不能让缓存掩盖归档篡改。
            with archive.extractfile(item) as stream:
                digest = hashlib.file_digest(stream, "sha256").hexdigest()
            require("sha256:" + digest == descriptor["digest"], "blob_digest_mismatch")
            if not target.exists() and not target.is_symlink():
                with archive.extractfile(item) as stream, target.open("xb") as output:
                    shutil.copyfileobj(stream, output)
            return verify_blob(descriptor, cache)
        descriptor = index["manifests"][0]
        require(descriptor["mediaType"] in MANIFEST_TYPES, "base_manifest_type_invalid")
        require(descriptor["size"] <= 4194304, "metadata_too_large")
        manifest = json.loads(load(descriptor).read_bytes())
        return manifest, descriptor["digest"], load, archive
    except BaseException:
        archive.close()
        raise


def base_layers(args, cache):
    manifest, manifest_digest, load, archive = offline_base(args.base_oci, cache) if args.base_oci else online_base(cache)
    try:
        require(not args.base_digest or args.base_digest == manifest_digest, "base_manifest_mismatch")
        require(manifest.get("schemaVersion") == 2 and manifest.get("mediaType") in MANIFEST_TYPES,
                "base_manifest_type_invalid")
        config_descriptor = manifest["config"]
        require(config_descriptor["mediaType"] in {OCI + "config.v1+json", "application/vnd.docker.container.image.v1+json"},
                "base_config_type_invalid")
        require(config_descriptor["size"] <= 4194304, "metadata_too_large")
        configuration = json.loads(load(config_descriptor).read_bytes())
        require(configuration.get("os") == "linux" and configuration.get("architecture") == "amd64", "base_platform_invalid")
        layers = copy.deepcopy(manifest["layers"])
        rootfs = configuration["rootfs"]
        require(rootfs.get("type") == "layers" and len(rootfs["diff_ids"]) == len(layers) and 0 < len(layers) <= 128,
                "base_rootfs_invalid")
        for layer, diff_id in zip(layers, rootfs["diff_ids"]):
            require(layer["mediaType"] in LAYER_TYPES, "base_layer_type_invalid")
            require(isinstance(diff_id, str) and re.fullmatch(r"sha256:[0-9a-f]{64}", diff_id), "base_rootfs_invalid")
            load(layer)
        history = configuration.setdefault("history", [])
        if history and history[-1].get("created_by") == MARKER:
            require(not history[-1].get("empty_layer") and len(layers) >= 2, "previous_application_layer_invalid")
            old = verify_blob(layers[-1], cache)
            with tarfile.open(old, "r:*") as previous:
                names = archive_members(previous)
                require(names and all(name == "app" or name.startswith("app/") for name in names),
                        "previous_application_layer_invalid")
            opener = gzip.open if layers[-1]["mediaType"].endswith("gzip") else open
            with opener(old, "rb") as stream:
                actual_diff = diff_digest(stream)
            require(actual_diff == rootfs["diff_ids"][-1], "previous_application_diff_mismatch")
            layers.pop()
            rootfs["diff_ids"].pop()
            history.pop()
        return configuration, layers, manifest_digest
    finally:
        if archive is not None:
            archive.close()


def application_layer(publish, role, work):
    raw = work / "application.tar"
    with tarfile.open(publish, "r:*") as source, tarfile.open(raw, "w", format=tarfile.PAX_FORMAT) as target:
        members = archive_members(source)
        host = "Full.NET.Host." + role.capitalize()
        require(all(name in members and members[name].isfile() and members[name].size > 0
                    for name in (host + ".dll", host + ".deps.json", host + ".runtimeconfig.json")), "publish_host_missing")
        require(sum(item.size for item in members.values()) <= MAX_BLOB, "publish_size_limit")
        for name in sorted(members):
            item = members[name]
            require(not any(part.startswith(".wh.") for part in PurePosixPath(name).parts), "publish_whiteout_invalid")
            require(all(str(parent) not in members or members[str(parent)].isdir()
                        for parent in PurePosixPath(name).parents if str(parent) != "."), "publish_path_collision")
            normalized = tarfile.TarInfo("app/" + name)
            normalized.type = tarfile.DIRTYPE if item.isdir() else tarfile.REGTYPE
            normalized.mode = 0o755 if item.isdir() or item.mode & 0o111 else 0o644
            normalized.size = 0 if item.isdir() else item.size
            normalized.uid = normalized.gid = normalized.mtime = 0
            target.addfile(normalized, source.extractfile(item) if item.isfile() else None)
    diff_id = digest_file(raw)
    zipped = work / "application.tar.gz"
    with raw.open("rb") as stream, zipped.open("wb") as output, gzip.GzipFile(filename="", fileobj=output, mode="wb", mtime=0) as compressor:
        shutil.copyfileobj(stream, compressor)
    return zipped, diff_id


def add_file(archive, path, name):
    item = tarfile.TarInfo(name)
    item.size = path.stat().st_size
    item.mode = 0o644
    with path.open("rb") as stream:
        archive.addfile(item, stream)


def pack(args):
    require(re.fullmatch(r"[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}", args.tag), "image_tag_invalid")
    output = Path(args.output)
    sidecar = Path(str(output) + ".manifest.json")
    require(not output.exists() and not output.is_symlink() and not sidecar.exists() and not sidecar.is_symlink(),
            "output_already_exists")
    cache = Path(args.cache)
    require(not cache.is_symlink(), "cache_path_invalid")
    cache.mkdir(parents=True, exist_ok=True)
    os.umask(0o077)
    with tempfile.TemporaryDirectory(prefix=".fullnet-oci-", dir=output.parent) as temporary:
        work = Path(temporary)
        configuration, layers, base_digest = base_layers(args, cache)
        zipped, diff_id = application_layer(args.publish, args.role, work)
        layer = {"mediaType": OCI + "layer.v1.tar+gzip", "digest": digest_file(zipped), "size": zipped.stat().st_size}
        layer_path = descriptor_path(layer, cache)
        if not layer_path.exists() and not layer_path.is_symlink():
            shutil.copyfile(zipped, layer_path)
        verify_blob(layer, cache)
        configuration["rootfs"]["diff_ids"].append(diff_id)
        configuration["history"].append({"created_by": MARKER})
        settings = configuration.setdefault("config", {})
        settings.update({"WorkingDir": "/app", "User": "1654",
                         "Entrypoint": ["dotnet", "Full.NET.Host." + args.role.capitalize() + ".dll"], "Cmd": []})
        environment = dict(item.split("=", 1) for item in settings.get("Env", []))
        environment.update({"ASPNETCORE_URLS": "http://+:8080", "DOTNET_EnableDiagnostics": "0"})
        settings["Env"] = [key + "=" + environment[key] for key in sorted(environment)]
        config = write_json(configuration, cache, OCI + "config.v1+json")
        result = {"schemaVersion": 2, "mediaType": OCI + "manifest.v1+json", "config": config, "layers": layers + [layer]}
        descriptor = write_json(result, cache, result["mediaType"])
        reference = f"docker.io/library/fullnet-{args.role}:{args.tag}"
        descriptor["annotations"] = {"org.opencontainers.image.ref.name": reference, "io.containerd.image.name": reference}
        index = {"schemaVersion": 2, "manifests": [descriptor]}
        archive_path = work / "image.oci.tar"
        with tarfile.open(archive_path, "w") as archive:
            for name, content in (("oci-layout", b'{"imageLayoutVersion":"1.0.0"}'), ("index.json", encode(index))):
                item = tarfile.TarInfo(name)
                item.size = len(content)
                item.mode = 0o644
                archive.addfile(item, io.BytesIO(content))
            emitted = set()
            for blob in result["layers"] + [config, descriptor]:
                if blob["digest"] not in emitted:
                    add_file(archive, verify_blob(blob, cache), "blobs/sha256/" + blob["digest"][7:])
                    emitted.add(blob["digest"])
        manifest_path = work / "manifest.json"
        manifest_path.write_bytes(encode(result) + b"\n")
        archive_digest = digest_file(archive_path)
        sidecar_created = False
        try:
            # 排他创建两个目标；归档最后可见，常规失败只删除本轮创建的 sidecar。
            os.link(manifest_path, sidecar)
            sidecar_created = True
            os.link(archive_path, output)
        except BaseException:
            if sidecar_created:
                sidecar.unlink()
            raise
        return {"status": "packed", "role": args.role, "manifestDigest": descriptor["digest"],
                "baseManifestDigest": base_digest, "archiveSha256": archive_digest}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("publish", "tag", "output", "cache"):
        parser.add_argument("--" + name, required=True)
    parser.add_argument("--role", required=True, choices=("api", "worker", "migrator"))
    parser.add_argument("--base-oci")
    parser.add_argument("--base-digest")
    args = parser.parse_args(argv)
    try:
        result = pack(args)
    except InvalidImage as error:
        code = str(error)
    except subprocess.CalledProcessError as error:
        code = f"image_download_failed_{error.returncode}"
    except OSError as error:
        code = f"image_io_failed_{error.errno or 0}"
    except (ValueError, KeyError, TypeError):
        code = "image_metadata_invalid"
    except tarfile.TarError:
        code = "image_archive_invalid"
    except subprocess.SubprocessError:
        code = "image_pack_failed"
    else:
        print(json.dumps(result, separators=(",", ":")))
        return 0
    print(json.dumps({"status": "failed", "code": code}, separators=(",", ":")), file=sys.stderr)
    return 1


if __name__ == "__main__":
    sys.exit(main())
