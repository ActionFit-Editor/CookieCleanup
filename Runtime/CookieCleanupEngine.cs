using System;
using System.Collections.Generic;
using ActionFit.Content;
using ActionFit.Time;

namespace ActionFit.CookieCleanup
{
    public sealed class CookieCleanupEngine
    {
        public const int MinRound = 1;
        public const int MaxBoardCells = 64;

        private readonly IContentStateStore _stateStore;
        private readonly IContentRewardService _rewardService;
        private readonly ICookieCleanupCatalogResolver _catalogResolver;
        private readonly IClock _utcClock;
        private readonly TimeZoneInfo _calendarTimeZone;
        private readonly ICookieCleanupLegacyLocalClock _legacyLocalClock;
        private readonly ICookieCleanupSeedSource _seedSource;
        private readonly ICookieCleanupAccessPolicy _accessPolicy;
        private readonly ICookieCleanupSchedulePolicy _schedulePolicy;
        private readonly ICookieCleanupAnalyticsSink _analytics;
        private readonly string _contentId;

        private CookieCleanupStateData _state = CookieCleanupStateSerializer.CreateDefault();
        private CookieCleanupCatalog _catalog;

        public CookieCleanupEngine(
            IContentStateStore stateStore,
            IContentRewardService rewardService,
            ICookieCleanupCatalogResolver catalogResolver,
            IClock utcClock,
            TimeZoneInfo calendarTimeZone,
            ICookieCleanupLegacyLocalClock legacyLocalClock,
            ICookieCleanupSeedSource seedSource,
            string contentId,
            ICookieCleanupAccessPolicy accessPolicy,
            ICookieCleanupSchedulePolicy schedulePolicy,
            ICookieCleanupAnalyticsSink analytics = null)
        {
            _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
            _rewardService = rewardService ?? throw new ArgumentNullException(nameof(rewardService));
            _catalogResolver = catalogResolver ?? throw new ArgumentNullException(nameof(catalogResolver));
            _utcClock = utcClock ?? throw new ArgumentNullException(nameof(utcClock));
            _calendarTimeZone = calendarTimeZone ?? throw new ArgumentNullException(nameof(calendarTimeZone));
            _legacyLocalClock = legacyLocalClock ?? throw new ArgumentNullException(nameof(legacyLocalClock));
            _seedSource = seedSource ?? throw new ArgumentNullException(nameof(seedSource));
            _contentId = string.IsNullOrWhiteSpace(contentId)
                ? throw new ArgumentException("Content ID is required.", nameof(contentId))
                : contentId;
            _accessPolicy = accessPolicy ?? throw new ArgumentNullException(nameof(accessPolicy));
            _schedulePolicy = schedulePolicy ?? throw new ArgumentNullException(nameof(schedulePolicy));
            _analytics = analytics ?? new NullCookieCleanupAnalyticsSink();
            _catalog = _catalogResolver.Current
                ?? throw new InvalidOperationException("Current CookieCleanup catalog is unavailable.");
        }

        public event Action<CookieCleanupState> StateChanged;

        public CookieCleanupState State => new CookieCleanupState(_state);
        public CookieCleanupCatalog Catalog => ResolveCatalog();
        public bool IsEventStarted => _state.eventStarted;
        public bool PendingEnd => _state.pendingEnd;
        public bool IsEventDay => _schedulePolicy.IsEnabled && _schedulePolicy.IsActiveDay(ServiceNow.DayOfWeek);
        public bool IsEventActive => _accessPolicy.IsAccessAllowed
            && _schedulePolicy.IsEnabled
            && EventRemainingTime > TimeSpan.Zero;
        public bool HasValidTimeMetadata => _state.timeSchemaVersion == CookieCleanupStateSerializer.CurrentTimeSchemaVersion
            && Enum.IsDefined(typeof(CookieCleanupTimeBasis), _state.timeBasis);
        public long EventEndTicks => _state.eventEndTicks;
        public TimeSpan EventRemainingTime => GetRemaining(_state.eventEndTicks);
        public TimeSpan ExpectedRemainingTime
        {
            get
            {
                TimeSpan remaining = EventRemainingTime;
                if (remaining > TimeSpan.Zero) return remaining;
                return TryGetActiveWindowEndTicks(TimeBasis, out long endTicks)
                    ? GetRemaining(endTicks)
                    : TimeSpan.Zero;
            }
        }

