"""Image connection tests: real PNG bytes, fake Codex (no account/tokens)."""
import io
import json
import struct
import subprocess
import tempfile
import unittest
import zlib
from pathlib import Path
from unittest import mock

import image_probe
import repair


def png(width=64, height=48):
    def chunk(kind, data):
        return (struct.pack(">I", len(data)) + kind + data
                + struct.pack(">I", zlib.crc32(kind + data) & 0xffffffff))
    raw = b"".join(b"\x00" + b"\x20\x30\x40" * width for _ in range(height))
    return (image_probe.PNG_MAGIC
            + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw))
            + chunk(b"IEND", b""))


def sample(source="github:runtime-errors/2026-10-24/20261024_183512_324_다중가공_오류_e3a380/summary.json"):
    return dict(id="test-fingerprint", source=source, mode="가공", title="다중가공 오류",
                version="V3.2.61",
                detail="K 재진입 HUD 2/4 필드 인증 실패. 입력하지 않고 정지")


class ImageProbeTests(unittest.TestCase):
    def test_local_png_metadata_and_no_codex_by_default(self):
        with tempfile.TemporaryDirectory() as tmp:
            image = Path(tmp) / "screenshot.png"
            image.write_bytes(png())
            with mock.patch.object(image_probe.subprocess, "run",
                                   side_effect=AssertionError("Unexpected paid Codex call")):
                result = image_probe.inspect_image(sample(), image, False, False, False)
            self.assertEqual(result["status"], "image_ready")
            self.assertEqual(result["image"]["width"], 64)
            self.assertEqual(result["image"]["height"], 48)
            self.assertEqual(result["codex_calls"], 0)
            self.assertEqual(result["model"], "gpt-6.1-sol")
            self.assertEqual(result["reasoning_effort"], "low")

    def test_png_validation_rejects_invalid_and_oversize(self):
        for bad in (b"not an image", image_probe.PNG_MAGIC + b"\x00" * 32,
                    png(16, 16), b"\x89PNG\r\n\x1a\n" + b"0" * (image_probe.MAX_BYTES + 1)):
            with self.assertRaises(ValueError):
                image_probe.validated_png(bad)

    def test_github_screenshot_fetch_is_strictly_scoped(self):
        data = png(100, 90)
        with mock.patch.object(image_probe.urllib.request, "urlopen", return_value=io.BytesIO(data)) as hit:
            content = image_probe.read_png(sample()["source"], None, True)
        self.assertEqual(content, data)
        url = hit.call_args.args[0].full_url
        self.assertTrue(url.startswith(image_probe.LOG_RAW))
        self.assertIn("20261024_183512_324_", url)
        self.assertTrue(url.endswith("/screen.png"))
        for invalid in ["local:runtime-errors/2026-10-24/x/summary.json",
                        "github:../../outside/summary.json",
                        "github:runtime-errors/2026-10-24/x/recent.log"]:
            with self.assertRaises(ValueError):
                image_probe.read_png(invalid, None, True)

    def test_explicit_consent_required(self):
        with mock.patch.object(image_probe, "read_png",
                               side_effect=AssertionError("Must not read screenshots")):
            with self.assertRaisesRegex(ValueError, "acknowledge-cost"):
                image_probe.inspect_image(sample(), None, True, True, False)

    def test_real_png_is_passed_to_sol_61_light_and_output_returned(self):
        with tempfile.TemporaryDirectory() as tmp:
            file = Path(tmp) / "screen.png"
            file.write_bytes(png())
            calls = []
            def fake_run(argv, cwd, **kwargs):
                calls.append(list(argv))
                self.assertEqual(argv[0:4], ["codex", "exec", "--model", "gpt-6.1-sol"])
                self.assertEqual(argv[4:6], ["-c", "model_reasoning_effort=low"])
                self.assertIn("--sandbox", argv)
                self.assertEqual(argv[argv.index("--sandbox") + 1], "read-only")
                self.assertIn("--image", argv)
                self.assertIn("<untrusted-incident>", argv[-3])
                image_path = Path(argv[-1])
                self.assertTrue(image_path.is_file())
                self.assertEqual(image_path.read_bytes(), png())
                self.assertEqual(argv[-2], "--image")
                return subprocess.CompletedProcess(argv, 0, "HUD 표시가 확인되지 않음", "")
            with mock.patch.object(image_probe.subprocess, "run", side_effect=fake_run):
                result = image_probe.inspect_image(sample(), file, False, True, True)
            self.assertEqual(result["status"], "image_analysis_returned")
            self.assertEqual(result["codex_calls"], 1)
            self.assertEqual(len(calls), 1)
            self.assertIn("HUD", result["diagnostic"])
            self.assertFalse(result["code_modified"])
            self.assertFalse(result["real_game_verified"])
            self.assertFalse(Path(calls[0][-1]).exists(), "Temporary image must be removed")

    def test_codex_failure_is_not_misreported_as_success(self):
        with tempfile.TemporaryDirectory() as tmp:
            file = Path(tmp) / "screen.png"
            file.write_bytes(png())
            with mock.patch.object(image_probe.subprocess, "run",
                                   return_value=subprocess.CompletedProcess([], 1, "", "unsupported model")):
                result = image_probe.inspect_image(sample(), file, False, True, True)
            self.assertEqual(result["status"], "image_analysis_failed")
            self.assertEqual(result["codex_calls"], 1)
            self.assertNotIn("diagnostic", result)

    def test_no_daily_limit_by_default_but_manual_opt_in_works(self):
        with tempfile.TemporaryDirectory() as tmp:
            with repair.State(Path(tmp)) as state:
                src = dict(schema=1, source="runtime", version="V3.2.62", mode="가공",
                           title="new error", detail="plain logic case")
                for idx in range(5):
                    key = state.add(f"fixture-{idx}", dict(src, detail=f"different problem {idx}"))
                    state.reserve(key, None)
                self.assertEqual(sum(j["calls"] for j in state.jobs()), 5)
                self.assertEqual(len(state.jobs()), 5)

    def test_cli_real_image_preview_using_existing_error(self):
        with tempfile.TemporaryDirectory() as tmp:
            base = Path(tmp)
            image = base / "screen.png"
            image.write_bytes(png())
            with repair.State(base / "db") as state:
                identity = state.add("github:runtime-errors/2026-10-24/20261024_183512_324_다중가공_오류_e3a380/summary.json",
                                    dict(schema=1, source="runtime", version="V3.2.61",
                                         mode="가공", title="다중가공 오류",
                                         detail="HUD 확인 실패"))
            with mock.patch.object(image_probe.subprocess, "run",
                                   side_effect=AssertionError("No paid Codex")):
                with mock.patch("sys.stdout", new_callable=io.StringIO) as captured:
                    ret = repair.main(["--state", str(base / "db"), "inspect-image",
                                       identity, "--image", str(image)])
            self.assertEqual(ret, 0)
            result = json.loads(captured.getvalue())
            self.assertEqual(result["status"], "image_ready")
            self.assertEqual(result["codex_calls"], 0)


if __name__ == "__main__":
    unittest.main()
