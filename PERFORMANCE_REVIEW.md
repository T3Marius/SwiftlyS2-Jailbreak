# Performance and correctness review

This pass reviewed the solution structure and searched across the core, contracts,
and three bundled modules for tick work, roster scans, allocations, database
access, resource loading, and delayed callbacks. Focused changes preserve the
existing public contracts and the local edits present before this review.
This is a source review, not a measured server performance audit.

## Implemented

| Area | Change | Expected benefit |
| --- | --- | --- |
| Player lookup | Use the existing identity dictionary for cuffs, drawing, guard queue, and Special Day freeze lookups; validate identity and synchronize the selected player | Avoid full-roster synchronization and snapshot allocation for those individual lookups |
| Roster synchronization | Guard against reentrant synchronization from role notification handlers | Avoid nested full synchronizations while normalizing roles |
| Guard gun persistence | Cache absent/invalid saved loadouts as well as existing ones | One read per player cache lifetime for missing loadouts; saves and disconnect invalidation still work |
| Beacons | Precompute circle directions; stop rewriting completed, stationary, single-color ping beams | Remove repeated trigonometry and unnecessary entity/network updates; keep animated and rainbow effects |
| Special Day weapons | Reuse immutable Taser/Chicken weapon sets; cache OITC weapon resolution by configured gun | Avoid recreating sets and resolving weapon class names on repeated acquire checks |
| Last Request cleanup | Take one player snapshot and use membership checks for modified players | Avoid a full roster synchronization for each modified player |
| Bunnyhop replication | Resolve both convars once per setting change | Avoid repeated convar lookup for every player |
| Currency HUD | Validate delayed callbacks against player session and registration state; resolve the localizer once; reject blank currencies; isolate rendering failures | Avoid callbacks acting on recycled slots, null-player exceptions, and HUD failures interrupting balance operations |
| Pawn changes | Revalidate delayed color/weapon callbacks; skip unchanged colors; send the render-mode update instead of duplicate render updates | Reduce redundant network notifications and invalid pawn access |
| Models/resources | Exclude blank and duplicate precache entries; reject blank model changes; reset role tint when restoring team models | Avoid invalid resource requests and retained role colors on replacement models |
| Team changes | Check session identity in delayed ratio enforcement | Avoid applying an old callback to a new occupant of the slot |

## Validation

- Baseline Debug build passed before edits.
- Release build of all five projects passed with zero warnings and errors.
- Seven executable regression checks passed for database cache behavior, including
  invalidation and failure retry. See `Tests/RegressionChecks/README.md`.
- `git diff --check` passed.

## Findings that need deployment context or a separate design change

- **Model assets are external.** The repository contains model configuration paths,
  not the mesh/texture sources or compiled model assets. Polygon counts, LODs,
  texture memory, and material costs cannot be verified here. The default prisoner
  path containing `p_variantd_e/p_variant_e.vmdl` looks inconsistent with the other
  entries; verify against the installed asset package before renaming it.
- **Database work is synchronous.** Cache misses, saves, shop purchases, and
  leaderboard reads can block gameplay callbacks. Moving these to background work
  needs a deliberate ordering/rollback design and returning native player/entity
  operations to the game thread. Measure database latency before this refactor.
- **Persistence uses update-then-insert in several places.** Provider-specific
  affected-row behavior and concurrent writers should be verified against the
  deployed database. A provider-aware atomic upsert would require integration tests.
- **Shop callbacks are external code.** Equip/unequip, persistence rollback, and
  event subscribers need explicit transaction and exception semantics before a
  broader rewrite. HUD rendering failures are isolated in this pass, but arbitrary
  other shop subscribers can still throw.
- **Full-roster getters still synchronize.** Their public snapshot semantics were
  retained because callers can change teams or roles. An event-driven roster could
  reduce more work, but needs coverage for external modules and hot reloads.
- **Some effects still key by Steam ID.** Beacon and laser paths can conflate bots
  with Steam ID zero. The new indexed lookups preserve session identities in the
  systems already using them; migrating remaining effect ownership requires
  reviewing cleanup callers together.
- **Schema migration catches are broad.** Stats and warden database migrations
  can hide errors other than existing columns. Fix with provider-aware metadata or
  exception handling once supported database providers are confirmed.

## Server checks before deployment

Exercise joins/disconnects and slot reuse, hot reload, guard queue promotion,
cuffs/grabbing, drawing, all Special Days, and Last Request cleanup. Confirm HUD
behavior when its service disappears, restores, or rejects an update. Test missing
and saved gun loadouts across reconnects. Confirm role tint resets with custom team
models and verify all resource paths against installed packages.

For performance, compare the same map/player count before and after: server tick
duration, allocation/GC rate, database query count and latency, and network/entity
update volume with pings, lasers, drawing, and duels active. No percentage speedup
is claimed without those measurements.