        public int CurrentRound => _state.round;
        public int CompletedRoundCount => _state.completedRoundCount;
        public int CollectedSpray => _state.collectedSpray;
        public long FoamMask => _state.foamMask;
        public int CollectedProgress => _state.collectedProgress;
        public bool PendingStageEnd => _state.pendingStageEnd;
        public bool TutorialDone => _state.tutorialDone;
        public int PlacementSeed => _state.placementSeed;
        public int RoundCount => ResolveCatalog().RoundCount;
        public int RequiredProgress => ResolveCatalog().GetRound(_state.round).RequiredProgress;
        public bool IsRoundComplete => RequiredProgress > 0 && _state.collectedProgress >= RequiredProgress;
        public bool IsMaxRound => _state.round >= RoundCount;

        private CookieCleanupTimeBasis TimeBasis => (CookieCleanupTimeBasis)_state.timeBasis;
        private DateTime ServiceNow => TimeBasis == CookieCleanupTimeBasis.LegacyLocalTicks
            ? _legacyLocalClock.Now
            : _utcClock.GetCurrentTime(_calendarTimeZone).DateTime;
        private long NowTicks => TimeBasis == CookieCleanupTimeBasis.LegacyLocalTicks
            ? _legacyLocalClock.Now.Ticks
            : _utcClock.UtcNow.Ticks;

        public void Restore()
        {
            if (!_stateStore.TryLoad(_contentId, out string json))
            {
                _state = CookieCleanupStateSerializer.CreateDefault();
                _catalog = _catalogResolver.Current
                    ?? throw new InvalidOperationException("Current CookieCleanup catalog is unavailable.");
                NotifyStateChanged();
                return;
            }

            _state = CookieCleanupStateSerializer.Deserialize(json);
            _catalog = ResolveCatalog();
            ValidateCatalogState();
            RecoverPendingTransaction();
            NotifyStateChanged();
        }

        public bool ImportStateIfEmpty(CookieCleanupImportState importState)
        {
            if (importState == null) throw new ArgumentNullException(nameof(importState));
            if (_stateStore.TryLoad(_contentId, out _)) return false;
            if (importState.TimeSchemaVersion != CookieCleanupStateSerializer.CurrentTimeSchemaVersion)
                throw new NotSupportedException($"Unsupported imported CookieCleanup time schema {importState.TimeSchemaVersion}.");
            if (!Enum.IsDefined(typeof(CookieCleanupTimeBasis), importState.TimeBasis))
                throw new FormatException("Imported CookieCleanup time basis is invalid.");

            CookieCleanupCatalog catalog = _catalogResolver.Current
                ?? throw new InvalidOperationException("Current CookieCleanup catalog is unavailable.");
            var imported = CookieCleanupStateSerializer.CreateDefault();
            imported.catalogVersion = catalog.CatalogVersion;
            imported.balanceRevision = catalog.BalanceRevision;
            imported.eventStarted = importState.EventStarted;
            imported.pendingEnd = importState.PendingEnd;
            imported.eventEndTicks = importState.EventEndTicks;
            imported.timeSchemaVersion = importState.TimeSchemaVersion;
            imported.timeBasis = (int)importState.TimeBasis;
            imported.round = importState.Round;
            imported.completedRoundCount = importState.CompletedRoundCount;
            imported.collectedSpray = importState.CollectedSpray;
            imported.foamMask = importState.FoamMask;
            imported.collectedProgress = importState.CollectedProgress;
            imported.pendingStageEnd = importState.PendingStageEnd;
            imported.tutorialDone = importState.TutorialDone;
            imported.placementSeed = importState.PlacementSeed;
            imported.roundStartTicks = importState.RoundStartTicks;
            imported.roundSpraysUsed = importState.RoundSpraysUsed;
            imported.eventSpraysUsed = importState.EventSpraysUsed;
            imported.claimedRoundRewards = importState.ClaimedRoundRewards == null
                ? new List<int>()
                : new List<int>(importState.ClaimedRoundRewards);
            imported.claimedBoxRewards = importState.ClaimedBoxRewards == null
                ? new List<int>()
                : new List<int>(importState.ClaimedBoxRewards);
            if (imported.eventStarted)
                imported.eventInstanceId = BuildLegacyEventInstanceId(imported.placementSeed, imported.eventEndTicks);

            CookieCleanupStateSerializer.Validate(imported);
            CookieCleanupStateData previousState = _state;
            CookieCleanupCatalog previousCatalog = _catalog;
            _state = imported;
            _catalog = catalog;
            try
            {
                CookieCleanupStateSerializer.Validate(_state);
                ValidateCatalogState();
                Save(true);
            }
            catch
            {
                _state = previousState;
                _catalog = previousCatalog;
                throw;
            }
            return true;
        }

