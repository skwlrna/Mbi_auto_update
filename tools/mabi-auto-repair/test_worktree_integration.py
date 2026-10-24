"""Real offline Git worktree + .NET red/green, with a deterministic fake Codex."""
import json
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest import mock
import repair

BAD = "public static class RepairProbe { public static int Add(int a, int b) => a - b; }\n"
FIX = BAD.replace("a - b", "a + b")
BEFORE = ('using System;\n'
          'if (RepairProbe.Add(2, 1) != 3) throw new Exception("Regression");\n'
          'Console.WriteLine("OK");\n')
AFTER = BEFORE.replace('Console.WriteLine("OK");',
                       'if (RepairProbe.Add(-1,2) != 1) throw new Exception("negative regression");\n'
                       'Console.WriteLine("OK");')
PROJECT = """<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework>
<EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>
<ItemGroup><Compile Include="Program.cs"/>
<Compile Include="../../source/FishingAutomation/RepairProbe.cs" Link="RepairProbe.cs"/>
</ItemGroup></Project>
"""


def git(directory, *args):
    return subprocess.run(["git", *args], cwd=directory, text=True,
                          capture_output=True, check=True, timeout=30)


def make_repo(root):
    src = root / "source_checkout"
    app = src / "source/FishingAutomation"
    tests = src / "tests/altering"
    app.mkdir(parents=True)
    tests.mkdir(parents=True)
    (src / ".gitignore").write_text("bin/\nobj/\n", encoding="utf-8")
    (app / "FishingAutomation.csproj").write_text("<Project/>", encoding="utf-8")
    (app / "UpdateManager.cs").write_text(
        'public const string CurrentVersion = "V3.2.62";', encoding="utf-8")
    (app / "RepairProbe.cs").write_text(BAD, encoding="utf-8")
    (tests / "Regression.csproj").write_text(PROJECT, encoding="utf-8")
    (tests / "Program.cs").write_text(BEFORE, encoding="utf-8")
    git(src, "init")
    git(src, "config", "user.email", "fixture@example.invalid")
    git(src, "config", "user.name", "Offline Fixture")
    git(src, "add", ".")
    git(src, "commit", "-m", "fixture failing C# regression")
    return src


@unittest.skipUnless(shutil.which("git") and shutil.which("dotnet"),
                     "Git and .NET 8 SDK required; CI installs both")
class RealWorktreeTests(unittest.TestCase):
    def verify(self, patch):
        with tempfile.TemporaryDirectory(prefix="repair-e2e-") as tmp:
            root = Path(tmp)
            source = make_repo(root)
            initial = repair.shell(["dotnet", "run", "--project",
                                    "tests/altering/Regression.csproj",
                                    "-c", "Release"], source, 150)
            self.assertNotEqual(initial.returncode, 0, "Must reproduce RED")
            with repair.State(root / "db") as state:
                key = state.add("fixture", {
                    "schema": 1, "source": "runtime", "version": "V3.2.62",
                    "mode": "가공", "title": "addition bug", "detail": "wrong arithmetic"
                })
                real_shell = repair.shell
                real_which = shutil.which
                agent_calls = []

                def fake_which(cmd):
                    return "/fixture/codex" if cmd == "codex" else real_which(cmd)

                def fake_shell(argv, cwd, timeout=30, prompt=None):
                    if argv[0] != "codex":
                        return real_shell(argv, cwd, timeout, prompt)
                    agent_calls.append(argv)
                    self.assertIn("workspace-write", argv)
                    self.assertIn("<untrusted-incident>", prompt)
                    if patch:
                        (cwd / "source/FishingAutomation/RepairProbe.cs").write_text(
                            FIX, encoding="utf-8")
                        (cwd / "tests/altering/Program.cs").write_text(
                            AFTER, encoding="utf-8")
                    return subprocess.CompletedProcess(argv, 0, "fake", "")

                with mock.patch.object(repair.shutil, "which", side_effect=fake_which), \
                     mock.patch.object(repair, "shell", side_effect=fake_shell):
                    result = repair.run_one(state, key, source, True, True, 1)
                self.assertEqual(len(agent_calls), 1)
                self.assertEqual(state.job(key)["calls"], 1)
                saved_path = root / "db/workspaces" / key / "result.json"
                self.assertTrue(saved_path.is_file())
                saved = json.loads(saved_path.read_text(encoding="utf-8"))
                self.assertEqual(saved["status"], result["status"])
                self.assertFalse(saved["real_game_verified"])
                self.assertEqual(
                    (source / "source/FishingAutomation/RepairProbe.cs").read_text(
                        encoding="utf-8"), BAD, "Must not edit main source")
                self.assertFalse(git(source, "status", "--porcelain").stdout.strip())
                if patch:
                    self.assertEqual(result["status"], "candidate_passed")
                    self.assertEqual(result["test_returncode"], 0)
                    self.assertIn("source/FishingAutomation/RepairProbe.cs",
                                  result["changed_files"])
                    self.assertIn("tests/altering/Program.cs",
                                  result["changed_files"])
                else:
                    self.assertEqual(result["status"], "needs_review")
                    self.assertEqual(result["changed_files"], [])
                    self.assertNotIn("test_returncode", result)
                with self.assertRaisesRegex(ValueError, "repeated"):
                    state.reserve(key, 3)

    def test_real_git_worktree_dotnet_red_green(self):
        self.verify(True)

    def test_real_git_worktree_codex_no_patch(self):
        self.verify(False)


if __name__ == "__main__":
    unittest.main()
