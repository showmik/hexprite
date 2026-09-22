using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Hexprite.Core
{
    /// <summary>
    /// Distribution strategies for auto-balancing dolphin animation schedule matrices.
    /// </summary>
    public enum FlipperAutoBalanceStrategy
    {
        LinearLevels,
        MoodTiers,
        StageEvolution,
        FillGapsOnly,
    }

    /// <summary>
    /// Represents the calculated probability share of an animation competing in a specific matrix cell.
    /// </summary>
    public record FlipperCellProbability(
        string Name,
        int Weight,
        double Probability,
        double Percentage
    )
    {
        public string DisplayText => string.Create(CultureInfo.InvariantCulture, $"{Percentage:F0}% (W:{Weight})");
    }

    /// <summary>
    /// Represents a single cell in the Flipper dolphin scheduling matrix.
    /// </summary>
    public sealed class FlipperScheduleCell(int level, int mood)
    {
        public int Level { get; init; } = level;
        public int Mood { get; init; } = mood;
        public List<FlipperManifestEntry> MatchingEntries { get; } = [];
        public int TotalWeight => MatchingEntries.Sum(e => e.Weight);
        public bool HasCoverage => MatchingEntries.Count > 0 && TotalWeight > 0;

        public double GetProbability(string animationName)
        {
            if (TotalWeight <= 0) return 0.0;
            var entry = MatchingEntries.FirstOrDefault(e => e.Name.Equals(animationName, StringComparison.OrdinalIgnoreCase));
            return entry == null ? 0.0 : (double)entry.Weight / TotalWeight;
        }

        public IReadOnlyList<FlipperCellProbability> GetProbabilities()
        {
            if (TotalWeight <= 0) return [];
            return [.. MatchingEntries
                .Select(e =>
                {
                    double prob = (double)e.Weight / TotalWeight;
                    return new FlipperCellProbability(e.Name, e.Weight, prob, prob * 100.0);
                })
                .OrderByDescending(p => p.Probability)
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)];
        }
    }

    /// <summary>
    /// Analyzes the scheduling matrix for Flipper Zero dolphin animations.
    /// Supports both Stock mode (Levels 1..3 x Moods 0..14 = 45 states) and
    /// Extended Momentum mode (Levels 1..30 x Moods 0..14 = 450 states).
    /// </summary>
    public sealed class FlipperScheduleMatrix
    {
        public const int MinLevel = 1;
        public const int DefaultMaxLevel = 30;
        public const int StockMaxLevel = 3;
        public const int MinMood = 0;
        public const int MaxMood = 14;

        public int MaxLevel { get; }
        public int TotalCells => (MaxLevel - MinLevel + 1) * (MaxMood - MinMood + 1);

        /// <summary>
        /// Maps an extended mode level (1..30) to its corresponding stock mode stage index (1..3).
        /// Level 1..9 -> Stage 1 (Baby), Level 10..19 -> Stage 2 (Teen), Level 20..30 -> Stage 3 (Adult).
        /// </summary>
        public static int ExtendedLevelToStockStage(int level)
        {
            if (level <= 9) return 1;
            if (level <= 19) return 2;
            return 3;
        }

        /// <summary>
        /// Returns the minimum extended mode level for a given stock stage (1..3).
        /// Stage 1 -> 1, Stage 2 -> 10, Stage 3 -> 20.
        /// </summary>
        public static int StockStageToExtendedMinLevel(int stage)
        {
            if (stage <= 1) return 1;
            if (stage == 2) return 10;
            return 20;
        }

        /// <summary>
        /// Returns the maximum extended mode level for a given stock stage (1..3).
        /// Stage 1 -> 9, Stage 2 -> 19, Stage 3 -> 30.
        /// </summary>
        public static int StockStageToExtendedMaxLevel(int stage)
        {
            if (stage <= 1) return 9;
            if (stage == 2) return 19;
            return 30;
        }

        /// <summary>
        /// Projects an extended level range (1..30) into the corresponding stock stages (1..3).
        /// </summary>
        public static (int stockMinLevel, int stockMaxLevel) ConvertExtendedToStock(int minLevel, int maxLevel)
        {
            int safeMin = Math.Clamp(Math.Min(minLevel, maxLevel), 1, 30);
            int safeMax = Math.Clamp(Math.Max(minLevel, maxLevel), 1, 30);

            int sMin = ExtendedLevelToStockStage(safeMin);
            int sMax = ExtendedLevelToStockStage(safeMax);
            return (sMin, Math.Max(sMin, sMax));
        }

        /// <summary>
        /// Expands a stock level range (1..3) into the corresponding extended level range (1..30).
        /// </summary>
        public static (int extendedMinLevel, int extendedMaxLevel) ConvertStockToExtended(int stockMinLevel, int stockMaxLevel)
        {
            int safeMin = Math.Clamp(Math.Min(stockMinLevel, stockMaxLevel), 1, 3);
            int safeMax = Math.Clamp(Math.Max(stockMinLevel, stockMaxLevel), 1, 3);

            int eMin = StockStageToExtendedMinLevel(safeMin);
            int eMax = StockStageToExtendedMaxLevel(safeMax);
            return (eMin, Math.Max(eMin, eMax));
        }

        private readonly FlipperScheduleCell[,] _grid;
        private readonly int _coveredCellsCount;
        private readonly int _maxCollidingAnimations;

        public IReadOnlyList<FlipperManifestEntry> Entries { get; }

        public FlipperScheduleMatrix(IEnumerable<FlipperManifestEntry>? entries, int maxLevel = DefaultMaxLevel)
        {
            MaxLevel = Math.Clamp(maxLevel, StockMaxLevel, DefaultMaxLevel);
            Entries = entries?.ToList() ?? [];
            _grid = new FlipperScheduleCell[MaxLevel + 1, MaxMood + 1];

            int coveredCount = 0;
            int maxColliding = 0;

            for (int lvl = MinLevel; lvl <= MaxLevel; lvl++)
            {
                for (int mood = MinMood; mood <= MaxMood; mood++)
                {
                    var cell = new FlipperScheduleCell(lvl, mood);
                    foreach (var entry in Entries)
                    {
                        if (lvl >= entry.MinLevel && lvl <= entry.MaxLevel &&
                            mood >= entry.MinButthurt && mood <= entry.MaxButthurt)
                        {
                            cell.MatchingEntries.Add(entry);
                        }
                    }

                    if (cell.HasCoverage) coveredCount++;
                    if (cell.MatchingEntries.Count > maxColliding) maxColliding = cell.MatchingEntries.Count;

                    _grid[lvl, mood] = cell;
                }
            }

            _coveredCellsCount = coveredCount;
            _maxCollidingAnimations = maxColliding;
        }

        public FlipperScheduleCell GetCell(int level, int mood)
        {
            int safeLvl = Math.Clamp(level, MinLevel, MaxLevel);
            int safeMood = Math.Clamp(mood, MinMood, MaxMood);
            return _grid[safeLvl, safeMood];
        }

        public IReadOnlyList<FlipperCellProbability> GetCellProbabilities(int level, int mood)
        {
            return GetCell(level, mood).GetProbabilities();
        }

        public int CoveredCellsCount => _coveredCellsCount;

        public int UncoveredCellsCount => TotalCells - _coveredCellsCount;
        public int UncoveredStatesCount => TotalCells - _coveredCellsCount;

        public double CoveragePercentage => TotalCells > 0 ? (double)_coveredCellsCount / TotalCells * 100.0 : 0.0;

        public List<(int Level, int Mood)> GetUncoveredCells()
        {
            var gaps = new List<(int Level, int Mood)>();
            for (int lvl = MinLevel; lvl <= MaxLevel; lvl++)
            {
                for (int mood = MinMood; mood <= MaxMood; mood++)
                {
                    if (!_grid[lvl, mood].HasCoverage)
                    {
                        gaps.Add((lvl, mood));
                    }
                }
            }
            return gaps;
        }

        public int MaxCollidingAnimations => _maxCollidingAnimations;

        /// <summary>
        /// Automatically recalculates and balances level and mood ranges across manifest entries
        /// using the specified distribution strategy.
        /// </summary>
        public static List<FlipperManifestEntry> AutoBalanceEntries(
            IReadOnlyList<FlipperManifestEntry> entries,
            FlipperAutoBalanceStrategy strategy = FlipperAutoBalanceStrategy.LinearLevels,
            int maxLevel = DefaultMaxLevel)
        {
            if (entries == null || entries.Count == 0) return [];

            int safeMaxLevel = Math.Clamp(maxLevel, StockMaxLevel, DefaultMaxLevel);
            int count = entries.Count;

            return strategy switch
            {
                FlipperAutoBalanceStrategy.MoodTiers => BalanceByMoodTiers(entries, safeMaxLevel),
                FlipperAutoBalanceStrategy.StageEvolution => BalanceByStageEvolution(entries, safeMaxLevel),
                FlipperAutoBalanceStrategy.FillGapsOnly => BalanceByFillingGaps(entries, safeMaxLevel),
                _ => BalanceLinearLevels(entries, safeMaxLevel),
            };
        }

        private static List<FlipperManifestEntry> BalanceLinearLevels(IReadOnlyList<FlipperManifestEntry> entries, int maxLevel)
        {
            var balanced = new List<FlipperManifestEntry>();
            int count = entries.Count;

            if (count == 1)
            {
                balanced.Add(new FlipperManifestEntry
                {
                    Name = entries[0].Name,
                    MinLevel = 1,
                    MaxLevel = maxLevel,
                    MinButthurt = 0,
                    MaxButthurt = 14,
                    Weight = Math.Max(1, entries[0].Weight),
                });
                return balanced;
            }

            if (count <= maxLevel)
            {
                for (int i = 0; i < count; i++)
                {
                    int minL = 1 + (i * maxLevel) / count;
                    int maxL = Math.Max(minL, ((i + 1) * maxLevel) / count);
                    if (i == count - 1) maxL = maxLevel;

                    balanced.Add(new FlipperManifestEntry
                    {
                        Name = entries[i].Name,
                        MinLevel = minL,
                        MaxLevel = maxL,
                        MinButthurt = 0,
                        MaxButthurt = 14,
                        Weight = Math.Max(1, entries[i].Weight),
                    });
                }
            }
            else
            {
                // High density (e.g. 40 animations in 30 or 3 levels):
                // Distribute level groups and partition mood tiers to maximize 2D matrix variety.
                int levelGroups = Math.Min(count, maxLevel);
                for (int g = 0; g < levelGroups; g++)
                {
                    int minL = 1 + (g * maxLevel) / levelGroups;
                    int maxL = Math.Max(minL, ((g + 1) * maxLevel) / levelGroups);
                    if (g == levelGroups - 1) maxL = maxLevel;

                    int startIdx = (g * count) / levelGroups;
                    int endIdx = ((g + 1) * count) / levelGroups;
                    int groupCount = endIdx - startIdx;

                    for (int j = 0; j < groupCount; j++)
                    {
                        int entryIdx = startIdx + j;
                        var (minMood, maxMood) = CalculateMoodSubRange(j, groupCount);

                        balanced.Add(new FlipperManifestEntry
                        {
                            Name = entries[entryIdx].Name,
                            MinLevel = minL,
                            MaxLevel = maxL,
                            MinButthurt = minMood,
                            MaxButthurt = maxMood,
                            Weight = Math.Max(1, entries[entryIdx].Weight),
                        });
                    }
                }
            }

            return balanced;
        }

        private static (int MinMood, int MaxMood) CalculateMoodSubRange(int index, int totalInGroup)
        {
            if (totalInGroup <= 1) return (0, 14);
            if (totalInGroup == 2)
            {
                return index == 0 ? (0, 7) : (8, 14);
            }
            if (totalInGroup == 3)
            {
                return index switch
                {
                    0 => (0, 4),   // Happy (0-4)
                    1 => (5, 9),   // Neutral (5-9)
                    _ => (10, 14),  // Angry (10-14)
                };
            }
            if (totalInGroup == 4)
            {
                return index switch
                {
                    0 => (0, 3),
                    1 => (4, 7),
                    2 => (8, 11),
                    _ => (12, 14),
                };
            }

            int minMood = (index * 15) / totalInGroup;
            int maxMood = Math.Max(minMood, ((index + 1) * 15) / totalInGroup - 1);
            if (index == totalInGroup - 1) maxMood = 14;
            return (minMood, maxMood);
        }

        private static List<FlipperManifestEntry> BalanceByMoodTiers(IReadOnlyList<FlipperManifestEntry> entries, int maxLevel)
        {
            var balanced = new List<FlipperManifestEntry>();
            int count = entries.Count;

            // Divide entries across level groups (each level group has mood tiers)
            int totalGroups = Math.Max(1, Math.Min(maxLevel, (count + 2) / 3));

            for (int g = 0; g < totalGroups; g++)
            {
                int minL = 1 + (g * maxLevel) / totalGroups;
                int maxL = Math.Max(minL, ((g + 1) * maxLevel) / totalGroups);
                if (g == totalGroups - 1) maxL = maxLevel;

                int startIdx = (g * count) / totalGroups;
                int endIdx = ((g + 1) * count) / totalGroups;
                int groupCount = endIdx - startIdx;

                for (int j = 0; j < groupCount; j++)
                {
                    int entryIdx = startIdx + j;
                    var (minMood, maxMood) = CalculateMoodSubRange(j, groupCount);

                    balanced.Add(new FlipperManifestEntry
                    {
                        Name = entries[entryIdx].Name,
                        MinLevel = minL,
                        MaxLevel = maxL,
                        MinButthurt = minMood,
                        MaxButthurt = maxMood,
                        Weight = Math.Max(1, entries[entryIdx].Weight),
                    });
                }
            }

            return balanced;
        }

        private static List<FlipperManifestEntry> BalanceByStageEvolution(IReadOnlyList<FlipperManifestEntry> entries, int maxLevel)
        {
            var balanced = new List<FlipperManifestEntry>();
            int count = entries.Count;
            if (count == 0) return balanced;

            if (count == 1)
            {
                balanced.Add(new FlipperManifestEntry
                {
                    Name = entries[0].Name,
                    MinLevel = 1,
                    MaxLevel = maxLevel,
                    MinButthurt = 0,
                    MaxButthurt = 14,
                    Weight = Math.Max(1, entries[0].Weight),
                });
                return balanced;
            }

            if (count == 2)
            {
                int splitLvl = maxLevel == 3 ? 1 : 14;
                balanced.Add(new FlipperManifestEntry
                {
                    Name = entries[0].Name,
                    MinLevel = 1,
                    MaxLevel = splitLvl,
                    MinButthurt = 0,
                    MaxButthurt = 14,
                    Weight = Math.Max(1, entries[0].Weight),
                });
                balanced.Add(new FlipperManifestEntry
                {
                    Name = entries[1].Name,
                    MinLevel = splitLvl + 1,
                    MaxLevel = maxLevel,
                    MinButthurt = 0,
                    MaxButthurt = 14,
                    Weight = Math.Max(1, entries[1].Weight),
                });
                return balanced;
            }

            // Stage thresholds: Baby, Teen, Adult
            (int MinL, int MaxL)[] stages = maxLevel switch
            {
                3 => [(1, 1), (2, 2), (3, 3)],
                _ => [(1, Math.Min(9, maxLevel)), (Math.Min(10, maxLevel), Math.Min(19, maxLevel)), (Math.Min(20, maxLevel), maxLevel)],
            };

            int numStages = stages.Length;
            for (int s = 0; s < numStages; s++)
            {
                int startIdx = (s * count) / numStages;
                int endIdx = ((s + 1) * count) / numStages;
                int stageCount = endIdx - startIdx;
                var (MinL, MaxL) = stages[s];

                for (int j = 0; j < stageCount; j++)
                {
                    int entryIdx = startIdx + j;
                    var (minMood, maxMood) = CalculateMoodSubRange(j, stageCount);

                    balanced.Add(new FlipperManifestEntry
                    {
                        Name = entries[entryIdx].Name,
                        MinLevel = MinL,
                        MaxLevel = MaxL,
                        MinButthurt = minMood,
                        MaxButthurt = maxMood,
                        Weight = Math.Max(1, entries[entryIdx].Weight),
                    });
                }
            }

            return balanced;
        }

        private static List<FlipperManifestEntry> BalanceByFillingGaps(IReadOnlyList<FlipperManifestEntry> entries, int maxLevel)
        {
            if (entries == null || entries.Count == 0) return [];

            // Clone entries preserving original list order (do NOT re-sort result)
            var result = entries.Select(e => new FlipperManifestEntry
            {
                Name = e.Name,
                MinLevel = Math.Clamp(e.MinLevel, 1, maxLevel),
                MaxLevel = Math.Clamp(e.MaxLevel, 1, maxLevel),
                MinButthurt = Math.Clamp(e.MinButthurt, 0, 14),
                MaxButthurt = Math.Clamp(e.MaxButthurt, 0, 14),
                Weight = Math.Max(1, e.Weight),
            }).ToList();

            // Fix any inverted bounds on individual entries
            foreach (var e in result)
            {
                if (e.MinLevel > e.MaxLevel) (e.MinLevel, e.MaxLevel) = (e.MaxLevel, e.MinLevel);
                if (e.MinButthurt > e.MaxButthurt) (e.MinButthurt, e.MaxButthurt) = (e.MaxButthurt, e.MinButthurt);
            }

            var currentMatrix = new FlipperScheduleMatrix(result, maxLevel);
            var gaps = currentMatrix.GetUncoveredCells();

            if (gaps.Count == 0) return result;

            // 1. Expand mood coverage to full range 0..14 for all entries
            foreach (var entry in result)
            {
                entry.MinButthurt = 0;
                entry.MaxButthurt = 14;
            }

            // 2. Work on a sorted view of references to fill level gaps while preserving 'result' list order
            var sortedByLevel = result.OrderBy(e => e.MinLevel).ThenBy(e => e.MaxLevel).ToList();

            // Anchor perimeter levels
            if (sortedByLevel[0].MinLevel > 1)
            {
                sortedByLevel[0].MinLevel = 1;
            }

            int maxCovered = sortedByLevel.Max(e => e.MaxLevel);
            if (maxCovered < maxLevel)
            {
                var highestEntry = sortedByLevel.OrderByDescending(e => e.MaxLevel).First();
                highestEntry.MaxLevel = maxLevel;
            }

            // 3. Fill intermediate level gaps between consecutive intervals
            int runningMaxLvl = sortedByLevel[0].MaxLevel;
            for (int i = 0; i < sortedByLevel.Count - 1; i++)
            {
                var current = sortedByLevel[i];
                var next = sortedByLevel[i + 1];

                if (runningMaxLvl < next.MinLevel - 1)
                {
                    current.MaxLevel = next.MinLevel - 1;
                    runningMaxLvl = current.MaxLevel;
                }
                else
                {
                    runningMaxLvl = Math.Max(runningMaxLvl, current.MaxLevel);
                }
            }

            return result;
        }
    }
}