        public bool TryStartEvent()
        {
            if (!_accessPolicy.IsAccessAllowed || !_schedulePolicy.IsEnabled) return false;
            if (_state.eventStarted || IsEventActive
                || !TryGetActiveWindowEndTicks(CookieCleanupTimeBasis.UtcTicks, out long endTicks)) return false;

            int placementSeed = _seedSource.GenerateNonZeroSeed();
            if (placementSeed == 0) throw new InvalidOperationException("CookieCleanup seed source returned zero.");
            bool tutorialDone = _state.tutorialDone;
            ResetGameplayState();
            CookieCleanupCatalog current = _catalogResolver.Current
                ?? throw new InvalidOperationException("Current CookieCleanup catalog is unavailable.");
            _state.catalogVersion = current.CatalogVersion;
            _state.balanceRevision = current.BalanceRevision;
            _state.timeBasis = (int)CookieCleanupTimeBasis.UtcTicks;
            _state.timeSchemaVersion = CookieCleanupStateSerializer.CurrentTimeSchemaVersion;
            _state.eventEndTicks = endTicks;
            _state.eventStarted = true;
            _state.pendingEnd = false;
            _state.placementSeed = placementSeed;
            _state.eventInstanceId = BuildNewEventInstanceId(placementSeed, endTicks);
            _state.collectedSpray = current.InitialSprayCount;
            _state.roundStartTicks = _utcClock.UtcNow.Ticks;
            _state.tutorialDone = tutorialDone;
            _catalog = current;
            Save(true);
            _analytics.EventStarted(placementSeed, ToSeconds(EventRemainingTime));
            return true;
        }

        public void EvaluateEventTimeout()
        {
            if (!_schedulePolicy.IsEnabled)
            {
                ForceTerminate();
                return;
            }
            if (!_state.eventStarted || EventRemainingTime > TimeSpan.Zero || _state.pendingEnd) return;
            _state.pendingEnd = true;
            Save(true);
        }

        public void SetPendingEnd(bool pending)
        {
            if (_state.pendingEnd == pending) return;
            _state.pendingEnd = pending;
            Save(true);
        }

        public void EndEvent()
        {
            bool trackEnd = _state.eventStarted;
            int placementSeed = _state.placementSeed;
            int completedRounds = _state.completedRoundCount;
            int eventSpraysUsed = _state.eventSpraysUsed;
            long eventEndTicks = _state.eventEndTicks;
            bool tutorialDone = _state.tutorialDone;
            ResetGameplayState();
            _state.tutorialDone = tutorialDone;
            _state.eventStarted = false;
            _state.pendingEnd = false;
            _state.eventEndTicks = eventEndTicks;
            Save(true);
            if (trackEnd) _analytics.EventEnded(placementSeed, completedRounds, eventSpraysUsed);
        }

        public void ResetGameplay()
        {
            if (_state.eventStarted)
                throw new InvalidOperationException("CookieCleanup gameplay cannot be reset while an event is active. End the event instead.");
            bool tutorialDone = _state.tutorialDone;
            ResetGameplayState();
            _state.tutorialDone = tutorialDone;
            Save(true);
        }

        public bool AddSpray(int amount)
        {
            if (amount <= 0 || !_state.eventStarted || !IsEventActive || _state.pendingEnd) return false;
            _state.collectedSpray = checked(_state.collectedSpray + amount);
            Save(true);
            return true;
        }

