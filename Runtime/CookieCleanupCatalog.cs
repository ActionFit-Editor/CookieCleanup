using System;
using System.Collections.Generic;
using ActionFit.Content;

namespace ActionFit.CookieCleanup
{
    public sealed class CookieCleanupObjectDefinition
    {
        public CookieCleanupObjectDefinition(string objectId, int width, int height)
        {
            ObjectId = ValidateId(objectId, nameof(objectId));
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            Width = width;
            Height = height;
        }

        public string ObjectId { get; }
        public int Width { get; }
        public int Height { get; }

        private static string ValidateId(string value, string parameterName)
        {
            return string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException("Value must not be empty or whitespace.", parameterName)
                : value;
        }
    }

    public sealed class CookieCleanupObjectRequirement
    {
        public CookieCleanupObjectRequirement(string objectId, int count)
        {
            ObjectId = string.IsNullOrWhiteSpace(objectId)
                ? throw new ArgumentException("Object ID is required.", nameof(objectId))
                : objectId;
            if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
            Count = count;
        }

        public string ObjectId { get; }
        public int Count { get; }
    }

    public sealed class CookieCleanupRoundDefinition
    {
        public CookieCleanupRoundDefinition(
            int round,
            int boardWidth,
            int boardHeight,
            IEnumerable<CookieCleanupObjectRequirement> objects,
            IReadOnlyList<ContentReward> roundRewards,
            IReadOnlyList<ContentReward> boxRewards)
        {
            if (round < CookieCleanupEngine.MinRound) throw new ArgumentOutOfRangeException(nameof(round));
            if (boardWidth <= 0) throw new ArgumentOutOfRangeException(nameof(boardWidth));
            if (boardHeight <= 0) throw new ArgumentOutOfRangeException(nameof(boardHeight));
            int boardCells = checked(boardWidth * boardHeight);
            if (boardCells > CookieCleanupEngine.MaxBoardCells)
                throw new ArgumentOutOfRangeException(nameof(boardWidth), "CookieCleanup boards support at most 64 cells.");
            if (objects == null) throw new ArgumentNullException(nameof(objects));

            var objectCopy = new List<CookieCleanupObjectRequirement>();
            foreach (CookieCleanupObjectRequirement requirement in objects)
                objectCopy.Add(requirement ?? throw new ArgumentException("Requirements must not contain null entries.", nameof(objects)));
            if (objectCopy.Count == 0) throw new ArgumentException("At least one object requirement is required.", nameof(objects));

            Round = round;
            BoardWidth = boardWidth;
            BoardHeight = boardHeight;
            Objects = objectCopy;
            RoundRewards = CopyRewards(roundRewards, nameof(roundRewards));
            BoxRewards = CopyRewards(boxRewards, nameof(boxRewards));

            int required = 0;
            foreach (CookieCleanupObjectRequirement requirement in objectCopy)
                required = checked(required + requirement.Count);
            RequiredProgress = required;
        }

        public int Round { get; }
        public int BoardWidth { get; }
        public int BoardHeight { get; }
        public int BoardCellCount => checked(BoardWidth * BoardHeight);
        public int RequiredProgress { get; }
        public IReadOnlyList<CookieCleanupObjectRequirement> Objects { get; }
        public IReadOnlyList<ContentReward> RoundRewards { get; }
        public IReadOnlyList<ContentReward> BoxRewards { get; }

        private static IReadOnlyList<ContentReward> CopyRewards(IReadOnlyList<ContentReward> rewards, string parameterName)
        {
            if (rewards == null) throw new ArgumentNullException(parameterName);
            var copy = new List<ContentReward>(rewards.Count);
            for (int index = 0; index < rewards.Count; index++)
                copy.Add(rewards[index] ?? throw new ArgumentException("Rewards must not contain null entries.", parameterName));
            return copy;
        }
    }

    public sealed class CookieCleanupCatalog
    {
        private readonly Dictionary<string, CookieCleanupObjectDefinition> _objects = new Dictionary<string, CookieCleanupObjectDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<int, CookieCleanupRoundDefinition> _rounds = new Dictionary<int, CookieCleanupRoundDefinition>();
        private readonly Dictionary<int, int> _orderSprayRewards = new Dictionary<int, int>();

