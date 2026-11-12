"""V3.3.17: processing is the only confirmed reason for scheduled Abyss exit."""
import unittest
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]
def read(path):
    return (ROOT/path).read_text(encoding="utf-8")

class ConditionalAbyssExit(unittest.TestCase):
    def test_result_and_recovery_ask_the_same_fresh_callback(self):
        s=read("source/FishingAutomation/dungeon/ScenarioEngine.cs")
        self.assertIn('Func<CancellationToken, Task<bool>>? shouldExitForProcessingAfterRound',s)
        self.assertIn('_shouldExitForProcessingAfterRound = shouldExitForProcessingAfterRound;',s)
        self.assertIn('if (scheduledResultExit && shouldExitForProcessingAfterRound is null)',s)
        self.assertIn('return await _shouldExitForProcessingAfterRound(ct);',s)
        self.assertEqual(s.count('await ShouldExitScheduledAbyssAfterRoundAsync(ct)'),2)
        self.assertIn('if (_scheduledResultExit && await ShouldExitScheduledAbyssAfterRoundAsync(ct))\n                await ExitAbyssResultForScheduledProcessingAsync(ct);\n            else\n                await RetryAbyssResultAsync(ct);',s)
        self.assertIn('if (_scheduledResultExit && await ShouldExitScheduledAbyssAfterRoundAsync(ct))',s)
        self.assertIn('await RetryAbyssResultAsync(ct);\n                    _resumeStepIndex = AbyssCombatStepIndex;',s)
        self.assertNotIn('ScheduledAbyssReturnPolicy.RecoveryResultAction(_scheduledResultExit)',s)
        self.assertIn('ScheduledAbyssReturnPolicy.RequireVerifiedOutside(',s)

    def test_cli_works_are_fresh_each_result_and_no_other_input_worker(self):
        s=read("source/FishingAutomation/MainForm.MultiAltering.cs")
        combined=read("source/FishingAutomation/MainForm.CombinedAltering.cs")
        self.assertIn('async Task<bool> NeedsProcessingAtResultAsync(CancellationToken queryToken)',s)
        self.assertIn('LifeSkillStage.Altering, "abyss-result-works"',s)
        self.assertIn('LifeSkillStage.Altering, "abyss-result-works-confirm"',s)
        self.assertIn('rawAlteringData.WorksAsync, queryToken',s)
        self.assertIn('AbyssAlteringSchedulePolicy.NeedsProcessingAfterRound(',s)
        self.assertIn('if (!needed)',s)
        self.assertIn('await Task.Delay(250, queryToken);',s)
        self.assertIn('NeedsProcessingAtResultAsync);',s)
        self.assertIn('scheduledResultExit: true,',combined)
        self.assertIn('shouldExitForProcessingAfterRound: shouldExitForProcessingAfterRound',combined)
        self.assertIn('ScheduledAbyssReturnPolicy.RequireVerifiedOutside(',combined)
        self.assertIn('scenario.Repeat = false;',combined)

    def test_queue_policy_keeps_completed_partial_batches_inside(self):
        s=read("source/FishingAutomation/AbyssAlteringSchedulePolicy.cs")
        self.assertIn('NeedsProcessingAfterRound(',s)
        self.assertIn('batch.All(w => w.IsCompleted)',s)
        self.assertIn('if (batch.Length == 0) return true;',s)
        self.assertIn('batch.Any(w => w.State == "NotStarted")',s)
        self.assertNotIn('batch.Any(w => w.IsCompleted))\n                return true;',s)
        t=read("tests/combined-scheduler/Program.cs")
        self.assertIn('partially completed facility batch -> stay in Abyss',t)
        self.assertIn('whole leather facility completed -> exit for receipt/registration',t)
        self.assertIn('selected empty facility needs processing reconciliation',t)
        self.assertIn('unrecognized CLI state cannot authorize another Abyss round',t)

    def test_other_safety_flows_unchanged(self):
        a=read("source/FishingAutomation/dungeon/ScenarioEngine.AbyssSchedulerExit.cs")
        b=read("source/FishingAutomation/dungeon/ScenarioEngine.AbyssRetry.cs")
        self.assertIn('HasStableScheduledExitLabel(',a)
        self.assertIn('WaitForAbyssHomeAfterNormalExitAsync(ct)',a)
        self.assertIn('ConfirmAbyssFixedButtonClick(frame, exit: true)',a)
        self.assertIn('ConfirmAbyssFixedButtonClick(fresh, exit: false)',b)
        self.assertIn('WaitForAbyssRetryTransitionAsync(ct)',b)

if __name__=="__main__":
    unittest.main()
