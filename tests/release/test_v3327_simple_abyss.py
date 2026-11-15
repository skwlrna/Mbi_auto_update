import unittest
from pathlib import Path
R=Path(__file__).resolve().parents[2]
def read(p):return (R/p).read_text(encoding="utf-8")
D="source/FishingAutomation/dungeon/"
class SimpleAbyss(unittest.TestCase):
 def test_common_reward_detector(self):
  s=read(D+"ScenarioEngine.AbyssRetry.cs")
  body=s.split("private async Task<DetectionResult> DetectAbyssResultRetryAsync",1)[1].split("private async Task<Dictionary",1)[0]
  self.assertIn("AbyssRewardVisualPolicy.TryDetect",body)
  for forbidden in ("HasAbyssActiveCombatHudAsync","FindCompactLabelAsync","GetAbyssFlowState", "_input."):
   self.assertNotIn(forbidden,body)
  for old in ("DetectAbyssConfirmedResultVisualRetryAsync","DetectAbyssPostTouchVisualResultAsync","TryRecoverVerifiedClearTouchAfterResultAsync"):
   self.assertNotIn(old,s)
  self.assertIn("DetectAbyssResultRetryAsync(frame, ct)",read(D+"ScenarioEngine.AbyssCompletedResult.cs"))
  for guard in ("retry.Bounds.IntersectsWith(previous.Bounds)","ConfirmAbyssFixedButtonClick(fresh, exit: false)","ConfirmAbyssFixedButtonClick(confirmedFrame, exit: false)","VerifyAbyssClickClientGeometry"):
   self.assertIn(guard,s)
 def test_touch_prompt_without_title(self):
  s=read(D+"AbyssClearEvidencePolicy.cs")
  self.assertIn("=> touchPrompt ?",s)
  s=read(D+"ScenarioEngine.AbyssClearEvidence.cs")
  self.assertIn('"화면을 터치해 주세요"',s)
  self.assertIn("FindCompactLabelAsync",s)
  self.assertIn("area.Contains(touch.Bounds)",s)
  self.assertNotIn("_input.",s)
  e=read(D+"ScenarioEngine.cs")
  self.assertIn("retryClicks < 1",e)
  self.assertIn("result.Found && previousResult",e)
 def test_timeout_and_scheduler(self):
  t=read(D+"ScenarioEngine.AbyssTimeout.cs")
  self.assertLess(t.index("ConfirmAbyssCompletedResultAsync(frame, ct)"),t.index('var target = await _detector.DetectAsync("abyss_leave_dungeon"'))
  self.assertIn("target.Bounds.IntersectsWith(previous.Bounds)",t)
  self.assertIn("DetectAbyssResultRetryAsync(frame, ct)",read(D+"ScenarioEngine.AbyssSchedulerExit.cs"))
 def test_real_fixtures_and_production_assembly(self):
  s=read("tests/simple-abyss/Program.cs")
  self.assertIn('Assembly.Load("FishingAutomation")',s)
  for f in ("reward_2335","reward_0020","clear_2253","combat_2006","combat_2258"):
   self.assertTrue((R/"tests/simple-abyss/fixtures"/(f+".png")).exists())