        public bool UseSpray(int amount)
        {
            if (amount <= 0 || !_state.eventStarted || !IsEventActive || _state.pendingEnd
                || _state.pendingStageEnd || _state.collectedSpray < amount) return false;
            SpendSpray(amount);
            Save(true);
            return true;
        }

        public bool TryClearFoamCell(int cellIndex)
        {
            CookieCleanupRoundDefinition round = ResolveCatalog().GetRound(_state.round);
            if (cellIndex < 0 || cellIndex >= round.BoardCellCount) return false;
            long bit = 1L << cellIndex;
            if ((_state.foamMask & bit) != 0L) return false;
            if (_state.collectedSpray <= 0 || !_state.eventStarted || !IsEventActive
                || _state.pendingEnd || _state.pendingStageEnd) return false;
            SpendSpray(1);
            _state.foamMask |= bit;
            Save(true);
            return true;
        }

        public bool SetFoamMask(long foamMask)
        {
            if (!_state.eventStarted || _state.pendingEnd || _state.pendingStageEnd) return false;
            ValidateFoamMask(foamMask, ResolveCatalog().GetRound(_state.round).BoardCellCount);
            if (_state.foamMask == foamMask) return true;
            _state.foamMask = foamMask;
            Save(true);
            return true;
        }

        public bool AddProgress(int amount)
        {
            if (amount <= 0 || !_state.eventStarted || !IsEventActive || _state.pendingEnd
                || _state.pendingStageEnd || IsRoundComplete) return false;
            _state.collectedProgress = Math.Min(RequiredProgress, checked(_state.collectedProgress + amount));
            if (IsRoundComplete) return CompleteRound();
            Save(false);
            return true;
        }

        public bool CompleteRound()
        {
            if (!_state.eventStarted || !IsEventActive || _state.pendingEnd || _state.pendingStageEnd) return false;
            int completedRound = _state.round;
            int durationSeconds = GetElapsedSeconds(_state.roundStartTicks);
            int spraysUsed = _state.roundSpraysUsed;
            int placementSeed = _state.placementSeed;
            _state.pendingStageEnd = true;
            _state.completedRoundCount = Math.Min(RoundCount, _state.completedRoundCount + 1);
            _state.collectedProgress = 0;
            _state.foamMask = 0L;
            if (_state.round < RoundCount) _state.round++;
            _state.roundStartTicks = NowTicks;
            _state.roundSpraysUsed = 0;
            Save(true);
            _analytics.RoundCleared(placementSeed, completedRound, spraysUsed, durationSeconds);
            return true;
        }

        public void SetPendingStageEnd(bool pending)
        {
            if (_state.pendingStageEnd == pending) return;
            if (pending && _state.completedRoundCount <= 0)
                throw new InvalidOperationException("CookieCleanup cannot enter pending stage end without a completed round.");
            _state.pendingStageEnd = pending;
            Save(true);
        }

        public void SetTutorialDone(bool done)
        {
            if (_state.tutorialDone == done) return;
            _state.tutorialDone = done;
            if (done)
            {
                _state.roundStartTicks = NowTicks;
                _state.roundSpraysUsed = 0;
            }
            Save(true);
        }

        public void ResetSprayToInitial()
        {
            int initial = ResolveCatalog().InitialSprayCount;
            if (_state.collectedSpray == initial) return;
            _state.collectedSpray = initial;
            Save(true);
        }

        public int GetOrderSprayReward(int orderItemValue) => ResolveCatalog().GetOrderSprayReward(orderItemValue);

        public IReadOnlyList<CookieCleanupPlacement> GetPlacements(int round)
        {
            return CookieCleanupPlacementEngine.Place(ResolveCatalog(), _state.placementSeed, round);
        }

        public bool ClaimRoundReward(int round) => ClaimReward(CookieCleanupRewardKind.Round, round);
        public bool ClaimBoxReward(int round) => ClaimReward(CookieCleanupRewardKind.Box, round);
        public bool IsRoundRewardClaimed(int round) => _state.claimedRoundRewards.Contains(round);
        public bool IsBoxRewardClaimed(int round) => _state.claimedBoxRewards.Contains(round);

        public bool HasUnclaimedBoxReward(int round)
        {
            if (IsBoxRewardClaimed(round)) return false;
            return ResolveCatalog().GetRound(round).BoxRewards.Count > 0;
        }

