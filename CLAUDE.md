# Working on Attack on Crateris

## Review before reporting complete

Before reporting any piece of work as complete (a plan item, a fix, a review's findings), have a subagent review it first:

1. Commit the work, then start a subagent to review that commit's diff for correctness bugs, gaps against the plan or the request, missing tests, and docs that no longer match the code.
2. Fix what it finds that holds up, or say why a finding doesn't, and run the tests again.
3. The fixes are work too: commit them and have a fresh subagent review that commit the same way. Repeat until a review finds nothing that holds up (if rounds keep finding things after three, stop and tell the user rather than looping on).
4. Only then report the work complete, saying what each round found and what was done about it.

## Project rules

- The UQM content package must never be committed to the repository.
- Tests that need the content read it from `CRATERIS_CONTENT`; without it they're skipped, so set it when checking work.
- Tests are grouped by topic (`tests/topics.json`, a `[Trait("Topic", ...)]` on each test class). While working, run the topics a change can affect with `python3 tools/run_tests.py --changed`; run every topic (`python3 tools/run_tests.py`) before reporting work complete. A new test class needs a topic (`TopicTests` checks). `--changed` reads `tests/topic-map.json`; a file it doesn't know runs every topic, and a file changed since the map was made counts as changed. When the map has fallen behind, remake it (`CRATERIS_CONTENT=... python3 tools/run_tests.py --map`) and commit it with the change.
