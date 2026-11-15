# 0008: Failed startup steps leave vanilla behaviour

Status: Implemented in 0.14.1 (in Earth 1.0.0); in Earth 2.0.0. The failure paths are checked by hand only (no automated test) | Date: 2026-10-16 | Decided by: Claude, fixing review findings the user asked to fix

## Context

Two startup steps changed the game's state piece by piece and could fail halfway:

- **Ore activation.** Earth's ore layer wraps vanilla's deposit generators (`EarthOres.Bind`): it replaces
  `GeneratorInst`, turns on `WithOreMap`, adds Earth-authored variants and, with Earth's bog peat, sets provider bog-iron
  tries to 0. Bind rolled this back if binding itself failed. But the steps after it could fail too: vanilla's
  `initWorldGen` and `AfterNativeInit` after a `/wgen regen` reload, `AfterNativeInit` at startup, and saving the
  fingerprint. Then the session was only forgotten. Generation went on through Earth's wrappers with vanilla's attempt
  factors, while the log said "vanilla deposits".
- **Story placement.** `StoryPlacer.Place` moved each story location and added its flat zone as it went. A failure
  halfway left some locations moved and others not, while the log said "vanilla's positions are kept", and
  `StoryPlaced` was saved, so the half-done layout stayed for good.

## Decision

A startup step that changes the game's state is all or nothing. If it fails, the vanilla behaviour it replaced stays,
and the log says so truthfully.

- Ores: the session keeps Bind's rollback (`OreSession.Undo`) until activation has completed. If a later step fails,
  everything is restored: native generators, ore-map flags, bog-iron tries and the deposit list. After a failed reload,
  vanilla's `initWorldGen` runs again for its own generators. A failure in `AfterNativeInit` at startup now disables
  Earth's ores for the session (it used to log a warning and run half-adjusted). This happens before any chunk is
  generated.
- Story locations: every location is planned first (searches only, nothing changed), then all the moves are applied.
  A failure while planning leaves every vanilla position untouched. The world keeps them (`StoryPlaced` stays saved, so
  a later start doesn't move locations next to chunks that already exist); `/wgen story setpos` still moves one by hand.

## Alternatives

- *Keep running half-adjusted.* Nobody can predict the balance or the layout, and the log was wrong about it.
- *Throw and stop the server.* Against the rule that nothing throws at startup ([0002](0002-save-compatibility.md)).
- *Retry story placement on the next start.* By then chunks may exist where a location would move to.

## Implementation

`src/EarthOres.cs` (`Bind`, `Activate`, `AfterNativeWorldGenInit`, `AfterReload`, `OreSession.Undo`),
`src/StoryPlacer.cs` (`Place`, `Move`). There is no automated test: both need a game server, so a failure injected
after binding or during planning is still a manual check.

## History

- 2026-10-16: ore rollback found by review round 1, story placement by round 2; both fixed.

## Status before 15 November 2026

The status line as it stood before the 2.0.0 check of the decisions' status (EA-0092 D1):

Status: Implemented (0.14.1-pre, not committed)
