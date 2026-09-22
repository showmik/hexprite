using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Hexprite.Core
{
    /// <summary>
    /// Categorization of life stage boundaries for Flipper Zero schedule entries.
    /// </summary>
    public enum FlipperStageCategory
    {
        Baby,
        Teen,
        Adult,
        BabyTeen,
        TeenAdult,
        AllStages,
        Spanning,
    }

    public enum FlipperValidationSeverity
    {
        Info,
        Warning,
        Error,
    }

    public record FlipperValidationDiagnostic(
        FlipperValidationSeverity Severity,
        string Code,
        string Message,
        string? PropertyName = null
    );

    /// <summary>
    /// Represents a single animation entry in a Flipper Zero manifest.txt file.
    /// Used by custom firmware (Momentum, Xtreme, Unleashed) and stock firmware to schedule animations.
    /// </summary>
    public class FlipperManifestEntry
    {
        public string Name { get; set; } = string.Empty;
        public int MinLevel { get; set; } = 1;
        public int MaxLevel { get; set; } = 3;
        public int MinButthurt { get; set; }
        public int MaxButthurt { get; set; } = 14;
        public int Weight { get; set; } = 1;

        public FlipperManifestEntry Clone() => new()
        {
            Name = Name,
            MinLevel = MinLevel,
            MaxLevel = MaxLevel,
            MinButthurt = MinButthurt,
            MaxButthurt = MaxButthurt,
            Weight = Weight,
        };

        public string ToManifestBlock()
        {
            var sb = new StringBuilder();
            sb.AppendLine(CultureInfo.InvariantCulture, $"Name: {Name}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"Min butthurt: {MinButthurt}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"Max butthurt: {MaxButthurt}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"Min level: {MinLevel}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"Max level: {MaxLevel}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"Weight: {Weight}");
            return sb.ToString();
        }

        public List<FlipperValidationDiagnostic> Validate(bool isMomentum = true)
        {
            var diagnostics = new List<FlipperValidationDiagnostic>();

            if (string.IsNullOrWhiteSpace(Name))
            {
                diagnostics.Add(new FlipperValidationDiagnostic(
                    FlipperValidationSeverity.Error,
                    "FZ001",
                    "Animation Name cannot be empty.",
                    nameof(Name)));
            }

            int maxAllowedLevel = isMomentum ? 30 : 3;

            if (MinLevel < 1 || MinLevel > maxAllowedLevel)
            {
                diagnostics.Add(new FlipperValidationDiagnostic(
                    FlipperValidationSeverity.Error,
                    "FZ002",
                    string.Create(CultureInfo.InvariantCulture, $"Min level must be between 1 and {maxAllowedLevel} (current: {MinLevel})."),
                    nameof(MinLevel)));
            }

            if (MaxLevel < MinLevel || MaxLevel > maxAllowedLevel)
            {
                diagnostics.Add(new FlipperValidationDiagnostic(
                    FlipperValidationSeverity.Error,
                    "FZ003",
                    string.Create(CultureInfo.InvariantCulture, $"Max level must be >= Min level ({MinLevel}) and <= {maxAllowedLevel} (current: {MaxLevel})."),
                    nameof(MaxLevel)));
            }

            if (MinButthurt < 0 || MinButthurt > 14)
            {
                diagnostics.Add(new FlipperValidationDiagnostic(
                    FlipperValidationSeverity.Error,
                    "FZ004",
                    string.Create(CultureInfo.InvariantCulture, $"Min butthurt must be between 0 and 14 (current: {MinButthurt})."),
                    nameof(MinButthurt)));
            }

            if (MaxButthurt < MinButthurt || MaxButthurt > 14)
            {
                diagnostics.Add(new FlipperValidationDiagnostic(
                    FlipperValidationSeverity.Error,
                    "FZ005",
                    string.Create(CultureInfo.InvariantCulture, $"Max butthurt must be >= Min butthurt ({MinButthurt}) and <= 14 (current: {MaxButthurt})."),
                    nameof(MaxButthurt)));
            }

            if (Weight <= 0)
            {
                diagnostics.Add(new FlipperValidationDiagnostic(
                    FlipperValidationSeverity.Error,
                    "FZ006",
                    string.Create(CultureInfo.InvariantCulture, $"Weight must be greater than 0 (current: {Weight})."),
                    nameof(Weight)));
            }
            else if (Weight > 100)
            {
                diagnostics.Add(new FlipperValidationDiagnostic(
                    FlipperValidationSeverity.Warning,
                    "FZ007",
                    string.Create(CultureInfo.InvariantCulture, $"Weight is unusually high ({Weight}). Standard community range is 1-10."),
                    nameof(Weight)));
            }

            return diagnostics;
        }
    }

    /// <summary>
    /// Represents the full contents of a Flipper Zero manifest.txt file.
    /// </summary>
    public class FlipperManifest
    {
        public string Filetype { get; set; } = "Flipper Animation Manifest";
        public int Version { get; set; } = 1;
        public List<FlipperManifestEntry> Entries { get; set; } = [];

        public static FlipperManifest Parse(string content)
        {
            var manifest = new FlipperManifest();
            if (string.IsNullOrWhiteSpace(content)) return manifest;

            var lines = content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            FlipperManifestEntry? currentEntry = null;

            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.StartsWith('#') || string.IsNullOrEmpty(line)) continue;

                var parts = line.Split(':', 2);
                if (parts.Length != 2) continue;

                string key = parts[0].Trim();
                string val = parts[1].Trim();

                if (key.Equals("Filetype", StringComparison.OrdinalIgnoreCase))
                {
                    manifest.Filetype = val;
                }
                else if (key.Equals("Version", StringComparison.OrdinalIgnoreCase) && int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ver))
                {
                    manifest.Version = ver;
                }
                else if (key.Equals("Name", StringComparison.OrdinalIgnoreCase))
                {
                    currentEntry = new FlipperManifestEntry { Name = val };
                    manifest.Entries.Add(currentEntry);
                }
                else if (currentEntry != null)
                {
                    if (key.Equals("Min level", StringComparison.OrdinalIgnoreCase) && int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int minLvl))
                        currentEntry.MinLevel = minLvl;
                    else if (key.Equals("Max level", StringComparison.OrdinalIgnoreCase) && int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int maxLvl))
                        currentEntry.MaxLevel = maxLvl;
                    else if (key.Equals("Min butthurt", StringComparison.OrdinalIgnoreCase) && int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int minBh))
                        currentEntry.MinButthurt = minBh;
                    else if (key.Equals("Max butthurt", StringComparison.OrdinalIgnoreCase) && int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int maxBh))
                        currentEntry.MaxButthurt = maxBh;
                    else if (key.Equals("Weight", StringComparison.OrdinalIgnoreCase) && int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int weight))
                        currentEntry.Weight = weight;
                }
            }

            return manifest;
        }

        public string Serialize()
        {
            var sb = new StringBuilder();
            sb.AppendLine(CultureInfo.InvariantCulture, $"Filetype: {Filetype}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"Version: {Version}");
            sb.AppendLine();

            for (int i = 0; i < Entries.Count; i++)
            {
                sb.Append(Entries[i].ToManifestBlock());
                if (i < Entries.Count - 1)
                {
                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }

        public List<FlipperValidationDiagnostic> Validate(bool isMomentum = true)
        {
            var diagnostics = new List<FlipperValidationDiagnostic>();

            if (Entries.Count == 0)
            {
                diagnostics.Add(new FlipperValidationDiagnostic(
                    FlipperValidationSeverity.Warning,
                    "FZ010",
                    "Manifest contains 0 animation entries."));
                return diagnostics;
            }

            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in Entries)
            {
                if (!seenNames.Add(entry.Name))
                {
                    diagnostics.Add(new FlipperValidationDiagnostic(
                        FlipperValidationSeverity.Error,
                        "FZ011",
                        $"Duplicate animation name '{entry.Name}' found in manifest.",
                        nameof(entry.Name)));
                }

                diagnostics.AddRange(entry.Validate(isMomentum));
            }

            return diagnostics;
        }
    }

    /// <summary>
    /// Represents the parsed metadata of an individual animation folder's meta.txt.
    /// </summary>
    public class FlipperAnimationMeta
    {
        public string Filetype { get; set; } = "Flipper Animation";
        public int Version { get; set; } = 1;
        public int Width { get; set; } = 128;
        public int Height { get; set; } = 64;
        public int PassiveFrames { get; set; }
        public int ActiveFrames { get; set; }
        public int[] FramesOrder { get; set; } = [];
        public int ActiveCycles { get; set; } = 1;
        public int FrameRate { get; set; } = 5;
        public int Duration { get; set; } = 3600;
        public int ActiveCooldown { get; set; }
        public int BubbleSlots { get; set; }
        public List<FlipperSpeechBubble> SpeechBubbles { get; set; } = [];

        public static FlipperAnimationMeta Parse(string content)
        {
            var meta = new FlipperAnimationMeta();
            if (string.IsNullOrWhiteSpace(content)) return meta;

            var lines = content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            FlipperSpeechBubble? currentBubble = null;

            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.StartsWith('#') || string.IsNullOrEmpty(line)) continue;

                var parts = line.Split(':', 2);
                if (parts.Length != 2) continue;

                string key = parts[0].Trim();
                string val = parts[1].Trim();

                if (key.Equals("Filetype", StringComparison.OrdinalIgnoreCase))
                    meta.Filetype = val;
                else if (key.Equals("Version", StringComparison.OrdinalIgnoreCase) && int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ver))
                    meta.Version = ver;
                else if (key.Equals("Width", StringComparison.OrdinalIgnoreCase) && int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int w))
                    meta.Width = w;
                else if (key.Equals("Height", StringComparison.OrdinalIgnoreCase) && int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int h))
                    meta.Height = h;
                else if (key.Equals("Passive frames", StringComparison.OrdinalIgnoreCase) && int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int pf))
                    meta.PassiveFrames = pf;
                else if (key.Equals("Active frames", StringComparison.OrdinalIgnoreCase) && int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int af))
                    meta.ActiveFrames = af;
                else if (key.Equals("Active cycles", StringComparison.OrdinalIgnoreCase) && int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ac))
                    meta.ActiveCycles = ac;
                else if (key.Equals("Frame rate", StringComparison.OrdinalIgnoreCase) && int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int fr))
                    meta.FrameRate = fr;
                else if (key.Equals("Duration", StringComparison.OrdinalIgnoreCase) && int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int dur))
                    meta.Duration = dur;
                else if (key.Equals("Active cooldown", StringComparison.OrdinalIgnoreCase) && int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int cd))
                    meta.ActiveCooldown = cd;
                else if (key.Equals("Bubble slots", StringComparison.OrdinalIgnoreCase) && int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int bs))
                    meta.BubbleSlots = bs;
                else if (key.Equals("Frames order", StringComparison.OrdinalIgnoreCase))
                {
                    var order = new List<int>();
                    foreach (var token in val.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int idx))
                            order.Add(idx);
                    }
                    meta.FramesOrder = [.. order];
                }
                else if (key.Equals("Slot", StringComparison.OrdinalIgnoreCase) && int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int slotIdx))
                {
                    currentBubble = new FlipperSpeechBubble { SlotIndex = slotIdx };
                    meta.SpeechBubbles.Add(currentBubble);
                }
                else if (currentBubble != null)
                {
                    if (key.Equals("StartFrame", StringComparison.OrdinalIgnoreCase) && int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int sf))
                        currentBubble.StartFrame = sf;
                    else if (key.Equals("EndFrame", StringComparison.OrdinalIgnoreCase) && int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ef))
                        currentBubble.EndFrame = ef;
                    else if (key.Equals("Text", StringComparison.OrdinalIgnoreCase))
                        currentBubble.Text = val;
                    else if (key.Equals("X", StringComparison.OrdinalIgnoreCase) && int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int bx))
                        currentBubble.X = bx;
                    else if (key.Equals("Y", StringComparison.OrdinalIgnoreCase) && int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int by))
                        currentBubble.Y = by;
                    else if (key.Equals("AlignH", StringComparison.OrdinalIgnoreCase))
                        currentBubble.AlignH = val;
                    else if (key.Equals("AlignV", StringComparison.OrdinalIgnoreCase))
                        currentBubble.AlignV = val;
                }
            }

            return meta;
        }

        public string Serialize()
        {
            var sb = new StringBuilder();
            sb.AppendLine(CultureInfo.InvariantCulture, $"Filetype: {Filetype}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"Version: {Version}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"Width: {Width}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"Height: {Height}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"Passive frames: {PassiveFrames}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"Active frames: {ActiveFrames}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"Frames order: {string.Join(' ', FramesOrder)}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"Active cycles: {ActiveCycles}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"Frame rate: {FrameRate}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"Duration: {Duration}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"Active cooldown: {ActiveCooldown}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"Bubble slots: {BubbleSlots}");

            foreach (var bubble in SpeechBubbles)
            {
                sb.AppendLine();
                sb.AppendLine(CultureInfo.InvariantCulture, $"Slot: {bubble.SlotIndex}");
                if (bubble.StartFrame > 0) sb.AppendLine(CultureInfo.InvariantCulture, $"StartFrame: {bubble.StartFrame}");
                if (bubble.EndFrame > 0) sb.AppendLine(CultureInfo.InvariantCulture, $"EndFrame: {bubble.EndFrame}");
                sb.AppendLine(CultureInfo.InvariantCulture, $"Text: {bubble.Text}");
                sb.AppendLine(CultureInfo.InvariantCulture, $"X: {bubble.X}");
                sb.AppendLine(CultureInfo.InvariantCulture, $"Y: {bubble.Y}");
                if (!string.IsNullOrEmpty(bubble.AlignH)) sb.AppendLine(CultureInfo.InvariantCulture, $"AlignH: {bubble.AlignH}");
                if (!string.IsNullOrEmpty(bubble.AlignV)) sb.AppendLine(CultureInfo.InvariantCulture, $"AlignV: {bubble.AlignV}");
            }

            return sb.ToString();
        }

        public List<FlipperValidationDiagnostic> Validate(int physicalFrameCount)
        {
            var diagnostics = new List<FlipperValidationDiagnostic>();

            if (Width <= 0 || Height <= 0 || Width > 128 || Height > 64)
            {
                diagnostics.Add(new FlipperValidationDiagnostic(
                    FlipperValidationSeverity.Error,
                    "FZM001",
                    string.Create(CultureInfo.InvariantCulture, $"Dimensions {Width}x{Height} exceed maximum Flipper screen limits (128x64).")));
            }

            if (FrameRate <= 0 || FrameRate > 30)
            {
                diagnostics.Add(new FlipperValidationDiagnostic(
                    FlipperValidationSeverity.Warning,
                    "FZM002",
                    string.Create(CultureInfo.InvariantCulture, $"Frame rate {FrameRate} fps is outside standard Flipper range (1-30 fps).")));
            }

            if (FramesOrder.Length == 0)
            {
                diagnostics.Add(new FlipperValidationDiagnostic(
                    FlipperValidationSeverity.Error,
                    "FZM003",
                    "Frames order cannot be empty."));
            }
            else
            {
                for (int i = 0; i < FramesOrder.Length; i++)
                {
                    if (FramesOrder[i] < 0 || FramesOrder[i] >= physicalFrameCount)
                    {
                        diagnostics.Add(new FlipperValidationDiagnostic(
                            FlipperValidationSeverity.Error,
                            "FZM004",
                            string.Create(CultureInfo.InvariantCulture, $"Frames order references frame index {FramesOrder[i]} which does not exist (available frames: 0..{physicalFrameCount - 1}).")));
                        break;
                    }
                }
            }

            if (PassiveFrames + ActiveFrames != FramesOrder.Length && FramesOrder.Length > 0)
            {
                diagnostics.Add(new FlipperValidationDiagnostic(
                    FlipperValidationSeverity.Warning,
                    "FZM005",
                    string.Create(CultureInfo.InvariantCulture, $"Passive frames ({PassiveFrames}) + Active frames ({ActiveFrames}) != Total frames in order ({FramesOrder.Length}).")));
            }

            return diagnostics;
        }
    }
}
