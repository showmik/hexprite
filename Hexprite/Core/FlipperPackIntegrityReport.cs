using System;
using System.Collections.Generic;
using System.Linq;

namespace Hexprite.Core
{
    public class FlipperPackIntegrityIssue
    {
        public string AnimationName { get; set; } = string.Empty;
        public string Severity { get; set; } = "Warning"; // "Error", "Warning", "Info"
        public string Message { get; set; } = string.Empty;
    }

    public class FlipperPackIntegrityReport
    {
        public bool IsValid => Issues.All(i => i.Severity != "Error");
        public int TotalAnimations { get; set; }
        public int TotalFrames { get; set; }
        public double MatrixCoveragePercent { get; set; }
        public int MatrixCoveredCells { get; set; }
        public List<FlipperPackIntegrityIssue> Issues { get; } = [];
    }
}
