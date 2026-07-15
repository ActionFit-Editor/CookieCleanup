using System;
using System.Collections.Generic;
using ActionFit.Content;
using UnityEngine;

namespace ActionFit.CookieCleanup
{
    public enum CookieCleanupTimeBasis
    {
        LegacyLocalTicks = 0,
        UtcTicks = 1,
    }

    public enum CookieCleanupRewardKind
    {
        None = 0,
        Round = 1,
        Box = 2,
    }

    public sealed class CookieCleanupImportState
    {
        public bool EventStarted { get; set; }
        public bool PendingEnd { get; set; }
        public long EventEndTicks { get; set; }
        public int TimeSchemaVersion { get; set; } = CookieCleanupStateSerializer.CurrentTimeSchemaVersion;
        public CookieCleanupTimeBasis TimeBasis { get; set; } = CookieCleanupTimeBasis.LegacyLocalTicks;
        public int Round { get; set; } = CookieCleanupEngine.MinRound;
        public int CompletedRoundCount { get; set; }
        public int CollectedSpray { get; set; }
        public long FoamMask { get; set; }
        public int CollectedProgress { get; set; }
        public bool PendingStageEnd { get; set; }
        public bool TutorialDone { get; set; }
        public int PlacementSeed { get; set; }
        public long RoundStartTicks { get; set; }
        public int RoundSpraysUsed { get; set; }
        public int EventSpraysUsed { get; set; }
        public IReadOnlyList<int> ClaimedRoundRewards { get; set; } = Array.Empty<int>();
        public IReadOnlyList<int> ClaimedBoxRewards { get; set; } = Array.Empty<int>();
    }

    public sealed class CookieCleanupState
    {
        internal CookieCleanupState(CookieCleanupStateData data)
        {
            SchemaVersion = data.schemaVersion;
            CatalogVersion = data.catalogVersion ?? string.Empty;
            BalanceRevision = data.balanceRevision ?? string.Empty;
            EventInstanceId = data.eventInstanceId ?? string.Empty;
            EventStarted = data.eventStarted;
            PendingEnd = data.pendingEnd;
            EventEndTicks = data.eventEndTicks;
            TimeSchemaVersion = data.timeSchemaVersion;
            TimeBasis = (CookieCleanupTimeBasis)data.timeBasis;
            Round = data.round;
            CompletedRoundCount = data.completedRoundCount;
            CollectedSpray = data.collectedSpray;
            FoamMask = data.foamMask;
            CollectedProgress = data.collectedProgress;
            PendingStageEnd = data.pendingStageEnd;
            TutorialDone = data.tutorialDone;
            PlacementSeed = data.placementSeed;
            RoundStartTicks = data.roundStartTicks;
            RoundSpraysUsed = data.roundSpraysUsed;
            EventSpraysUsed = data.eventSpraysUsed;
            ClaimedRoundRewards = new List<int>(data.claimedRoundRewards);
            ClaimedBoxRewards = new List<int>(data.claimedBoxRewards);
            PendingRewardKind = (CookieCleanupRewardKind)data.pendingRewardKind;
            PendingRewardRound = data.pendingRewardRound;
            PendingTransactionId = data.pendingTransactionId ?? string.Empty;
        }

        public int SchemaVersion { get; }
        public string CatalogVersion { get; }
        public string BalanceRevision { get; }
        public string EventInstanceId { get; }
        public bool EventStarted { get; }
        public bool PendingEnd { get; }
        public long EventEndTicks { get; }
        public int TimeSchemaVersion { get; }
        public CookieCleanupTimeBasis TimeBasis { get; }
        public int Round { get; }
        public int CompletedRoundCount { get; }
        public int CollectedSpray { get; }
        public long FoamMask { get; }
        public int CollectedProgress { get; }
        public bool PendingStageEnd { get; }
        public bool TutorialDone { get; }
        public int PlacementSeed { get; }
        public long RoundStartTicks { get; }
        public int RoundSpraysUsed { get; }
        public int EventSpraysUsed { get; }
        public IReadOnlyList<int> ClaimedRoundRewards { get; }
        public IReadOnlyList<int> ClaimedBoxRewards { get; }
        public CookieCleanupRewardKind PendingRewardKind { get; }
        public int PendingRewardRound { get; }
        public string PendingTransactionId { get; }
    }

    [Serializable]
    internal sealed class CookieCleanupStateData
    {
        public int schemaVersion = CookieCleanupStateSerializer.CurrentSchemaVersion;
        public string catalogVersion = string.Empty;
        public string balanceRevision = string.Empty;
        public string eventInstanceId = string.Empty;
        public bool eventStarted;
        public bool pendingEnd;
        public long eventEndTicks;
        public int timeSchemaVersion = CookieCleanupStateSerializer.CurrentTimeSchemaVersion;
        public int timeBasis = (int)CookieCleanupTimeBasis.UtcTicks;
        public int round = CookieCleanupEngine.MinRound;
        public int completedRoundCount;
        public int collectedSpray;
        public long foamMask;
        public int collectedProgress;
        public bool pendingStageEnd;
        public bool tutorialDone;
        public int placementSeed;
        public long roundStartTicks;
        public int roundSpraysUsed;
        public int eventSpraysUsed;
        public List<int> claimedRoundRewards = new List<int>();
        public List<int> claimedBoxRewards = new List<int>();
        public int pendingRewardKind;
        public int pendingRewardRound;
        public string pendingTransactionId = string.Empty;
        public List<CookieCleanupRewardData> pendingRewards = new List<CookieCleanupRewardData>();
    }

