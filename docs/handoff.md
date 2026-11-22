# Handoff

Where the work stands, so the next session (on any computer) can pick it up. Keep it current: see the rule in `CLAUDE.md`. This is not a history; the plans' Progress sections and git hold that.

**Last updated:** 2026-11-22, the owner's answers to the 3b-2 plan's ten questions written in (question 7 as the owner put it, the other nine as recommended).

## Where things are

- **Branch:** `claude/ac2c-coop-engine-setup-wuj7hq` on `github.com/maudrid/Sc2c`. Everything is committed and pushed (as of the stage 3b-2 plan's answers).
- **Current plan:** `docs/plans/stage-3b-2.md` (planned, its ten questions answered by the owner on 2026-11-22, nothing started). Stage 3b-1 is built (`docs/plans/stage-3b.md`, its Progress section).
- **Last plan built:** `docs/plans/escort-upgrades.md`, the escorts' upgrades (the owner's decision of 2026-11-13, go-ahead 2026-11-16 for this item alone): escorts buy crew pods, fuel tanks, thrusters and turning jets up to host settings (`GameSettings.EscortCaps`, placeholders 8, 6, 4 and 2, the kit's counted), by the flagship's rules and prices. Built and reviewed; its readings of what the decision left open are in the plan (the owner's answers of 2026-11-17 to its two questions with them), its try-out in `docs/how-to-try/escort-upgrades.md`.
- **Stage 4b is built** (`docs/plans/stage-4b.md`, `docs/how-to-try/stage-4b.md`); stage 4a's too (`docs/plans/stage-4.md`).
- **Debugging aid:** a UQM run of one recorded battle with every frame whole can be had with a small script on `tools/extract/golden_uqm.py`'s `BattleProgram` (build `battle`, `strings` and `images` with its `build`, then `run_race_battles` on the recorded input).
- **Last checks** (the review of the question 12 and 13 builds' fixes, 2026-11-21): all topics 2,384 passed, 0 failed, 0 skipped with the content set; the game build has only the two long-standing warnings; `tools/test_network.py` passed. Before them: (question 13's build, 2026-11-21): all topics 2,382 passed, 0 failed, 0 skipped with the content set; the game build has only the two long-standing warnings; `tools/test_network.py` passed; the save's pass taken out, its three tests fail. Before it: (3b-1 item 11 after review round 5's fixes, 2026-11-21): all topics 2,378 passed, 0 failed, 0 skipped with the content set (the game build and `tools/test_network.py` last run after round 1's fixes: only the two long-standing warnings; passed). After round 1's fixes: the fix of the story's lines shown again checked by taking it out (the story test fails). As built: all topics 2,372 passed, 0 failed, 0 skipped with the content set; the topic map remade; the game build has only the two long-standing warnings (`game/scripts/Main.cs` CS8602, now line 1169; `game/scripts/ReportView.cs` 78); `tools/test_network.py` passed (its first run failed "R joined the host's battle": the drone at Earth now holds R at its message's closing lines, losing `--guests-in-battle`'s mark, so the testing aid now lets such a guest in again each frame until it's in a battle); `golden_uqm.py --only encounter-screens` recorded. Not run: the data checks (no data changed). Earlier (3b-1 item 10 after the owner's answer to question 9, 2026-11-21: all topics 2,356 passed, 0 failed, 0 skipped after its review's fix; the host's game menu entry's mutation caught; earlier: 3b-1 item 10 after review round 4's fixes, 2026-11-21): all topics 2,355 passed, 0 failed, 0 skipped with the content set; the topic map remade; round 4's mutations 2 tried, 2 caught (the counter taken out: the end-to-end test's meeting reply goes to the story; the old order: a number skipped); the game build has only the two long-standing warnings (`game/scripts/Main.cs` CS8602, now line 1155; `game/scripts/ReportView.cs` 78); `tools/test_network.py` passed (and on every run while item 10 was built and reviewed). Not run: the golden recorders and the data checks (no recorded behaviour or data changed).
- **Headless runs:** always give the host a `--save` file in a scratch folder. `--load` without `--save` goes on saving to the default `host-save.json`, and `--start-fleet`, `--start-allied`, `--start-units` and the like without a `--start-*` place change the default game.

## Open items

- `tools/test_network.py`'s check "host's flagship is where the save left it" failed twice while item 9 was built (a Slylandro probe kept in the first session's save meeting the host after loading; when it reaches the host depends on the real-time joins) and passed in 9 runs since: a rare flake since the probes fly from day one (item 8). If it comes back, keep the probes off in the scenario or compare the host's place before any meeting.
- **Notices to everyone (the owner, 2026-11-21, question 6):** every player is told every notice (those it touches in words of their own, the others in words for anyone); built (item 9's Progress row, `differences.md` row 76). Watch in playtesting whether it's too much; if so, add a "near" rule (`GameWorld.IsNear` already says who's near).
- **Stage 3b-1's left-open points**, by its items (each item's Progress row in `docs/plans/stage-3b.md`):
  - Item 12: `docs/how-to-try/stage-3b-1.md` describes the meetings with a friend and the spheres' groups from the tests and the plan's rules (not run by hand with two players: the network test covers joining); its other commands were run headless and three windowed with `--screenshot`.
  - Item 11: the encounter screen's menu and message line and the battle-won screen's status area are the status lines' (the port draws the space window only); the host's fleet ships aren't drawn in the battle-won screen's pick frame; the encounters' radar blips aren't drawn (the port has no hyperspace radar); the talk's intro fade (`SetCommIntroMode`) isn't sent or drawn. The new `EncounterView.cs` has no Godot `.uid` file (made in code, as `EncounterView` isn't in a scene).
  - Item 9: the notices go in words over the battle notices' message (item 10 kept them so: the client only shows them; a message of their own when a client does more with them); 3b-2 and 3c add their events to `NoticeEvent`; the owner's answers to questions 6 to 8 under "Decided".
  - Item 8: the destruct code is reachable only through the Slylandro at home (3b-2), and their ramp-down with it; no testing option sets the probe's story counters or the code (a save's story fields can).
  - Item 7: Spathiwa's moon's talk (`spahome.c`, 3b-2; kept so by the owner till then); the Chmmr's Ilwrath branch (Procyon's, wired by 3b-2's item 11), the broadcaster's (3c, with the device), Fwiffo on Pluto and the Ilwrath's mission (3c) ported and recorded but not reachable.
  - Item 6: the endgame's and 3c's branches of the Ur-Quan's and the Kohr-Ah's scripts are ported and recorded but not reachable yet.
  - Item 5: the Chmmr's queue and the Thraddash's body count (3b-2).
  - Items 3 and 4: the other races' groups fly with 3b-2 (row 68, `GameWorld.EncounterRaces`; flying now: the Ur-Quan, the drone, the Kohr-Ah, the Ilwrath, the Spathi and the Slylandro probes); the special systems' story (Tanaka lost, the scout and the recruited Shofixti flying off, the Yehat's rebellion check, the Sa-Matra's guards) left (row 72, a note in the 3b-2 row).
  - Item 2: the fleets' `crew_level`, `max_crew` and `max_energy` are the race's ship's, not kept per fleet; the fleets aren't sent to clients until there's a starmap.
  - Item 1: 3c's events are set aside, not run (`differences.md` row 66, which now says a fleet's arrival event would be set aside too).
- The escorts' caps are placeholders to be set by playtesting (`design.md`'s open item).

Smaller items left for later stages are notes under the items that own them in the plans (`stage-3.md` for 3b and 3c; `stage-4b.md`'s notes for 3b; Tanaka's Shofixti scout and the Melnorme's trading for escorts are in `stage-3b.md`'s 3b-2 row).

## Owner's rules (2026-11-12)

- **A talk is the answerer's ship's.** The group as a whole starts the story; whoever answers speaks for it with their own ship (names, hold, crew, fuel), and when the replies pass on, the talk goes on with the new answerer's ship. An opening taken back takes back what the talks changed on each ship; a save during it is without them.
- **Only battle salvage is split**, between the players in the battle when it's won (online: a disconnect leaves the battle). The starbase's 2,500 RU go to the answerer; players can give units to each other.
- **A guest's talk gives to the host's fleet (2026-11-15).** A guest who talks to the commander can bring the Arilou's ships (and any talk's ships) into the host's fleet; only the fleet's worth counts as 0 for a guest.
- **No messages for the host's fleet (2026-11-15).** Item 9 built none for the pick, the shipyard's fleet actions or the roster: the host's game acts on its own world and guests have no fleet. The owner agreed; needed only if guests may act on the host's fleet or the host's game becomes a client of its own world.
- **No ranks.** Guests as "Commander" was considered and dropped: UQM's lines and voices say "Captain".

