# ActionFit Cookie Cleanup

`com.actionfit.cookie-cleanup` is a private, code-only CookieCleanup engine. It owns the schedule window, schema-versioned state, deterministic board placement, spray and round transitions, reward transactions, migration input, and restart recovery. The consuming project owns UI, prefabs, sprites, generated CSV/SO assets, unlock rules, analytics transport, Addressables, and inventory adapters.

## Install

After a separately approved repository and immutable tag exist, add the private Git UPM package to the consuming project:

```json
{
  "dependencies": {
    "com.actionfit.cookie-cleanup": "https://github.com/ActionFitGames/CookieCleanup.git#0.1.0"
  }
}
```

## Runtime boundary

- `CookieCleanupEngine` is the authoritative command and state surface.
- `CookieCleanupCatalog` pins objects, rounds, board sizes, spray values, and round/box reward snapshots.
- `CookieCleanupPlacementEngine` preserves the existing `System.Random`, candidate-order, Fisher-Yates, retry, rotation, and first-fit contract.
- `IContentStateStore` persists one complete runtime snapshot; critical transitions flush through `IFlushableContentStateStore`.
- `IContentRewardService` grants event-instance-scoped round and box transactions exactly once.
- `ActionFit.Time.IClock` supplies UTC for new events and an explicit UTC+09:00 service calendar.
- `ICookieCleanupLegacyLocalClock` only compares active pre-migration local ticks.

The first rollout is additive. Existing Cat Merge scripts remain compatibility facades, and all legacy keys and project assets remain available for rollback.

Runtime dependencies are pinned to `com.actionfit.content-core@0.2.0` and `com.actionfit.time@1.0.2`.
