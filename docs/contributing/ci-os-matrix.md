# Choosing the CI operating systems

The Development workflow (`.github/workflows/development.yml`) tests on Linux, Windows and macOS. Windows and macOS
runners cost much more than Linux runners. Use this page to run Linux only while a pull request is not ready to merge.

The Fallout target `CIPlan` makes the decision (`build/Build.CI.cs`, `build/Helpers/CiPlan.cs`). The workflow only
reads its outputs.

## What runs

| Pull request | Operating systems | The `ci` check |
| --- | --- | --- |
| Draft | Linux | Passes with a warning that Windows and macOS did not run |
| Draft with the `ci:full` label | Linux, Windows, macOS | Passes when all pass |
| Ready for review | Linux, Windows, macOS | Passes when all pass |
| Ready for review with the `ci:linux-only` label | Linux | Fails: a merge needs Windows and macOS |
| Release call (`workflow_call`) | Linux, Windows, macOS, all run again | Passes when all pass |

When both labels are set, `ci:full` wins.

## Jobs that already passed do not run again

The plan job looks up the Development runs for the same head commit. A lint, OpenSpec or test job that passed in one
of them does not run again. Thus:

- When a draft becomes ready for review, only Windows and macOS run.
- When you add `ci:full`, only Windows and macOS run.
- A new push changes the head commit, so every selected job runs again.

The job summary of the plan job shows each check as "runs", "passed in an earlier run for this commit" or
"not selected".

## Start a run without a push

- Change a `ci:*` label. The CI request workflow (`.github/workflows/ci-request.yml`) starts a Development run for
  the pull request. Other labels start nothing.
- Run the Development workflow by hand. Set `os` to `auto`, `linux` or `full`, and `pr` to the pull request number:

  ```bash
  gh workflow run development.yml --ref <branch> -f os=full -f pr=<number>
  ```

  With `pr`, the run tests the merge commit of the pull request, and `os=auto` uses its draft state and labels.

A label change on a pull request from a fork does not start a run: the fork branch is not in this repository. Push
to the pull request to start a run.
