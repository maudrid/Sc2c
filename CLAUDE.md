# Working on Attack on Crateris

## Review before reporting complete

Before reporting any piece of work as complete (a plan item, a fix, a review's findings), have a subagent review it first:

1. Commit the work, then start a subagent to review that commit's diff for correctness bugs, gaps against the plan or the request, missing tests, and docs that no longer match the code.
2. Fix what it finds that holds up, or say why a finding doesn't, and run the tests again.
3. Only then report the work complete, saying what the review found and what was done about it.

## Project rules

- The UQM content package must never be committed to the repository.
- Tests that need the content read it from `CRATERIS_CONTENT`; without it they're skipped, so set it when checking work.
