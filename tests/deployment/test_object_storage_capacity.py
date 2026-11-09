import contextlib
import importlib.util
import io
import json
import os
from pathlib import Path
import stat
import tempfile
import unittest
from unittest.mock import patch


SCRIPT = Path(__file__).resolve().parents[2] / "eng/deploy/ubuntu/Verify-ObjectStorageCapacity.py"
SPEC = importlib.util.spec_from_file_location("object_capacity", SCRIPT)
PROBE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PROBE)


class ObjectStorageCapacityTests(unittest.TestCase):
    def test_existing_thresholds_distinguish_risk_and_blocked_growth(self):
        for slots, free_bytes, expected in (
            (7, PROBE.MIN_FREE_BYTES + 1, "growth_headroom_available"),
            (6, PROBE.MIN_FREE_BYTES + 1, "growth_at_risk"),
            (0, PROBE.MIN_FREE_BYTES + 1, "growth_blocked"),
            (7, PROBE.MIN_FREE_BYTES, "growth_blocked"),
        ):
            with self.subTest(slots=slots, free_bytes=free_bytes):
                self.assertEqual(expected, PROBE.assess({"Max": 20, "Free": slots}, free_bytes))

    def test_invalid_topology_returns_unavailable_without_capacity_values(self):
        with patch.object(PROBE.urllib.request, "build_opener") as opener:
            opener.return_value.open.return_value.__enter__.return_value.read.return_value = (
                b'{"Topology":{"Max":20,"Free":21}}'
            )
            with patch.object(PROBE.shutil, "disk_usage") as disk_usage:
                disk_usage.return_value.free = PROBE.MIN_FREE_BYTES + 1
                self.assertEqual({"state": "probe_unavailable"}, PROBE.collect())

    def test_cli_replaces_capacity_sample_across_blocked_failure_and_recovery(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            for timestamp, state, slots, exit_code in (
                (100, "growth_blocked", 0, 1),
                (200, "probe_unavailable", None, 2),
                (300, "growth_headroom_available", 10, 0),
            ):
                result = {"state": state}
                if slots is not None:
                    result.update(volumeSlots=20, freeSlots=slots, freeBytes=PROBE.MIN_FREE_BYTES + 1)
                stdout, stderr = io.StringIO(), io.StringIO()
                with patch.object(PROBE, "collect", return_value=result), patch.object(PROBE.time, "time", return_value=timestamp):
                    with patch("sys.argv", [str(SCRIPT), "--metrics-directory", temporary]):
                        with contextlib.redirect_stdout(stdout), contextlib.redirect_stderr(stderr):
                            self.assertEqual(exit_code, PROBE.main())
                self.assertEqual(result, json.loads(stderr.getvalue() if exit_code == 2 else stdout.getvalue()))
                files = list(directory.iterdir())
                self.assertEqual([f"{PROBE.METRIC_PREFIX}.prom"], [file.name for file in files])
                if os.name != "nt":
                    self.assertEqual(0o644, stat.S_IMODE(files[0].stat().st_mode))
                sample = files[0].read_text()
                self.assertIn(f"sample_timestamp_seconds {timestamp}\n", sample)
                self.assertIn(f"probe_success {int(slots is not None)}\n", sample)
                if slots is None:
                    self.assertNotIn("growth_state", sample)
                    self.assertNotIn("free_slots", sample)
                    self.assertNotIn("free_bytes", sample)
                else:
                    self.assertIn(f"growth_state {PROBE.GROWTH_STATE[state]}\n", sample)
                    self.assertIn(f"free_slots {slots}\n", sample)

    def test_failed_atomic_replace_retains_previous_sample_and_removes_temporary_file(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            previous = directory / f"{PROBE.METRIC_PREFIX}.prom"
            previous.write_text("previous\n")
            with patch.object(Path, "replace", side_effect=PermissionError):
                with self.assertRaises(PermissionError):
                    PROBE.publish_metrics(directory, {"state": "probe_unavailable"})
            self.assertEqual("previous\n", previous.read_text())
            self.assertEqual([previous], list(directory.iterdir()))

    def test_cli_reports_publish_failure_separately_from_growth(self):
        result = {"state": "growth_headroom_available", "volumeSlots": 20, "freeSlots": 10, "freeBytes": PROBE.MIN_FREE_BYTES + 1}
        stdout, stderr = io.StringIO(), io.StringIO()
        with patch.object(PROBE, "collect", return_value=result), patch.object(PROBE, "publish_metrics", side_effect=PermissionError):
            with patch("sys.argv", [str(SCRIPT), "--metrics-directory", "unwritable"]):
                with contextlib.redirect_stdout(stdout), contextlib.redirect_stderr(stderr):
                    self.assertEqual(2, PROBE.main())
        self.assertEqual("", stdout.getvalue())
        self.assertEqual({"state": "metrics_publish_failed"}, json.loads(stderr.getvalue()))


if __name__ == "__main__":
    unittest.main()
