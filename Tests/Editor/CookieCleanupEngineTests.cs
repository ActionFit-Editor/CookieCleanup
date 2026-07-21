using System;
using System.Collections.Generic;
using System.Linq;
using ActionFit.Content;
using ActionFit.Time;
using NUnit.Framework;

namespace ActionFit.CookieCleanup.Tests
{
    public sealed class CookieCleanupEngineTests
    {
        [Test]
        public void Placement_MatchesGoldenLegacyFixtures()
        {
            CookieCleanupCatalog catalog = CreateCurrentShapeCatalog();

            Assert.That(
                Describe(CookieCleanupPlacementEngine.Place(catalog, 123456, 1)),
                Is.EqualTo(new[]
                {
                    "ob03@0,2,-90[6,7]",
                    "ob03@0,1,-90[3,4]",
                    "ob03@0,0,-90[0,1]",
                }));
            Assert.That(
                Describe(CookieCleanupPlacementEngine.Place(catalog, 8675309, 4)),
                Is.EqualTo(new[]
                {
                    "ob06@1,2,0[9,10,13,14]",
                    "ob05@1,1,-90[5,6,7]",
                    "ob02@0,1,0[4]",
                }));
            Assert.That(
                Describe(CookieCleanupPlacementEngine.Place(catalog, 42, 8)),
                Is.EqualTo(new[]
                {
                    "ob05@0,3,-90[15,16,17]",
                    "ob04@3,1,0[8,13]",
                    "ob04@0,2,-90[10,11]",
                    "ob02@3,0,0[3]",
                    "ob02@0,0,0[0]",
                }));
        }

