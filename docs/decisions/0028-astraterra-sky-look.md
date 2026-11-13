# 0028: Earth suggests its sky look to AstraTerra

Status: Implemented (Earth source, not released; AstraTerra local branch `19d9282`); offline checks pass, not played | Date: 2026-10-25 | Decided by: the user (2026-10-25)

## Context

AstraTerra's local branch (`feat/world-longitude-provider`) gained drawing settings while it was tuned in Earth
play-tests: a round sun, a telescope moon, their sizes, a dimmer Milky Way and a naked-eye star field. The user's own
`astraterra.json` holds the values chosen in play, but a new player gets AstraTerra's defaults, and those should stay
AstraTerra's original look for players without Earth, especially if any of this goes upstream.

The user's words (2026-10-25): "i want 'Your config' be default for players using Earth with Astra".

## Decision

In an Earth world, Earth suggests its look to AstraTerra on the client, by reflection only, like the sky longitude
([0010](0010-real-day-and-night.md)): Earth stays GPL-3.0 and references nothing of AstraTerra's AGPL-3.0 assembly.

- **Earth** (`src/Core/SkyLook.cs`, `src/AstraTerraLook.cs`, `src/Core/SettingSuggestionBinding.cs`) calls
  `AstraTerraModSystem.SuggestSetting(name, value)` once per world for each setting, and logs one line saying what
  AstraTerra took and what the player's own settings kept.
- **AstraTerra** (`Config/ConfigSuggestions.cs`) takes a suggestion only for a drawing setting the player has left at its
  default, keeps it for the session and never writes it to `astraterra.json`. A setting the player changes with a
  `.stars` command is theirs from then on. `AcceptMapSuggestions: false` turns suggestions off. World-changing and
  server settings (longitude sun, near-body light, `TelescopeSunDamage`) cannot be suggested.
- AstraTerra's star settings now default to its original look (`BrightStarEmphasis` 0, `StarLimitMagnitude` 7,
  `FaintStarBrightness` 1), so its sky without Earth is drawn as before.

Earth's look (the user's config on 25 October):

| Setting | Earth | AstraTerra default |
|---|---|---|
| `SunArt` | pixel | vanilla |
| `SunSizeDeg` | 3 | 4.4 |
| `MoonArt` | telescope | vanilla |
| `MoonSizeDeg` | 3 | 7 |
| `MilkyWayBrightness` | 0.3 | 1.0 |
| `BrightStarEmphasis` | 1 | 0 |
| `StarLimitMagnitude` | 5 | 7 |
| `FaintStarBrightness` | 0.25 | 1 |

AstraTerra without `SuggestSetting` (the 0.15.0 release) is left alone; Earth logs once.

## Verification

- AstraTerra suite 1283/1283, including `ConfigSuggestionsTests` (default only, player's choice kept, opt-out, saving
  writes the file's values, refused settings and values).
- Earth `tests/SkyLongitudeTests`: fakes for the binding, and the branch DLL end to end: every Earth setting is taken
  by a fresh config, saving writes the player's own `SunArt`, a player's `MoonSizeDeg` 5 is kept, the opt-out takes
  nothing, `TelescopeSunDamage` is refused. Earth compiles (Debug; no release zip built).
- **Not run in game.** Check in an Earth world with a fresh `astraterra.json`: the log line, the round sun and telescope
  moon, the star field; then a non-Earth world with the same file: AstraTerra's original look.

## Open

- Whether this goes upstream with the other AstraTerra changes, or Earth players get it from a separately published
  AstraTerra build, is not decided.
- The moon's pictures are queued again when the suggestion changes `MoonArt`; the first look through a telescope may
  still hitch if the suggestion comes late.
