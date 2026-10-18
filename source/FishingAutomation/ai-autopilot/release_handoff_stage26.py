"""Stage26: read-only DEVELOPMENT handoff and rollback preparedness audit.

This is NOT a release/merge/updater command or a production-signoff oracle.
Never executes subprocess, Git, network, Codex, tests, live CLI, GUI, or game.
Reads only fixed repository sources and optional local build-output files.
The user-PC validation, current upstream CI/SHA, rollback dry-run and human
authorization must be verified separately; all release authorities remain OFF.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re
from xml.etree import ElementTree as ET

import integration_safety_gate_stage25 as stage25

REQUIRED_RUNTIME_MODULES = (
    "autopilot.py",
    "evidence_ab.py",
    "incident_coverage_audit.py",
    "incident_priority_review.py",
    "incident_review_context.py",
    "paired_prompt_ab_stage24.py",
    "integration_safety_gate_stage25.py",
    "release_source.py",
    "regression_policy.py",
    "supervised_codex.py",
    "quota_handoff.py",
    "error_memory.py",
    "pipeline_readiness.py",
    "github_pr.py",
    "config.json",
)
MAX_MODULE_BYTES = 150000
MAX_TOTAL_MODULE_BYTES = 500000
MAX_META_BYTES = 150000
VERSION = re.compile(r"3\.[0-9]+\.[0-9]+")
SHA40 = re.compile(r"^[a-f0-9]{40}$")
APP_TFM = "net8.0-windows10.0.19041.0"


def _bounded(root: Path, rel: str, bound: int = MAX_META_BYTES) -> bytes | None:
    """Never traverse a symlink, including through a parent directory."""
    try:
        if root.is_symlink() or not root.is_dir():
            return None
        cur = root
        for part in rel.split("/"):
            if not part or part in (".", ".."):
                return None
            cur = cur / part
            if cur.is_symlink():
                return None
        if not cur.is_file() or not 1 <= cur.stat().st_size <= bound:
            return None
        data = cur.read_bytes()
        return data if 1 <= len(data) <= bound and b"\x00" not in data else None
    except (OSError, ValueError):
        return None


def _modules(root: Path, packaged: Path | None = None) -> tuple[bool, bool, int]:
    """Compare package bytes to fixed source modules when build output exists.

    Never include source file bytes/names of unknown extras in output, and
    don't attempt to turn an existing build folder into a release package.
    """
    total = 0
    try:
        for name in REQUIRED_RUNTIME_MODULES:
            raw = _bounded(root, "source/FishingAutomation/ai-autopilot/" + name,
                           MAX_MODULE_BYTES)
            if raw is None:
                return False, False, 0
            total += len(raw)
            if total > MAX_TOTAL_MODULE_BYTES:
                return False, False, 0
        if packaged is None:
            return True, False, len(REQUIRED_RUNTIME_MODULES)
        if packaged.is_symlink() or not packaged.is_dir():
            return True, False, len(REQUIRED_RUNTIME_MODULES)
        for name in REQUIRED_RUNTIME_MODULES:
            raw = _bounded(root, "source/FishingAutomation/ai-autopilot/" + name,
                           MAX_MODULE_BYTES)
            candidate = _bounded(packaged, "ai-autopilot/" + name,
                                 MAX_MODULE_BYTES)
            if raw is None or candidate is None or hashlib.sha256(raw).digest() != hashlib.sha256(candidate).digest():
                return True, False, len(REQUIRED_RUNTIME_MODULES)
        if _bounded(packaged, "FishingAutomation.exe", 250_000_000) is None:
            # EXE is a binary; _bounded deliberately rejects NUL bytes.
            # Check its file identity without opening/executing the EXE.
            app = packaged / "FishingAutomation.exe"
            if app.is_symlink() or not app.is_file() or app.stat().st_size <= 0:
                return True, False, len(REQUIRED_RUNTIME_MODULES)
        return True, True, len(REQUIRED_RUNTIME_MODULES)
    except (OSError, ValueError):
        return False, False, 0


def audit(root: Path, packaged: Path | None = None) -> dict[str, object]:
    stage = stage25.inspect(root)
    local_ok = stage["stage26_review_candidate"] is True
    src = _bounded(root, "source/FishingAutomation/FishingAutomation.csproj")
    updater = _bounded(root, "source/FishingAutomation/UpdateManager.cs")
    ci = _bounded(root, ".github/workflows/ci.yml")
    rollback_test = _bounded(root, "changes/tests/stability/Updater.Tests.ps1")
    apply = _bounded(root, "source/tools/ApplyUpdate.ps1")
    revert = _bounded(root, "source/tools/RollbackPrevious.ps1")
    marker_reader = _bounded(root, "source/FishingAutomation/ai-autopilot/release_source.py")
    source_modules, packaged_modules, checked = _modules(root, packaged)

    version = ""
    version_ok = False
    packaged_declared = False
    try:
        if src is not None:
            tree = ET.fromstring(src.decode("utf-8-sig", errors="strict"))
            props = [item for item in tree.iter("PropertyGroup")]
            version_nodes = [node.findtext("Version") for node in props if node.find("Version") is not None]
            assembly_nodes = [node.findtext("AssemblyVersion") for node in props if node.find("AssemblyVersion") is not None]
            file_nodes = [node.findtext("FileVersion") for node in props if node.find("FileVersion") is not None]
            if len(version_nodes) == len(assembly_nodes) == len(file_nodes) == 1:
                candidate = version_nodes[0] or ""
                if (VERSION.fullmatch(candidate) and
                        assembly_nodes[0] == candidate + ".0" and
                        file_nodes[0] == candidate + ".0"):
                    version = candidate
                    version_ok = True
            packaged_declared = any(
                node.get("CopyToPublishDirectory") == "PreserveNewest" and
                "ai-autopilot\\*.py" in node.get("Include", "")
                for node in tree.iter("Content")
            )
    except (ET.ParseError, UnicodeError, ValueError, TypeError):
        pass
    updater_ok = False
    release_wf_ok = False
    rollback_ok = False
    pinned_ok = False
    try:
        if version_ok and updater is not None and ci is not None:
            v = version.encode("ascii")
            updater_ok = b'CurrentVersion = "V' + v + b'"' in updater
            ci_text = ci.decode("utf-8-sig", errors="strict")
            rollback_ok = (
                rollback_test is not None and apply is not None and
                revert is not None and
                "Updater rollback regression tests" in ci_text and
                "./changes/tests/stability/Updater.Tests.ps1 -Updater ./source/tools/ApplyUpdate.ps1" in ci_text and
                b".update_rollback" in apply and b".previous_version" in apply and
                b"health" in apply.lower() and
                b".previous_version" in revert and b"Manual rollback" in revert
            )
            workflow = _bounded(root, ".github/workflows/release-v" +
                                version.replace(".", "") + "-auto-update.yml")
            release_wf_ok = (
                workflow is not None and
                b"branches: [main]" in workflow and
                b"BUILD_COMMIT.txt" in workflow and
                b"Get-FileHash" in workflow and
                b"--draft" in workflow and
                b"gh release edit" in workflow and
                b"tools/RollbackPrevious.ps1" in workflow and
                b"tools/ApplyUpdate.ps1" in workflow
            )
        pinned_ok = (marker_reader is not None and
                     b"BUILD_COMMIT.txt" in marker_reader and
                     b"release_source_guard_missing" in marker_reader and
                     b"release_source_verified" in marker_reader)
    except (UnicodeError, ValueError, TypeError):
        pass
    checks = {
        "stage25_integration_contract": local_ok,
        "local_version_fields_match": version_ok,
        "updater_matches_project_version": updater_ok,
        "python_modules_packaging_declared": packaged_declared,
        "required_runtime_modules_present": source_modules,
        "pinned_source_marker_contract": pinned_ok,
        "rollback_scripts_and_ci_regression_present": rollback_ok,
        "historical_release_workflow_safety_contract": release_wf_ok,
        "optional_local_build_output_matches_source": packaged_modules if packaged is not None else False,
    }
    checks_ok = all(v for k, v in checks.items() if k != "optional_local_build_output_matches_source")
    if packaged is not None:
        checks_ok = checks_ok and checks["optional_local_build_output_matches_source"]
    return {
        "schema": 1,
        "stage": 26,
        "status": "development_handoff_checks_passed_release_held" if checks_ok else
                  "development_handoff_blocked",
        "scope": "offline_development_and_release_handoff_only",
        "candidate_version": version if version_ok else "unverified",
        "checks": checks,
        "checks_passed": sum(checks.values()),
        "module_count": checked,
        "build_output_supplied": packaged is not None,
        "built_binary_executed": False,
        "build_commit_marker_provenance_verified": False,
        "upstream_main_sha_checked": False,
        "same_head_ci_triple_success_checked": False,
        "actual_release_version_checked": False,
        "upstream_divergence_resolved": False,
        "user_pc_trial_verified": False,
        "user_approval_verified": False,
        "rollback_trial_verified": False,
        "ready_for_upstream_reconciliation": checks_ok,
        "ready_for_production_release": False,
        "release_hold": "require_current_main_rebase_ci_and_live_game_user_approval",
        "automatic_update_authorized": False,
        "auto_merge_authorized": False,
        "auto_repair_authorized": False,
        "deployment_authorized": False,
        "gameplay_authorized": False,
        "codex_executions": 0,
        "network_requests": 0,
        "game_runs": 0,
        "files_written": 0,
        "ai_accuracy_change": "not_measured",
    }


def main() -> int:
    parser = argparse.ArgumentParser(description="Read-only release handoff gate; NEVER approval")
    parser.add_argument("--build-dir", type=Path, default=None,
                        help="Optional existing local .NET output directory (read-only)")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[3]
    result = audit(root, args.build_dir)
    print(json.dumps(result, ensure_ascii=False, indent=2))
    # Success means DEV HANDOFF ONLY, not release permission.
    return 0 if result["ready_for_upstream_reconciliation"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
