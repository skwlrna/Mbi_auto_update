"""V3.3.16: ESC only after a verified post-exit Stellagram, once per exit."""
import unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
def read(p): return (ROOT/p).read_text(encoding="utf-8")

class PostExitStellagramEsc(unittest.TestCase):
    def test_two_independent_exact_text_areas(self):
        c=read("source/FishingAutomation/dungeon/ScenarioEngine.Stellagram.cs")
        self.assertIn('StellagramTopTitleRoi = new(35, 34, 280, 72)',c)
        self.assertIn('StellagramBestTitleRoi = new(275, 150, 265, 85)',c)
        self.assertIn('ScaleAbyssResultRoi(StellagramTopTitleRoi, frame.Size)',c)
        self.assertIn('ScaleAbyssResultRoi(StellagramBestTitleRoi, frame.Size)',c)
        self.assertIn('frame, topRoi, "오늘의 스텔라 픽", ct',c)
        self.assertIn('frame, bestRoi, "베스트 스텔라그램", ct',c)
        self.assertIn('topRoi.Contains(top.Bounds)',c)
        self.assertIn('best.Found && bestRoi.Contains(best.Bounds)',c)

    def test_one_shot_two_frames_and_fail_closed(self):
        c=read("source/FishingAutomation/dungeon/ScenarioEngine.Stellagram.cs")
        self.assertIn('(_scheduledResultExit && !_scheduledExitClicked)',c)
        self.assertIn('if (!await IsPostExitStellagramAsync(initialFrame, ct))',c)
        self.assertIn('using (var fresh = await CaptureGameWindowAsync(ct))',c)
        self.assertIn('if (!await IsPostExitStellagramAsync(fresh, ct))',c)
        self.assertIn('if (_postExitStellagramEscAttempted)',c)
        self.assertIn('_postExitStellagramEscAttempted = true;',c)
        self.assertEqual(c.count('_input.TapScanCode(0x01, ct);'),1)
        self.assertNotIn('ClickClientPoint(',c)
        self.assertIn('if (++absentFrames >= 2)',c)
        self.assertIn('TimeSpan.FromSeconds(8)',c)
        self.assertLess(c.index('using (var fresh'),c.index('_input.TapScanCode(0x01, ct);'))

    def test_normal_exit_recovery_and_outside_hud_remain_guarded(self):
        n=read("source/FishingAutomation/dungeon/ScenarioEngine.cs")
        e=read("source/FishingAutomation/dungeon/ScenarioEngine.AbyssSchedulerExit.cs")
        normal=n.split('private async Task WaitForAbyssHomeAfterNormalExitAsync',1)[1].split('private async Task ClickTargetWithTimeoutAsync',1)[0]
        recovery=n.split('if (_scheduledResultExit && _scheduledExitClicked)',1)[1].split('if ((await DetectAbyssResultRetryAsync',1)[0]
        self.assertIn('if (await TryDismissPostExitStellagramAsync(frame, ct))',normal)
        self.assertIn('if (await TryDismissPostExitStellagramAsync(frame, ct))',recovery)
        self.assertLess(normal.index('TryDismissPostExitStellagramAsync'),normal.index('TryAcknowledgeScheduledMissionCompleteAsync'))
        self.assertLess(recovery.index('TryDismissPostExitStellagramAsync'),recovery.index('TryAcknowledgeScheduledMissionCompleteAsync'))
        self.assertIn('sw.Restart()',normal)
        self.assertIn('exitPendingRecovery.Restart()',recovery)
        self.assertIn('outsideConsecutive >= 3',normal)
        self.assertIn('outsideConsecutive >= 3',recovery)
        self.assertIn('_postExitStellagramEscAttempted = false;',e)
        self.assertIn('_scheduledExitClicked = true;',e)

if __name__=="__main__": unittest.main()
