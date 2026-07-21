using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ActionFit.Content;

namespace ActionFit.CookieCleanup
{
    /// <summary>Canonical CSV text required to build the standalone Cookie Cleanup balance.</summary>
    public sealed class CookieCleanupCatalogCsvData
    {
        public CookieCleanupCatalogCsvData(
            string eventSettings,
            string objects,
            string boxRewards,
            string roundRewards,
            string rounds,
            string sprayOrders)
        {
            EventSettings = RequireText(eventSettings, nameof(eventSettings));
            Objects = RequireText(objects, nameof(objects));
            BoxRewards = RequireText(boxRewards, nameof(boxRewards));
            RoundRewards = RequireText(roundRewards, nameof(roundRewards));
            Rounds = RequireText(rounds, nameof(rounds));
            SprayOrders = RequireText(sprayOrders, nameof(sprayOrders));
        }

        public string EventSettings { get; }
        public string Objects { get; }
        public string BoxRewards { get; }
        public string RoundRewards { get; }
        public string Rounds { get; }
        public string SprayOrders { get; }

        private static string RequireText(string value, string parameterName)
        {
            return string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException("CSV text must not be empty.", parameterName)
                : value;
        }
    }

    /// <summary>Complete importer-independent balance used by standalone compositions.</summary>
    public sealed class CookieCleanupStandaloneCatalog
    {
        internal CookieCleanupStandaloneCatalog(
            CookieCleanupCatalog catalog,
            ICookieCleanupSchedulePolicy schedulePolicy)
        {
            Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            SchedulePolicy = schedulePolicy ?? throw new ArgumentNullException(nameof(schedulePolicy));
        }

        public CookieCleanupCatalog Catalog { get; }
        public ICookieCleanupSchedulePolicy SchedulePolicy { get; }
    }

    /// <summary>Builds the package catalog directly from the canonical CSV text.</summary>
    public static class CookieCleanupCatalogFactory
    {
        public const string DefaultCatalogVersion = "cat-merge-cookie-cleanup-v1";
        public const string DefaultSegment = "";
        public const string RewardSegment = "Reward";
        public const string DefaultBalanceRevision = "balance-v1-default";
        public const string RewardBalanceRevision = "balance-v1-reward";
        public const int DefaultInitialSprayCount = 10;
        public const int DefaultPlacementMaxAttempts = 1000;

        public static CookieCleanupStandaloneCatalog Create(
            CookieCleanupCatalogCsvData csv,
            string segment = DefaultSegment,
            int initialSprayCount = DefaultInitialSprayCount,
            int placementMaxAttempts = DefaultPlacementMaxAttempts)
        {
            string balanceRevision = NormalizeSegment(segment) == RewardSegment
                ? RewardBalanceRevision
                : DefaultBalanceRevision;
            return Create(
                csv,
                DefaultCatalogVersion,
                balanceRevision,
                segment,
                initialSprayCount,
                placementMaxAttempts);
        }

        public static CookieCleanupStandaloneCatalog Create(
            CookieCleanupCatalogCsvData csv,
            string catalogVersion,
            string balanceRevision,
            string segment,
            int initialSprayCount,
            int placementMaxAttempts)
        {
            if (csv == null) throw new ArgumentNullException(nameof(csv));
            segment = NormalizeSegment(segment);

            List<DayOfWeek> activeDays = ParseActiveDays(csv.EventSettings);
            List<CookieCleanupObjectDefinition> objects = ParseObjects(csv.Objects);
            Dictionary<RowKey, IReadOnlyList<ContentReward>> boxRewards = ParseRewards(
                csv.BoxRewards,
                "Reward_Box");
            Dictionary<RowKey, IReadOnlyList<ContentReward>> roundRewards = ParseRewards(
                csv.RoundRewards,
                "Reward_Round");
            List<RoundRow> roundRows = ParseRounds(csv.Rounds);
            List<KeyValuePair<int, int>> sprayOrders = ParseSprayOrders(csv.SprayOrders, segment);

            var rounds = new List<CookieCleanupRoundDefinition>(roundRows.Count);
            for (int index = 0; index < roundRows.Count; index++)
            {
                RoundRow row = roundRows[index];
                rounds.Add(new CookieCleanupRoundDefinition(
                    row.Round,
                    row.BoardWidth,
                    row.BoardHeight,
                    row.Objects,
                    Select(roundRewards, row.Round, segment),
                    Select(boxRewards, row.Round, segment)));
            }

            var catalog = new CookieCleanupCatalog(
                catalogVersion,
                balanceRevision,
                initialSprayCount,
                placementMaxAttempts,
                objects,
                rounds,
                sprayOrders);
            return new CookieCleanupStandaloneCatalog(catalog, new FixedSchedulePolicy(activeDays));
        }

