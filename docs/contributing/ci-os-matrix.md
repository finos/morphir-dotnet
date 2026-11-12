# Choosing the CI operating systems

The Development workflow (`.github/workflows/development.yml`) tests on Linux, Windows and macOS. Windows and macOS
runners cost much more than Linux runners. Use this page to run Linux only while a pull request is not ready to merge.

The Fallout target `CIPlan` makes the decision (`build/Build.CI.cs`, `build/Helpers/CiPlan.cs`). The workflow only
reads its outputs.

## What runs

Linux always runs: lint, OpenSpec and the Linux tests. Labels add the other platforms.

| Pull request | Operating systems | The `ci` check |
| --- | --- | --- |
| Draft, no platform label | Linux | Passes with a warning that Windows and macOS did not run |
| Ready for review, no platform label | Linux, Windows, macOS | Passes when all pass |
| `ci:windows` | Linux, Windows | Passes with a warning on a draft; fails on a ready pull request |
| `ci:macos` | Linux, macOS | Passes with a warning on a draft; fails on a ready pull request |
| `ci:windows` and `ci:macos`, or `ci:full` | Linux, Windows, macOS | Passes when all pass |
| `ci:linux-only` | Linux | Passes with a warning on a draft; fails on a ready pull request |
| Release call (`workflow_call`) | Linux, Windows, macOS, all run again | Passes when all pass |

When a pull request has a platform label, the labels give the exact set, on a draft and on a ready pull request.
Labels combine, and the larger set wins: `ci:linux-only` with `ci:windows` runs Linux and Windows.

A pull request that is ready for review needs every platform before it can merge. The `ci` check names the missing
platform and the label that adds it.

## Where the selection shows

- The run page shows a "CI plan" notice, for example `Linux, Windows (label ci:windows). Runs: ...`.
- The job summary of the `plan` job shows the selection, why it was selected, and for each check: runs, passed in an
  earlier run (with a link to that job), or not selected (with the label that adds it).
- The job summary of the `ci` job shows the verdict and the result of each check.

## Jobs that already passed do not run again

The plan job looks up the pull_request runs of Development for the same head commit, with every attempt. A lint,
OpenSpec or test job that passed in one of them does not run again. Thus:

- When a draft becomes ready for review, only Windows and macOS run.
- When you add `ci:macos` to a pull request that ran Linux and Windows, only macOS runs.
- A new push changes the head commit, so every selected job runs again.

Manual runs (`workflow_dispatch`) are not evidence: GitHub does not count their checks for a pull request.

## Change the selection without a push

Change a `ci:*` label. The CI request workflow (`.github/workflows/ci-request.yml`) re-runs the Development run of
the pull request for its head commit. If that run has not completed, it is cancelled first. The plan job of the re-run
reads the labels and the draft state as they are now. A re-run keeps the `pull_request` event, so its `ci` check
counts for the pull request. Other labels start nothing.

"Re-run all jobs" on the run page does the same.

A label change on a pull request from a fork starts nothing. Push to the pull request to replan with its labels.

## Manual runs

Run the Development workflow by hand to test a branch on chosen platforms. Set `os` to `auto` (all), `linux`,
`full`, or a list of `windows` and `macos`:

```bash
gh workflow run development.yml --ref <branch> -f os=windows
```

A manual run never reuses jobs, and its checks do not count for a pull request.
