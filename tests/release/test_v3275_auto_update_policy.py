"""V3.2.75 rollout gates. Static CI policy; no game-test fabrication."""
import json
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = ROOT / ".github/workflows/release-v3275-auto-update.yml"
AUTH = ROOT / "source/V3.2.75_RELEASE_AUTHORIZATION.json"
NOTES = ROOT / "source/RELEASE_NOTES_V3.2.75.md"
UPDATER = ROOT / "source/FishingAutomation/UpdateManager.cs"
PROJECT = ROOT / "source/FishingAutomation/FishingAutomation.csproj"
CI = ROOT / ".github/workflows/ci.yml"

class V3275ReleaseSafety(unittest.TestCase):
    def test_user_authorization_and_unverified_live_game_status(self):
        a = json.loads(AUTH.read_text(encoding="utf-8"))
        self.assertEqual(a["version"], "V3.2.75")
        self.assertEqual(a["purpose"], "f9_unified_scene_resume_stock_and_latency")
        self.assertEqual(a["sourcePullRequest"], 341)
        self.assertEqual(a["approvedBy"], "user_direct_request_in_chat")
        self.assertEqual(a["request"], "테스트는 다하고 자동업데이트 까지 진행해")
        self.assertIs(a["realGameTested"], False)
        self.assertIs(a["postUpdateGameTestingPending"], True)
        self.assertIs(a["rollbackProvided"], True)
        self.assertIs(a["mainCiRequired"], True)
        self.assertTrue(NOTES.exists())
        self.assertIn("77 not separately replayed", NOTES.read_text(encoding="utf-8"))

    def test_version_and_exact_merged_main_ci_gate(self):
        workflow = WORKFLOW.read_text(encoding="utf-8")
        for marker in (
            'workflows: ["Check V3 Production Engine"]',
            "types: [completed]", "branches: [main]",
            "workflow_run.conclusion == 'success'",
            "workflow_run.event == 'push'",
            "workflow_run.head_branch == 'main'",
            "workflow_run.repository.full_name == github.repository",
            "CI_HEAD_SHA", "main changed since CI; abort",
            "No successful production CI for current main SHA",
            'CurrentVersion = "V3.2.75"',
            "V3.2.75 tag already exists",
            "Release publication failed",
            "Published release missing asset",
        ):
            self.assertIn(marker, workflow)
        self.assertNotIn("  push:\\n", workflow)
        for exact in ("<Version>3.2.75</Version>",
                      "<AssemblyVersion>3.2.75.0</AssemblyVersion>",
                      "<FileVersion>3.2.75.0</FileVersion>",
                      "VersionInfo.FileVersion -ne '3.2.75.0'"):
            self.assertIn(exact, workflow,
                          f"V3.2.75 release workflow missing exact published version check: {exact}")
        self.assertNotIn("3.2.74", workflow,
                         "V3.2.75 workflow must never validate stale V3.2.74 binaries")
        # Historical V3.2.75 release remains immutable, while current source may
        # advance to a newer, separately authorized release.
        current = UPDATER.read_text(encoding="utf-8")
        project = PROJECT.read_text(encoding="utf-8")
        if 'CurrentVersion = "V3.2.75"' in current:
            for marker in ("<Version>3.2.75</Version>",
                           "<AssemblyVersion>3.2.75.0</AssemblyVersion>",
                           "<FileVersion>3.2.75.0</FileVersion>"):
                self.assertIn(marker, project)
        elif 'CurrentVersion = "V3.2.76"' in current:
            self.assertIn('CurrentVersion = "V3.2.76"', current)
            for marker in ("<Version>3.2.76</Version>",
                           "<AssemblyVersion>3.2.76.0</AssemblyVersion>",
                           "<FileVersion>3.2.76.0</FileVersion>"):
                self.assertIn(marker, project)
            self.assertTrue((ROOT / ".github/workflows/release-v3276-auto-update.yml").exists())
        elif 'CurrentVersion = "V3.2.77"' in current:
            self.assertIn('CurrentVersion = "V3.2.77"', current)
            for marker in ("<Version>3.2.77</Version>",
                           "<AssemblyVersion>3.2.77.0</AssemblyVersion>",
                           "<FileVersion>3.2.77.0</FileVersion>"):
                self.assertIn(marker, project)
            self.assertTrue((ROOT / ".github/workflows/release-v3277-auto-update.yml").exists())
            authorization = json.loads((ROOT / "source/V3.2.77_RELEASE_AUTHORIZATION.json").read_text(encoding="utf-8"))
            self.assertEqual(authorization["sourcePullRequest"], 344)
            self.assertEqual(authorization["request"], "진행해")
        elif 'CurrentVersion = "V3.2.78"' in current:
            self.assertIn('CurrentVersion = "V3.2.78"', current)
            for tag, version in [('Version', '3.2.78'), ('AssemblyVersion', '3.2.78.0'), ('FileVersion', '3.2.78.0')]:
                self.assertIn(f'<{tag}>{version}</{tag}>', project)
            self.assertTrue((ROOT / '.github/workflows/release-v3278-auto-update.yml').exists())
            authorization = json.loads((ROOT / 'source/V3.2.78_RELEASE_AUTHORIZATION.json').read_text(encoding='utf-8'))
            self.assertEqual(authorization['sourcePullRequest'], 345)
            self.assertEqual(authorization['request'], '계속 진행해')
        elif 'CurrentVersion = "V3.2.80"' in current:
            for tag, version in [('Version', '3.2.80'), ('AssemblyVersion', '3.2.80.0'), ('FileVersion', '3.2.80.0')]:
                self.assertIn(f'<{tag}>{version}</{tag}>', project)
            self.assertTrue((ROOT / '.github/workflows/release-v3280-auto-update.yml').exists())
            authorization = json.loads((ROOT / 'source/V3.2.80_RELEASE_AUTHORIZATION.json').read_text(encoding='utf-8'))
            self.assertEqual(authorization['sourcePullRequest'], 347)
            self.assertEqual(authorization['request'], '배포해')
        elif 'CurrentVersion = "V3.2.81"' in current:
            for tag, version in [('Version', '3.2.81'), ('AssemblyVersion', '3.2.81.0'), ('FileVersion', '3.2.81.0')]:
                self.assertIn(f'<{tag}>{version}</{tag}>', project)
            self.assertTrue((ROOT / '.github/workflows/release-v3281-auto-update.yml').exists())
            authorization = json.loads((ROOT / 'source/V3.2.81_RELEASE_AUTHORIZATION.json').read_text(encoding='utf-8'))
            self.assertEqual(authorization['sourcePullRequest'], 349)
            self.assertEqual(authorization['request'], '계속 진행해')
        elif 'CurrentVersion = "V3.2.82"' in current:
            for tag, version in [('Version', '3.2.82'), ('AssemblyVersion', '3.2.82.0'), ('FileVersion', '3.2.82.0')]:
                self.assertIn(f'<{tag}>{version}</{tag}>', project)
            self.assertTrue((ROOT / '.github/workflows/release-v3282-auto-update.yml').exists())
            authorization = json.loads((ROOT / 'source/V3.2.82_RELEASE_AUTHORIZATION.json').read_text(encoding='utf-8'))
            self.assertEqual(authorization['sourcePullRequest'], 351)
            self.assertEqual(authorization['request'], '배포까지 계속 진행해')
        elif 'CurrentVersion = "V3.2.83"' in current:
            for tag, version in [('Version', '3.2.83'), ('AssemblyVersion', '3.2.83.0'), ('FileVersion', '3.2.83.0')]:
                self.assertIn(f'<{tag}>{version}</{tag}>', project)
            self.assertTrue((ROOT / '.github/workflows/release-v3283-auto-update.yml').exists())
            authorization = json.loads((ROOT / 'source/V3.2.83_RELEASE_AUTHORIZATION.json').read_text(encoding='utf-8'))
            self.assertEqual(authorization['sourcePullRequest'], 352)
            self.assertEqual(authorization['request'], '배포해')
        elif 'CurrentVersion = "V3.2.84"' in current:
            for tag, version in [('Version', '3.2.84'), ('AssemblyVersion', '3.2.84.0'), ('FileVersion', '3.2.84.0')]:
                self.assertIn(f'<{tag}>{version}</{tag}>', project)
            self.assertTrue((ROOT / '.github/workflows/release-v3284-auto-update.yml').exists())
            authorization = json.loads((ROOT / 'source/V3.2.84_RELEASE_AUTHORIZATION.json').read_text(encoding='utf-8'))
            self.assertEqual(authorization['sourcePullRequest'], 353)
            self.assertEqual(authorization['request'], '배포해')
        elif 'CurrentVersion = "V3.2.85"' in current:
            for tag, version in [('Version', '3.2.85'), ('AssemblyVersion', '3.2.85.0'), ('FileVersion', '3.2.85.0')]:
                self.assertIn(f'<{tag}>{version}</{tag}>', project)
            release_workflow = ROOT / '.github/workflows/release-v3285-auto-update.yml'
            self.assertTrue(release_workflow.exists())
            release_text = release_workflow.read_text(encoding='utf-8')
            for marker in (
                'workflow_run.conclusion == \'success\'',
                'CI_HEAD_SHA', 'main changed since CI; abort release',
                'ROLLBACK_PREVIOUS.cmd', 'Get-FileHash $zip -Algorithm SHA256',
                'MabiAuto_V3.2.85_Windows_Lite.zip.sha256',
                'gh release create V3.2.85',
                'gh release edit V3.2.85 --draft=false',
            ):
                self.assertIn(marker, release_text)
            authorization = json.loads((ROOT / 'source/V3.2.85_RELEASE_AUTHORIZATION.json').read_text(encoding='utf-8'))
            self.assertEqual(authorization['sourcePullRequest'], 360)
            self.assertEqual(authorization['sourcePullRequests'], [354, 358, 359, 360, 361, 363])
            self.assertEqual(authorization['request'], '지금 최신 업데이트 이후 올라온 깃허브 pr 확인해보고 검증하고 배포해')
            self.assertIs(authorization['realGameTested'], False)
            self.assertIs(authorization['mainCiRequired'], True)
        elif 'CurrentVersion = "V3.2.86"' in current:
            for tag, version in [('Version', '3.2.86'), ('AssemblyVersion', '3.2.86.0'), ('FileVersion', '3.2.86.0')]:
                self.assertIn(f'<{tag}>{version}</{tag}>', project)
            release_workflow = ROOT / '.github/workflows/release-v3286-auto-update.yml'
            self.assertTrue(release_workflow.exists())
            release_text = release_workflow.read_text(encoding='utf-8')
            self.assertIn("No successful production CI for current main SHA", release_text)
            self.assertIn("main changed since CI; abort release", release_text)
            self.assertIn("MabiAuto_V3.2.86_Windows_Lite.zip.sha256", release_text)
            self.assertIn("gh release create V3.2.86", release_text)
            self.assertIn("gh release edit V3.2.86 --draft=false", release_text)
            authorization = json.loads((ROOT / 'source/V3.2.86_RELEASE_AUTHORIZATION.json').read_text(encoding='utf-8'))
            self.assertEqual(authorization['sourcePullRequest'], 370)
            self.assertEqual(authorization['request'], '이번꺼 까지 하고 정식배포해')
            self.assertIs(authorization['realGameTested'], False)
            self.assertIs(authorization['mainCiRequired'], True)
        elif 'CurrentVersion = "V3.2.88"' in current:
            for tag, version in [('Version', '3.2.88'),
                                 ('AssemblyVersion', '3.2.88.0'),
                                 ('FileVersion', '3.2.88.0')]:
                self.assertIn(f'<{tag}>{version}</{tag}>', project)
            release = ROOT / '.github/workflows/release-v3288-auto-update.yml'
            self.assertTrue(release.exists())
            w = release.read_text(encoding='utf-8')
            for token in ('No successful production CI for current main SHA',
                          'main changed since CI; abort release',
                          'MabiAuto_V3.2.88_Windows_Lite.zip.sha256',
                          'gh release create V3.2.88',
                          'gh release edit V3.2.88 --draft=false'):
                self.assertIn(token, w)
            authorization = json.loads(
                (ROOT / 'source/V3.2.88_RELEASE_AUTHORIZATION.json').read_text(encoding='utf-8'))
            self.assertEqual(authorization['sourcePullRequest'], 376)
            self.assertEqual(authorization['request'], '한번에 업데이트 하지')
            self.assertIs(authorization['realGameTested'], False)
            self.assertIs(authorization['mainCiRequired'], True)
        elif 'CurrentVersion = "V3.2.87"' in current:
            # Only the current release's exact-version policy controls V3.2.87.
            # Historical published V3.2.75 files still undergo immutable
            # authorization and original workflow checks above.
            for tag, version in [('Version', '3.2.87'), ('AssemblyVersion', '3.2.87.0'), ('FileVersion', '3.2.87.0')]:
                self.assertIn(f'<{tag}>{version}</{tag}>', project)
            release_workflow = ROOT / '.github/workflows/release-v3287-auto-update.yml'
            self.assertTrue(release_workflow.exists())
            release_text = release_workflow.read_text(encoding='utf-8')
            for marker in ('No successful production CI for current main SHA',
                           'main changed since CI; abort release',
                           'MabiAuto_V3.2.87_Windows_Lite.zip.sha256',
                           'gh release create V3.2.87',
                           'gh release edit V3.2.87 --draft=false'):
                self.assertIn(marker, release_text)
            authorization = json.loads(
                (ROOT / 'source/V3.2.87_RELEASE_AUTHORIZATION.json').read_text(encoding='utf-8'))
            self.assertEqual(authorization['sourcePullRequest'], 371)
            self.assertEqual(authorization['request'], '진행해')
            self.assertIs(authorization['realGameTested'], False)
            self.assertIs(authorization['mainCiRequired'], True)
        elif 'CurrentVersion = "V3.2.89"' in current:
            # Historical V3.2.75 publication contract remains unchanged. The
            # newest one-shot publisher owns the current binary version gate.
            for tag, version in [('Version', '3.2.89'),
                                 ('AssemblyVersion', '3.2.89.0'),
                                 ('FileVersion', '3.2.89.0')]:
                self.assertIn(f'<{tag}>{version}</{tag}>', project)
            release = ROOT / '.github/workflows/release-v3289-auto-update.yml'
            self.assertTrue(release.exists())
            w = release.read_text(encoding='utf-8')
            for token in ('No successful production CI for current main SHA',
                          'main changed since CI; abort release',
                          'MabiAuto_V3.2.89_Windows_Lite.zip.sha256',
                          'gh release create V3.2.89',
                          'gh release edit V3.2.89 --draft=false',
                          'Stop/UI CLI safe handoff and regression evidence missing'):
                self.assertIn(token, w)
            authorization = json.loads(
                (ROOT / 'source/V3.2.89_RELEASE_AUTHORIZATION.json').read_text(encoding='utf-8'))
            self.assertEqual(authorization['sourcePullRequest'], 377)
            self.assertEqual(authorization['request'], '계속 진행해서 업데이트 배포해')
            self.assertIs(authorization['realGameTested'], False)
            self.assertIs(authorization['mainCiRequired'], True)
        elif 'CurrentVersion = "V3.2.90"' in current:
            # Preserve historical release contracts, while checking the
            # currently authorized schema-diagnostics publisher independently.
            for tag, version in [('Version', '3.2.90'),
                                 ('AssemblyVersion', '3.2.90.0'),
                                 ('FileVersion', '3.2.90.0')]:
                self.assertIn(f'<{tag}>{version}</{tag}>', project)
            release = ROOT / '.github/workflows/release-v3290-auto-update.yml'
            self.assertTrue(release.exists())
            w = release.read_text(encoding='utf-8')
            for token in ('No successful production CI for current main SHA',
                          'main changed since CI; abort release',
                          'MabiAuto_V3.2.90_Windows_Lite.zip.sha256',
                          'gh release create V3.2.90',
                          'gh release edit V3.2.90 --draft=false',
                          'get_activity schema diagnostics or fail-closed regression missing'):
                self.assertIn(token, w)
            authorization = json.loads(
                (ROOT / 'source/V3.2.90_RELEASE_AUTHORIZATION.json').read_text(encoding='utf-8'))
            self.assertEqual(authorization['sourcePullRequest'], 379)
            self.assertEqual(authorization['request'], '배포 까지 계속 진행해')
            self.assertIs(authorization['realGameTested'], False)
            self.assertIs(authorization['mainCiRequired'], True)
        elif 'CurrentVersion = "V3.2.91"' in current:
            # Historical release tests remain intact, but the newly
            # authorized binary must match the next one-shot publisher.
            for tag, version in [('Version', '3.2.91'),
                                 ('AssemblyVersion', '3.2.91.0'),
                                 ('FileVersion', '3.2.91.0')]:
                self.assertIn(f'<{tag}>{version}</{tag}>', project)
            release = ROOT / '.github/workflows/release-v3291-auto-update.yml'
            self.assertTrue(release.exists())
            workflow = release.read_text(encoding='utf-8')
            for token in ('No successful production CI for current main SHA',
                          'main changed since CI; abort release',
                          'MabiAuto_V3.2.91_Windows_Lite.zip.sha256',
                          'gh release create V3.2.91',
                          'gh release edit V3.2.91 --draft=false',
                          'activity contract schema-only diagnosis or fail-closed regression missing'):
                self.assertIn(token, workflow)
            authorization = json.loads(
                (ROOT / 'source/V3.2.91_RELEASE_AUTHORIZATION.json').read_text(encoding='utf-8'))
            self.assertEqual(authorization['sourcePullRequest'], 381)
            self.assertEqual(authorization['request'], '진행하고 배포까지 해')
            self.assertIs(authorization['realGameTested'], False)
            self.assertIs(authorization['mainCiRequired'], True)
        elif 'CurrentVersion = "V3.2.92"' in current:
            for tag, version in [('Version', '3.2.92'),
                                 ('AssemblyVersion', '3.2.92.0'),
                                 ('FileVersion', '3.2.92.0')]:
                self.assertIn(f'<{tag}>{version}</{tag}>', project)
            release = ROOT / '.github/workflows/release-v3292-auto-update.yml'
            self.assertTrue(release.exists())
            workflow = release.read_text(encoding='utf-8')
            for token in ('No successful production CI for current main SHA',
                          'main changed since CI; abort release',
                          'MabiAuto_V3.2.92_Windows_Lite.zip.sha256',
                          'gh release create V3.2.92',
                          'gh release edit V3.2.92 --draft=false',
                          'optional IsReviving only or unchanged safety guard regression missing'):
                self.assertIn(token, workflow)
            authorization = json.loads(
                (ROOT / 'source/V3.2.92_RELEASE_AUTHORIZATION.json').read_text(encoding='utf-8'))
            self.assertEqual(authorization['sourcePullRequest'], 383)
            self.assertEqual(authorization['request'], '수정하고 배포해')
            self.assertIs(authorization['realGameTested'], False)
            self.assertIs(authorization['mainCiRequired'], True)
        elif 'CurrentVersion = "V3.2.93"' in current:
            for tag, version in [('Version', '3.2.93'),
                                 ('AssemblyVersion', '3.2.93.0'),
                                 ('FileVersion', '3.2.93.0')]:
                self.assertIn(f'<{tag}>{version}</{tag}>', project)
            release = ROOT / '.github/workflows/release-v3293-auto-update.yml'
            self.assertTrue(release.exists())
            workflow = release.read_text(encoding='utf-8')
            for token in ('No successful production CI for current main SHA',
                          'main changed since CI; abort release',
                          'MabiAuto_V3.2.93_Windows_Lite.zip.sha256',
                          'gh release create V3.2.93',
                          'gh release edit V3.2.93 --draft=false',
                          'verified herb bottom-row ROI or two-frame OCR+icon input gate missing'):
                self.assertIn(token, workflow)
            authorization = json.loads(
                (ROOT / 'source/V3.2.93_RELEASE_AUTHORIZATION.json').read_text(encoding='utf-8'))
            self.assertEqual(authorization['sourcePullRequest'], 385)
            self.assertEqual(authorization['request'], '수정하고 배포해')
            self.assertIs(authorization['realGameTested'], False)
            self.assertIs(authorization['mainCiRequired'], True)
        elif 'CurrentVersion = "V3.2.94"' in current:
            for tag, version in [('Version', '3.2.94'),
                                 ('AssemblyVersion', '3.2.94.0'),
                                 ('FileVersion', '3.2.94.0')]:
                self.assertIn(f'<{tag}>{version}</{tag}>', project)
            release = ROOT / '.github/workflows/release-v3294-auto-update.yml'
            self.assertTrue(release.exists())
            workflow = release.read_text(encoding='utf-8')
            for token in ('No successful production CI for current main SHA',
                          'main changed since CI; abort release',
                          'MabiAuto_V3.2.94_Windows_Lite.zip.sha256',
                          'gh release create V3.2.94',
                          'gh release edit V3.2.94 --draft=false',
                          'native-color herb row verification or safe row rejection regression missing'):
                self.assertIn(token, workflow)
            authorization = json.loads(
                (ROOT / 'source/V3.2.94_RELEASE_AUTHORIZATION.json').read_text(encoding='utf-8'))
            self.assertEqual(authorization['sourcePullRequest'], 387)
            self.assertEqual(authorization['request'], 'pr 387 배포해')
            self.assertIs(authorization['realGameTested'], False)
            self.assertIs(authorization['mainCiRequired'], True)
        elif 'CurrentVersion = "V3.2.95"' in current:
            for tag, version in [('Version', '3.2.95'),
                                 ('AssemblyVersion', '3.2.95.0'),
                                 ('FileVersion', '3.2.95.0')]:
                self.assertIn(f'<{tag}>{version}</{tag}>', project)
            release = ROOT / '.github/workflows/release-v3295-auto-update.yml'
            self.assertTrue(release.exists())
            workflow = release.read_text(encoding='utf-8')
            for token in ('No successful production CI for current main SHA',
                          'main changed since CI; abort release',
                          'MabiAuto_V3.2.95_Windows_Lite.zip.sha256',
                          'gh release create V3.2.95',
                          'gh release edit V3.2.95 --draft=false',
                          'actual medicine first card center or unchanged crafting coordinate regression missing'):
                self.assertIn(token, workflow)
            authorization = json.loads(
                (ROOT / 'source/V3.2.95_RELEASE_AUTHORIZATION.json').read_text(encoding='utf-8'))
            self.assertEqual(authorization['sourcePullRequest'], 389)
            self.assertEqual(authorization['request'], '진행해')
            self.assertIs(authorization['realGameTested'], False)
            self.assertIs(authorization['mainCiRequired'], True)
        elif 'CurrentVersion = "V3.2.96"' in current:
            # Next authorized version must satisfy the same immutable
            # main-SHA CI release contract, not masquerade as V3.2.79.
            for tag, version in [('Version', '3.2.96'),
                                 ('AssemblyVersion', '3.2.96.0'),
                                 ('FileVersion', '3.2.96.0')]:
                self.assertIn(f'<{tag}>{version}</{tag}>', project)
            release = ROOT / '.github/workflows/release-v3296-auto-update.yml'
            self.assertTrue(release.exists())
            workflow = release.read_text(encoding='utf-8')
            for token in ('No successful production CI for current main SHA',
                          'main changed since CI; abort release',
                          'MabiAuto_V3.2.96_Windows_Lite.zip.sha256',
                          'gh release create V3.2.96',
                          'gh release edit V3.2.96 --draft=false',
                          'actual medicine first card center or unchanged crafting coordinate regression missing'):
                self.assertIn(token, workflow)
            authorization = json.loads(
                (ROOT / 'source/V3.2.96_RELEASE_AUTHORIZATION.json').read_text(encoding='utf-8'))
            self.assertEqual(authorization['sourcePullRequest'], 392)
            self.assertEqual(authorization['request'], '수정하고 배포해')
            self.assertIs(authorization['realGameTested'], False)
            self.assertIs(authorization['mainCiRequired'], True)
        elif 'CurrentVersion = "V3.2.96"' in current:
            # V3.2.75 is historical; V3.2.96 has its own exact-SHA,
            # one-shot release gate and must not be mistaken for V3.2.79.
            for tag, version in [('Version', '3.2.96'),
                                 ('AssemblyVersion', '3.2.96.0'),
                                 ('FileVersion', '3.2.96.0')]:
                self.assertIn(f'<{tag}>{version}</{tag}>', project)
            release = ROOT / '.github/workflows/release-v3296-auto-update.yml'
            self.assertTrue(release.exists())
            workflow = release.read_text(encoding='utf-8')
            for token in ('No successful production CI for current main SHA',
                          'main changed since CI; abort release',
                          'MabiAuto_V3.2.96_Windows_Lite.zip.sha256',
                          'gh release create V3.2.96',
                          'gh release edit V3.2.96 --draft=false'):
                self.assertIn(token, workflow)
            authorization = json.loads(
                (ROOT / 'source/V3.2.96_RELEASE_AUTHORIZATION.json').read_text(encoding='utf-8'))
            self.assertEqual(authorization['sourcePullRequest'], 392)
            self.assertEqual(authorization['request'], '수정하고 배포해')
            self.assertIs(authorization['realGameTested'], False)
            self.assertIs(authorization['mainCiRequired'], True)
        elif 'CurrentVersion = "V3.2.97"' in current:
            for tag, version in [('Version', '3.2.97'),
                                 ('AssemblyVersion', '3.2.97.0'),
                                 ('FileVersion', '3.2.97.0')]:
                self.assertIn(f'<{tag}>{version}</{tag}>', project)
            workflow = (ROOT / '.github/workflows/release-v3297-auto-update.yml').read_text(encoding='utf-8')
            for token in ('No successful production CI for current main SHA',
                          'main changed since CI; abort release',
                          'MabiAuto_V3.2.97_Windows_Lite.zip.sha256',
                          'gh release create V3.2.97',
                          'gh release edit V3.2.97 --draft=false',
                          'Require V3.2.97 managed receipt on-site proof'):
                self.assertIn(token, workflow)
            authorization = json.loads(
                (ROOT / 'source/V3.2.97_RELEASE_AUTHORIZATION.json').read_text(encoding='utf-8'))
            self.assertEqual(authorization['sourcePullRequest'], 394)
            self.assertEqual(authorization['request'], '수정하고 배포해')
            self.assertIs(authorization['realGameTested'], False)
            self.assertIs(authorization['mainCiRequired'], True)
        elif 'CurrentVersion = "V3.2.98"' in current:
            for tag, version in [('Version', '3.2.98'),
                                 ('AssemblyVersion', '3.2.98.0'),
                                 ('FileVersion', '3.2.98.0')]:
                self.assertIn(f'<{tag}>{version}</{tag}>', project)
            workflow = (ROOT / '.github/workflows/release-v3298-auto-update.yml').read_text(encoding='utf-8')
            for token in ('No successful production CI for current main SHA',
                          'main changed since CI; abort release',
                          'MabiAuto_V3.2.98_Windows_Lite.zip.sha256',
                          'gh release create V3.2.98',
                          'gh release edit V3.2.98 --draft=false',
                          'Require V3.2.98 F07 fresh-pair identity'):
                self.assertIn(token, workflow)
            authorization = json.loads(
                (ROOT / 'source/V3.2.98_RELEASE_AUTHORIZATION.json').read_text(encoding='utf-8'))
            self.assertEqual(authorization['sourcePullRequest'], 395)
            self.assertEqual(authorization['request'], '수정하고 배포해')
            self.assertIs(authorization['realGameTested'], False)
            self.assertIs(authorization['mainCiRequired'], True)
        elif 'CurrentVersion = "V3.2.99"' in current:
            for tag, version in [('Version', '3.2.99'),
                                 ('AssemblyVersion', '3.2.99.0'),
                                 ('FileVersion', '3.2.99.0')]:
                self.assertIn(f'<{tag}>{version}</{tag}>', project)
            workflow = (ROOT / '.github/workflows/release-v3299-auto-update.yml').read_text(encoding='utf-8')
            for token in ('No successful production CI for current main SHA',
                          'main changed since CI; abort release',
                          'MabiAuto_V3.2.99_Windows_Lite.zip.sha256',
                          'gh release create V3.2.99',
                          'gh release edit V3.2.99 --draft=false',
                          'Require V3.2.99 Enter focus'):
                self.assertIn(token, workflow)
            authorization = json.loads(
                (ROOT / 'source/V3.2.99_RELEASE_AUTHORIZATION.json').read_text(encoding='utf-8'))
            self.assertEqual(authorization['sourcePullRequest'], 397)
            self.assertIn('확인 했어', authorization['request'])
            self.assertIs(authorization['realGameTested'], False)
            self.assertIs(authorization['mainCiRequired'], True)
        elif 'CurrentVersion = "V3.3.00"' in current:
            # V3.2.75 remains a historical release contract; V3.3.00 has a
            # distinct numeric Windows version and a guarded one-shot release.
            for tag, version in [('Version', '3.3.0'),
                                 ('AssemblyVersion', '3.3.0.0'),
                                 ('FileVersion', '3.3.0.0')]:
                self.assertIn(f'<{tag}>{version}</{tag}>', project)
            workflow = (ROOT / '.github/workflows/release-v3300-auto-update.yml').read_text(encoding='utf-8')
            for token in ('No successful production CI for current main SHA',
                          'main changed since CI; abort release',
                          'MabiAuto_V3.3.00_Windows_Lite.zip.sha256',
                          'gh release create V3.3.00',
                          'gh release edit V3.3.00 --draft=false',
                          'Require V3.3.00 medicine F07 selection evidence',
                          'Require V3.3.00 food and item craft-ready'):
                self.assertIn(token, workflow)
            authorization = json.loads(
                (ROOT / 'source/V3.3.00_RELEASE_AUTHORIZATION.json').read_text(encoding='utf-8'))
            self.assertEqual(authorization['sourcePullRequest'], 399)
            self.assertIn('수정하고 배포해', authorization['request'])
            self.assertIs(authorization['realGameTested'], False)
            self.assertIs(authorization['mainCiRequired'], True)
        elif 'CurrentVersion = "V3.3.01"' in current:
            # V3.2.75 remains a historical release contract; V3.3.01 has a
            # distinct numeric Windows version and a guarded one-shot release.
            for tag, version in [('Version', '3.3.1'),
                                 ('AssemblyVersion', '3.3.1.0'),
                                 ('FileVersion', '3.3.1.0')]:
                self.assertIn(f'<{tag}>{version}</{tag}>', project)
            workflow = (ROOT / '.github/workflows/release-v3301-auto-update.yml').read_text(encoding='utf-8')
            for token in ('No successful production CI for current main SHA',
                          'main changed since CI; abort release',
                          'MabiAuto_V3.3.01_Windows_Lite.zip.sha256',
                          'gh release create V3.3.01',
                          'gh release edit V3.3.01 --draft=false',
                          'Require V3.3.01 medicine F07 selection evidence',
                          'Require V3.3.01 food and item craft-ready'):
                self.assertIn(token, workflow)
            authorization = json.loads(
                (ROOT / 'source/V3.3.01_RELEASE_AUTHORIZATION.json').read_text(encoding='utf-8'))
            self.assertEqual(authorization['sourcePullRequest'], 400)
            self.assertIn('수정하고 배포해', authorization['request'])
            self.assertIs(authorization['realGameTested'], False)
            self.assertIs(authorization['mainCiRequired'], True)
        elif 'CurrentVersion = "V3.3.02"' in current:
            for tag, version in [('Version', '3.3.2'),
                                 ('AssemblyVersion', '3.3.2.0'),
                                 ('FileVersion', '3.3.2.0')]:
                self.assertIn(f'<{tag}>{version}</{tag}>', project)
            workflow = (ROOT / '.github/workflows/release-v3302-auto-update.yml').read_text(encoding='utf-8')
            for token in ('No successful production CI for current main SHA',
                          'main changed since CI; abort release',
                          'MabiAuto_V3.3.02_Windows_Lite.zip.sha256',
                          'gh release create V3.3.02',
                          'gh release edit V3.3.02 --draft=false',
                          'Require V3.3.02 medicine F07 selection evidence',
                          'Require V3.3.02 food and item craft-ready'):
                self.assertIn(token, workflow)
            authorization = json.loads(
                (ROOT / 'source/V3.3.02_RELEASE_AUTHORIZATION.json').read_text(encoding='utf-8'))
            self.assertEqual(authorization['sourcePullRequest'], 401)
            self.assertIn('배포해', authorization['request'])
            self.assertIs(authorization['realGameTested'], False)
            self.assertIs(authorization['mainCiRequired'], True)
        elif 'CurrentVersion = "V3.3.05"' in current:
            for tag, version in [('Version', '3.3.5'),
                                 ('AssemblyVersion', '3.3.5.0'),
                                 ('FileVersion', '3.3.5.0')]:
                self.assertIn(f'<{tag}>{version}</{tag}>', project)
            workflow = (ROOT / '.github/workflows/release-v3305-auto-update.yml').read_text(encoding='utf-8')
            for token in ('No successful production CI for current main SHA',
                          'main changed since CI; abort release',
                          'MabiAuto_V3.3.05_Windows_Lite.zip.sha256',
                          'gh release create V3.3.05',
                          'gh release edit V3.3.05 --draft=false'):
                self.assertIn(token, workflow)
            authorization = json.loads(
                (ROOT / 'source/V3.3.05_RELEASE_AUTHORIZATION.json').read_text(encoding='utf-8'))
            self.assertEqual(authorization['sourcePullRequest'], 406)
            self.assertIn('진행해', authorization['request'])
            self.assertIs(authorization['realGameTested'], False)
            self.assertIs(authorization['mainCiRequired'], True)
        elif 'CurrentVersion = "V3.3.04"' in current:
            for tag, version in [('Version', '3.3.4'),
                                 ('AssemblyVersion', '3.3.4.0'),
                                 ('FileVersion', '3.3.4.0')]:
                self.assertIn(f'<{tag}>{version}</{tag}>', project)
            workflow = (ROOT / '.github/workflows/release-v3304-auto-update.yml').read_text(encoding='utf-8')
            for token in ('No successful production CI for current main SHA',
                          'main changed since CI; abort release',
                          'MabiAuto_V3.3.04_Windows_Lite.zip.sha256',
                          'gh release create V3.3.04',
                          'gh release edit V3.3.04 --draft=false'):
                self.assertIn(token, workflow)
            authorization = json.loads(
                (ROOT / 'source/V3.3.04_RELEASE_AUTHORIZATION.json').read_text(encoding='utf-8'))
            self.assertEqual(authorization['sourcePullRequest'], 404)
            self.assertIn('배포해', authorization['request'])
            self.assertIs(authorization['realGameTested'], False)
            self.assertIs(authorization['mainCiRequired'], True)
        elif 'CurrentVersion = "V3.3.03"' in current:
            for tag, version in [('Version', '3.3.3'),
                                 ('AssemblyVersion', '3.3.3.0'),
                                 ('FileVersion', '3.3.3.0')]:
                self.assertIn(f'<{tag}>{version}</{tag}>', project)
            workflow = (ROOT / '.github/workflows/release-v3303-auto-update.yml').read_text(encoding='utf-8')
            for token in ('No successful production CI for current main SHA',
                          'main changed since CI; abort release',
                          'MabiAuto_V3.3.03_Windows_Lite.zip.sha256',
                          'gh release create V3.3.03',
                          'gh release edit V3.3.03 --draft=false',
                          'Require V3.3.03 medicine F07 selection evidence',
                          'Require V3.3.03 food and item craft-ready'):
                self.assertIn(token, workflow)
            authorization = json.loads(
                (ROOT / 'source/V3.3.03_RELEASE_AUTHORIZATION.json').read_text(encoding='utf-8'))
            self.assertGreater(authorization['sourcePullRequest'], 0)
            self.assertIn('배포해', authorization['request'])
            self.assertIs(authorization['realGameTested'], False)
            self.assertIs(authorization['mainCiRequired'], True)
        else:
            self.assertIn('CurrentVersion = "V3.2.79"', current)
            for tag, version in [('Version', '3.2.79'), ('AssemblyVersion', '3.2.79.0'), ('FileVersion', '3.2.79.0')]:
                self.assertIn(f'<{tag}>{version}</{tag}>', project)
            self.assertTrue((ROOT / '.github/workflows/release-v3279-auto-update.yml').exists())
            authorization = json.loads((ROOT / 'source/V3.2.79_RELEASE_AUTHORIZATION.json').read_text(encoding='utf-8'))
            self.assertEqual(authorization['sourcePullRequest'], 346)
            self.assertEqual(authorization['request'], '계속 진행해')




    def test_no_unverified_shortcuts_and_required_package_guards(self):
        w = WORKFLOW.read_text(encoding="utf-8")
        for marker in (
            "source/V3.2.75_RELEASE_AUTHORIZATION.json",
            "F9 code, resume, safety, latency or historical regression guard missing",
            "tests/altering-screen-integration/Regression.csproj",
            "N02 deterministic batch persistence regressions",
            "Updater rollback regression tests",
            "Smoke test executable",
            "Get-FileHash $zip -Algorithm SHA256",
            "BUILD_COMMIT.txt",
            "ROLLBACK_PREVIOUS.cmd",
            "tools/RollbackPrevious.ps1",
            "MabiAuto_V3.2.75_Windows_Lite.zip.sha256",
            "MabiAuto_V3.2.75_Source.zip",
            "gh release create V3.2.75",
            "gh release edit V3.2.75 --draft=false",
            "main changed since CI; abort release",
        ):
            self.assertIn(marker, w)
        self.assertLess(w.index("Smoke test executable"),
                        w.index("Publish public auto-update Release"))
        self.assertIn("V3.2.74", (ROOT / ".github/workflows/release-v3274-auto-update.yml").read_text(encoding="utf-8"))
        c = CI.read_text(encoding="utf-8")
        for marker in ("tests/n02-resume/Regression.csproj",
                       "tests/f05-consumption/Regression.csproj",
                       "tests/altering-screen-integration/Regression.csproj",
                       "Updater.Tests.ps1"):
            self.assertIn(marker, c)

if __name__ == "__main__":
    unittest.main()