        private static string NormalizeSegment(string segment)
        {
            segment = segment?.Trim() ?? string.Empty;
            if (segment.Length == 0) return DefaultSegment;
            if (string.Equals(segment, RewardSegment, StringComparison.Ordinal)) return RewardSegment;
            throw new ArgumentException($"Unsupported CookieCleanup segment '{segment}'.", nameof(segment));
        }

        private static List<DayOfWeek> ParseActiveDays(string csv)
        {
            CanonicalCsvTable table = CanonicalCsvTable.Parse(csv, "EventSettings");
            if (table.Rows.Count != 1)
                throw new FormatException("CookieCleanup EventSettings must contain exactly one row.");
            return CanonicalCsvValue.ParseDays(table.Value(table.Rows[0], "ActiveDays"));
        }

        private static List<CookieCleanupObjectDefinition> ParseObjects(string csv)
        {
            CanonicalCsvTable table = CanonicalCsvTable.Parse(csv, "Object");
            var result = new List<CookieCleanupObjectDefinition>(table.Rows.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < table.Rows.Count; index++)
            {
                IReadOnlyList<string> row = table.Rows[index];
                string id = table.Value(row, "Id").Trim();
                if (!seen.Add(id)) throw new FormatException($"CookieCleanup Object contains duplicate ID '{id}'.");
                (int width, int height) = CanonicalCsvValue.ParseVector2Int(
                    table.Value(row, "ItemSize"),
                    "Object.ItemSize");
                result.Add(new CookieCleanupObjectDefinition(id, width, height));
            }
            if (result.Count == 0) throw new FormatException("CookieCleanup Object must contain at least one row.");
            return result;
        }

        private static Dictionary<RowKey, IReadOnlyList<ContentReward>> ParseRewards(
            string csv,
            string tableName)
        {
            CanonicalCsvTable table = CanonicalCsvTable.Parse(csv, tableName);
            var result = new Dictionary<RowKey, IReadOnlyList<ContentReward>>();
            for (int index = 0; index < table.Rows.Count; index++)
            {
                IReadOnlyList<string> row = table.Rows[index];
                int round = CanonicalCsvValue.ParseInt(table.Value(row, "Round"), tableName + ".Round");
                string segment = table.Value(row, "NetSeg").Trim();
                var key = new RowKey(round, segment);
                if (!result.TryAdd(
                        key,
                        CanonicalCsvValue.ParseRewards(
                            table.Value(row, "Reward"),
                            tableName + ".Reward",
                            requireAny: true)))
                {
                    throw new FormatException($"CookieCleanup {tableName} contains duplicate row {round}/{segment}.");
                }
            }
            return result;
        }

        private static List<RoundRow> ParseRounds(string csv)
        {
            CanonicalCsvTable table = CanonicalCsvTable.Parse(csv, "Round_Settings");
            var result = new List<RoundRow>(table.Rows.Count);
            for (int index = 0; index < table.Rows.Count; index++)
            {
                IReadOnlyList<string> row = table.Rows[index];
                int round = CanonicalCsvValue.ParseInt(table.Value(row, "Round"), "Round_Settings.Round");
                (int width, int height) = CanonicalCsvValue.ParseVector2Int(
                    table.Value(row, "BoardSize"),
                    "Round_Settings.BoardSize");
                result.Add(new RoundRow(
                    round,
                    width,
                    height,
                    CanonicalCsvValue.ParseObjectRequirements(
                        table.Value(row, "Objects"),
                        "Round_Settings.Objects")));
            }
            result.Sort((left, right) => left.Round.CompareTo(right.Round));
            for (int index = 0; index < result.Count; index++)
            {
                if (result[index].Round != index + 1)
                    throw new FormatException($"CookieCleanup Round_Settings expected round {index + 1}.");
            }
            return result;
        }

        private static List<KeyValuePair<int, int>> ParseSprayOrders(string csv, string segment)
        {
            CanonicalCsvTable table = CanonicalCsvTable.Parse(csv, "Spray_Order");
            var rows = new Dictionary<RowKey, int>();
            var levels = new SortedSet<int>();
            for (int index = 0; index < table.Rows.Count; index++)
            {
                IReadOnlyList<string> row = table.Rows[index];
                int level = CanonicalCsvValue.ParseInt(
                    table.Value(row, "OrderItemValue"),
                    "Spray_Order.OrderItemValue");
                string rowSegment = table.Value(row, "NetSeg").Trim();
                int reward = CanonicalCsvValue.ParseInt(
                    table.Value(row, "CompleteReward"),
                    "Spray_Order.CompleteReward");
                var key = new RowKey(level, rowSegment);
                if (!rows.TryAdd(key, reward))
                    throw new FormatException($"CookieCleanup Spray_Order contains duplicate row {level}/{rowSegment}.");
                levels.Add(level);
            }

            var result = new List<KeyValuePair<int, int>>(levels.Count);
            foreach (int level in levels)
            {
                if (!TrySelect(rows, level, segment, out int reward))
                    throw new FormatException($"CookieCleanup Spray_Order has no default row for level {level}.");
                result.Add(new KeyValuePair<int, int>(level, reward));
            }
            return result;
        }

