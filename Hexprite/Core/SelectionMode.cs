namespace Hexprite.Core
{
    /// <summary>
    /// Specifies the boolean logic for modifying an existing selection.
    /// </summary>
    public enum SelectionMode
    {
        /// <summary>Replaces the existing selection with the new shape.</summary>
        Replace,
        /// <summary>Adds the new shape to the existing selection.</summary>
        Add,
        /// <summary>Removes the new shape from the existing selection.</summary>
        Subtract,
        /// <summary>Keeps only the intersection of the existing selection and the new shape.</summary>
        Intersect,
    }
}
