using System;
using System.IO;
using NUnit.Framework;

namespace ActionFit.CookieCleanup.Tests
{
    public sealed class CookieCleanupStandaloneCatalogParityTests
    {
        [Test]
        public void CanonicalCsvFactory_DefaultAndRewardSegmentsPreserveReleasedBalance()
        {
            CookieCleanupCatalogCsvData csv = ReadCsv();
            CookieCleanupStandaloneCatalog defaultStandalone = CookieCleanupCatalogFactory.Create(csv);
            CookieCleanupStandaloneCatalog rewardStandalone = CookieCleanupCatalogFactory.Create(
                csv,
                CookieCleanupCatalogFactory.RewardSegment);
            CookieCleanupCatalog catalog = defaultStandalone.Catalog;

            Assert.That(catalog.CatalogVersion, Is.EqualTo(CookieCleanupCatalogFactory.DefaultCatalogVersion));
            Assert.That(catalog.BalanceRevision, Is.EqualTo(CookieCleanupCatalogFactory.DefaultBalanceRevision));
            Assert.That(rewardStandalone.Catalog.BalanceRevision, Is.EqualTo(CookieCleanupCatalogFactory.RewardBalanceRevision));
            Assert.That(defaultStandalone.SchedulePolicy.IsActiveDay(DayOfWeek.Monday), Is.True);
            Assert.That(defaultStandalone.SchedulePolicy.IsActiveDay(DayOfWeek.Tuesday), Is.False);
            Assert.That(catalog.InitialSprayCount, Is.EqualTo(10));
            Assert.That(catalog.PlacementMaxAttempts, Is.EqualTo(1000));
            Assert.That(catalog.RoundCount, Is.EqualTo(8));
            Assert.That(catalog.GetObject("cookie_cleanup_ob06").Width, Is.EqualTo(2));
            Assert.That(catalog.GetObject("cookie_cleanup_ob06").Height, Is.EqualTo(2));
            Assert.That(catalog.GetRound(1).BoardCellCount, Is.EqualTo(9));
            Assert.That(catalog.GetRound(1).RequiredProgress, Is.EqualTo(3));
            Assert.That(catalog.GetRound(8).Objects, Has.Count.EqualTo(3));
            Assert.That(catalog.GetOrderSprayReward(9), Is.EqualTo(10));
            Assert.That(rewardStandalone.Catalog.GetOrderSprayReward(9), Is.EqualTo(8));
            Assert.That(catalog.GetRound(4).RoundRewards[0].RewardId, Is.EqualTo("Dia"));
            Assert.That(catalog.GetRound(4).RoundRewards[0].Amount, Is.EqualTo(20));
            Assert.That(rewardStandalone.Catalog.GetRound(4).RoundRewards[0].Amount, Is.EqualTo(10));
            Assert.That(catalog.GetRound(4).BoxRewards[0].Amount, Is.EqualTo(100));
            Assert.That(rewardStandalone.Catalog.GetRound(4).BoxRewards[0].Amount, Is.EqualTo(70));
            Assert.That(catalog.GetRound(3).BoxRewards, Is.Empty);
        }

        [Test]
        public void CanonicalCsvFactory_UnsupportedSegmentFailsClosed()
        {
            Assert.Throws<ArgumentException>(() => CookieCleanupCatalogFactory.Create(ReadCsv(), "UnknownVariant"));
        }

        private static CookieCleanupCatalogCsvData ReadCsv()
        {
            return new CookieCleanupCatalogCsvData(
                Read("CookieCleanup_EventSettings.csv"),
                Read("CookieCleanup_Object.csv"),
                Read("CookieCleanup_Reward_Box.csv"),
                Read("CookieCleanup_Reward_Round.csv"),
                Read("CookieCleanup_Round_Settings.csv"),
                Read("CookieCleanup_Spray_Order.csv"));
        }

        private static string Read(string fileName)
        {
            return File.ReadAllText(Path.Combine(
                "Packages/com.actionfit.cookie-cleanup/Data/CSV",
                fileName));
        }
    }
}