        private static IReadOnlyList<ContentReward> Select(
            Dictionary<RowKey, IReadOnlyList<ContentReward>> rows,
            int key,
            string segment)
        {
            return TrySelect(rows, key, segment, out IReadOnlyList<ContentReward> value)
                ? value
                : Array.Empty<ContentReward>();
        }

        private static bool TrySelect<T>(Dictionary<RowKey, T> rows, int key, string segment, out T value)
        {
            if (segment.Length > 0 && rows.TryGetValue(new RowKey(key, segment), out value)) return true;
            return rows.TryGetValue(new RowKey(key, DefaultSegment), out value);
        }

        private readonly struct RowKey : IEquatable<RowKey>
        {
            public RowKey(int key, string segment)
            {
                Key = key;
                Segment = segment ?? string.Empty;
            }

            private int Key { get; }
            private string Segment { get; }

            public bool Equals(RowKey other)
            {
                return Key == other.Key && string.Equals(Segment, other.Segment, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is RowKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return (Key * 397) ^ StringComparer.Ordinal.GetHashCode(Segment);
            }
        }

        private sealed class RoundRow
        {
            public RoundRow(
                int round,
                int boardWidth,
                int boardHeight,
                IReadOnlyList<CookieCleanupObjectRequirement> objects)
            {
                Round = round;
                BoardWidth = boardWidth;
                BoardHeight = boardHeight;
                Objects = objects;
            }

            public int Round { get; }
            public int BoardWidth { get; }
            public int BoardHeight { get; }
            public IReadOnlyList<CookieCleanupObjectRequirement> Objects { get; }
        }

        private sealed class FixedSchedulePolicy : ICookieCleanupSchedulePolicy
        {
            private readonly HashSet<DayOfWeek> _activeDays;

            public FixedSchedulePolicy(IEnumerable<DayOfWeek> activeDays)
            {
                _activeDays = new HashSet<DayOfWeek>(activeDays);
            }

            public bool IsEnabled => _activeDays.Count > 0;

            public bool IsActiveDay(DayOfWeek dayOfWeek)
            {
                return _activeDays.Contains(dayOfWeek);
            }
        }
    }

    internal sealed class CanonicalCsvTable
    {
        private readonly Dictionary<string, int> _columns;

        private CanonicalCsvTable(string name, Dictionary<string, int> columns, List<IReadOnlyList<string>> rows)
        {
            Name = name;
            _columns = columns;
            Rows = rows;
        }

        public string Name { get; }
        public IReadOnlyList<IReadOnlyList<string>> Rows { get; }

        public string Value(IReadOnlyList<string> row, string column)
        {
            if (!_columns.TryGetValue(column, out int index))
                throw new FormatException($"{Name} is missing column '{column}'.");
            return index < row.Count ? row[index] : string.Empty;
        }

        public static CanonicalCsvTable Parse(string text, string name)
        {
            List<List<string>> records = ParseRecords(text);
            if (records.Count < 3)
                throw new FormatException($"{name} must contain the three canonical header rows.");
            var columns = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int index = 0; index < records[2].Count; index++)
            {
                string field = records[2][index].Trim().TrimStart('\uFEFF');
                int annotation = field.IndexOf('(');
                string column = (annotation >= 0 ? field.Substring(0, annotation) : field).Trim();
                if (column.Length == 0 || !columns.TryAdd(column, index))
                    throw new FormatException($"{name} contains an empty or duplicate column name.");
            }
            var rows = new List<IReadOnlyList<string>>();
            for (int index = 3; index < records.Count; index++)
            {
                bool hasValue = false;
                for (int fieldIndex = 0; fieldIndex < records[index].Count; fieldIndex++)
                {
                    if (!string.IsNullOrWhiteSpace(records[index][fieldIndex]))
                    {
                        hasValue = true;
                        break;
                    }
                }
                if (hasValue) rows.Add(records[index]);
            }
            return new CanonicalCsvTable(name, columns, rows);
        }

