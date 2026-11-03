# Decouple published-version release workflows from main CI

## Why

Historically, many `.github/workflows/release-*.yml` files subscribe to
`workflow_run` for the exact workflow display name
`Check V3 Production Engine`. Even when an already-published version would be
rejected by its tag or release guard later, every successful `main` CI run
starts those workflows. Some unrelated historical version policy checks then
fail and create noisy alerts.

## Scoped fix

The canonical `.github/workflows/ci.yml` display name is
`Main Build CI (Release-Decoupled)`. Its workflow filename, job ID
(`build-and-test`), push and pull-request events, .NET builds, and tests
remain unchanged. Repository rules that require the `build-and-test`
GitHub Actions check remain applicable.

GitHub `workflow_run.workflows` filters by workflow **name**. Retained
historical release subscriptions use the old literal name. Because the
canonical CI does not emit that name, the published-version publishers do
not start on subsequent successful main CI executions.

No release files, versions, authorizations, tags, installed executables or
release artifacts are modified. Manual `workflow_dispatch` in historical
release workflows remains available, with their own existing checks and
permissions; it is not approval to use it.

## Deliberate tradeoff

This change disconnects **all** release workflows listening for that old
name, including any that might be reused for a future release. New release
automation must be intentionally designed, reviewed and approved with an
explicit trigger; do not change the CI name back just to make a future
release run. Changing it back would awaken all legacy subscribers.

It does not stop legacy releases with independent `push` triggers scoped
to changes of their own workflow files, nor does it cancel jobs already
queued or running before this PR is merged.

## Safety verification

The source-only contract in
`tests/release/test_legacy_release_event_isolation.py` verifies:
- Current CI display name differs from the retired trigger name.
- Branch protection's `build-and-test` job name remains unchanged.
- No `release-*.yml` inline `workflow_run.workflows` filters match
  the new CI name, including glob patterns.
- A historical release manual dispatch trigger remains defined.

The test runs as part of the **Offline Build and Mock Test Gate** PR
checks, with read-only workflow permissions.

On a later approved merge, confirm that `main` CI and the offline
mock/build gate succeed, that no historical `workflow_run` release jobs
were spawned by the new CI run, and that the latest public release remains
unchanged. PR-level tests cannot confirm GitHub event behavior before merge.

Run just this contract locally:

```powershell
python -m unittest discover -s tests/release -p 'test_legacy_release_event_isolation.py' -v
```
