using System;

namespace ActionFit.CookieCleanup
{
    public interface ICookieCleanupLegacyLocalClock
    {
        DateTime Now { get; }
    }

    public interface ICookieCleanupSeedSource
    {
        int GenerateNonZeroSeed();
    }

    public interface ICookieCleanupAccessPolicy
    {
        bool IsAccessAllowed { get; }
    }

    public interface ICookieCleanupSchedulePolicy
    {
        bool IsEnabled { get; }
        bool IsActiveDay(DayOfWeek dayOfWeek);
    }

    public interface ICookieCleanupCatalogResolver
    {
        CookieCleanupCatalog Current { get; }
        bool TryResolve(string catalogVersion, string balanceRevision, out CookieCleanupCatalog catalog);
    }

    public interface ICookieCleanupAnalyticsSink
    {
        void EventStarted(int placementSeed, int remainingSeconds);
        void RoundCleared(int placementSeed, int round, int spraysUsed, int durationSeconds);
        void RewardClaimed(int placementSeed, int round, CookieCleanupRewardKind kind);
        void EventEnded(int placementSeed, int completedRounds, int totalSpraysUsed);
    }

    public sealed class AllowCookieCleanupAccessPolicy : ICookieCleanupAccessPolicy
    {
        public bool IsAccessAllowed => true;
    }

    public sealed class NullCookieCleanupAnalyticsSink : ICookieCleanupAnalyticsSink
    {
        public void EventStarted(int placementSeed, int remainingSeconds) { }
        public void RoundCleared(int placementSeed, int round, int spraysUsed, int durationSeconds) { }
        public void RewardClaimed(int placementSeed, int round, CookieCleanupRewardKind kind) { }
        public void EventEnded(int placementSeed, int completedRounds, int totalSpraysUsed) { }
    }
}