        private static List<List<string>> ParseRecords(string text)
        {
            var records = new List<List<string>>();
            var record = new List<string>();
            var field = new StringBuilder();
            bool quoted = false;
            for (int index = 0; index < text.Length; index++)
            {
                char character = text[index];
                if (quoted)
                {
                    if (character == '"')
                    {
                        if (index + 1 < text.Length && text[index + 1] == '"')
                        {
                            field.Append('"');
                            index++;
                        }
                        else quoted = false;
                    }
                    else field.Append(character);
                    continue;
                }
                if (character == '"' && field.Length == 0) quoted = true;
                else if (character == ',')
                {
                    record.Add(field.ToString());
                    field.Clear();
                }
                else if (character == '\r' || character == '\n')
                {
                    if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n') index++;
                    record.Add(field.ToString());
                    field.Clear();
                    records.Add(record);
                    record = new List<string>();
                }
                else field.Append(character);
            }
            if (quoted) throw new FormatException("CSV contains an unterminated quoted field.");
            if (field.Length > 0 || record.Count > 0)
            {
                record.Add(field.ToString());
                records.Add(record);
            }
            return records;
        }
    }

    internal static class CanonicalCsvValue
    {
        public static int ParseInt(string value, string field)
        {
            if (!int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int result))
                throw new FormatException($"{field} must be an integer.");
            return result;
        }

        public static (int X, int Y) ParseVector2Int(string value, string field)
        {
            value = value.Trim();
            if (value.Length < 5 || value[0] != '(' || value[value.Length - 1] != ')')
                throw new FormatException($"{field} must use '(x,y)' format.");
            string[] values = value.Substring(1, value.Length - 2).Split(',');
            if (values.Length != 2) throw new FormatException($"{field} must contain two integers.");
            return (ParseInt(values[0], field), ParseInt(values[1], field));
        }

        public static List<DayOfWeek> ParseDays(string value)
        {
            var result = new List<DayOfWeek>();
            string[] values = value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (int index = 0; index < values.Length; index++)
            {
                if (!Enum.TryParse(values[index].Trim(), false, out DayOfWeek day)
                    || !Enum.IsDefined(typeof(DayOfWeek), day))
                    throw new FormatException($"Unsupported active day '{values[index]}'.");
                if (!result.Contains(day)) result.Add(day);
            }
            return result;
        }

        public static IReadOnlyList<CookieCleanupObjectRequirement> ParseObjectRequirements(
            string value,
            string field)
        {
            List<string[]> tuples = ParseTuples(value, field);
            var result = new List<CookieCleanupObjectRequirement>(tuples.Count);
            for (int index = 0; index < tuples.Count; index++)
            {
                if (tuples[index].Length != 2)
                    throw new FormatException($"{field} tuples require object ID and count.");
                result.Add(new CookieCleanupObjectRequirement(
                    tuples[index][0].Trim(),
                    ParseInt(tuples[index][1], field + ".Count")));
            }
            if (result.Count == 0) throw new FormatException($"{field} requires at least one object.");
            return result;
        }

        public static IReadOnlyList<ContentReward> ParseRewards(string value, string field, bool requireAny)
        {
            List<string[]> tuples = ParseTuples(value, field);
            var result = new List<ContentReward>(tuples.Count);
            for (int index = 0; index < tuples.Count; index++)
            {
                if (tuples[index].Length != 3)
                    throw new FormatException($"{field} reward tuples require type, item ID, and amount.");
                string type = tuples[index][0].Trim();
                string itemId = tuples[index][1].Trim();
                int amount = ParseInt(tuples[index][2], field + ".Amount");
                result.Add(new ContentReward(UsesItemKey(type) ? type + "/" + itemId : type, amount));
            }
            if (requireAny && result.Count == 0)
                throw new FormatException($"{field} requires at least one reward.");
            return result;
        }

        private static List<string[]> ParseTuples(string value, string field)
        {
            value = value.Trim();
            var result = new List<string[]>();
            if (value.Length == 0) return result;
            if (value.Length < 2 || value[0] != '[' || value[value.Length - 1] != ']')
                throw new FormatException($"{field} must use an array.");
            int index = 1;
            while (index < value.Length - 1)
            {
                while (index < value.Length - 1 && (char.IsWhiteSpace(value[index]) || value[index] == ',')) index++;
                if (index >= value.Length - 1) break;
                if (value[index] != '(') throw new FormatException($"{field} contains an invalid tuple.");
                int end = value.IndexOf(')', index + 1);
                if (end < 0) throw new FormatException($"{field} contains an unterminated tuple.");
                result.Add(value.Substring(index + 1, end - index - 1).Split(','));
                index = end + 1;
            }
            return result;
        }

        private static bool UsesItemKey(string itemType)
        {
            return itemType == "BoardItem" || itemType == "Pass" || itemType == "Profile" || itemType == "Frame";
        }
    }
}
