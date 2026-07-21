# AI Guide: ActionFit Cookie Cleanup

## Package Identity

- Package ID: `com.actionfit.cookie-cleanup`
- Display name: ActionFit Cookie Cleanup
- Repository: `https://github.com/ActionFit-Editor/CookieCleanup.git`
- Repository visibility: Public
- Current package version at generation time: `0.2.0`
- Unity version: `6000.2`
- Runtime dependency: `com.actionfit.content-core@0.2.3`
- Runtime dependency: `com.actionfit.time@1.0.4`

## Project Router Registration

- `Packages/com.actionfit.cookie-cleanup/AI_GUIDE.md` - ActionFit Cookie Cleanup owns the reusable schedule, deterministic board placement, schema-versioned progress, legacy timer compatibility, and durable round/box reward recovery.

## Safe scope

Keep the runtime engine project-neutral. The package owns canonical CSV files under `Data/CSV/`, but Runtime must not reference `Assembly-CSharp`, Cat Merge generated table types, Addressables, analytics SDKs, UGUI, DOTween, project persistence APIs, prefabs, or presentation assets.

Consuming-project adapters may translate generated tables, `DataStore`, `TimeProvider`, unlock state, analytics, and `GameItemProvider` into package contracts.

## Invariants

- The package runtime JSON is authoritative after migration.
- `placementSeed`, `round`, and `foamMask` are validated and persisted as one snapshot.
- Boards are limited to 64 cells; out-of-range mask bits and invalid catalogs are rejected.
- Placement preserves input order, stable area ordering, X/Y/rotation candidate enumeration, `System.Random`, Fisher-Yates consumption, retry number, and first-fit behavior.
- An active event pins `CatalogVersion` and `BalanceRevision` and has a nonzero placement seed plus a stable event instance ID.
- New events use UTC ticks and an explicitly injected calendar. Cat Merge selects synchronized UTC plus `TimeZoneInfo.Utc` in server mode, or device-backed UTC plus `TimeZoneInfo.Local` in device mode. Imported active local ticks remain numeric local ticks on their legacy calendar until that event ends.
- Reward snapshots and `cookie_cleanup:{eventInstanceId}:round:{round}:{rewardKind}` transaction IDs are saved before grant.
- Grant confirmation is durable before pending reward state is cleared.
- Normal event end does not erase the shared durable reward ledger; every new event uses a new event instance ID.
- UI can observe state and invoke commands but cannot mutate authoritative schedule, round, spray, mask, or reward state directly.
- Legacy per-field keys and all project assets remain intact during the first rollout.

Canonical CSVs ship under `Data/CSV/`; generated Row/Table code and imported Table SOs are project outputs under `Assets/_Data/_CookieCleanup/`. Run package tests, package contract validation, isolated Unity validation, project compilation, AI docs validation, and a serialized diff audit before handoff. Repository creation, tagging, publishing, catalog append, and deployment require separate approval.
