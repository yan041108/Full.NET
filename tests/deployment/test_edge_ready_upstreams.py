import importlib.util
import contextlib
import io
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import call, patch


SCRIPT = Path(__file__).resolve().parents[2] / "eng/deploy/ubuntu/Refresh-Edge.py"
SPEC = importlib.util.spec_from_file_location("refresh_edge", SCRIPT)
EDGE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(EDGE)


def endpoint(address, **conditions):
    return {"addresses": [address], "conditions": conditions}


def slices(*endpoints):
    return {"items": [{"endpoints": endpoints}]}


class EdgeReadyUpstreamTests(unittest.TestCase):
    def setUp(self):
        directory = self.enterContext(tempfile.TemporaryDirectory())
        self.target = Path(directory) / "fullnet-upstream.conf"
        self.previous = EDGE.render_upstream(["10.42.0.88"]).encode("utf-8")

    def test_selects_unique_ready_serving_endpoints_only(self):
        payload = slices(
            endpoint("10.42.0.91", ready=True, serving=True),
            endpoint("10.42.0.90", ready=True),
            endpoint("10.42.0.90", ready=True),
            endpoint("10.42.0.92", ready=True, serving=False),
            endpoint("10.42.0.93", ready=True, terminating=True),
            endpoint("10.42.0.94", ready=False),
            endpoint("10.42.0.95"),
        )
        self.assertEqual(EDGE.ready_addresses(payload), ["10.42.0.90", "10.42.0.91"])
        with self.assertRaises(ValueError):
            EDGE.ready_addresses(slices(endpoint("10.43.0.10", ready=True)))

    def test_empty_snapshot_disables_stale_routes_and_recovery_reopens_proxy(self):
        self.target.write_bytes(self.previous)
        empty = json.dumps(slices(endpoint("10.42.0.88", ready=False)))
        recovered = json.dumps(slices(endpoint("10.42.0.97", ready=True, serving=True)))
        with (patch.object(EDGE.subprocess, "check_output", side_effect=[empty, empty, recovered]),
              patch.object(EDGE.subprocess, "run") as commands):
            self.assertEqual(EDGE.refresh(self.target), 0)
            disabled = self.target.read_text(encoding="utf-8")
            self.assertIn("server 127.0.0.1:1 down;", disabled)
            self.assertNotIn("10.42.0.88", disabled)
            self.assertEqual(EDGE.refresh(self.target), 0)
            self.assertEqual(EDGE.refresh(self.target), 1)
            self.assertEqual(commands.call_args_list, [
                call(["nginx", "-t"], check=True, timeout=15, capture_output=True),
                call(["systemctl", "reload", "nginx"], check=True, timeout=15, capture_output=True),
            ] * 2)
        current = self.target.read_text(encoding="utf-8")
        self.assertIn("server 10.42.0.97:8080 max_fails=2 fail_timeout=5s;", current)
        self.assertIn("ip_hash;", current)
        self.assertIn("keepalive 32;", current)
        self.assertNotIn(" down;", current)

    def test_failed_endpoint_lookup_preserves_last_valid_config(self):
        self.target.write_bytes(self.previous)
        with (patch.object(EDGE.subprocess, "check_output", side_effect=subprocess.TimeoutExpired("k3s", 15)),
              patch.object(EDGE.subprocess, "run") as commands):
            with self.assertRaises(subprocess.TimeoutExpired):
                EDGE.refresh(self.target)
            commands.assert_not_called()
        self.assertEqual(self.target.read_bytes(), self.previous)

    def test_nginx_check_or_reload_failure_restores_previous_config(self):
        for stage in ("validation", "reload"):
            with self.subTest(stage=stage):
                self.target.write_bytes(self.previous)
                error = subprocess.CalledProcessError(1, stage)
                commands = [error] if stage == "validation" else [None, error, None, None]
                with (patch.object(EDGE.subprocess, "check_output", return_value=json.dumps(slices())),
                      patch.object(EDGE.subprocess, "run", side_effect=commands)):
                    with self.assertRaises(EDGE.RefreshFailure) as failure:
                        EDGE.refresh(self.target)
                self.assertTrue(failure.exception.config_restored)
                self.assertEqual(failure.exception.rollback_reload_acknowledged, stage == "reload")
                self.assertEqual(self.target.read_bytes(), self.previous)
                self.assertEqual(list(self.target.parent.iterdir()), [self.target])

    def test_first_write_is_removed_when_nginx_validation_fails(self):
        with (patch.object(EDGE.subprocess, "check_output", return_value=json.dumps(slices())),
              patch.object(EDGE.subprocess, "run", side_effect=subprocess.CalledProcessError(1, "nginx"))):
            with self.assertRaises(EDGE.RefreshFailure):
                EDGE.refresh(self.target)
        self.assertEqual(list(self.target.parent.iterdir()), [])

    def test_applied_reload_with_lost_response_reloads_restored_config(self):
        self.target.write_bytes(self.previous)
        active, reloads = [self.previous], []
        def command(arguments, **kwargs):
            if arguments[0] == "systemctl":
                active[0] = self.target.read_bytes()
                reloads.append(arguments)
                if len(reloads) == 1:
                    raise subprocess.TimeoutExpired("private-command", 15)
        with (patch.object(EDGE.subprocess, "check_output", return_value=json.dumps(slices())),
              patch.object(EDGE.subprocess, "run", side_effect=command)):
            with self.assertRaises(EDGE.RefreshFailure) as failure:
                EDGE.refresh(self.target)
        self.assertEqual(str(failure.exception), "edge_reload_failed")
        self.assertTrue(failure.exception.rollback_reload_acknowledged)
        self.assertEqual(active[0], self.previous)
        self.assertEqual(len(reloads), 2)
        self.assertEqual(self.target.read_bytes(), self.previous)

    def test_rollback_validation_failure_reports_unacknowledged_recovery(self):
        self.target.write_bytes(self.previous)
        with (patch.object(EDGE.subprocess, "check_output", return_value=json.dumps(slices())),
              patch.object(EDGE.subprocess, "run", side_effect=[None, OSError("private"), OSError("private")]) as commands):
            with self.assertRaises(EDGE.RefreshFailure) as failure:
                EDGE.refresh(self.target)
        self.assertEqual(str(failure.exception), "edge_rollback_reload_failed")
        self.assertTrue(failure.exception.config_restored)
        self.assertFalse(failure.exception.rollback_reload_acknowledged)
        self.assertEqual(commands.call_count, 3)
        self.assertEqual(self.target.read_bytes(), self.previous)

    def test_rollback_reload_failure_reports_unacknowledged_recovery(self):
        self.target.write_bytes(self.previous)
        with (patch.object(EDGE.subprocess, "check_output", return_value=json.dumps(slices())),
              patch.object(EDGE.subprocess, "run", side_effect=[None, OSError("private"), None, OSError("private")])):
            with self.assertRaises(EDGE.RefreshFailure) as failure:
                EDGE.refresh(self.target)
        self.assertEqual(str(failure.exception), "edge_rollback_reload_failed")
        self.assertTrue(failure.exception.config_restored)
        self.assertFalse(failure.exception.rollback_reload_acknowledged)

    def test_rollback_write_failure_does_not_claim_restored_config(self):
        self.target.write_bytes(self.previous)
        original = EDGE.atomic_write
        attempts = []
        def write(*arguments):
            attempts.append(arguments)
            if len(attempts) == 2:
                raise OSError("private-write-error")
            return original(*arguments)
        with (patch.object(EDGE.subprocess, "check_output", return_value=json.dumps(slices())),
              patch.object(EDGE.subprocess, "run", side_effect=[None, OSError("private")]) as commands,
              patch.object(EDGE, "atomic_write", side_effect=write)):
            with self.assertRaises(EDGE.RefreshFailure) as failure:
                EDGE.refresh(self.target)
        self.assertEqual(str(failure.exception), "edge_rollback_file_failed")
        self.assertFalse(failure.exception.config_restored)
        self.assertFalse(failure.exception.rollback_reload_acknowledged)
        self.assertEqual(commands.call_count, 2)

    def test_first_reload_failure_removes_new_file_and_reloads_original_config(self):
        with (patch.object(EDGE.subprocess, "check_output", return_value=json.dumps(slices())),
              patch.object(EDGE.subprocess, "run", side_effect=[None, OSError("private"), None, None]) as commands):
            with self.assertRaises(EDGE.RefreshFailure) as failure:
                EDGE.refresh(self.target)
        self.assertTrue(failure.exception.rollback_reload_acknowledged)
        self.assertFalse(self.target.exists())
        self.assertEqual(commands.call_count, 4)

    def test_main_failure_has_no_success_or_sensitive_command_output(self):
        for error, expected in ((EDGE.RefreshFailure("edge_rollback_reload_failed", True), "edge_rollback_reload_failed"),
                                (subprocess.CalledProcessError(1, "private-command", stderr="private-response"), "edge_refresh_failed")):
            with self.subTest(code=expected):
                output, errors = io.StringIO(), io.StringIO()
                with (patch.object(EDGE, "REFRESH_LOCK", self.target.parent / "lock"),
                      patch.object(EDGE, "refresh", side_effect=error),
                      contextlib.redirect_stdout(output), contextlib.redirect_stderr(errors)):
                    status = EDGE.main()
                self.assertEqual(status, 1)
                self.assertEqual(output.getvalue(), "")
                report = json.loads(errors.getvalue())
                self.assertEqual(report["code"], expected)
                self.assertFalse(report["rollbackReloadAcknowledged"])
                self.assertNotIn("private", errors.getvalue())


if __name__ == "__main__":
    unittest.main()