## Decided

- **A save made at a meeting's closing lines (the owner, 2026-11-21, question 13): it carries the talk's end, as leaving then does.** After a peace the group written back re-forming and one-shot, the drone sent off; a surrender or an instant victory going through; no battle; after a hostile ending the group as it was. Only the saved copy gets the end: the live meeting waits on for its player. Built (`HostSave.ToJson`: a copy loaded from the save, `GameWorld.ClosingEndsForSave`, `MakeClosingEnds`; a host's flagship surrendering so isn't saved, the game over to come); reviewed.

- **The battle-won screen (the owner, 2026-11-21, question 12): watched or skipped.** It plays as UQM's, the ship waiting meanwhile; Escape pressed afresh ends it at any time (UQM's only after its first wait). Built (`BattleWonScreen.Advance`, `differences.md` row 18).

- **A choice made during a pause (the owner, 2026-11-21, question 11): as built.** An encounter choice or a meeting's reply reaching the host during its pause or a story talk's is kept and made as the pause ends.

- **A meeting's closing lines (the owner, 2026-11-21, question 10): on the communication screen, the meeting waiting.** The alien's last lines after the player's last reply, and the drone's whole message, play on the talk screen as UQM's; only that player's meeting waits (a new step after the talk), and its battle starts or its ship flies on once they've gone through them; everyone else goes on as now (the race's lock held that much longer). Built with 3b-1 item 11 (its Progress row). The starbase's story talks keep their closing lines as notices.

