"""Guard against accidental reconnection of main CI to historical release publishers.

This is a source-only contract test: no GitHub API, tags, release or game input.
"""
import fnmatch
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
WORKFLOWS = ROOT / ".github" / "workflows"
CI_NAME = "Main Build CI (Release-Decoupled)"
RETIRED_NAME = "Check V3 Production Engine"


def display_name(text: str) -> str:
    match = re.search(r"(?m)^name:\s*(.+?)\s*$", text)
    if not match:
        raise AssertionError("Main CI must have a static display name")
    return match.group(1).strip("'\"")


def run_subscriptions(text: str) -> list[str]:
    """Read normal inline workflow_run 'workflows: [..]' filter syntax."""
    patterns = []
    for match in re.finditer(r"(?m)^\s*workflows:\s*\[([^\]]+)\]", text):
        patterns.extend(
            item.strip().strip("'\"")
            for item in match.group(1).split(",")
            if item.strip()
        )
    return patterns


class LegacyPublisherEventIsolation(unittest.TestCase):
    def test_main_ci_name_is_decoupled_but_required_job_is_stable(self):
        ci = (WORKFLOWS / "ci.yml").read_text(encoding="utf-8")
        self.assertEqual(display_name(ci), CI_NAME)
        self.assertNotEqual(display_name(ci), RETIRED_NAME)
        self.assertRegex(ci, r"(?m)^  build-and-test:\s*$")
        self.assertIn("  pull_request:", ci)
        self.assertIn("  push:", ci)
        self.assertIn("    branches: [ main", ci)

    def test_no_release_workflow_subscribes_to_main_build_ci(self):
        legacy_subscribers = []
        new_subscribers = []
        for path in sorted(WORKFLOWS.glob("release-*.yml")):
            content = path.read_text(encoding="utf-8")
            if not re.search(r"(?m)^  workflow_run:\s*$", content):
                continue
            for pattern in run_subscriptions(content):
                if fnmatch.fnmatchcase(CI_NAME, pattern):
                    new_subscribers.append(str(path.relative_to(ROOT)))
                if fnmatch.fnmatchcase(RETIRED_NAME, pattern):
                    legacy_subscribers.append(str(path.relative_to(ROOT)))
        self.assertGreater(
            len(legacy_subscribers), 1,
            "Expected historical release listeners to stay identifiable",
        )
        self.assertEqual(
            new_subscribers, [],
            "Reconnecting historical release workflows to main CI requires a "
            "separate release authorization and explicit review",
        )

    def test_historical_current_release_can_still_be_dispatched_manually(self):
        latest = (WORKFLOWS / "release-v3285-auto-update.yml").read_text(encoding="utf-8")
        self.assertIn("  workflow_dispatch:", latest)
        self.assertIn('workflows: ["Check V3 Production Engine"]', latest)
        self.assertNotIn('workflows: ["Main Build CI (Release-Decoupled)"]', latest)


if __name__ == "__main__":
    unittest.main()
