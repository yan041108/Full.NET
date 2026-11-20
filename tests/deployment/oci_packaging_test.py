"""真实 tar/gzip/摘要与文件提交验收；基础运行时为合成夹具，不启动应用或访问网络。"""
import argparse
import contextlib
import gzip
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import subprocess
import sys
import tarfile
import tempfile
import unittest
from unittest import mock

SCRIPT = Path(__file__).resolve().parents[2] / "eng/deploy/ubuntu/Pack-OciImage.py"
spec = importlib.util.spec_from_file_location("fullnet_pack_oci", SCRIPT)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
OCI = module.OCI


def tar_bytes(entries):
    stream = io.BytesIO()
    with tarfile.open(fileobj=stream, mode="w") as archive:
        for name, content, kind, mode in entries:
            item = tarfile.TarInfo(name)
            item.mode = mode
            item.mtime = 123456
            item.type = kind
            item.size = len(content) if kind == tarfile.REGTYPE else 0
            if kind in (tarfile.SYMTYPE, tarfile.LNKTYPE):
                item.linkname = "../../outside"
            archive.addfile(item, io.BytesIO(content) if item.isfile() else None)
    return stream.getvalue()


def descriptor(content, media):
    return {"mediaType": media, "size": len(content), "digest": "sha256:" + hashlib.sha256(content).hexdigest()}


class OciPackagingTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="fullnet-oci-test-")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.base_path = self.root / "base.oci.tar"
        self.publish = self.root / "publish.tar"
        self.output = self.root / "image.oci.tar"
        self.cache = self.root / "cache"
        self.make_base()
        self.make_publish()

    def make_base(self, previous=False, unsafe_old=False, mutate_config=None, extra=None, duplicate=False):
        self.base_blobs = {}
        def save(content, media):
            item = descriptor(content, media)
            self.base_blobs[item["digest"]] = content
            return item
        raw = tar_bytes([("usr/share/dotnet/runtime.txt", b"runtime-fixture", tarfile.REGTYPE, 0o644)])
        layers = [save(gzip.compress(raw, mtime=0), OCI + "layer.v1.tar+gzip")]
        diffs = ["sha256:" + hashlib.sha256(raw).hexdigest()]
        history = [{"created_by": "runtime-fixture"}]
        if previous:
            old_raw = tar_bytes([("etc/forbidden" if unsafe_old else "app/old-only.txt", b"old", tarfile.REGTYPE, 0o644)])
            layers.append(save(gzip.compress(old_raw, mtime=0), OCI + "layer.v1.tar+gzip"))
            diffs.append("sha256:" + hashlib.sha256(old_raw).hexdigest())
            history.append({"created_by": module.MARKER})
        self.base_config = {"architecture": "amd64", "os": "linux", "rootfs": {"type": "layers", "diff_ids": diffs},
                            "history": history, "config": {"Env": ["RUNTIME_FIXTURE=1"]}}
        if mutate_config:
            mutate_config(self.base_config)
        config = save(module.encode(self.base_config), OCI + "config.v1+json")
        self.base_manifest = {"schemaVersion": 2, "mediaType": OCI + "manifest.v1+json", "config": config, "layers": layers}
        manifest = save(module.encode(self.base_manifest), OCI + "manifest.v1+json")
        self.base_descriptor = manifest
        self.base_digest = manifest["digest"]
        self.base_entries = [("oci-layout", b'{"imageLayoutVersion":"1.0.0"}', tarfile.REGTYPE, 0o644),
                             ("index.json", module.encode({"schemaVersion": 2, "manifests": [manifest]}), tarfile.REGTYPE, 0o644)]
        self.base_entries += [("blobs/sha256/" + digest[7:], content, tarfile.REGTYPE, 0o644)
                              for digest, content in self.base_blobs.items()]
        if extra:
            self.base_entries.append(extra)
        if duplicate:
            self.base_entries.append(self.base_entries[1])
        self.base_path.write_bytes(tar_bytes(self.base_entries))

    def make_publish(self, role="api", extra=(), reverse=False):
        host = "Full.NET.Host." + role.capitalize()
        entries = [(host + suffix, b"fixture-assembly" if suffix == ".dll" else b"{}", tarfile.REGTYPE, 0o644)
                   for suffix in (".dll", ".deps.json", ".runtimeconfig.json")]
        entries += [(".config", b"dotfile-kept", tarfile.REGTYPE, 0o644),
                    ("run-helper.sh", b"#!/bin/sh\n", tarfile.REGTYPE, 0o755)]
        entries.extend(extra)
        self.publish.write_bytes(tar_bytes(list(reversed(entries)) if reverse else entries))

    def args(self):
        return argparse.Namespace(publish=str(self.publish), role="api", tag="test-tag", output=str(self.output),
                                  cache=str(self.cache), base_oci=str(self.base_path), base_digest=self.base_digest)

    def cli(self, role="api", *extra):
        return subprocess.run([sys.executable, "-O", str(SCRIPT), "--publish", str(self.publish), "--role", role,
                               "--tag", "test-tag", "--output", str(self.output), "--cache", str(self.cache),
                               "--base-oci", str(self.base_path), *map(str, extra)], capture_output=True, text=True, timeout=15)

    def failed(self, code, *extra):
        result = self.cli("api", *extra)
        self.assertEqual(result.returncode, 1, result.stderr)
        self.assertEqual(result.stdout, "")
        self.assertEqual(json.loads(result.stderr), {"status": "failed", "code": code})
        self.assertFalse(self.output.exists())
        self.assertFalse(Path(str(self.output) + ".manifest.json").exists())
        self.assertEqual(list(self.root.glob(".fullnet-oci-*")), [])
        return result

    def inspect(self):
        with tarfile.open(self.output) as archive:
            members = archive.getmembers()
            self.assertEqual(len({m.name for m in members}), len(members))
            index = json.load(archive.extractfile("index.json"))
            def read(item):
                value = archive.extractfile("blobs/sha256/" + item["digest"][7:]).read()
                self.assertEqual(len(value), item["size"])
                self.assertEqual("sha256:" + hashlib.sha256(value).hexdigest(), item["digest"])
                return value
            manifest = json.loads(read(index["manifests"][0]))
            config = json.loads(read(manifest["config"]))
            for item in manifest["layers"]:
                read(item)
            raw = gzip.decompress(read(manifest["layers"][-1]))
            self.assertEqual("sha256:" + hashlib.sha256(raw).hexdigest(), config["rootfs"]["diff_ids"][-1])
            with tarfile.open(fileobj=io.BytesIO(raw)) as layer:
                entries = {item.name: (item.mode, layer.extractfile(item).read() if item.isfile() else b"")
                           for item in layer}
            return manifest, config, entries, index

    def test_three_roles_produce_valid_descriptors_and_expected_entrypoints(self):
        for role in ("api", "worker", "migrator"):
            with self.subTest(role=role):
                self.make_publish(role)
                result = self.cli(role)
                self.assertEqual(result.returncode, 0, result.stderr)
                manifest, config, entries, index = self.inspect()
                self.assertEqual(config["config"]["Entrypoint"], ["dotnet", "Full.NET.Host." + role.capitalize() + ".dll"])
                self.assertEqual(config["config"]["User"], "1654")
                self.assertEqual(config["config"]["WorkingDir"], "/app")
                self.assertEqual(len(manifest["layers"]), 2)
                self.assertEqual(entries["app/.config"][1], b"dotfile-kept")
                self.assertEqual(entries["app/run-helper.sh"][0], 0o755)
                self.assertEqual(json.loads(result.stdout)["manifestDigest"], index["manifests"][0]["digest"])
                self.assertEqual(json.loads(Path(str(self.output) + ".manifest.json").read_text()), manifest)
                self.output.unlink()
                Path(str(self.output) + ".manifest.json").unlink()

    def test_identical_inputs_are_reproducible_even_with_reordered_publish_tar(self):
        self.assertEqual(self.cli().returncode, 0)
        digest = hashlib.sha256(self.output.read_bytes()).hexdigest()
        self.output.unlink()
        Path(str(self.output) + ".manifest.json").unlink()
        self.make_publish(reverse=True)
        self.assertEqual(self.cli().returncode, 0)
        self.assertEqual(hashlib.sha256(self.output.read_bytes()).hexdigest(), digest)

    def test_offline_update_replaces_old_application_layer(self):
        self.make_base(previous=True)
        result = self.cli()
        self.assertEqual(result.returncode, 0, result.stderr)
        manifest, config, entries, _ = self.inspect()
        self.assertEqual(len(manifest["layers"]), 2)
        self.assertEqual(len(config["rootfs"]["diff_ids"]), 2)
        self.assertEqual(sum(item["created_by"] == module.MARKER for item in config["history"]), 1)
        self.assertNotIn("app/old-only.txt", entries)
        self.assertEqual(manifest["layers"][0], self.base_manifest["layers"][0])

    def test_offline_update_refuses_non_application_old_layer(self):
        self.make_base(previous=True, unsafe_old=True)
        self.failed("previous_application_layer_invalid")

    def test_offline_update_checks_old_uncompressed_diff_id(self):
        self.make_base(previous=True, mutate_config=lambda c: c["rootfs"]["diff_ids"].__setitem__(-1, "sha256:" + "0" * 64))
        self.failed("previous_application_diff_mismatch")

    def test_expected_base_digest_mismatch(self):
        self.failed("base_manifest_mismatch", "--base-digest", "sha256:" + "0" * 64)

    def test_corrupt_base_blob_rejected_under_optimization(self):
        entries = list(self.base_entries)
        name, content, kind, mode = entries[2]
        entries[2] = (name, bytes([content[0] ^ 1]) + content[1:], kind, mode)
        self.base_path.write_bytes(tar_bytes(entries))
        self.failed("blob_digest_mismatch")

    def test_shortened_base_blob_rejected(self):
        entries = list(self.base_entries)
        name, content, kind, mode = entries[2]
        entries[2] = (name, content[:-1], kind, mode)
        self.base_path.write_bytes(tar_bytes(entries))
        self.failed("blob_size_mismatch")

    def test_cache_does_not_mask_corrupt_input_archive(self):
        self.cache.mkdir()
        for key, content in self.base_blobs.items():
            (self.cache / key[7:]).write_bytes(content)
        entries = list(self.base_entries)
        name, content, kind, mode = entries[2]
        entries[2] = (name, b"x" * len(content), kind, mode)
        self.base_path.write_bytes(tar_bytes(entries))
        self.failed("blob_digest_mismatch")

    def test_corrupt_cache_blob_is_not_trusted(self):
        self.cache.mkdir()
        layer = self.base_manifest["layers"][0]
        (self.cache / layer["digest"][7:]).write_bytes(b"x" * layer["size"])
        self.failed("blob_digest_mismatch")

    def test_cache_symlink_rejected(self):
        self.cache.mkdir()
        layer = self.base_manifest["layers"][0]
        target = self.root / "unchanged"
        target.write_bytes(self.base_blobs[layer["digest"]])
        (self.cache / layer["digest"][7:]).symlink_to(target)
        self.failed("cache_file_invalid")
        self.assertEqual(target.read_bytes(), self.base_blobs[layer["digest"]])

    def test_base_platform_mismatch(self):
        self.make_base(mutate_config=lambda c: c.update(architecture="arm64"))
        self.failed("base_platform_invalid")

    def test_base_rootfs_count_mismatch(self):
        self.make_base(mutate_config=lambda c: c["rootfs"]["diff_ids"].clear())
        self.failed("base_rootfs_invalid")

    def test_duplicate_base_archive_member_rejected(self):
        self.make_base(duplicate=True)
        self.failed("archive_entry_invalid")

    def test_base_archive_traversal_rejected(self):
        self.make_base(extra=("../outside", b"private-payload", tarfile.REGTYPE, 0o644))
        self.failed("archive_path_invalid")
        self.assertFalse((self.root / "outside").exists())

    def test_publish_traversal_rejected(self):
        self.make_publish(extra=[("../outside", b"private-payload", tarfile.REGTYPE, 0o644)])
        result = self.failed("archive_path_invalid")
        self.assertNotIn("private-payload", result.stderr)

    def test_absolute_publish_path_rejected(self):
        self.make_publish(extra=[("/outside", b"x", tarfile.REGTYPE, 0o644)])
        self.failed("archive_path_invalid")

    def test_publish_backslash_path_rejected(self):
        self.make_publish(extra=[("dir\\..\\outside", b"x", tarfile.REGTYPE, 0o644)])
        self.failed("archive_path_invalid")

    def test_publish_links_and_devices_are_rejected(self):
        for kind in (tarfile.SYMTYPE, tarfile.LNKTYPE, tarfile.CHRTYPE, tarfile.FIFOTYPE):
            with self.subTest(kind=kind):
                self.make_publish(extra=[("unsafe", b"", kind, 0o777)])
                self.failed("archive_entry_invalid")

    def test_normalized_duplicate_publish_member_rejected(self):
        self.make_publish(extra=[("./.config", b"other", tarfile.REGTYPE, 0o644)])
        self.failed("archive_entry_invalid")

    def test_publish_whiteout_rejected(self):
        self.make_publish(extra=[(".wh.forbidden", b"x", tarfile.REGTYPE, 0o644)])
        self.failed("publish_whiteout_invalid")

    def test_publish_file_directory_collision_rejected(self):
        self.make_publish(extra=[("parent", b"x", tarfile.REGTYPE, 0o644), ("parent/child", b"x", tarfile.REGTYPE, 0o644)])
        self.failed("publish_path_collision")

    def test_missing_selected_host_rejected(self):
        self.make_publish(role="worker")
        self.failed("publish_host_missing")

    def test_invalid_tag_rejected_before_base_read(self):
        self.failed("image_tag_invalid", "--tag", "../bad")

    def test_previous_output_is_preserved(self):
        self.output.write_bytes(b"previous")
        result = self.cli()
        self.assertEqual(json.loads(result.stderr)["code"], "output_already_exists")
        self.assertEqual(self.output.read_bytes(), b"previous")

    def test_previous_sidecar_is_preserved(self):
        sidecar = Path(str(self.output) + ".manifest.json")
        sidecar.write_bytes(b"previous")
        result = self.cli()
        self.assertEqual(json.loads(result.stderr)["code"], "output_already_exists")
        self.assertEqual(sidecar.read_bytes(), b"previous")
        self.assertFalse(self.output.exists())

    def test_archive_commit_failure_removes_only_own_sidecar(self):
        args = self.args()
        original = module.os.link
        def fail_archive(source, destination):
            if Path(destination) == self.output:
                raise OSError("private-output-path")
            return original(source, destination)
        with mock.patch.object(module.os, "link", side_effect=fail_archive):
            with self.assertRaises(OSError):
                module.pack(args)
        self.assertFalse(self.output.exists())
        self.assertFalse(Path(str(self.output) + ".manifest.json").exists())
        self.assertEqual(list(self.root.glob(".fullnet-oci-*")), [])

    def test_offline_mode_never_downloads(self):
        with mock.patch.object(module, "fetch_json", side_effect=AssertionError("no_network")):
            report = module.pack(self.args())
        self.assertEqual(report["status"], "packed")

    def test_online_index_and_downloads_verify_declared_digests(self):
        args = self.args()
        args.base_oci = None
        platform = {**self.base_descriptor, "platform": {"os": "linux", "architecture": "amd64"}}
        index = {"schemaVersion": 2, "manifests": [platform]}
        answers = [(module.encode(index), index), (module.encode(self.base_manifest), self.base_manifest)]
        def download(command, **kwargs):
            self.assertEqual(command[0], "curl")
            self.assertIs(kwargs["stderr"], subprocess.DEVNULL)
            digest = command[command.index("-o") - 1].rsplit("/", 1)[-1]
            Path(command[command.index("-o") + 1]).write_bytes(self.base_blobs[digest])
            return subprocess.CompletedProcess(command, 0)
        with mock.patch.object(module, "fetch_json", side_effect=answers), mock.patch.object(module.subprocess, "run", side_effect=download):
            report = module.pack(args)
        self.assertEqual(report["baseManifestDigest"], self.base_digest)
        self.inspect()
        self.assertEqual(list(self.cache.glob(".download-*")), [])

    def test_failed_download_leaves_no_completed_cache_entry(self):
        args = self.args()
        args.base_oci = None
        def fail_download(command, **_):
            Path(command[command.index("-o") + 1]).write_bytes(b"partial-private-payload")
            raise subprocess.CalledProcessError(7, command)
        with mock.patch.object(module, "fetch_json", return_value=(module.encode(self.base_manifest), self.base_manifest)), mock.patch.object(module.subprocess, "run", side_effect=fail_download):
            with self.assertRaises(subprocess.CalledProcessError):
                module.pack(args)
        self.assertEqual(list(self.cache.iterdir()), [])
        self.assertFalse(self.output.exists())


if __name__ == "__main__":
    unittest.main()
