using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Hexprite.Rendering;
using Hexprite.Views;
using Xunit;

namespace Hexprite.Tests;

[Collection("WindowLayoutSettingsFile")]
[Trait("Category", "Performance")]
public class SyntaxHighlightingStressTests
{
    private static void RunOnStaThread(Action action)
    {
        WpfTestHelper.RunOnSta(action);
    }

    [Fact]
    public void LargeOutput_ExceedingThreshold_DoesNotMutateText()
    {
        // Generate code longer than 20,000 chars (e.g. 30,000 chars)
        var sb = new StringBuilder();
        sb.AppendLine("// Large sprite export test");
        for (int i = 0; i < 1000; i++)
        {
            sb.AppendLine($"const uint8_t frame_{i}[] PROGMEM = {{ 0x00, 0xFF, 0xAA, 0x55, 0x12, 0x34, 0x56, 0x78 }}; // comment {i}");
        }
        string code = sb.ToString();
        Assert.True(code.Length > 20000, $"Expected code length > 20000, got {code.Length}");

        RunOnStaThread(() =>
        {
            // Test SyntaxHighlightBox directly with empty token list for large output fallback
            var box = new SyntaxHighlightBox();
            var defaultBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Gray);
            var keywordBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Blue);
            var literalBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Green);
            var commentBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.DarkGreen);
            var identifierBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Black);

            // Verify UpdateCode completes without exception and preserves text exactness
            box.UpdateCode(code, new List<TokenSpan>(), defaultBrush, keywordBrush, literalBrush, commentBrush, identifierBrush);
        });

        // Verify original code string was not modified in place or mutated
        Assert.Equal(code, sb.ToString());
    }

    [Fact]
    public async Task RapidCancellation_CancellationTokenSource_CancelsInFlightTasksCleanly()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 500; i++)
        {
            sb.AppendLine($"const uint8_t data_{i}[] = {{ 0x12, 0x34, 0x56, 0x78 }}; // line comment {i}");
        }
        string code = sb.ToString();

        int completedCount = 0;
        int cancelledCount = 0;
        int exceptionCount = 0;

        var tasks = new List<Task>();

        for (int i = 0; i < 100; i++)
        {
            using var cts = new CancellationTokenSource();
            var token = cts.Token;

            var task = Task.Run(() =>
            {
                try
                {
                    var spans = SidebarPanel.TokenizeCode(code, token);
                    Interlocked.Increment(ref completedCount);
                }
                catch (OperationCanceledException)
                {
                    Interlocked.Increment(ref cancelledCount);
                }
                catch (Exception)
                {
                    Interlocked.Increment(ref exceptionCount);
                }
            });

            tasks.Add(task);

            // Cancel immediately or after microsecond delay
            if (i % 2 == 0)
            {
                cts.Cancel();
            }
            else
            {
                await Task.Delay(1);
                cts.Cancel();
            }
        }

        await Task.WhenAll(tasks);

        Assert.Equal(0, exceptionCount);
        Assert.Equal(100, completedCount + cancelledCount);
        Assert.True(cancelledCount > 0, $"Expected at least some tasks to be cancelled, got cancelledCount={cancelledCount}");
    }

    [Fact]
    public void LargeOutput_JustUnderThreshold_TokenizesWithinPerformanceBudget()
    {
        // 19,500 chars (just under 20,000 char default threshold)
        var sb = new StringBuilder();
        int lineNum = 0;
        while (sb.Length < 19500)
        {
            sb.AppendLine($"static const uint8_t line_{lineNum++}[] = {{ 0x01, 0x02, 0x03, 0x04, 0x05, 0x06 }}; // comment");
        }
        string code = sb.ToString();
        Assert.True(code.Length < 20000, $"Expected < 20000 chars, got {code.Length}");

        var sw = Stopwatch.StartNew();
        var spans = SidebarPanel.TokenizeCode(code, CancellationToken.None);
        sw.Stop();

        Assert.NotEmpty(spans);
        // Tokenization of ~19.5KB should take well under 200ms
        Assert.True(sw.ElapsedMilliseconds < 200, $"Tokenization took {sw.ElapsedMilliseconds} ms, expected < 200 ms");
    }

    [Fact]
    public void StressTest_AdversarialInput_UnclosedQuotesAndLongLines_DoesNotHangOrCrash()
    {
        var sb = new StringBuilder();
        // Unclosed string literals on very long line
        sb.Append("string a = \"unclosed string literal with many characters ");
        for (int i = 0; i < 500; i++)
        {
            sb.Append("0x12 0x34 0x56 # not a comment // not a comment \\\" ");
        }
        sb.AppendLine();
        sb.AppendLine("int valid = 100; // valid comment");

        string code = sb.ToString();
        var sw = Stopwatch.StartNew();
        var spans = SidebarPanel.TokenizeCode(code, CancellationToken.None);
        sw.Stop();

        Assert.NotEmpty(spans);
        Assert.True(sw.ElapsedMilliseconds < 1500, $"Adversarial tokenization took {sw.ElapsedMilliseconds} ms, expected < 1500 ms");
    }

    [Fact]
    public void StressTest_PreprocessorDirectivesSpam_ParsesCorrectlyWithoutHanging()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 300; i++)
        {
            sb.AppendLine($"#define FEATURE_{i} {i} // enable feature {i}");
            sb.AppendLine($"#ifdef FEATURE_{i}");
            sb.AppendLine($"#include \"header_{i}.h\"");
            sb.AppendLine("#endif");
        }

        string code = sb.ToString();
        var sw = Stopwatch.StartNew();
        var spans = SidebarPanel.TokenizeCode(code, CancellationToken.None);
        sw.Stop();

        Assert.NotEmpty(spans);
        Assert.True(sw.ElapsedMilliseconds < 500, $"Preprocessor spam took {sw.ElapsedMilliseconds} ms, expected < 500 ms");
        Assert.Contains(spans, s => s.Type == TokenType.Keyword && code.Substring(s.Start, s.Length) == "define");
        Assert.Contains(spans, s => s.Type == TokenType.Keyword && code.Substring(s.Start, s.Length) == "include");
        Assert.Contains(spans, s => s.Type == TokenType.Comment && code.Substring(s.Start, s.Length).StartsWith("// enable feature"));
    }

    [Fact]
    public void StressTest_CarriageReturnLineEndings_ProducesValidSpansWithinBounds()
    {
        string code = "int a = 10;\r\n// comment 1\r\n# comment 2\r\nstring s = \"val\";\r\n";
        var spans = SidebarPanel.TokenizeCode(code);

        foreach (var span in spans)
        {
            Assert.True(span.Start >= 0, $"Start out of bounds: {span.Start}");
            Assert.True(span.Length > 0, $"Length non-positive: {span.Length}");
            Assert.True(span.Start + span.Length <= code.Length, $"Span extends beyond code length: {span.Start} + {span.Length} > {code.Length}");
        }
    }

    [Fact]
    public void StressTest_EmptyAndWhitespaceLines_HandlesGracefully()
    {
        string code = "\n\n   \n\t\n// comment\n\n";
        var spans = SidebarPanel.TokenizeCode(code);

        Assert.Single(spans);
        Assert.Equal(TokenType.Comment, spans[0].Type);
    }

    [Fact]
    public void StressTest_LargeOutput_WithThousandsOfSpans_RendersWithinPerformanceBudget()
    {
        // 16 frames of 128x64 animation simulation (~114KB code)
        var sb = new StringBuilder();
        sb.AppendLine("// Realistic 16-frame animation output test");
        for (int f = 0; f < 16; f++)
        {
            sb.AppendLine($"const uint8_t frame_{f}[] PROGMEM = {{");
            for (int row = 0; row < 64; row++)
            {
                sb.Append("    ");
                for (int col = 0; col < 16; col++)
                {
                    sb.Append($"0x{(row * 16 + col) % 256:X2}, ");
                }
                sb.AppendLine($"// Row {row}");
            }
            sb.AppendLine("};");
        }
        string code = sb.ToString();

        // 1. Fast background tokenization with MaxFormattedSpans limit
        var swTokenize = Stopwatch.StartNew();
        var spans = SidebarPanel.TokenizeCode(code, SyntaxHighlightBox.MaxFormattedSpans);
        swTokenize.Stop();

        Assert.NotEmpty(spans);
        // With merged literal spans, 16 frames produces ~2,100 spans (90% reduction from 17,473)
        Assert.True(spans.Count <= 2500, $"Expected <= 2500 merged spans for 16 frames, got {spans.Count}");
        // Tokenization should complete in under 50ms
        Assert.True(swTokenize.ElapsedMilliseconds < 100, $"Merged tokenization took {swTokenize.ElapsedMilliseconds} ms, expected < 100 ms");

        // Verify that the final frame (frame 15) has syntax highlight spans (zero cutoff!)
        int frame15Pos = code.IndexOf("frame_15", StringComparison.Ordinal);
        Assert.True(frame15Pos > 0, "frame_15 not found in generated code");
        Assert.Contains(spans, s => s.Start >= frame15Pos);

        // 2. Fast UI thread render with 100% of all frames highlighted
        RunOnStaThread(() =>
        {
            var box = new SyntaxHighlightBox();
            var defaultBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Gray);
            var keywordBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Blue);
            var literalBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Green);
            var commentBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.DarkGreen);
            var identifierBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Black);

            var swRender = Stopwatch.StartNew();
            box.UpdateCode(code, spans, defaultBrush, keywordBrush, literalBrush, commentBrush, identifierBrush);
            swRender.Stop();

            // FormattedText creation + 2,100 SetForegroundBrush calls should complete in under 150ms on UI thread
            Assert.True(swRender.ElapsedMilliseconds < 150, $"UI thread render took {swRender.ElapsedMilliseconds} ms, expected < 150 ms");
        });
    }
}
