# 0003: Earth stays geography; built places belong to Earth Ruins

Status: Agreed, implemented (0.14 pre-releases) | Date: 2026-10-14 | Decided by: the user

The suite register holds this decision: `overview/DECISIONS.md` D06 (superseding D05).

## Decision

Earth Map owns geography and generic terrain services (the terrain model, the companion terrain API for pads and
expansions). Authored cities, rooms, ruins and the Room Lab live in the Earth Ruins mod (`../Earth ruins`), which places
them through that API.

## Consequences

- Earth has no city code or `UndergroundCities` setting. `ProtoMember(54)` was used by 0.14 pre-releases and must not
  be reused.
- New terrain features in Earth are generic (any companion can use them); content-specific placement goes to the
  companion.

## History

- 2026-10-04: cities moved into Earth (D05).
- 2026-10-14: moved back out to Earth Ruins (D06).
- 2026-10-16: written down as this record.
- 2026-10-21: widened by [0017](0017-weather.md): Earth also owns the climate and weather of Earth worlds (suite D23); built content still goes to companions.
