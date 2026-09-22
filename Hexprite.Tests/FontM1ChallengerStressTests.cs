using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels;
using Xunit;

namespace Hexprite.Tests
{
    [Collection("AutosaveDirectory")]
    [Trait("Category", "Unit")]
    public class FontM1ChallengerStressTests : IDisposable
    {
        private readonly string _testAutosaveDir;

        public FontM1ChallengerStressTests()
        {
            WpfTestHelper.EnsureApplication();
            _testAutosaveDir = Path.Combine(Path.GetTempPath(), "Hexprite_ChallengerM1_2_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(_testAutosaveDir);
            AutosaveService.SetCustomAutosaveDirectory(_testAutosaveDir);
        }

        public void Dispose()
        {
            AutosaveService.SetCustomAutosaveDirectory(null);
            try
            {
                if (Directory.Exists(_testAutosaveDir))
                {
                    Directory.Delete(_testAutosaveDir, recursive: true);
                }
            }
            catch { }
        }

        // ═════════════════════════════════════════════════════════════════════
        // PART 1: AUTOSAVE CONCURRENCY & CANCELLATION STRESS
        // ═════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task Autosave_RapidRepeatedDirtyAndManualSave_NoTmpLeaks_NoCorruptEnvelopes()
        {
            // Challenge requirement:
            // "Verify that rapid repeated dirty states and manual saves cancel in-flight tasks cleanly.
            // Check that no .tmp files are left behind and no corrupt envelopes are created."

            using var svc = new AutosaveService();
            string docId = "stress_" + Guid.NewGuid().ToString("N");
            bool isDirty = true;
            var doc = FontDocument.CreateNew(8, 8);

            svc.StartFontAutosaveLoop(docId, () => doc, () => isDirty);

            // Run 50 rapid cycles of dirty state toggling, immediate saves, and manual clean saves
            for (int i = 0; i < 50; i++)
            {
                isDirty = true;
                doc.FontName = $"StressFont_{i}";
                doc.CellHeight = 8 + (i % 8);
                doc.NormalizeGlyphs();

                // Trigger in-flight save
                svc.TriggerImmediateAutosave();

                if (i % 3 == 0)
                {
                    // Interleave with SaveImmediatelyAsync
                    _ = svc.SaveImmediatelyAsync();
                }

                // Small jitter to catch different execution points in WriteEnvelopeAtomicAsync
                if (i % 4 == 0)
                {
                    await Task.Delay(1);
                }

                // Simulate user manual save (ClearCurrentAutosave and mark clean)
                isDirty = false;
                svc.ClearCurrentAutosave();
            }

            // Allow any in-flight background worker thread to finish its abort/cleanup
            await Task.Delay(100);

            // Verify: NO .tmp files left behind
            var tmpFiles = Directory.GetFiles(_testAutosaveDir, "*.tmp", SearchOption.AllDirectories);
            Assert.Empty(tmpFiles);

            // Verify: If any .json envelope file remains, it MUST NOT be corrupt
            var jsonFiles = Directory.GetFiles(_testAutosaveDir, "*.json", SearchOption.AllDirectories);
            foreach (var file in jsonFiles)
            {
                var envelope = svc.LoadAutosaveEnvelope(file);
                Assert.NotNull(envelope);
                Assert.Equal(DocumentMode.Font, envelope.Mode);
                Assert.NotNull(envelope.FontDocument);
            }
        }

        [Fact]
        public async Task Autosave_HighConcurrency_ParallelImmediateSaveAndClear_CleanCancellation()
        {
            using var svc = new AutosaveService();
            string docId = "parallel_" + Guid.NewGuid().ToString("N");
            bool isDirty = true;
            var doc = FontDocument.CreateNew(16, 16);

            svc.StartFontAutosaveLoop(docId, () => doc, () => isDirty);

            // Concurrently fire 20 tasks trying to SaveImmediatelyAsync while 20 tasks try to ClearCurrentAutosave
            var tasks = new List<Task>();
            for (int i = 0; i < 40; i++)
            {
                if (i % 2 == 0)
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        isDirty = true;
                        await svc.SaveImmediatelyAsync();
                    }));
                }
                else
                {
                    tasks.Add(Task.Run(() =>
                    {
                        isDirty = false;
                        svc.ClearCurrentAutosave();
                    }));
                }
            }

            await Task.WhenAll(tasks);

            // Final clear to ensure clean termination
            svc.ClearCurrentAutosave();
            await Task.Delay(50);

            // Verify: zero .tmp files
            var tmpFiles = Directory.GetFiles(_testAutosaveDir, "*.tmp", SearchOption.AllDirectories);
            Assert.Empty(tmpFiles);

            // Verify no corrupted files in recovery
            var available = svc.GetAvailableAutosaves().ToList();
            foreach (var path in available)
            {
                var envelope = svc.LoadAutosaveEnvelope(path);
                Assert.NotNull(envelope);
            }
        }

        [Fact]
        public async Task Autosave_InFlightCancellation_DeletesTempFileImmediately()
        {
            using var svc = new AutosaveService();
            string docId = "cancel_test_" + Guid.NewGuid().ToString("N");
            var doc = FontDocument.CreateNew(32, 32);

            svc.StartFontAutosaveLoop(docId, () => doc, () => true);

            // Launch save task and immediately cancel
            var saveTask = svc.SaveImmediatelyAsync();
            svc.ClearCurrentAutosave();
            await saveTask;

            string expectedTmp = Path.Combine(_testAutosaveDir, $"recovery_{docId}.json.tmp");
            string expectedJson = Path.Combine(_testAutosaveDir, $"recovery_{docId}.json");

            Assert.False(File.Exists(expectedTmp), "In-flight cancellation must not leave temp file");
            Assert.False(File.Exists(expectedJson), "ClearCurrentAutosave must ensure json recovery file does not exist");
        }

        // ═════════════════════════════════════════════════════════════════════
        // PART 2: FONTDOCUMENT EXTREME BOUNDARY & NULL RESILIENCE
        // ═════════════════════════════════════════════════════════════════════

        [Theory]
        [InlineData(-10, -5, 32, 126)]      // Negative width & height
        [InlineData(0, 0, 32, 126)]          // Zero width & height
        [InlineData(1000, 1000, 32, 126)]    // Exceeds max 512 bound
        [InlineData(8, 8, 120, 30)]          // Inverted character range (FirstChar > LastChar)
        [InlineData(8, 8, -50, -10)]         // Negative character range
        [InlineData(8, 8, 0, 5000)]          // Excessively wide character range (> 2048)
        [InlineData(8, 8, 0x110000, 0x110010)] // Beyond Unicode max 0x10FFFF
        public void FontDocument_CreateNew_ExtremeInputs_ClampsSafelyWithoutException(
            int cellW, int cellH, int firstChar, int lastChar)
        {
            var doc = FontDocument.CreateNew(cellW, cellH, firstChar, lastChar);

            Assert.NotNull(doc);
            Assert.InRange(doc.MaxCellWidth, 1, 512);
            Assert.InRange(doc.CellHeight, 1, 512);
            Assert.InRange(doc.FirstChar, 0, 0x10FFFF);
            Assert.InRange(doc.LastChar, 0, 0x10FFFF);
            Assert.True(doc.LastChar >= doc.FirstChar, "LastChar must be >= FirstChar");
            Assert.True(doc.GlyphCount <= 2049, "GlyphCount must be clamped to safe memory bounds");
            Assert.Equal(doc.GlyphCount, doc.Glyphs.Count);

            foreach (var glyph in doc.Glyphs)
            {
                Assert.NotNull(glyph);
                Assert.Equal(doc.CellHeight, glyph.Height);
                Assert.Equal(doc.MaxCellWidth, glyph.Width);
                Assert.NotNull(glyph.Pixels);
                Assert.Equal(doc.MaxCellWidth * doc.CellHeight, glyph.Pixels.Length);
            }
        }

        [Fact]
        public void FontDocument_Clone_NullCollectionsAndNullElements_ReturnsCleanSafeCopy()
        {
            var doc = new FontDocument
            {
                FontName = null!,
                Glyphs = null!,
                KerningPairs = null!,
                PreMonoWidths = null!,
                ExportSettings = null!,
            };

            var clone = doc.Clone();

            Assert.NotNull(clone);
            Assert.NotNull(clone.FontName);
            Assert.NotNull(clone.Glyphs);
            Assert.Empty(clone.Glyphs);
            Assert.NotNull(clone.KerningPairs);
            Assert.Empty(clone.KerningPairs);
            Assert.NotNull(clone.PreMonoWidths);
            Assert.Empty(clone.PreMonoWidths);
            Assert.NotNull(clone.ExportSettings);

            // Document with lists containing null elements
            var docWithNulls = new FontDocument
            {
                Glyphs = new List<GlyphState> { null!, new GlyphState { CodePoint = 65, Width = 8, Height = 8, Pixels = new bool[64] }, null! },
                KerningPairs = new List<KerningPair> { null!, new KerningPair { Left = 65, Right = 86, Adjustment = -1 }, null! },
            };

            var clone2 = docWithNulls.Clone();
            Assert.NotNull(clone2);
            Assert.Single(clone2.Glyphs);
            Assert.Equal(65, clone2.Glyphs[0].CodePoint);
            Assert.Single(clone2.KerningPairs);
            Assert.Equal(65, clone2.KerningPairs[0].Left);
        }

        [Fact]
        public void FontDocument_NormalizeGlyphs_ExtremeCorruptedState_SanitizesCompletely()
        {
            // Create a severely corrupted document
            var doc = new FontDocument
            {
                CellHeight = -20,
                MaxCellWidth = -10,
                Baseline = -50,
                YAdvance = 0,
                FirstChar = 200,
                LastChar = 50, // Inverted
                Glyphs = null!,
                KerningPairs = null!,
                PreMonoWidths = null!,
                ExportSettings = null!,
            };

            bool changed = doc.NormalizeGlyphs();
            Assert.True(changed);

            // Verify metrics sanitized
            Assert.InRange(doc.CellHeight, 1, 512);
            Assert.InRange(doc.MaxCellWidth, 1, 512);
            Assert.InRange(doc.Baseline, 0, doc.CellHeight);
            Assert.InRange(doc.YAdvance, 1, 512);
            Assert.True(doc.FirstChar <= doc.LastChar);
            Assert.NotNull(doc.Glyphs);
            Assert.Equal(doc.GlyphCount, doc.Glyphs.Count);
            Assert.NotNull(doc.KerningPairs);
            Assert.NotNull(doc.PreMonoWidths);
            Assert.NotNull(doc.ExportSettings);

            // Now test with corrupted glyph instances in the list
            doc.Glyphs = new List<GlyphState>
            {
                null!, // null element
                new GlyphState // null pixels
                {
                    CodePoint = doc.FirstChar,
                    Width = 8,
                    Height = 8,
                    Pixels = null!,
                },
                new GlyphState // mismatched pixels length
                {
                    CodePoint = doc.FirstChar + 1,
                    Width = 4,
                    Height = 4,
                    Pixels = new bool[3], // invalid length
                },
                new GlyphState // negative dimensions
                {
                    CodePoint = doc.FirstChar + 2,
                    Width = -5,
                    Height = -5,
                    Pixels = new bool[10],
                },
            };

            bool changed2 = doc.NormalizeGlyphs();
            Assert.True(changed2);

            Assert.Equal(doc.GlyphCount, doc.Glyphs.Count);
            for (int i = 0; i < doc.Glyphs.Count; i++)
            {
                var g = doc.Glyphs[i];
                Assert.NotNull(g);
                Assert.Equal(doc.FirstChar + i, g.CodePoint);
                Assert.InRange(g.Width, 1, 512);
                Assert.Equal(doc.CellHeight, g.Height);
                Assert.NotNull(g.Pixels);
                Assert.Equal(g.Width * g.Height, g.Pixels.Length);
            }
        }

        [Fact]
        public void FontGlyph_ConstructorsAndFactory_NullResilient()
        {
            // FromGlyphState with null
            var fgNull = FontGlyph.FromGlyphState(null!);
            Assert.NotNull(fgNull);

            // FromGlyphState with null pixels
            var stateWithNullPixels = new GlyphState { CodePoint = 65, Width = 8, Height = 8, Pixels = null! };
            var fgNullPixels = FontGlyph.FromGlyphState(stateWithNullPixels);
            Assert.NotNull(fgNullPixels);
            Assert.NotNull(fgNullPixels.Pixels);
            Assert.Empty(fgNullPixels.Pixels);

            // Parametric constructor
            var fg = new FontGlyph(65, 8, 12);
            Assert.Equal(65, fg.CodePoint);
            Assert.Equal(8, fg.Width);
            Assert.Equal(12, fg.Height);
            Assert.Equal(9, fg.XAdvance);
            Assert.Equal(96, fg.Pixels.Length);
        }

        [Fact]
        public void FontDocument_EstimateAdafruitGfxBytes_NullCollectionsAndPixels_Safe()
        {
            var doc = new FontDocument
            {
                Glyphs = null!,
            };
            int bytes1 = doc.EstimateAdafruitGfxBytes();
            Assert.Equal(13, bytes1);

            doc.Glyphs = new List<GlyphState>
            {
                null!,
                new GlyphState { Pixels = null! },
                new GlyphState { Width = 8, Height = 8, Pixels = new bool[64] },
            };
            int bytes2 = doc.EstimateAdafruitGfxBytes();
            Assert.True(bytes2 >= 7);
        }
    }
}
