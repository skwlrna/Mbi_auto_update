"""Prepare (not publish) a version bump after an explicitly reviewed repair PR.

All edits are bounded to release metadata, UpdateManager, project version and
CI contract. The GitHub Actions workflow does the security checks and publish
steps. Nothing in this module launches a game or infers game validation.
"""
from __future__ import annotations

import argparse
import json
import re
from pathlib import Path

VERSION = re.compile(r"V(\d+)\.(\d+)\.(\d+)\Z")
BASE_RELEASE = "V3.2.62"


class ReleasePrepError(ValueError):
    pass


def next_tag(current: str, published: str) -> str:
    match = VERSION.fullmatch(current)
    if match is None or published != current:
        raise ReleasePrepError("Source version must equal latest public release")
    a, b, c = map(int, match.groups())
    if c >= 9999:
        raise ReleasePrepError("Patch sequence exhausted")
    return f"V{a}.{b}.{c + 1}"


def required_replace(data: str, old: str, new: str, label: str) -> str:
    if old not in data:
        raise ReleasePrepError(f"Missing expected {label} contract")
    return data.replace(old, new)


def _read(root: Path, relative: str) -> str:
    file = root / relative
    if not file.is_file() or file.is_symlink():
        raise ReleasePrepError(f"Missing or symlinked release source: {relative}")
    return file.read_text(encoding="utf-8-sig")


