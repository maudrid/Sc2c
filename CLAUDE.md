# Working on Attack on Crateris

## Keep the handoff current

Work moves between computers, so `docs/handoff.md` says where things stand. Read it at the start of a session. Update it and commit it with the work whenever a step ends: a plan item built, a review's findings fixed, a decision made or asked of the owner, or the session stopping. It holds only what's open and the next step (the plans' Progress sections and git hold the history): the branch and whether everything is pushed, the last checks' results, open items with where they are, decisions waiting on the owner, the next step, and the working agreements and setup notes a fresh session needs. Drop items once they're done.

## Review before reporting complete

Before reporting any piece of work as complete (a plan item, a fix, a review's findings), have a subagent review it first:

1. Commit the work, then start a subagent to review that commit's diff for correctness bugs, gaps against the plan or the request, missing tests, and docs that no longer match the code.
2. Fix what it finds that holds up, or say why a finding doesn't, and run the tests again.
3. The fixes are work too: commit them and have a fresh subagent review that commit the same way. Repeat until a review finds nothing that holds up (if rounds keep finding things after five, stop and tell the user rather than looping on).
4. Only then report the work complete, saying what each round found and what was done about it.

## Project rules

- The UQM content package must never be committed to the repository.
- Tests that need the content read it from `CRATERIS_CONTENT`; without it they're skipped, so set it when checking work.
- Tests are grouped by topic (`tests/topics.json`, a `[Trait("Topic", ...)]` on each test class). While working, run the topics a change can affect with `python3 tools/run_tests.py --changed`; run every topic (`python3 tools/run_tests.py`) before reporting work complete. A new test class needs a topic (`TopicTests` checks). `--changed` reads `tests/topic-map.json`; a file it doesn't know runs every topic, and a file changed since the map was made counts as changed. When the map has fallen behind, remake it (`CRATERIS_CONTENT=... python3 tools/run_tests.py --map`) and commit it with the change.