        public TimeSpan GetRemaining(long deadlineTicks)
        {
            if (deadlineTicks <= 0 || deadlineTicks <= NowTicks) return TimeSpan.Zero;
            return TimeSpan.FromTicks(deadlineTicks - NowTicks);
        }

        public int GetElapsedSeconds(long startTicks)
        {
            if (startTicks <= 0 || startTicks >= NowTicks) return 0;
            return ToSeconds(TimeSpan.FromTicks(NowTicks - startTicks));
        }

        private bool ClaimReward(CookieCleanupRewardKind kind, int round)
        {
            if (kind == CookieCleanupRewardKind.None || round < MinRound || round > RoundCount) return false;
            if (string.IsNullOrWhiteSpace(_state.eventInstanceId)) return false;
            List<int> claimed = GetClaimedRewards(kind);
            if (claimed.Contains(round)) return false;

            CookieCleanupRewardKind pendingKind = (CookieCleanupRewardKind)_state.pendingRewardKind;
            if (pendingKind != CookieCleanupRewardKind.None)
            {
                if (!ClaimPendingReward()) return false;
                if (claimed.Contains(round)) return true;
            }

            IReadOnlyList<ContentReward> rewards = GetRewards(kind, round);
            if (rewards.Count == 0) return false;
            _state.pendingRewardKind = (int)kind;
            _state.pendingRewardRound = round;
            _state.pendingTransactionId = BuildTransactionId(kind, round);
            _state.pendingRewards = CookieCleanupStateSerializer.ToData(rewards);
            Save(true);
            return ClaimPendingReward();
        }

        private bool ClaimPendingReward()
        {
            CookieCleanupRewardKind kind = (CookieCleanupRewardKind)_state.pendingRewardKind;
            if (kind == CookieCleanupRewardKind.None || !_rewardService.IsAvailable) return false;
            string transactionId = _state.pendingTransactionId;
            IReadOnlyList<ContentReward> rewards = CookieCleanupStateSerializer.ToRewards(_state.pendingRewards);
            if (!_rewardService.HasGranted(transactionId)) _rewardService.GrantOnce(transactionId, rewards);
            if (!_rewardService.HasGranted(transactionId))
                throw new InvalidOperationException("CookieCleanup reward service did not confirm the transaction.");

            int round = _state.pendingRewardRound;
            int placementSeed = _state.placementSeed;
            List<int> claimed = GetClaimedRewards(kind);
            if (!claimed.Contains(round))
            {
                claimed.Add(round);
                claimed.Sort();
            }
            ClearPendingReward();
            Save(true);
            _analytics.RewardClaimed(placementSeed, round, kind);
            return true;
        }

        private void RecoverPendingTransaction()
        {
            if ((CookieCleanupRewardKind)_state.pendingRewardKind == CookieCleanupRewardKind.None
                || !_rewardService.IsAvailable) return;
            ClaimPendingReward();
        }

        private IReadOnlyList<ContentReward> GetRewards(CookieCleanupRewardKind kind, int round)
        {
            CookieCleanupRoundDefinition definition = ResolveCatalog().GetRound(round);
            return kind == CookieCleanupRewardKind.Round
                ? definition.RoundRewards
                : definition.BoxRewards;
        }

        private List<int> GetClaimedRewards(CookieCleanupRewardKind kind)
        {
            return kind == CookieCleanupRewardKind.Round
                ? _state.claimedRoundRewards
                : _state.claimedBoxRewards;
        }

        private void ForceTerminate()
        {
            if (!_state.eventStarted && !_state.pendingEnd && _state.eventEndTicks == 0) return;
            bool trackEnd = _state.eventStarted;
            int placementSeed = _state.placementSeed;
            int completedRounds = _state.completedRoundCount;
            int spraysUsed = _state.eventSpraysUsed;
            bool tutorialDone = _state.tutorialDone;
            ResetGameplayState();
            _state.tutorialDone = tutorialDone;
            _state.eventStarted = false;
            _state.pendingEnd = false;
            _state.eventEndTicks = 0L;
            Save(true);
            if (trackEnd) _analytics.EventEnded(placementSeed, completedRounds, spraysUsed);
        }

