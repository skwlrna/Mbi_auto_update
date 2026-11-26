"""V3.3.31: 21:32 real F9 facility switch must not dead-end before its FIRST card."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]


def load(path):
    return (ROOT / path).read_text(encoding="utf-8")


class F9FirstHubAfterKRecovery(unittest.TestCase):
    def test_two_disjoint_recovery_branches(self):
        layout = load("source/FishingAutomation/AlteringFacilityLayout.cs")
        screen = load("source/FishingAutomation/dungeon/AlteringScreen.cs")
        self.assertIn("CanRecoverFinalHubSelection(", layout)
        self.assertIn("CanSelectFirstCardAfterVerifiedKReturn(", layout)
        self.assertIn("verifiedKAfterForeignFacilityExit && previousCardClicks == 0", layout)
        self.assertIn("exactSixCardHub && !blockingModal", layout)
        self.assertIn("CanSelectFirstCardAfterVerifiedKReturn(", screen)
        self.assertIn("CanRecoverFinalHubSelection(", screen)
        self.assertIn("hubCardClicks == 1", screen)
        self.assertIn("hubCardClicks == 0 && foreignFacilityEscConfirmedWorld", screen)

    def test_verified_esc_to_world_and_input_provenance(self):
        screen = load("source/FishingAutomation/dungeon/AlteringScreen.cs")
        self.assertIn("destination == ManagedProductionSceneKind.WorldHud", screen)
        self.assertIn("verifiedKAfterForeignFacilityExit = false", screen)
        self.assertIn("await _ui.TapVerifiedFreshAsync(0x25, verifyManagedK, ct)", screen)
        self.assertIn("HasFixedProcessingHubVisual(frame)", screen)
        self.assertIn("managedScene == ManagedProductionSceneKind.ProcessingHub", screen)
        self.assertIn("HasManagedKEntryBlockingModal(frame)", screen)
        self.assertIn("HasBottomConfirmationModal(frame)", screen)
        self.assertIn("await IsFacilityTravelDialogAsync(frame, ct)", screen)
        self.assertIn("if (!existingCardRetry && !firstCardAfterVerifiedK)", screen)
        self.assertIn("추가 K 없음", screen)

    def test_incident_in_compiled_visual_regressions(self):
        compiled = load("tests/altering-screen-integration/Program.cs")
        self.assertIn("V3.3.31 21:32", compiled)
        self.assertIn("CanSelectFirstCardAfterVerifiedKReturn", compiled)
        self.assertIn("CanRecover(true, 0, true, false)", compiled)
        self.assertIn("previousClicks == 0", compiled)
        self.assertIn("V3.3.31 first-card recovery preserves", compiled)

    def test_new_version_and_ci_release_gating(self):
        # Historical V3.3.31 publisher stays immutable; newer versions
        # are validated by their own version-specific release gates.
        project = load("source/FishingAutomation/FishingAutomation.csproj")
        updater = load("source/FishingAutomation/UpdateManager.cs")
        if 'CurrentVersion = "V3.3.31"' in updater:
            self.assertIn('<Version>3.3.31</Version>', project)
        elif 'CurrentVersion = "V3.3.48"' in updater:
            self.assertIn('<Version>3.3.48</Version>', project)
            self.assertIn("gh release create V3.3.48",
                          load(".github/workflows/release-v3348-auto-update.yml"))
        elif 'CurrentVersion = "V3.3.47"' in updater:
            self.assertIn('<Version>3.3.47</Version>', project)
            self.assertIn("gh release create V3.3.47",
                          load(".github/workflows/release-v3347-auto-update.yml"))
        elif 'CurrentVersion = "V3.3.46"' in updater:
            self.assertIn('<Version>3.3.46</Version>', project)
            self.assertIn("gh release create V3.3.46",
                          load(".github/workflows/release-v3346-auto-update.yml"))
        elif 'CurrentVersion = "V3.3.45"' in updater:
            self.assertIn('<Version>3.3.45</Version>', project)
            self.assertIn("gh release create V3.3.45",
                          load(".github/workflows/release-v3345-auto-update.yml"))
        elif 'CurrentVersion = "V3.3.44"' in updater:
            self.assertIn('<Version>3.3.44</Version>', project)
            self.assertIn("gh release create V3.3.44",
                          load(".github/workflows/release-v3344-auto-update.yml"))
        elif 'CurrentVersion = "V3.3.43"' in updater:
            self.assertIn('<Version>3.3.43</Version>', project)
            self.assertIn("gh release create V3.3.43",
                          load(".github/workflows/release-v3343-auto-update.yml"))
        elif 'CurrentVersion = "V3.3.42"' in updater:
            self.assertIn('<Version>3.3.42</Version>', project)
            self.assertIn("gh release create V3.3.42",
                          load(".github/workflows/release-v3342-auto-update.yml"))
        elif 'CurrentVersion = "V3.3.41"' in updater:
            self.assertIn('<Version>3.3.41</Version>', project)
            self.assertIn("gh release create V3.3.41",
                          load(".github/workflows/release-v3341-auto-update.yml"))
        elif 'CurrentVersion = "V3.3.40"' in updater:
            self.assertIn('<Version>3.3.40</Version>', project)
            self.assertIn("gh release create V3.3.40",
                          load(".github/workflows/release-v3340-auto-update.yml"))
        elif 'CurrentVersion = "V3.3.39"' in updater:
            self.assertIn('<Version>3.3.39</Version>', project)
            self.assertIn("gh release create V3.3.39",
                          load(".github/workflows/release-v3339-auto-update.yml"))
        elif 'CurrentVersion = "V3.3.38"' in updater:
            self.assertIn('<Version>3.3.38</Version>', project)
            self.assertIn("gh release create V3.3.38",
                          load(".github/workflows/release-v3338-auto-update.yml"))
        elif 'CurrentVersion = "V3.3.37"' in updater:
            self.assertIn('<Version>3.3.37</Version>', project)
            self.assertIn("gh release create V3.3.37",
                          load(".github/workflows/release-v3337-auto-update.yml"))
        elif 'CurrentVersion = "V3.3.36"' in updater:
            self.assertIn('<Version>3.3.36</Version>', project)
            self.assertIn("gh release create V3.3.36",
                          load(".github/workflows/release-v3336-auto-update.yml"))
        elif 'CurrentVersion = "V3.3.35"' in updater:
            self.assertIn('<Version>3.3.35</Version>', project)
            self.assertIn("gh release create V3.3.35",
                          load(".github/workflows/release-v3335-auto-update.yml"))
        elif 'CurrentVersion = "V3.3.34"' in updater:
            self.assertIn('<Version>3.3.34</Version>', project)
            self.assertIn("gh release create V3.3.34",
                          load(".github/workflows/release-v3334-auto-update.yml"))
        elif 'CurrentVersion = "V3.3.33"' in updater:
            self.assertIn('<Version>3.3.33</Version>', project)
            self.assertIn("gh release create V3.3.33",
                          load(".github/workflows/release-v3333-auto-update.yml"))
        elif 'CurrentVersion = "V3.3.32"' in updater:
            self.assertIn('<Version>3.3.32</Version>', project)
            self.assertIn("gh release create V3.3.32",
                          load(".github/workflows/release-v3332-auto-update.yml"))
        else:
            self.fail("Unrecognized current version; update historical release policy")
        workflow = load(".github/workflows/release-v3331-auto-update.yml")
        for required in ("V3.3.31 21:32 verified K-first-card recovery regression",
                         "tests/altering-screen-integration/Regression.csproj",
                         "No successful production CI for current main SHA",
                         "MabiAuto_V3.3.31_Windows_Lite.zip.sha256",
                         "gh release create V3.3.31",
                         "gh release edit V3.3.31 --draft=false"):
            self.assertIn(required, workflow)
        self.assertIn("test_v3331_f9_verified_k_first_hub.py",
                      load(".github/workflows/ci.yml"))


if __name__ == "__main__":
    unittest.main()