def prepare(root: Path, published: str, repair_pr: int,
            approved_head: str, merged_sha: str) -> dict:
    if not isinstance(repair_pr, int) or repair_pr < 1:
        raise ReleasePrepError("Invalid repair PR")
    for sha in (approved_head, merged_sha):
        if not re.fullmatch(r"[0-9a-f]{40}", sha):
            raise ReleasePrepError("Expected exact 40-character source SHA")

    updater = _read(root, "source/FishingAutomation/UpdateManager.cs")
    match = re.search(r'CurrentVersion = "(V\d+\.\d+\.\d+)"', updater)
    if not match:
        raise ReleasePrepError("Source version not found")
    old = match.group(1)
    new = next_tag(old, published)
    old_digits = old[1:]
    new_digits = new[1:]
    old_short = old_digits.replace(".", "")
    new_short = new_digits.replace(".", "")
    release_path = f".github/workflows/release-v{new_short}-auto-update.yml"
    acceptance_path = f"source/{new}_CI_ONLY_REPAIR_APPROVAL.json"
    notes_path = f"source/RELEASE_NOTES_{new}.md"

    if any((root / x).exists() for x in (release_path, acceptance_path, notes_path)):
        raise ReleasePrepError("New release assets already exist")

    project = _read(root, "source/FishingAutomation/FishingAutomation.csproj")
    ci = _read(root, ".github/workflows/ci.yml")
    template = _read(root, ".github/workflows/release-v3262-auto-update.yml")

    updater = required_replace(updater,
        f'CurrentVersion = "{old}"', f'CurrentVersion = "{new}"', "updater version")
    for prefix, suffix in (("<Version>", "</Version>"),
                           ("<AssemblyVersion>", ".0</AssemblyVersion>"),
                           ("<FileVersion>", ".0</FileVersion>")):
        old_val = old_digits if prefix == "<Version>" else old_digits
        project = required_replace(project, prefix + old_val + suffix,
                                   prefix + new_digits + suffix, "project version")

    escaped_old = old_digits.replace(".", r"\.")
    escaped_new = new_digits.replace(".", r"\.")
    ci = required_replace(ci, "V" + escaped_old, "V" + escaped_new, "CI version")
    ci = required_replace(ci, escaped_old, escaped_new, "CI project version")
    ci = required_replace(ci, f"release-v{old_short}-auto-update.yml",
                          f"release-v{new_short}-auto-update.yml", "CI release workflow")
    if "  workflow_dispatch:" not in ci:
        ci = required_replace(ci, "on:\n  push:", "on:\n  workflow_dispatch:\n  push:",
                              "CI manual event")

    # Copy the known package/rollback/smoke steps from the existing
    # V3.2.62 verified release workflow; use a new tag, never overwrite.
    start = template.find("      - name: Require game signoff OR direct-user-requested unverified rollout")
    end = template.find("      - name: Require CI-approved current main", start)
    if start < 0 or end < 0:
        raise ReleasePrepError("Release template gate layout changed: review manually")
    approval = f"""      - name: Require reviewed repair and explicit CI-only release scope
        shell: pwsh
        run: |
          $ErrorActionPreference = 'Stop'
          $record = Get-Content 'source/{new}_CI_ONLY_REPAIR_APPROVAL.json' -Raw | ConvertFrom-Json
          if ($record.schema -ne 1 -or $record.version -ne '{new}' -or
              $record.pr -ne {repair_pr} -or
              $record.approved_head_sha -ne '{approved_head}' -or
              $record.reviewed_merge_sha -ne '{merged_sha}' -or
              $record.verification -ne 'ci_only_unverified' -or
              $record.real_game_verified -ne $false -or
              $record.operator_reviewed -ne $true) {{ throw 'Unreviewed CI-only release blocked' }}
          git cat-file -e '{merged_sha}^{{commit}}'
          if ($LASTEXITCODE -ne 0) {{ throw 'Reviewed merge commit missing' }}
          git merge-base --is-ancestor '{merged_sha}' HEAD
          if ($LASTEXITCODE -ne 0) {{ throw 'Reviewed repair not in release HEAD' }}
          $changes = @(git diff --name-only '{merged_sha}' HEAD)
          if ($LASTEXITCODE -ne 0) {{ throw 'Cannot validate release code diff' }}
          $allowed = @('source/FishingAutomation/UpdateManager.cs',
                       'source/FishingAutomation/FishingAutomation.csproj',
                       '.github/workflows/ci.yml',
                       '.github/workflows/{Path(release_path).name}',
                       'source/{new}_CI_ONLY_REPAIR_APPROVAL.json',
                       'source/RELEASE_NOTES_{new}.md')
          foreach ($file in $changes) {{
            if ($file -notin $allowed) {{ throw "Non-version change after repair review: $file" }}
          }}
          Write-Warning 'CI-ONLY RELEASE: code reviewed and offline CI passed; live-game behavior NOT verified.'
\n"""
    release = template[:start] + approval + template[end:]
    # Template defaults to push-event CI. New version bump made by actions
    # explicitly dispatches the CI workflow for its exact main commit.
    release = required_replace(release,
        "github.event.workflow_run.event == 'push'",
        "(github.event.workflow_run.event == 'push' || github.event.workflow_run.event == 'workflow_dispatch')",
        "workflow dispatch source")
    release = required_replace(release, BASE_RELEASE, new, "release tag")
    release = required_replace(release, BASE_RELEASE[1:], new_digits, "release package version")
    release = required_replace(release, "v3262", f"v{new_short}", "workflow id")
    release = release.replace("V3\\.2\\.62", "V" + escaped_new)
    release = release.replace("3\\.2\\.62", escaped_new)
    release = release.replace("Publish V3.2.62 Legacy AutoFix Retirement Auto Update After Main CI",
                              f"Publish {new} CI-only Reviewed Repair Auto Update After Main CI")
    if f"MabiAuto_{new}_Windows_Lite.zip" not in release:
        raise ReleasePrepError("Missing new Windows updater archive contract")

    record = {
        "schema": 1, "version": new, "pr": repair_pr,
        "approved_head_sha": approved_head, "reviewed_merge_sha": merged_sha,
        "verification": "ci_only_unverified", "real_game_verified": False,
        "operator_reviewed": True,
        "automatic_update": True, "rollback_required": True,
    }
    notes = (f"# Mabi Auto {new}\n\n"
             f"- CI-reviewed automatic repair: PR #{repair_pr} ({approved_head[:12]}).\n"
             f"- Released from reviewed merge {merged_sha[:12]} plus version-only changes.\n"
             "- Verified by exact-main Windows production regression CI, offline executable "
             "smoke, updater ZIP checksum and rollback assets.\n"
             "- **Real game operation was not tested before release.**\n"
             "- Startup/idle-time updater automatically installs published versions and "
             "restarts the macro; active jobs are not interrupted.\n")

    updates = {
        "source/FishingAutomation/UpdateManager.cs": updater,
        "source/FishingAutomation/FishingAutomation.csproj": project,
        ".github/workflows/ci.yml": ci,
        release_path: release,
        acceptance_path: json.dumps(record, indent=2, ensure_ascii=True) + "\n",
        notes_path: notes,
    }
    # No partial mutation if templates do not meet the expected contracts.
    for relative, contents in updates.items():
        dest = root / relative
        dest.parent.mkdir(parents=True, exist_ok=True)
        dest.write_text(contents, encoding="utf-8")
    return {"new_version": new, "files": sorted(updates),
            "real_game_verified": False,
            "release_workflow": Path(release_path).name}


def main(argv=None):
    parser = argparse.ArgumentParser(description="Prepare reviewed CI-only repair release metadata")
    parser.add_argument("--repo", type=Path, required=True)
    parser.add_argument("--latest-release", required=True)
    parser.add_argument("--repair-pr", type=int, required=True)
    parser.add_argument("--approved-head-sha", required=True)
    parser.add_argument("--reviewed-merge-sha", required=True)
    args = parser.parse_args(argv)
    try:
        result = prepare(args.repo, args.latest_release, args.repair_pr,
                         args.approved_head_sha, args.reviewed_merge_sha)
        print(json.dumps(result, ensure_ascii=True, indent=2))
    except ReleasePrepError as exc:
        parser.exit(2, f"Release preparation blocked: {exc}\n")


if __name__ == "__main__":
    main()
