using System;
using System.Collections.Generic;
using System.Linq;

namespace ActionFit.CookieCleanup
{
    public sealed class CookieCleanupPlacement
    {
        internal CookieCleanupPlacement(
            string objectId,
            int width,
            int height,
            int anchorX,
            int anchorY,
            int rotationDegrees,
            IReadOnlyList<int> occupiedCellIndexes)
        {
            ObjectId = objectId;
            Width = width;
            Height = height;
            AnchorX = anchorX;
            AnchorY = anchorY;
            RotationDegrees = rotationDegrees;
            OccupiedCellIndexes = occupiedCellIndexes;
        }

        public string ObjectId { get; }
        public int Width { get; }
        public int Height { get; }
        public int AnchorX { get; }
        public int AnchorY { get; }
        public int RotationDegrees { get; }
        public IReadOnlyList<int> OccupiedCellIndexes { get; }
    }

    public static class CookieCleanupPlacementEngine
    {
        private sealed class ObjectInstance
        {
            public string ObjectId;
            public int Width;
            public int Height;
            public int InputIndex;
        }

        private readonly struct Candidate
        {
            public Candidate(int x, int y, int rotationDegrees)
            {
                X = x;
                Y = y;
                RotationDegrees = rotationDegrees;
            }

            public int X { get; }
            public int Y { get; }
            public int RotationDegrees { get; }
        }

        public static IReadOnlyList<CookieCleanupPlacement> Place(
            CookieCleanupCatalog catalog,
            int placementSeed,
            int round)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            CookieCleanupRoundDefinition definition = catalog.GetRound(round);
            var items = new List<ObjectInstance>();
            int inputIndex = 0;
            foreach (CookieCleanupObjectRequirement requirement in definition.Objects)
            {
                CookieCleanupObjectDefinition item = catalog.GetObject(requirement.ObjectId);
                for (int count = 0; count < requirement.Count; count++)
                {
                    items.Add(new ObjectInstance
                    {
                        ObjectId = item.ObjectId,
                        Width = item.Width,
                        Height = item.Height,
                        InputIndex = inputIndex++,
                    });
                }
            }

            List<ObjectInstance> sorted = items
                .OrderByDescending(item => checked(item.Width * item.Height))
                .ThenBy(item => item.InputIndex)
                .ToList();

            for (int attempt = 0; attempt < catalog.PlacementMaxAttempts; attempt++)
            {
                var random = new Random(unchecked(placementSeed * 31 + round * 100003 + attempt));
                IReadOnlyList<CookieCleanupPlacement> result = TrySinglePass(definition, sorted, random);
                if (result != null) return result;
            }

            throw new InvalidOperationException(
                $"CookieCleanup round {round} placement failed after {catalog.PlacementMaxAttempts} attempts.");
        }

        private static IReadOnlyList<CookieCleanupPlacement> TrySinglePass(
            CookieCleanupRoundDefinition round,
            IReadOnlyList<ObjectInstance> items,
            Random random)
        {
            var occupied = new bool[round.BoardWidth, round.BoardHeight];
            var placements = new List<CookieCleanupPlacement>(items.Count);
            foreach (ObjectInstance item in items)
            {
                var rotations = new List<int> { 0 };
                if (item.Width != item.Height) rotations.Add(-90);
                var candidates = new List<Candidate>(round.BoardCellCount * rotations.Count);
                for (int x = 0; x < round.BoardWidth; x++)
                for (int y = 0; y < round.BoardHeight; y++)
                foreach (int rotation in rotations)
                    candidates.Add(new Candidate(x, y, rotation));
                Shuffle(candidates, random);

                bool placed = false;
                foreach (Candidate candidate in candidates)
                {
                    int width = candidate.RotationDegrees == 0 ? item.Width : item.Height;
                    int height = candidate.RotationDegrees == 0 ? item.Height : item.Width;
                    if (!CanFit(occupied, round, candidate.X, candidate.Y, width, height)) continue;
                    Mark(occupied, candidate.X, candidate.Y, width, height);
                    var cellIndexes = new List<int>(checked(width * height));
                    for (int dx = 0; dx < width; dx++)
                    for (int dy = 0; dy < height; dy++)
                        cellIndexes.Add(checked((candidate.Y + dy) * round.BoardWidth + candidate.X + dx));
                    cellIndexes.Sort();
                    placements.Add(new CookieCleanupPlacement(
                        item.ObjectId,
                        item.Width,
                        item.Height,
                        candidate.X,
                        candidate.Y,
                        candidate.RotationDegrees,
                        cellIndexes));
                    placed = true;
                    break;
                }

                if (!placed) return null;
            }
            return placements;
        }

        private static bool CanFit(
            bool[,] occupied,
            CookieCleanupRoundDefinition round,
            int x,
            int y,
            int width,
            int height)
        {
            if (x < 0 || y < 0 || x + width > round.BoardWidth || y + height > round.BoardHeight) return false;
            for (int dx = 0; dx < width; dx++)
            for (int dy = 0; dy < height; dy++)
                if (occupied[x + dx, y + dy]) return false;
            return true;
        }

        private static void Mark(bool[,] occupied, int x, int y, int width, int height)
        {
            for (int dx = 0; dx < width; dx++)
            for (int dy = 0; dy < height; dy++)
                occupied[x + dx, y + dy] = true;
        }

        private static void Shuffle<T>(IList<T> values, Random random)
        {
            for (int index = values.Count - 1; index > 0; index--)
            {
                int other = random.Next(index + 1);
                T value = values[index];
                values[index] = values[other];
                values[other] = value;
            }
        }
    }
}