        private void SpendSpray(int amount)
        {
            _state.collectedSpray -= amount;
            _state.roundSpraysUsed = checked(_state.roundSpraysUsed + amount);
            _state.eventSpraysUsed = checked(_state.eventSpraysUsed + amount);
        }

        private void ResetGameplayState()
        {
            _state.eventInstanceId = string.Empty;
            _state.round = MinRound;
            _state.completedRoundCount = 0;
            _state.collectedSpray = 0;
            _state.foamMask = 0L;
            _state.collectedProgress = 0;
            _state.pendingStageEnd = false;
            _state.placementSeed = 0;
            _state.roundStartTicks = 0L;
            _state.roundSpraysUsed = 0;
            _state.eventSpraysUsed = 0;
            _state.claimedRoundRewards.Clear();
            _state.claimedBoxRewards.Clear();
            ClearPendingReward();
        }

        private void ClearPendingReward()
        {
            _state.pendingRewardKind = (int)CookieCleanupRewardKind.None;
            _state.pendingRewardRound = 0;
            _state.pendingTransactionId = string.Empty;
            _state.pendingRewards.Clear();
        }

        private bool TryGetActiveWindowEndTicks(CookieCleanupTimeBasis basis, out long endTicks)
        {
            endTicks = 0L;
            DateTime serviceNow = basis == CookieCleanupTimeBasis.LegacyLocalTicks
                ? _legacyLocalClock.Now
                : _utcClock.GetCurrentTime(_calendarTimeZone).DateTime;
            long nowTicks = basis == CookieCleanupTimeBasis.LegacyLocalTicks
                ? _legacyLocalClock.Now.Ticks
                : _utcClock.UtcNow.Ticks;
            if (!_schedulePolicy.IsEnabled || !_schedulePolicy.IsActiveDay(serviceNow.DayOfWeek)) return false;
            DateTime date = serviceNow.Date;
            bool foundActive = false;
            for (int dayOffset = 0; dayOffset <= 14; dayOffset++)
            {
                DateTime candidate = date.AddDays(dayOffset);
                if (_schedulePolicy.IsActiveDay(candidate.DayOfWeek))
                {
                    foundActive = true;
                    continue;
                }
                if (!foundActive) continue;
                endTicks = basis == CookieCleanupTimeBasis.LegacyLocalTicks
                    ? candidate.Ticks
                    : ConvertServiceTimeToUtcTicks(candidate);
                return endTicks > nowTicks;
            }
            if (!foundActive) return false;
            DateTime fallback = date.AddDays(7);
            endTicks = basis == CookieCleanupTimeBasis.LegacyLocalTicks
                ? fallback.Ticks
                : ConvertServiceTimeToUtcTicks(fallback);
            return endTicks > nowTicks;
        }

        private long ConvertServiceTimeToUtcTicks(DateTime serviceTime)
        {
            DateTime unspecified = DateTime.SpecifyKind(serviceTime, DateTimeKind.Unspecified);
            return TimeZoneInfo.ConvertTimeToUtc(unspecified, _calendarTimeZone).Ticks;
        }

        private CookieCleanupCatalog ResolveCatalog()
        {
            if (string.IsNullOrWhiteSpace(_state.catalogVersion))
                return _catalogResolver.Current
                    ?? throw new InvalidOperationException("Current CookieCleanup catalog is unavailable.");
            if (_catalog != null
                && string.Equals(_catalog.CatalogVersion, _state.catalogVersion, StringComparison.Ordinal)
                && string.Equals(_catalog.BalanceRevision, _state.balanceRevision, StringComparison.Ordinal))
                return _catalog;
            if (!_catalogResolver.TryResolve(_state.catalogVersion, _state.balanceRevision, out CookieCleanupCatalog catalog))
                throw new InvalidOperationException(
                    $"Pinned CookieCleanup catalog {_state.catalogVersion}/{_state.balanceRevision} is unavailable.");
            _catalog = catalog;
            return catalog;
        }