        [Test]
        public void Catalog_RejectsBoardThatCannotFitLongMask()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new CookieCleanupRoundDefinition(
                    1,
                    9,
                    8,
                    new[] { new CookieCleanupObjectRequirement("ob01", 1) },
                    Array.Empty<ContentReward>(),
                    Array.Empty<ContentReward>()));
        }

        [Test]
        public void Import_PreservesLegacyTimerSeedRoundAndFoamAsOneSnapshot()
        {
            var store = new MemoryStateStore();
            var clock = new ManualClock(new DateTime(2026, 7, 12, 15, 30, 0, DateTimeKind.Utc));
            DateTime legacyNow = new DateTime(2026, 7, 13, 0, 30, 0, DateTimeKind.Unspecified);
            long legacyEnd = legacyNow.Date.AddDays(1).Ticks;
            CookieCleanupEngine engine = CreateEngine(store, clock, legacyNow);

            Assert.That(engine.ImportStateIfEmpty(new CookieCleanupImportState
            {
                EventStarted = true,
                EventEndTicks = legacyEnd,
                TimeBasis = CookieCleanupTimeBasis.LegacyLocalTicks,
                Round = 2,
                CompletedRoundCount = 1,
                CollectedSpray = 17,
                FoamMask = (1L << 0) | (1L << 4),
                CollectedProgress = 1,
                PlacementSeed = 424242,
                RoundStartTicks = legacyNow.AddMinutes(-3).Ticks,
                RoundSpraysUsed = 2,
                EventSpraysUsed = 7,
                ClaimedRoundRewards = new[] { 1 },
            }), Is.True);

            CookieCleanupState state = engine.State;
            Assert.That(state.EventEndTicks, Is.EqualTo(legacyEnd));
            Assert.That(state.TimeBasis, Is.EqualTo(CookieCleanupTimeBasis.LegacyLocalTicks));
            Assert.That(state.EventInstanceId, Is.EqualTo($"legacy-{424242u:x8}-{legacyEnd:x16}"));
            Assert.That(state.Round, Is.EqualTo(2));
            Assert.That(state.FoamMask, Is.EqualTo((1L << 0) | (1L << 4)));
            Assert.That(state.PlacementSeed, Is.EqualTo(424242));

            CookieCleanupEngine restarted = CreateEngine(store, clock, legacyNow);
            restarted.Restore();
            Assert.That(restarted.State.FoamMask, Is.EqualTo(state.FoamMask));
            Assert.That(restarted.State.EventInstanceId, Is.EqualTo(state.EventInstanceId));
        }

        [Test]
        public void Import_RejectsInvalidLegacyValuesWithoutPersistingOrNormalizingThem()
        {
            var store = new MemoryStateStore();
            var clock = new ManualClock(new DateTime(2026, 7, 12, 15, 30, 0, DateTimeKind.Utc));
            CookieCleanupEngine engine = CreateEngine(store, clock, new DateTime(2026, 7, 13, 0, 30, 0));

            Assert.Throws<FormatException>(() => engine.ImportStateIfEmpty(new CookieCleanupImportState
            {
                Round = 2,
                CollectedSpray = -1,
            }));
            Assert.Throws<FormatException>(() => engine.ImportStateIfEmpty(new CookieCleanupImportState
            {
                ClaimedRoundRewards = new[] { 1, 1 },
            }));

            Assert.That(store.TryLoad("tests/cookie-cleanup", out _), Is.False);
            Assert.That(engine.State.Round, Is.EqualTo(CookieCleanupEngine.MinRound));
            Assert.That(engine.State.CollectedSpray, Is.Zero);
        }

        [Test]
        public void ResetGameplay_RejectsActiveEventSoRewardReceiptsCannotBeCleared()
        {
            var store = new MemoryStateStore();
            var clock = new ManualClock(new DateTime(2026, 7, 12, 15, 30, 0, DateTimeKind.Utc));
            CookieCleanupEngine engine = CreateEngine(
                store,
                clock,
                new DateTime(2026, 7, 13, 0, 30, 0),
                new FixedSeedSource(77));
            Assert.That(engine.TryStartEvent(), Is.True);

            Assert.Throws<InvalidOperationException>(() => engine.ResetGameplay());
            Assert.That(engine.State.EventStarted, Is.True);
            Assert.That(engine.State.PlacementSeed, Is.EqualTo(77));
        }

        [Test]
        public void NewEvent_UsesUtcTicksAndExplicitInjectedCalendar()
        {
            var store = new MemoryStateStore();
            var clock = new ManualClock(new DateTime(2026, 7, 12, 15, 30, 0, DateTimeKind.Utc));
            CookieCleanupEngine engine = CreateEngine(
                store,
                clock,
                new DateTime(1999, 1, 1),
                new FixedSeedSource(77));

            Assert.That(engine.TryStartEvent(), Is.True);
            Assert.That(engine.State.TimeBasis, Is.EqualTo(CookieCleanupTimeBasis.UtcTicks));
            Assert.That(engine.State.EventEndTicks,
                Is.EqualTo(new DateTime(2026, 7, 13, 15, 0, 0, DateTimeKind.Utc).Ticks));
            Assert.That(engine.State.PlacementSeed, Is.EqualTo(77));
            Assert.That(engine.CollectedSpray, Is.EqualTo(10));
            Assert.That(engine.EventRemainingTime, Is.EqualTo(TimeSpan.FromHours(23.5)));
        }

        [Test]
        public void UtcCalendar_UsesUtcWeekdayAndMidnight()
        {
            var store = new MemoryStateStore();
            var clock = new ManualClock(new DateTime(2026, 7, 12, 15, 30, 0, DateTimeKind.Utc));
            CookieCleanupEngine engine = CreateEngine(
                store,
                clock,
                new DateTime(1999, 1, 1),
                new FixedSeedSource(77),
                calendarTimeZone: TimeZoneInfo.Utc);

            Assert.That(engine.IsEventDay, Is.False);
            Assert.That(engine.TryStartEvent(), Is.False);

            clock.SetUtcNow(new DateTime(2026, 7, 13, 0, 30, 0, DateTimeKind.Utc));

            Assert.That(engine.IsEventDay, Is.True);
            Assert.That(engine.TryStartEvent(), Is.True);
            Assert.That(engine.State.EventEndTicks,
                Is.EqualTo(new DateTime(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc).Ticks));
            Assert.That(engine.EventRemainingTime, Is.EqualTo(TimeSpan.FromHours(23.5)));
        }

        [Test]
        public void FoamClear_SpendsAndPersistsMaskAtomically()
        {
            var store = new MemoryStateStore();
            var clock = new ManualClock(new DateTime(2026, 7, 12, 15, 30, 0, DateTimeKind.Utc));
            CookieCleanupEngine engine = CreateEngine(store, clock, new DateTime(2026, 7, 13, 0, 30, 0), new FixedSeedSource(77));
            Assert.That(engine.TryStartEvent(), Is.True);

            Assert.That(engine.TryClearFoamCell(4), Is.True);
            Assert.That(engine.CollectedSpray, Is.EqualTo(9));
            Assert.That(engine.FoamMask, Is.EqualTo(1L << 4));
            Assert.That(store.FlushCount, Is.GreaterThan(0));
            Assert.That(engine.TryClearFoamCell(4), Is.False);
            Assert.That(engine.TryClearFoamCell(9), Is.False);
        }

        [Test]
        public void Reward_UsesEventScopedStableTransactionAndRestartBlocksDuplicate()
        {
            var store = new MemoryStateStore();
            var rewards = new MemoryRewardService();
            var clock = new ManualClock(new DateTime(2026, 7, 12, 15, 30, 0, DateTimeKind.Utc));
            CookieCleanupEngine engine = CreateEngine(store, clock, new DateTime(2026, 7, 13, 0, 30, 0), new FixedSeedSource(77), rewards);
            Assert.That(engine.TryStartEvent(), Is.True);
            Assert.That(engine.CompleteRound(), Is.True);

            Assert.That(engine.ClaimRoundReward(1), Is.True);
            string expected = $"cookie_cleanup:{engine.State.EventInstanceId}:round:1:round";
            Assert.That(rewards.LastTransactionId, Is.EqualTo(expected));
            Assert.That(rewards.GrantCount, Is.EqualTo(1));

            CookieCleanupEngine restarted = CreateEngine(store, clock, new DateTime(2026, 7, 13, 0, 30, 0), new FixedSeedSource(999), rewards);
            restarted.Restore();
            Assert.That(restarted.IsRoundRewardClaimed(1), Is.True);
            Assert.That(restarted.ClaimRoundReward(1), Is.False);
            Assert.That(rewards.GrantCount, Is.EqualTo(1));
        }

        private static CookieCleanupEngine CreateEngine(
            MemoryStateStore store,
            IClock utcClock,
            DateTime legacyNow,
            ICookieCleanupSeedSource seedSource = null,
            IContentRewardService rewardService = null,
            TimeZoneInfo calendarTimeZone = null)
        {
            CookieCleanupCatalog catalog = CreateCurrentShapeCatalog();
            return new CookieCleanupEngine(
                store,
                rewardService ?? new MemoryRewardService(),
                new FixedCatalogResolver(catalog),
                utcClock,
                calendarTimeZone ?? TimeZoneInfo.CreateCustomTimeZone(
                    "CookieCleanupTests+09",
                    TimeSpan.FromHours(9),
                    "CookieCleanupTests+09",
                    "CookieCleanupTests+09"),
                new FixedLegacyClock(legacyNow),
                seedSource ?? new FixedSeedSource(123),
                "tests/cookie-cleanup",
                new AllowCookieCleanupAccessPolicy(),
                new MondaySchedule());
        }

        private static CookieCleanupCatalog CreateCurrentShapeCatalog()
        {
            var objects = new[]
            {
                new CookieCleanupObjectDefinition("ob01", 1, 1),
                new CookieCleanupObjectDefinition("ob02", 1, 1),
                new CookieCleanupObjectDefinition("ob03", 1, 2),
                new CookieCleanupObjectDefinition("ob04", 1, 2),
                new CookieCleanupObjectDefinition("ob05", 1, 3),
                new CookieCleanupObjectDefinition("ob06", 2, 2),
            };
            IReadOnlyList<ContentReward> roundReward = new[] { new ContentReward("Energy", 30) };
            IReadOnlyList<ContentReward> noRewards = Array.Empty<ContentReward>();
            var rounds = new[]
            {
                Round(1, 3, 3, Req("ob03", 3), roundReward, noRewards),
                Round(2, 3, 3, Req("ob01", 1), Req("ob04", 1), Req("ob05", 1)),
                Round(3, 4, 4, Req("ob03", 2), Req("ob06", 1)),
                Round(4, 4, 4, Req("ob02", 1), Req("ob05", 1), Req("ob06", 1)),
                Round(5, 4, 4, Req("ob03", 1), Req("ob05", 2), Req("ob06", 1)),
                Round(6, 5, 5, Req("ob04", 2), Req("ob05", 1), Req("ob06", 1)),
                Round(7, 5, 5, Req("ob01", 1), Req("ob03", 2), Req("ob05", 2)),
                Round(8, 5, 5, Req("ob02", 2), Req("ob04", 2), Req("ob05", 1)),
            };
            return new CookieCleanupCatalog(
                "test-v1",
                "balance-v1",
                10,
                1000,
                objects,
                rounds,
                new[] { new KeyValuePair<int, int>(5, 1) });
        }

        private static CookieCleanupRoundDefinition Round(
            int round,
            int width,
            int height,
            params CookieCleanupObjectRequirement[] requirements)
        {
            return Round(round, width, height, requirements, Array.Empty<ContentReward>(), Array.Empty<ContentReward>());
        }

        private static CookieCleanupRoundDefinition Round(
            int round,
            int width,
            int height,
            CookieCleanupObjectRequirement requirement,
            IReadOnlyList<ContentReward> roundRewards,
            IReadOnlyList<ContentReward> boxRewards)
        {
            return Round(round, width, height, new[] { requirement }, roundRewards, boxRewards);
        }

        private static CookieCleanupRoundDefinition Round(
            int round,
            int width,
            int height,
            IReadOnlyList<CookieCleanupObjectRequirement> requirements,
            IReadOnlyList<ContentReward> roundRewards,
            IReadOnlyList<ContentReward> boxRewards)
        {
            return new CookieCleanupRoundDefinition(round, width, height, requirements, roundRewards, boxRewards);
        }

        private static CookieCleanupObjectRequirement Req(string objectId, int count) =>
            new CookieCleanupObjectRequirement(objectId, count);

        private static string[] Describe(IReadOnlyList<CookieCleanupPlacement> placements)
        {
            return placements.Select(value =>
                $"{value.ObjectId}@{value.AnchorX},{value.AnchorY},{value.RotationDegrees}[{string.Join(",", value.OccupiedCellIndexes)}]")
                .ToArray();
        }

        private sealed class MemoryStateStore : IContentStateStore, IFlushableContentStateStore
        {
            private string _json;
            public int FlushCount { get; private set; }
            public bool TryLoad(string contentId, out string json) { json = _json; return json != null; }
            public void Save(string contentId, string json) { _json = json; }
            public void Delete(string contentId) { _json = null; }
            public void Flush() { FlushCount++; }
        }

        private sealed class MemoryRewardService : IContentRewardService
        {
            private readonly HashSet<string> _granted = new HashSet<string>(StringComparer.Ordinal);
            public bool IsAvailable => true;
            public string LastTransactionId { get; private set; }
            public int GrantCount { get; private set; }
            public bool HasGranted(string transactionId) => _granted.Contains(transactionId);
            public bool GrantOnce(string transactionId, IReadOnlyList<ContentReward> rewards)
            {
                LastTransactionId = transactionId;
                if (!_granted.Add(transactionId)) return false;
                GrantCount++;
                return true;
            }
        }

        private sealed class FixedCatalogResolver : ICookieCleanupCatalogResolver
        {
            public FixedCatalogResolver(CookieCleanupCatalog catalog) { Current = catalog; }
            public CookieCleanupCatalog Current { get; }
            public bool TryResolve(string catalogVersion, string balanceRevision, out CookieCleanupCatalog catalog)
            {
                catalog = string.Equals(catalogVersion, Current.CatalogVersion, StringComparison.Ordinal)
                    && string.Equals(balanceRevision, Current.BalanceRevision, StringComparison.Ordinal)
                    ? Current
                    : null;
                return catalog != null;
            }
        }

        private sealed class FixedLegacyClock : ICookieCleanupLegacyLocalClock
        {
            public FixedLegacyClock(DateTime now) { Now = now; }
            public DateTime Now { get; }
        }

        private sealed class FixedSeedSource : ICookieCleanupSeedSource
        {
            private readonly int _seed;
            public FixedSeedSource(int seed) { _seed = seed; }
            public int GenerateNonZeroSeed() => _seed;
        }

        private sealed class MondaySchedule : ICookieCleanupSchedulePolicy
        {
            public bool IsEnabled => true;
            public bool IsActiveDay(DayOfWeek dayOfWeek) => dayOfWeek == DayOfWeek.Monday;
        }
    }
}
