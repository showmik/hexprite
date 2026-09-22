using Hexprite.Core;
using Hexprite.Rendering;
using Hexprite.Services;
using Hexprite.ViewModels;
using System;
using System.Windows.Input;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Fuzz")]
    public class FontViewModelChaosFuzzerTests
    {
        public FontViewModelChaosFuzzerTests()
        {
            WpfTestHelper.EnsureApplication();
        }

        [Fact]
        public void FontViewModel_ShouldNotCrashUnderChaoticInput()
        {
            var vm = new FontViewModel();
            var doc = FontDocument.CreateNew(16, 16);
            vm.Document = doc;
            var fontCodeGen = new FontCodeGeneratorService();
            var fontImporter = new FontImportService();

            var random = new Random(1337);
            int iterations = FuzzTestHelper.GetIterationCount(defaultFastCount: 10000, deepCount: 10000);

            for (int i = 0; i < iterations; i++)
            {
                int action = random.Next(30);
                try
                {
                    switch (action)
                    {
                        case 0: SafeExecute(vm.UndoCommand); break;
                        case 1: SafeExecute(vm.RedoCommand); break;
                        case 2: SafeExecute(vm.ScrubStartedCommand); break;
                        case 3: SafeExecute(vm.ScrubEndedCommand); break;
                        case 4: SafeExecute(vm.KeyboardScrubStartedCommand); break;
                        case 5: SafeExecute(vm.KeyboardScrubEndedCommand); break;
                        case 6:
                            vm.SetActiveGlyph(random.Next(0, vm.Document.Glyphs.Count));
                            break;
                        case 7:
                            vm.CellHeight = random.Next(4, 32);
                            break;
                        case 8:
                            vm.MaxCellWidth = random.Next(4, 32);
                            break;
                        case 9:
                            vm.FontName = $"Font_{random.Next()}";
                            break;
                        case 10:
                            // Random pixel flip
                            int glyphIdx = random.Next(0, vm.Document.Glyphs.Count);
                            var glyph = vm.Document.Glyphs[glyphIdx];
                            if (glyph.Pixels.Length > 0)
                            {
                                int pixelIdx = random.Next(0, glyph.Pixels.Length);
                                glyph.Pixels[pixelIdx] = !glyph.Pixels[pixelIdx];
                            }
                            break;
                        case 11:
                            // Change bounds of a glyph
                            vm.SetActiveGlyph(random.Next(0, vm.Document.Glyphs.Count));
                            vm.ResizeActiveGlyph(random.Next(4, 32));
                            break;
                        case 12:
                            vm.Baseline = random.Next(-2, vm.CellHeight + 4);
                            break;
                        case 13:
                            vm.YAdvance = random.Next(1, 40);
                            break;
                        case 14:
                            vm.IsMonospaced = !vm.IsMonospaced;
                            break;
                        case 15:
                            vm.AutoAdvance = !vm.AutoAdvance;
                            break;
                        case 16:
                            vm.XAdvance = random.Next(0, 32);
                            break;
                        case 17:
                            vm.XOffset = random.Next(-10, 20);
                            break;
                        case 18:
                            vm.YOffset = random.Next(-10, 20);
                            break;
                        case 19:
                            vm.GlyphWidth = random.Next(1, 32);
                            break;
                        case 20:
                            vm.FirstChar = random.Next(0, 60);
                            break;
                        case 21:
                            vm.LastChar = random.Next(vm.FirstChar, Math.Min(255, vm.FirstChar + 40));
                            break;
                        case 22:
                            vm.BeginDrawing();
                            if (vm.ActiveGlyph != null)
                            {
                                vm.DrawPixel(random.Next(-2, vm.ActiveGlyph.Width + 2), random.Next(-2, vm.ActiveGlyph.Height + 2), random.Next(2) == 0);
                            }
                            vm.EndDrawing();
                            break;
                        case 23:
                            if (vm.ActiveGlyph != null && vm.ActiveGlyph.Width > 0 && vm.ActiveGlyph.Height > 0)
                            {
                                vm.TogglePixel(random.Next(0, vm.ActiveGlyph.Width), random.Next(0, vm.ActiveGlyph.Height));
                            }
                            break;
                        case 24:
                            vm.ExportFormat = (FontExportFormat)random.Next(0, 5);
                            if (vm.Document != null)
                            {
                                var exportSettings = new FontExportSettings
                                {
                                    FontName = vm.FontName,
                                    Format = vm.ExportFormat
                                };
                                string genCode = fontCodeGen.GenerateCode(vm.Document, exportSettings);
                                Assert.NotNull(genCode);
                            }
                            break;
                        case 25:
                            // Negative offset scrubbing and rollback
                            SafeExecute(vm.ScrubStartedCommand);
                            vm.XOffset = random.Next(-15, 0);
                            vm.YOffset = random.Next(-15, 0);
                            SafeExecute(vm.ScrubEndedCommand);
                            SafeExecute(vm.UndoCommand);
                            break;
                        case 26:
                            // Dynamic Flash Byte estimation across formats
                            if (vm.Document != null)
                            {
                                int flashBytes = vm.Document.EstimateFlashBytes((FontExportFormat)random.Next(0, 5));
                                Assert.True(flashBytes >= 0);
                            }
                            break;
                        case 27:
                            // Sparse glyph mutation
                            if (vm.Document?.Glyphs != null && vm.Document.Glyphs.Count > 2)
                            {
                                vm.Document.Glyphs.RemoveAt(random.Next(0, vm.Document.Glyphs.Count));
                                vm.SetActiveGlyph(0);
                            }
                            break;
                        case 28:
                            // 2D stride-preserving glyph re-normalization
                            if (vm.Document != null)
                            {
                                vm.Document.CellHeight = random.Next(4, 32);
                                vm.Document.MaxCellWidth = random.Next(4, 32);
                                vm.Document.NormalizeGlyphs();
                            }
                            break;
                        case 29:
                            // Live text preview generation
                            if (vm.Document != null)
                            {
                                var previewMask = FontPreviewRenderer.RenderPreviewText(vm.Document, "Chaos 123! \n \t");
                                Assert.NotNull(previewMask);
                            }
                            break;
                    }

                    if (i % 250 == 0 && vm.Document != null)
                    {
                        Assert.NotNull(vm.ActiveGlyph);
                        Assert.True(vm.Document.Glyphs.Count > 0);
                        Assert.True(vm.ActiveGlyph.Width > 0);
                        Assert.True(vm.ActiveGlyph.Height > 0);
                        Assert.Equal(vm.ActiveGlyph.Width * vm.ActiveGlyph.Height, vm.ActiveGlyph.Pixels.Length);
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Crash during chaos action {action} at iteration {i}: {ex.Message}", ex);
                }
            }

            Assert.True(true);
        }

        private static void SafeExecute(ICommand command, object? parameter = null)
        {
            if (command != null && command.CanExecute(parameter))
            {
                command.Execute(parameter);
            }
        }
    }
}