- **The encounter screen's game menu (the owner, 2026-11-21, question 9): the host has it, as UQM's.** A third entry on the host's encounter screen (guests: Hail and Attack only); until a game menu is built it says it comes later, as orbit's does. A save made from it, when it comes, is an ordinary save: the meeting isn't in it (the guest's ship where it met the group, the group as before the meeting, the talk's story writes so far kept). Built (item 10's Progress row).

- **When a meeting holds its race's lock (the owner, 2026-11-21, question 1): as built.** From the touch (the encounter screen too) until the talk is over, not through the battle.
- **A player leaving mid-meeting before its battle (the owner, 2026-11-21, question 2): as built.** Dropped as UQM's aborted game is: the group as it was (not re-forming, not one-shot), the talk's story writes kept; this holds for leaving before the talk ends, except at its closing lines (stage 3b-1 item 11) after a peace, a surrender or an instant victory: there the talk's end is made, keeping the behaviour from before item 11, when it was made before its closing lines (then a notice), so leaving couldn't undo it. After a hostile ending, leaving at the closing lines follows this rule (the meeting dropped, the group as it was, as UQM's abort is): a change from before item 11, when the battle started at once and leaving was a retreat from it (the group written back re-forming in hyperspace, in a system the race turned on the players).
- **A player out of their meeting's battle while others fight on (the owner, 2026-11-21, question 3): as built.** They fly on and meet no group until the battle ends; the group is written back then.
- **A guest surrendering to the Ur-Quan (the owner, 2026-11-21, question 4): as built.** The host's flagship surrendering is the game over for everyone; a guest's ship surrendering is a wreck where it is, for a friend to revive (`differences.md` row 73).
- **Spathiwa's moon (the owner, 2026-11-21, question 5): as built, fixed later.** Orbited as any world until `spahome.c` is ported with 3b-2; `GameWorld.WorldTalkRace` starts its talk then (`differences.md` rows 27 and 74).
- **Notices for a player away (the owner, 2026-11-21, question 7): as built.** Not told, nor kept for their return (the battle news is kept, being about their own ship).
- **A group "chasing" a player (the owner, 2026-11-21, question 8): as built.** One that has flown at them since it was made (not saved); its going is told to them instead of the meeting's end while they're near and not in its battle.
- **A shortcut for the Arilou's gift: not now (the owner, 2026-11-16).** No shortcut reaches the commander's bulletin (`--start-docked` marks every bulletin told; `--start-devices PORTAL_SPAWNER_DEVICE` sets `PORTAL_SPAWNER_ON_SHIP`, not the `PORTAL_SPAWNER` the bulletin reads); `FleetTests` checks it and the how-to-try says so.
- **Backlog:** guests' alien hulls (`design.md`, "Backlog"), not to be built now.

## Next step

Wait for the owner. Stage 3b-1 is built (items 1 to 12, each reported). Stage 3b-2 is planned in `docs/plans/stage-3b-2.md`: twelve items; its ten questions answered by the owner on 2026-11-22 (question 7, the rescue-only Melnorme talk and fuel given between players, changing items 3 and 4; the other nine as recommended). Nothing of 3b-2 is started; next is the owner's go-ahead for item 1 (what talks can do), then one go-ahead per item.

## Decisions waiting on the owner

None open. The 3b-2 plan's ten questions are answered (2026-11-22); its items wait for a go-ahead each (the next step).

## Left for later (the owner, 2026-11-13: visual fidelity is fixed later)

- **The smooth zoom's scaling.** UQM's default smooth zoom scales stamps trilinearly (each frame blended with the next size's); the port scales by nearest pixels (sizes and places are UQM's, the pixels sharper). Porting the trilinear scaler (about 300 lines of `canvas.c`) waits for a visual-fidelity pass.
- **The minimap's place** (over the space window's bottom right corner) may move then too.

