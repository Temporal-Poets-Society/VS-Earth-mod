# 0103: A new world stops when a data file its rules need can't be had; it never goes without it for good

Status: **in Earth 2.2.0** (21 November 2026), built on `claude/terrain-2.2` (EA-0191) for the author's in-game test (test build 2.2.0-rc.9, 19 November
2026). Date: 2026-11-19. Decided by the maintainer (D72); the author agreed to it for 2.2 ("Yes, for 2.2").

## Context

`streams-glo90-1.bin` was baked twice under one name (17 November, then on courses rules 5 on 18 November). The AgentBridge
bench's store kept the first. A 1:10 world made there with 2.2.0-rc.8 had no streams at all, for good, and said so in one
log line only (EA-0191).

What happened, in `ReleaseFiles` (EA-0088, review A3):
1. The stale file failed its size and SHA-256 (`StreamData.IsExpected`), so it was fetched again from egl-v1.
2. egl-v1 doesn't have the file yet (its upload waits for the author's yes), and the release answered so.
3. That answer counted as final for a world that had not generated with the file: the world was marked
   `earth-streams-off` and went on without the streams layer for good.

The lake outlines, river outlines, flow and dam files go the same way. Review A3 made a network failure stop the server
(it may pass) and the release's "don't have it" mark the world (so its chunks match). But "don't have it" is never the
world's choice: it is our mistake (a pinned file not uploaded, a wrong file on the release, a stale copy in a store) or a
setting (no release set). Marking the world for good makes the mistake permanent for that world, and a player only sees
the result: a world without its streams or lakes, and no message in game.

## Decision

1. **A world that can't have a file its rules need stops**, whatever the reason: no release set, the release lacks the
   file, the release's file isn't the one this build reads, a network failure, a pinned file that can't be read,
   or a dam file made for other river courses. The
   server stops in WorldReady, before any chunk, and nothing is saved; the world's first start resumes as new once the
   file is there (`EarthMapSystem.ResolveParams`). No world is marked as going without a file any more
   (`ReleaseFiles.Unavailable`).
2. **The message says what to do.** It names the file, the reason (with the stale copy's size or hash when the store
   had one), the folder to put the file in and the release it comes from, and how to make the world without it where
   there is a way (the streams: `StreamsLayer` false in earth.json; the GLO-90 water files: a world on the Earth tab's
   built-in heights). In singleplayer the message is the error line on the world's loading screen ("It begins...", which
   lists the server's error lines; the game's `GuiScreenConnectingToServer`), where the player stays until they cancel.
3. **Worlds already marked keep working as they are** (`earth-*-off`, set by builds up to 2.2.0-rc.8): they go on without
   the file so their chunks match. **Now they say so in game**, once a session, to each player who can act on it (the
   single player, a server's admins), not only in the log.
4. **A release check** (`tests\DataReleaseCheck`, uses the network, outside the quick suite): every file this build pins is
   on its release (ed-v1, egts-v1, egl-v1) with the pinned size. It must pass before a release; on 19 November it fails
   for the seven files still waiting for upload (`rivers-glo90-snap-3.bin`, `rivers-glo90-arms-3.bin`,
   `lakes-glo90-5.bin`, `rivers-outline-glo90-5.bin`, `rivers-flow-glo90-2.bin`, `dams-glo90-4.bin`,
   `streams-glo90-1.bin`), which is right.
5. **A file baked again gets a new name**, also when the earlier bake was never uploaded (Earth's AGENTS.md, Data).

## Consequences

- Until those seven files are uploaded, a store that lacks one of them, or has an earlier bake of it, can't make a new
  2.2 world that needs it: the world stops with the message instead of going without. Stores filled by the test builds'
  install scripts have the pinned files.
- A world stopped at creation shows in the singleplayer world list. Starting it again once the file is there makes it
  as new, with the same settings.
- Saves are unchanged: the 12 saved worlds give the same terrain; a world with an `-off` key reads as before.
- Not changed: a world that has generated with a file and can't have it now stops, as before; the GLO-90 tiles keep
  their own rules (a square no source has keeps the built-in heights for good, EA-0099).

## History

- 2026-11-19: built for 2.2.0-rc.9 (EA-0191).
