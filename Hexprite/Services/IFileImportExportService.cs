using System.Collections.Generic;

namespace Hexprite.Services
{
    public class DetectedSprite
    {
        public string Name { get; set; } = string.Empty;
        public int Width { get; set; }
        public int Height { get; set; }
        public int FrameCount { get; set; } = 1;
        public string CodeSnippet { get; set; } = string.Empty;
        public Hexprite.Core.ExportFormat Format { get; set; }
    }

    public interface IFileImportExportService
    {
        List<DetectedSprite> ExtractSpritesFromFile(string filePath);
        string UpdateSpriteInFile(string filePath, string variableName, string newCodeSnippet, int? newWidth = null, int? newHeight = null, int? newFrameCount = null);
        string RestoreSpriteInFile(string filePath);
        bool HasBackup(string filePath);
        void CleanupOldBackups(int maxAgeDays = 30);
    }
}