        public CookieCleanupCatalog(
            string catalogVersion,
            string balanceRevision,
            int initialSprayCount,
            int placementMaxAttempts,
            IEnumerable<CookieCleanupObjectDefinition> objects,
            IEnumerable<CookieCleanupRoundDefinition> rounds,
            IEnumerable<KeyValuePair<int, int>> orderSprayRewards)
        {
            CatalogVersion = ValidateId(catalogVersion, nameof(catalogVersion));
            BalanceRevision = ValidateId(balanceRevision, nameof(balanceRevision));
            if (initialSprayCount < 0) throw new ArgumentOutOfRangeException(nameof(initialSprayCount));
            if (placementMaxAttempts <= 0) throw new ArgumentOutOfRangeException(nameof(placementMaxAttempts));
            if (objects == null) throw new ArgumentNullException(nameof(objects));
            if (rounds == null) throw new ArgumentNullException(nameof(rounds));
            if (orderSprayRewards == null) throw new ArgumentNullException(nameof(orderSprayRewards));

            InitialSprayCount = initialSprayCount;
            PlacementMaxAttempts = placementMaxAttempts;
            foreach (CookieCleanupObjectDefinition definition in objects)
            {
                if (definition == null) throw new ArgumentException("Objects must not contain null entries.", nameof(objects));
                if (!_objects.TryAdd(definition.ObjectId, definition))
                    throw new ArgumentException($"Duplicate object ID {definition.ObjectId}.", nameof(objects));
            }
            if (_objects.Count == 0) throw new ArgumentException("At least one object is required.", nameof(objects));

            foreach (CookieCleanupRoundDefinition definition in rounds)
            {
                if (definition == null) throw new ArgumentException("Rounds must not contain null entries.", nameof(rounds));
                if (!_rounds.TryAdd(definition.Round, definition))
                    throw new ArgumentException($"Duplicate round {definition.Round}.", nameof(rounds));
            }
            if (_rounds.Count == 0) throw new ArgumentException("At least one round is required.", nameof(rounds));
            for (int round = CookieCleanupEngine.MinRound; round <= _rounds.Count; round++)
            {
                if (!_rounds.ContainsKey(round)) throw new ArgumentException("Rounds must be contiguous from 1.", nameof(rounds));
            }

            foreach (CookieCleanupRoundDefinition round in _rounds.Values)
            {
                int occupiedArea = 0;
                foreach (CookieCleanupObjectRequirement requirement in round.Objects)
                {
                    CookieCleanupObjectDefinition definition = GetObject(requirement.ObjectId);
                    occupiedArea = checked(occupiedArea + checked(definition.Width * definition.Height) * requirement.Count);
                }
                if (occupiedArea > round.BoardCellCount)
                    throw new ArgumentException($"Round {round.Round} object area exceeds its board.", nameof(rounds));
            }

            foreach (KeyValuePair<int, int> reward in orderSprayRewards)
            {
                if (reward.Key < 0 || reward.Value < 0)
                    throw new ArgumentOutOfRangeException(nameof(orderSprayRewards));
                if (!_orderSprayRewards.TryAdd(reward.Key, reward.Value))
                    throw new ArgumentException($"Duplicate order spray level {reward.Key}.", nameof(orderSprayRewards));
            }
        }

        public string CatalogVersion { get; }
        public string BalanceRevision { get; }
        public int InitialSprayCount { get; }
        public int PlacementMaxAttempts { get; }
        public int RoundCount => _rounds.Count;

        public CookieCleanupObjectDefinition GetObject(string objectId)
        {
            if (!_objects.TryGetValue(objectId, out CookieCleanupObjectDefinition value))
                throw new KeyNotFoundException($"CookieCleanup object {objectId} is unavailable.");
            return value;
        }

        public CookieCleanupRoundDefinition GetRound(int round)
        {
            if (!_rounds.TryGetValue(round, out CookieCleanupRoundDefinition value))
                throw new KeyNotFoundException($"CookieCleanup round {round} is unavailable.");
            return value;
        }

        public int GetOrderSprayReward(int orderItemValue)
        {
            return _orderSprayRewards.TryGetValue(orderItemValue, out int value) ? value : 0;
        }

        private static string ValidateId(string value, string parameterName)
        {
            return string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException("Value must not be empty or whitespace.", parameterName)
                : value;
        }
    }
}