## Working agreements

- **One item per go-ahead.** "Start item N" (or "push and start item N without me") covers that one item. When it's done, report and wait; don't push further items or start the next one unasked.
- **Push freely.** The owner said (2026-11-12) to push whenever; commit and push as work goes.
- **Mind the usage budget.** Each item has cost a builder subagent, up to five review rounds and an independent review (mutation testing and probes), often an hour or more of agents. Keep rounds short when they're only finding wording, and say what's running.
- **Subagents get cut off.** Builders sometimes hand back mid-loop; their reviewer may still be running and leave a mutated file in the tree. Check `git status` and running agents before going on, and have the reviewer finish and restore rather than reverting its files yourself.

## Setting up on another computer

- **Commit identity:** this repo's commits are `Claude <noreply@anthropic.com>`, set in the repo's local git config. In a fresh clone run `git config user.name Claude` and `git config user.email noreply@anthropic.com` before committing. Don't change the global identity.
- **Commit trailer:** end commit messages with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- **Toolchain:** .NET 8 SDK and Godot 4 mono. Set `GODOT` to the mono build for `tools/test_network.py`.
- **UQM content:** never committed. The game downloads it on first start into Godot's user folder (on Linux `~/.local/share/godot/app_userdata/Attack on Crateris/content/uqm-0.8.0-content.uqm`). Point `CRATERIS_CONTENT` at it for tests and headless runs (`docs/how-to-try/content-stage.md`).
- **UQM 0.8.0 source:** expected beside the repo at `../uqm-0.8.0/sc2`, for the golden recorders and data checks (`golden_uqm.py --uqm PATH --content PACKAGE --only NAME,...`; `--only battle-races` takes about two minutes).
- **Commands:**
  - tests: `python3 tools/run_tests.py` (`--changed` while working; `--map` to remake `tests/topic-map.json`)
  - build: `dotnet build game/Crateris.Game.csproj`
  - data checks: `UQM_SC2=$(realpath ../uqm-0.8.0/sc2) python3 tools/extract/test_data.py`
  - network: `python3 tools/test_network.py`
