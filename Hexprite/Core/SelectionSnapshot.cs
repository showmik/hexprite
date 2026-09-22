using System;
using System.Collections.Generic;

namespace Hexprite.Core
{
    /// <summary>
    /// Captures a complete snapshot of the selection state for undo/redo history.
    /// </summary>
    public class SelectionSnapshot
    {
        public bool HasActiveSelection { get; set; }
        public bool IsSelecting { get; set; }
        public bool IsFloating { get; set; }
        public bool IsDragging { get; set; }
        public bool IsTransforming { get; set; }
        public TransformHandle ActiveTransformHandle { get; set; }
        public bool[,]? OriginalFloatingPixels { get; set; }
        public int OriginalFloatingX { get; set; }
        public int OriginalFloatingY { get; set; }
        public int OriginalFloatingWidth { get; set; }
        public int OriginalFloatingHeight { get; set; }
        public double RotationAngle { get; set; }

        // Pristine pixels: the original lifted/pasted data, never resampled.
        // Survives across transform sessions for lossless resample source.
        public bool[,]? PristineFloatingPixels { get; set; }
        public int PristineFloatingWidth { get; set; }
        public int PristineFloatingHeight { get; set; }
        public int MinX { get; set; }
        public int MaxX { get; set; }
        public int MinY { get; set; }
        public int MaxY { get; set; }
        public bool[,]? Mask { get; set; }
        public bool[,]? FloatingPixels { get; set; }
        public int FloatingX { get; set; }
        public int FloatingY { get; set; }
        public int FloatingWidth { get; set; }
        public int FloatingHeight { get; set; }
        public List<PixelPoint> LassoPoints { get; set; } = [];

        /// <summary>Creates a deep copy of the selection snapshot.</summary>
        public SelectionSnapshot Clone(bool includeOriginalFloatingPixels = true)
        {
            var clone = new SelectionSnapshot
            {
                HasActiveSelection = HasActiveSelection,
                IsSelecting = IsSelecting,
                IsFloating = IsFloating,
                IsDragging = IsDragging,
                IsTransforming = IsTransforming,
                ActiveTransformHandle = ActiveTransformHandle,
                OriginalFloatingX = OriginalFloatingX,
                OriginalFloatingY = OriginalFloatingY,
                OriginalFloatingWidth = OriginalFloatingWidth,
                OriginalFloatingHeight = OriginalFloatingHeight,
                RotationAngle = RotationAngle,
                MinX = MinX,
                MaxX = MaxX,
                MinY = MinY,
                MaxY = MaxY,
                FloatingX = FloatingX,
                FloatingY = FloatingY,
                FloatingWidth = FloatingWidth,
                FloatingHeight = FloatingHeight,
                PristineFloatingWidth = PristineFloatingWidth,
                PristineFloatingHeight = PristineFloatingHeight,
            };

            if (Mask != null)
                clone.Mask = (bool[,])Mask.Clone();

            if (FloatingPixels != null)
                clone.FloatingPixels = (bool[,])FloatingPixels.Clone();

            if (includeOriginalFloatingPixels && OriginalFloatingPixels != null)
                clone.OriginalFloatingPixels = (bool[,])OriginalFloatingPixels.Clone();

            if (PristineFloatingPixels != null)
                clone.PristineFloatingPixels = (bool[,])PristineFloatingPixels.Clone();

            if (LassoPoints != null)
                clone.LassoPoints = [.. LassoPoints];

            return clone;
        }
    }
}