        private void ValidateCatalogState()
        {
            CookieCleanupCatalog catalog = ResolveCatalog();
            if (_state.round > catalog.RoundCount || _state.completedRoundCount > catalog.RoundCount)
                throw new FormatException("CookieCleanup round is outside the pinned catalog.");
            CookieCleanupRoundDefinition round = catalog.GetRound(_state.round);
            if (_state.collectedProgress > round.RequiredProgress)
                throw new FormatException("CookieCleanup progress exceeds the pinned round requirement.");
            ValidateFoamMask(_state.foamMask, round.BoardCellCount);
            ValidateClaimedRewards(_state.claimedRoundRewards, catalog.RoundCount);
            ValidateClaimedRewards(_state.claimedBoxRewards, catalog.RoundCount);
            if (_state.pendingRewardKind != (int)CookieCleanupRewardKind.None)
            {
                CookieCleanupRewardKind kind = (CookieCleanupRewardKind)_state.pendingRewardKind;
                if (_state.pendingRewardRound > catalog.RoundCount)
                    throw new FormatException("CookieCleanup pending reward round is outside the pinned catalog.");
                IReadOnlyList<ContentReward> expected = GetRewards(kind, _state.pendingRewardRound);
                IReadOnlyList<ContentReward> stored = CookieCleanupStateSerializer.ToRewards(_state.pendingRewards);
                if (!RewardsEqual(expected, stored))
                    throw new FormatException("CookieCleanup pending reward snapshot does not match its pinned catalog.");
                if (!string.Equals(_state.pendingTransactionId, BuildTransactionId(kind, _state.pendingRewardRound), StringComparison.Ordinal))
                    throw new FormatException("CookieCleanup pending reward transaction ID is invalid.");
            }
        }

        private static void ValidateFoamMask(long foamMask, int boardCellCount)
        {
            if (boardCellCount <= 0 || boardCellCount > MaxBoardCells)
                throw new FormatException("CookieCleanup board size cannot be represented by the foam mask.");
            if (boardCellCount == MaxBoardCells) return;
            ulong validBits = (1UL << boardCellCount) - 1UL;
            if ((unchecked((ulong)foamMask) & ~validBits) != 0UL)
                throw new FormatException("CookieCleanup foam mask contains cells outside the pinned board.");
        }

        private static void ValidateClaimedRewards(IReadOnlyList<int> rounds, int roundCount)
        {
            for (int index = 0; index < rounds.Count; index++)
            {
                int round = rounds[index];
                if (round > roundCount)
                    throw new FormatException("CookieCleanup claimed reward is outside the pinned catalog.");
            }
        }

        private static bool RewardsEqual(IReadOnlyList<ContentReward> left, IReadOnlyList<ContentReward> right)
        {
            if (left.Count != right.Count) return false;
            for (int index = 0; index < left.Count; index++)
            {
                if (!string.Equals(left[index].RewardId, right[index].RewardId, StringComparison.Ordinal)
                    || left[index].Amount != right[index].Amount) return false;
            }
            return true;
        }

        private string BuildTransactionId(CookieCleanupRewardKind kind, int round)
        {
            return $"cookie_cleanup:{_state.eventInstanceId}:round:{round}:{kind.ToString().ToLowerInvariant()}";
        }

        private static string BuildLegacyEventInstanceId(int placementSeed, long eventEndTicks)
        {
            if (placementSeed == 0 || eventEndTicks <= 0)
                throw new FormatException("Active legacy CookieCleanup state requires a placement seed and deadline.");
            return $"legacy-{unchecked((uint)placementSeed):x8}-{eventEndTicks:x16}";
        }

        private static string BuildNewEventInstanceId(int placementSeed, long eventEndTicks)
        {
            return $"utc-{eventEndTicks:x16}-{unchecked((uint)placementSeed):x8}";
        }

        private void Save(bool critical)
        {
            CookieCleanupStateSerializer.Validate(_state);
            ValidateCatalogState();
            _stateStore.Save(_contentId, CookieCleanupStateSerializer.Serialize(_state));
            if (critical && _stateStore is IFlushableContentStateStore flushable) flushable.Flush();
            NotifyStateChanged();
        }

        private void NotifyStateChanged() => StateChanged?.Invoke(new CookieCleanupState(_state));

        private static int ToSeconds(TimeSpan value)
        {
            if (value <= TimeSpan.Zero) return 0;
            return value.TotalSeconds >= int.MaxValue ? int.MaxValue : (int)value.TotalSeconds;
        }

    }
}
