"""Offline tests: no external tools, Windows services, macros or network."""
import importlib.util
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

SOURCE = Path(__file__).resolve().parents[2] / "source" / "FishingAutomation" / "ai-autopilot" / "scheduler_diagnostics.py"
spec = importlib.util.spec_from_file_location("scheduler_diagnostics", SOURCE)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class DiagnosticsTests(unittest.TestCase):
    def test_missing_tools_default_to_no_capture(self):
        with patch.object(module, "locate", return_value=None):
            self.assertEqual(module.doctor()["available"], dict.fromkeys(module.TOOLS, False))
        with patch.dict(os.environ, {"MABIAUTO_SCHEDULER_DEBUG": "0"}):
            self.assertFalse(module.doctor()["debug_output_opt_in"])

    def test_pid_validation(self):
        self.assertEqual(module.pid_value("123"), 123)
        for p in ("0", "-1", "abc", "2147483648"):
            with self.assertRaises(Exception):
                module.pid_value(p)

    def test_dump_requires_explicit_consent(self):
        with tempfile.TemporaryDirectory() as folder:
            with self.assertRaisesRegex(RuntimeError, "allow-sensitive-dump"):
                module.dump(123, Path(folder), False)
            self.assertEqual(list(Path(folder).iterdir()), [])

    def test_capture_rejects_busy_debugview_before_writing_files(self):
        from subprocess import CompletedProcess
        with tempfile.TemporaryDirectory() as folder:
            with patch.object(module, "locate", return_value=Path(__file__)):
                with patch.object(module.subprocess, "run", return_value=CompletedProcess([], 0, "running=true\n", "")):
                    with self.assertRaisesRegex(RuntimeError, "Another DebugView"):
                        module.capture(123, 60, Path(folder))
            self.assertEqual(list(Path(folder).iterdir()), [])

    def test_capture_arguments_bounded_pid_filtered_kernel_disabled(self):
        from subprocess import CompletedProcess
        with tempfile.TemporaryDirectory() as folder:
            with patch.object(module, "locate", return_value=Path(__file__)), \
                 patch.object(module.subprocess, "run", return_value=CompletedProcess([], 0, "running=false", "")), \
                 patch.object(module, "run_bounded", return_value=0) as run:
                result = module.capture(4321, 30, Path(folder))
                args = run.call_args.args[0]
                self.assertIn("--pid-filter", args)
                self.assertEqual(args[args.index("--pid-filter") + 1], "4321")
                self.assertIn("--no-kernel", args)
                self.assertIn("--max-lines", args)
                self.assertEqual(result["status"], "captured")

    def test_dump_once_minimal_not_full_and_no_persistent_monitor(self):
        with tempfile.TemporaryDirectory() as folder:
            with patch.object(module, "locate", return_value=Path(__file__)), \
                 patch.object(module, "run_bounded", return_value=0) as run:
                result = module.dump(4321, Path(folder), True)
                args = run.call_args.args[0]
                self.assertIn("-mm", args)
                self.assertNotIn("-ma", args)
                self.assertNotIn("-h", args)
                self.assertNotIn("-e", args)
                self.assertEqual(result["status"], "tool_failed")

    def test_compare_diff_exit_one_is_success_if_diff_written(self):
        with tempfile.TemporaryDirectory() as folder:
            with patch.object(module, "locate", return_value=Path(__file__)):
                def fake_run(args, timeout, out, err):
                    Path(args[-1]).write_bytes(b"PNG")
                    return 1
                with patch.object(module, "run_bounded", side_effect=fake_run):
                    result = module.compare(Path("baseline.png"), Path("observed.png"), Path(folder))
                self.assertEqual(result["status"], "compared")
                self.assertTrue(result["different"])

    def test_image_rejects_symlink_and_oversize(self):
        with tempfile.TemporaryDirectory() as folder:
            base = Path(folder)
            img = base / "a.png"
            img.write_bytes(b"DATA")
            self.assertEqual(module.image_file(str(img)), img.resolve())
            other = base / "link.png"
            other.symlink_to(img)
            with self.assertRaises(ValueError):
                module.image_file(str(other))
            with (base / "huge.png").open("wb") as f:
                f.truncate(17 * 1024 * 1024)
            with self.assertRaises(ValueError):
                module.image_file(str(base / "huge.png"))


if __name__ == "__main__":
    unittest.main()
