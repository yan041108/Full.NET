"""仅在一次性无网络容器验收；systemd/SeaweedFS/namespace 为替身，tar 与清理实际执行。"""
import fcntl
import hashlib
import io
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import tarfile
import tempfile
import time
import unittest

REPOSITORY = Path(__file__).resolve().parents[2]
SCRIPT = REPOSITORY / "eng/deploy/ubuntu/Verify-ObjectRestore.sh"

STUB = r'''#!/usr/bin/env python3
import json, os, pathlib, subprocess, sys, time
name = pathlib.Path(sys.argv[0]).name
args = sys.argv[1:]
state_path = pathlib.Path(os.environ["RESTORE_STATE"])
state = json.loads(state_path.read_text())
marker = pathlib.Path(os.environ["RESTORE_BACKUP"]) / "OBJECT-RESTORE-VERIFIED"
with pathlib.Path(os.environ["RESTORE_TRACE"]).open("a") as stream:
    stream.write(json.dumps({"command":name,"args":args,"markerExists":marker.exists()}) + "\n")
def save(): state_path.write_text(json.dumps(state))
if name == "systemd-run":
    unit = next(v.split("=",1)[1] for v in args if v.startswith("--unit="))
    work = next(v.split("=",2)[2] for v in args if v.startswith("--property=WorkingDirectory="))
    state.update(active=True,unit=unit,work=work)
    save()
    if state.get("start_failure"): sys.exit(1)
elif name == "systemctl":
    if args[0] == "stop":
        if args[1] != state.get("unit","")+".service": raise RuntimeError("foreign_unit")
        if state.get("stop_failure"): sys.exit(1)
        if not state.get("false_stop"): state["active"]=False;save()
    elif args[0] == "show":
        prop = args[args.index("-p")+1]
        if prop == "MainPID": print("4321" if state.get("active") else "0")
        elif prop == "PrivateNetwork": print("no" if state.get("public_network") else "yes")
        elif prop == "WorkingDirectory": print("/foreign" if state.get("wrong_directory") else state["work"])
elif name == "nsenter":
    if "ip" in args:
        if state.get("address_failure"): sys.exit(1)
    else:
        if args[args.index("python3")+1] != "-I": raise RuntimeError("optimization_not_isolated")
        if state.get("mutate_backup"):
            with (pathlib.Path(os.environ["RESTORE_BACKUP"])/"object-storage.tar.gz").open("ab") as stream: stream.write(b"tampered")
        if state.get("wait_signal"):
            pathlib.Path(os.environ["RESTORE_WAIT"]).touch()
            time.sleep(30)
        if state.get("probe_failure"):
            print("private-credential-sentinel")
            sys.exit(1)
elif name == "rm":
    if state.get("remove_failure"): sys.exit(1)
    subprocess.run(["/bin/rm",*args],check=True)
elif name == "install":
    if state.get("diagnostic_failure"): sys.exit(1)
    subprocess.run(["/usr/bin/install",*args],check=True)
elif name in ("chown","sleep"): pass
else: raise RuntimeError("unexpected_stub")
'''

class ObjectRestoreTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if os.environ.get("FULLNET_OBJECT_RESTORE_TEST_CONTAINER") != "isolated" or os.geteuid() != 0:
            raise RuntimeError("对象恢复测试必须由专用一次性 root 容器运行")
        cls.paths = [Path("/var/backups/fullnet"),Path("/usr/local/lib/fullnet"),Path("/var/log/fullnet"),Path("/srv/fullnet")]
        if any(path.exists() for path in cls.paths):
            raise RuntimeError("容器已有同名目录，拒绝接管")
        for path in cls.paths:
            path.mkdir(parents=True,mode=0o700)
        (Path("/srv/fullnet")/"online-data-sentinel").write_text("original-online-data")

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="fullnet-object-exit-test-")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.backup = Path("/var/backups/fullnet") / self.root.name
        self.backup.mkdir(mode=0o700)
        self.addCleanup(shutil.rmtree,self.backup)
        self.state = self.root/"state.json"
        self.state.write_text("{}")
        self.trace = self.root/"trace.jsonl"
        self.wait = self.root/"waiting"
        binaries = self.root/"bin"
        binaries.mkdir()
        stub = binaries/"fixture"
        stub.write_text(STUB)
        stub.chmod(0o700)
        for name in ("systemctl","systemd-run","nsenter","chown","sleep","rm","install"):
            (binaries/name).symlink_to(stub)
        self.env = {**os.environ,"PATH":str(binaries)+os.pathsep+os.environ["PATH"],
                    "RESTORE_STATE":str(self.state),"RESTORE_TRACE":str(self.trace),
                    "RESTORE_BACKUP":str(self.backup),"RESTORE_WAIT":str(self.wait),
                    "PYTHONOPTIMIZE":"1"}
        self.before_work = set(Path("/var/tmp").glob("fullnet-object-restore-*"))
        self.addCleanup(self.clean_work)
        for name,content in {"ROLE":b"secondary\n","redis.rdb":b"redis-fixture","redis-check.txt":b"ok",
                             "configuration.tar.gz":b"configuration-fixture","packages.tsv":b"fixture\t1"}.items():
            (self.backup/name).write_bytes(content)
        self.archive()
        self.checksums()
        (self.backup/"COMPLETE").touch()

    def clean_work(self):
        for directory in set(Path("/var/tmp").glob("fullnet-object-restore-*"))-self.before_work:
            if directory.parent != Path("/var/tmp") or directory.is_symlink():
                raise RuntimeError("cleanup_target_invalid")
            shutil.rmtree(directory)

    def configure(self, **values):
        state=json.loads(self.state.read_text())
        state.update(values)
        self.state.write_text(json.dumps(state))

    def archive(self, extra=(), empty=False):
        with tarfile.open(self.backup/"object-storage.tar.gz","w:gz") as archive:
            entries=[] if empty else [("srv/fullnet/object-storage/volume.dat",b"original-volume",tarfile.REGTYPE)]
            entries.extend(extra)
            for name,content,kind in entries:
                item=tarfile.TarInfo(name)
                item.type=kind
                item.mode=0o600
                item.size=len(content) if kind==tarfile.REGTYPE else 0
                if kind in (tarfile.SYMTYPE,tarfile.LNKTYPE): item.linkname="../../outside"
                archive.addfile(item,io.BytesIO(content) if kind==tarfile.REGTYPE else None)

    def checksums(self):
        entries=[]
        for path in sorted(self.backup.iterdir()):
            if path.name not in ("SHA256SUMS","COMPLETE","OBJECT-RESTORE-VERIFIED"):
                path.chmod(0o600)
                entries.append(hashlib.sha256(path.read_bytes()).hexdigest()+"  ./"+path.name)
        (self.backup/"SHA256SUMS").write_text("\n".join(entries)+"\n")
        (self.backup/"SHA256SUMS").chmod(0o600)

    def commands(self):
        return [json.loads(line) for line in self.trace.read_text().splitlines()] if self.trace.exists() else []

    def execute(self,*args):
        return subprocess.run(["bash",str(SCRIPT),*(args or ("192.0.2.20",str(self.backup)))],
                              env=self.env,capture_output=True,text=True,timeout=35)

    def failure(self):
        result=self.execute()
        self.assertNotEqual(result.returncode,0)
        self.assertNotIn("隔离恢复通过",result.stdout)
        self.assertFalse((self.backup/"OBJECT-RESTORE-VERIFIED").exists())
        self.assertNotIn("private-credential-sentinel",result.stdout+result.stderr)
        self.assertEqual(Path("/srv/fullnet/online-data-sentinel").read_text(),"original-online-data")
        return result

    def test_success_receipt_is_after_stop_and_data_removal(self):
        manifest_hash=hashlib.sha256((self.backup/"SHA256SUMS").read_bytes()).hexdigest()
        archive_hash=hashlib.sha256((self.backup/"object-storage.tar.gz").read_bytes()).hexdigest()
        result=self.execute()
        self.assertEqual(result.returncode,0,result.stderr)
        receipt=self.backup/"OBJECT-RESTORE-VERIFIED"
        value=json.loads(receipt.read_text())
        self.assertEqual(value["backupManifestSha256"],manifest_hash)
        self.assertEqual(value["objectArchiveSha256"],archive_hash)
        self.assertTrue(value["serviceStopped"] and value["temporaryDataRemoved"] and value["versioningAuthAndProbeVerified"])
        self.assertEqual(receipt.stat().st_mode&0o777,0o600)
        self.assertFalse(json.loads(self.state.read_text())["active"])
        self.assertEqual(set(Path("/var/tmp").glob("fullnet-object-restore-*")),self.before_work)
        self.assertTrue(all(not call["markerExists"] for call in self.commands()))
        start=next(call for call in self.commands() if call["command"]=="systemd-run")
        self.assertIn("--property=PrivateNetwork=yes",start["args"])
        self.assertIn("--property=RuntimeMaxSec=300",start["args"])
        self.assertIn("-volume.max=32",start["args"])
        self.assertEqual(Path("/srv/fullnet/online-data-sentinel").read_text(),"original-online-data")

    def test_failed_stop_never_signs_success(self):
        self.configure(stop_failure=True)
        self.failure()
        self.assertTrue(json.loads(self.state.read_text())["active"])
        self.assertTrue(set(Path("/var/tmp").glob("fullnet-object-restore-*"))-self.before_work)

    def test_false_stop_with_live_pid_never_signs_success(self):
        self.configure(false_stop=True)
        self.failure()
        self.assertTrue(json.loads(self.state.read_text())["active"])

    def test_failed_directory_cleanup_never_signs_success(self):
        self.configure(remove_failure=True)
        self.failure()
        self.assertFalse(json.loads(self.state.read_text())["active"])

    def test_start_response_loss_still_stops_owned_unit(self):
        self.configure(start_failure=True)
        self.failure()
        self.assertFalse(json.loads(self.state.read_text())["active"])
        self.assertTrue(any(call["command"]=="systemctl" and call["args"][0]=="stop" for call in self.commands()))

    def test_non_private_network_rejected(self):
        self.configure(public_network=True)
        self.failure()
        self.assertFalse(json.loads(self.state.read_text())["active"])

    def test_wrong_restore_directory_rejected(self):
        self.configure(wrong_directory=True)
        self.failure()

    def test_network_address_failure_cleans_up(self):
        self.configure(address_failure=True)
        self.failure()

    def test_probe_failure_keeps_private_bounded_diagnostic(self):
        self.configure(probe_failure=True)
        self.failure()
        unit=json.loads(self.state.read_text())["unit"]
        path=Path("/var/log/fullnet")/(unit+".log")
        self.assertIn("private-credential-sentinel",path.read_text())
        self.assertLessEqual(path.stat().st_size,65536)
        self.assertEqual(path.stat().st_mode&0o777,0o600)
        self.assertFalse(json.loads(self.state.read_text())["active"])

    def test_backup_changed_during_probe_rejected(self):
        self.configure(mutate_backup=True)
        self.failure()

    def test_diagnostic_write_failure_still_stops_restore(self):
        self.configure(start_failure=True,diagnostic_failure=True)
        self.failure()
        self.assertFalse(json.loads(self.state.read_text())["active"])

    def test_corrupt_archive_rejected_before_service_start(self):
        with (self.backup/"object-storage.tar.gz").open("ab") as output: output.write(b"tampered")
        self.failure()
        self.assertEqual(self.commands(),[])

    def test_wrong_backup_role_rejected(self):
        (self.backup/"ROLE").write_text("primary\n")
        self.checksums()
        self.failure()
        self.assertEqual(self.commands(),[])

    def test_missing_complete_rejected(self):
        (self.backup/"COMPLETE").unlink()
        self.failure()

    def test_backup_permission_breach_rejected(self):
        (self.backup/"object-storage.tar.gz").chmod(0o644)
        self.failure()

    def test_archive_traversal_rejected(self):
        self.archive(extra=[("../outside",b"x",tarfile.REGTYPE)])
        self.checksums()
        self.failure()
        self.assertFalse(any(call["command"]=="systemd-run" for call in self.commands()))

    def test_archive_wrong_data_root_rejected(self):
        self.archive(extra=[("etc/forbidden",b"x",tarfile.REGTYPE)])
        self.checksums()
        self.failure()

    def test_archive_links_and_special_files_rejected(self):
        for kind in (tarfile.SYMTYPE,tarfile.LNKTYPE,tarfile.CHRTYPE,tarfile.FIFOTYPE):
            with self.subTest(kind=kind):
                self.archive(extra=[("srv/fullnet/object-storage/unsafe",b"",kind)])
                self.checksums()
                self.failure()

    def test_archive_duplicate_paths_rejected(self):
        self.archive(extra=[("./srv/fullnet/object-storage/volume.dat",b"other",tarfile.REGTYPE)])
        self.checksums()
        self.failure()

    def test_archive_parent_file_collision_rejected(self):
        self.archive(extra=[("srv/fullnet/object-storage/parent",b"x",tarfile.REGTYPE),
                            ("srv/fullnet/object-storage/parent/child",b"x",tarfile.REGTYPE)])
        self.checksums()
        self.failure()

    def test_empty_archive_rejected(self):
        self.archive(empty=True)
        self.checksums()
        self.failure()

    def test_old_restore_marker_preserved(self):
        marker=self.backup/"OBJECT-RESTORE-VERIFIED"
        marker.write_text("historical-evidence")
        result=self.execute()
        self.assertEqual(result.returncode,3)
        self.assertEqual(marker.read_text(),"historical-evidence")
        self.assertEqual(self.commands(),[])

    def test_backup_generator_lock_blocks_restore(self):
        with Path("/run/fullnet-backup.lock").open("a") as lock:
            fcntl.flock(lock,fcntl.LOCK_EX|fcntl.LOCK_NB)
            self.failure()
        self.assertEqual(self.commands(),[])

    def test_another_restore_lock_blocks_restore(self):
        with Path("/run/fullnet-object-restore.lock").open("a") as lock:
            fcntl.flock(lock,fcntl.LOCK_EX|fcntl.LOCK_NB)
            self.failure()
        self.assertEqual(self.commands(),[])

    def test_invalid_address_rejected_before_backup_read(self):
        result=self.execute("invalid-address",str(self.backup))
        self.assertEqual(result.returncode,2)
        self.assertEqual(self.commands(),[])

    def test_explicit_backup_is_required(self):
        result=self.execute("192.0.2.20")
        self.assertEqual(result.returncode,2)
        self.assertEqual(self.commands(),[])

    def test_external_backup_directory_rejected(self):
        result=self.execute("192.0.2.20",str(self.root))
        self.assertEqual(result.returncode,2)
        self.assertEqual(self.commands(),[])

    def test_signal_stops_owned_unit_without_success_marker(self):
        self.configure(wait_signal=True)
        with subprocess.Popen(["bash",str(SCRIPT),"192.0.2.20",str(self.backup)],env=self.env,
                              stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True,start_new_session=True) as process:
            deadline=time.monotonic()+10
            while not self.wait.exists() and process.poll() is None and time.monotonic()<deadline: time.sleep(.02)
            self.assertTrue(self.wait.exists())
            os.killpg(process.pid,signal.SIGTERM)
            output,error=process.communicate(timeout=10)
            self.assertNotEqual(process.returncode,0)
            self.assertNotIn("隔离恢复通过",output)
        self.assertFalse((self.backup/"OBJECT-RESTORE-VERIFIED").exists())
        self.assertFalse(json.loads(self.state.read_text())["active"])
        self.assertEqual(set(Path("/var/tmp").glob("fullnet-object-restore-*")),self.before_work)


if __name__=="__main__":
    unittest.main()
