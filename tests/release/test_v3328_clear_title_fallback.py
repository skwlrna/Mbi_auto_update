import unittest
import re
from pathlib import Path
R=Path(__file__).resolve().parents[2]
def read(p):return (R/p).read_text(encoding="utf-8")
class ClearFallback(unittest.TestCase):
 def test_visual_proof(self):
  p=read("source/FishingAutomation/dungeon/AbyssClearEvidencePolicy.cs")
  for term in ("=> touchPrompt ?", "title.Score >= 0.78", "SameTitle", "TitleTouchPoint"): self.assertIn(term,p)
  e=read("source/FishingAutomation/dungeon/ScenarioEngine.AbyssClearEvidence.cs")
  self.assertIn("StrongTitle(title, safeTitle)",e)
  self.assertIn('"abyss_dungeon_clear_visual"',e)
 def test_click_transition_and_reward_guards(self):
  s=read("source/FishingAutomation/dungeon/ScenarioEngine.cs")
  for term in ("SameTitle", "visualTitleConsecutive", "retryClicks < 1", "WaitForAbyssClearScreenGoneAsync", '"clear_title_visual_fallback"', "DetectAbyssResultRetryAsync"): self.assertIn(term,s)
  t=read("source/FishingAutomation/dungeon/AbyssRewardVisualPolicy.cs")
  self.assertIn("chest < 0.15",t)
 def test_lanczos_fallback(self):
  o=read("source/FishingAutomation/dungeon/OcrRecognizer.cs")
  for term in ("2.8, true, ct","ResizeLanczos3","Sinc","Math.Clamp",'wanted == "화면을 터치해 주세요"'): self.assertIn(term,o)
 def test_version(self):
  # This verifies the V3.3.28 behavior is retained in later releases;
  # newer patches must not be rejected solely for advancing the app version.
  source = read("source/FishingAutomation/UpdateManager.cs")
  match = re.search(r'CurrentVersion = "V(\\d+)\\.(\\d+)\\.(\\d+)"', source)
  self.assertIsNotNone(match)
  version = tuple(map(int, match.groups()))
  self.assertGreaterEqual(version, (3, 3, 28))
  project = read("source/FishingAutomation/FishingAutomation.csproj")
  self.assertIn(f"<Version>{'.'.join(map(str, version))}</Version>", project)