    [Serializable]
    internal sealed class CookieCleanupRewardData
    {
        public string rewardId = string.Empty;
        public long amount;
    }

    internal static class CookieCleanupStateSerializer
    {
        internal const int CurrentSchemaVersion = 1;
        internal const int CurrentTimeSchemaVersion = 1;

        internal static CookieCleanupStateData CreateDefault() => new CookieCleanupStateData();

        internal static string Serialize(CookieCleanupStateData state)
        {
            Validate(state);
            return JsonUtility.ToJson(state);
        }

        internal static CookieCleanupStateData Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new FormatException("CookieCleanup state JSON is empty.");
            CookieCleanupStateData state;
            try
            {
                state = JsonUtility.FromJson<CookieCleanupStateData>(json);
            }
            catch (ArgumentException exception)
            {
                throw new FormatException("CookieCleanup state JSON is malformed.", exception);
            }
            Validate(state);
            return state;
        }

        internal static void Validate(CookieCleanupStateData state)
        {
            if (state == null) throw new FormatException("CookieCleanup state is null.");
            if (state.schemaVersion != CurrentSchemaVersion)
                throw new NotSupportedException($"Unsupported CookieCleanup state schema {state.schemaVersion}.");
            if (state.timeSchemaVersion != CurrentTimeSchemaVersion)
                throw new NotSupportedException($"Unsupported CookieCleanup time schema {state.timeSchemaVersion}.");
            if (!Enum.IsDefined(typeof(CookieCleanupTimeBasis), state.timeBasis))
                throw new FormatException("CookieCleanup time basis is invalid.");
            if (!Enum.IsDefined(typeof(CookieCleanupRewardKind), state.pendingRewardKind))
                throw new FormatException("CookieCleanup pending reward kind is invalid.");
            if (state.eventEndTicks < 0 || state.roundStartTicks < 0
                || state.eventEndTicks > DateTime.MaxValue.Ticks || state.roundStartTicks > DateTime.MaxValue.Ticks)
                throw new FormatException("CookieCleanup timer ticks are outside the DateTime range.");
            if (state.round < CookieCleanupEngine.MinRound
                || state.completedRoundCount < 0
                || state.collectedSpray < 0
                || state.collectedProgress < 0
                || state.roundSpraysUsed < 0
                || state.eventSpraysUsed < 0)
                throw new FormatException("CookieCleanup progress values must be non-negative.");
            if (state.eventStarted && (state.eventEndTicks <= 0
                || state.placementSeed == 0
                || string.IsNullOrWhiteSpace(state.eventInstanceId)
                || string.IsNullOrWhiteSpace(state.catalogVersion)
                || string.IsNullOrWhiteSpace(state.balanceRevision)))
                throw new FormatException("An active CookieCleanup event requires timer, seed, instance, and catalog pins.");
            if (state.pendingStageEnd && state.completedRoundCount <= 0)
                throw new FormatException("CookieCleanup pending stage end requires a completed round.");

            state.claimedRoundRewards ??= new List<int>();
            state.claimedBoxRewards ??= new List<int>();
            ValidateRoundList(state.claimedRoundRewards, "round reward");
            ValidateRoundList(state.claimedBoxRewards, "box reward");

            state.pendingRewards ??= new List<CookieCleanupRewardData>();
            CookieCleanupRewardKind rewardKind = (CookieCleanupRewardKind)state.pendingRewardKind;
            bool hasPending = rewardKind != CookieCleanupRewardKind.None;
            if (hasPending && (state.pendingRewardRound < CookieCleanupEngine.MinRound
                || string.IsNullOrWhiteSpace(state.pendingTransactionId)
                || state.pendingRewards.Count == 0))
                throw new FormatException("CookieCleanup pending reward snapshot is incomplete.");
            if (!hasPending && (state.pendingRewardRound != 0
                || !string.IsNullOrWhiteSpace(state.pendingTransactionId)
                || state.pendingRewards.Count != 0))
                throw new FormatException("CookieCleanup has orphan pending reward data.");
            foreach (CookieCleanupRewardData reward in state.pendingRewards)
            {
                if (reward == null || string.IsNullOrWhiteSpace(reward.rewardId) || reward.amount <= 0)
                    throw new FormatException("CookieCleanup pending reward snapshot is invalid.");
            }
        }

        internal static List<CookieCleanupRewardData> ToData(IReadOnlyList<ContentReward> rewards)
        {
            var result = new List<CookieCleanupRewardData>(rewards.Count);
            for (int index = 0; index < rewards.Count; index++)
            {
                ContentReward reward = rewards[index];
                result.Add(new CookieCleanupRewardData { rewardId = reward.RewardId, amount = reward.Amount });
            }
            return result;
        }

        internal static IReadOnlyList<ContentReward> ToRewards(IReadOnlyList<CookieCleanupRewardData> rewards)
        {
            var result = new List<ContentReward>(rewards.Count);
            for (int index = 0; index < rewards.Count; index++)
            {
                CookieCleanupRewardData reward = rewards[index];
                result.Add(new ContentReward(reward.rewardId, reward.amount));
            }
            return result;
        }

        private static void ValidateRoundList(IReadOnlyList<int> rounds, string label)
        {
            var seen = new HashSet<int>();
            for (int index = 0; index < rounds.Count; index++)
            {
                if (rounds[index] < CookieCleanupEngine.MinRound || !seen.Add(rounds[index]))
                    throw new FormatException($"CookieCleanup claimed {label} list is invalid.");
            }
        }
    }
}
