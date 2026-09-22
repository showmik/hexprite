using System;
using System.Threading;
using Hexprite.Rendering;
using Hexprite.Views;
using Xunit;

namespace Hexprite.Tests;

[Trait("Category", "Unit")]
    public class SyntaxHighlightingTests
{
    [Fact]
    public void TokenizeCode_PythonHashInlineComment_IdentifiesCommentSpan()
    {
        string code = "val = 42 # comment";
        var spans = SidebarPanel.TokenizeCode(code);

        Assert.Contains(spans, s => s.Type == TokenType.Comment && code.Substring(s.Start, s.Length) == "# comment");
        Assert.Contains(spans, s => s.Type == TokenType.Identifier && code.Substring(s.Start, s.Length) == "val");
        Assert.Contains(spans, s => s.Type == TokenType.Literal && code.Substring(s.Start, s.Length) == "42");
    }

    [Fact]
    public void TokenizeCode_CStyleInlineComment_IdentifiesCommentSpan()
    {
        string code = "int x = 1; // comment";
        var spans = SidebarPanel.TokenizeCode(code);

        Assert.Contains(spans, s => s.Type == TokenType.Comment && code.Substring(s.Start, s.Length) == "// comment");
        Assert.Contains(spans, s => s.Type == TokenType.Identifier && code.Substring(s.Start, s.Length) == "x");
        Assert.Contains(spans, s => s.Type == TokenType.Literal && code.Substring(s.Start, s.Length) == "1");
    }

    [Fact]
    public void TokenizeCode_StringsContainingSlashesAndHashes_NotClassifiedAsComments()
    {
        string code1 = "string url = \"http://example.com\";";
        var spans1 = SidebarPanel.TokenizeCode(code1);

        Assert.DoesNotContain(spans1, s => s.Type == TokenType.Comment);
        Assert.Contains(spans1, s => s.Type == TokenType.Literal && code1.Substring(s.Start, s.Length) == "\"http://example.com\"");

        string code2 = "const char* hex = \"#FF0000\";";
        var spans2 = SidebarPanel.TokenizeCode(code2);

        Assert.DoesNotContain(spans2, s => s.Type == TokenType.Comment);
        Assert.Contains(spans2, s => s.Type == TokenType.Literal && code2.Substring(s.Start, s.Length) == "\"#FF0000\"");
    }

    [Fact]
    public void TokenizeCode_EscapedQuotesInsideStrings_TracksStringBoundariesCorrectly()
    {
        string codeInside = "string s = \"hello \\\"world\\\" // comment\";";
        var spansInside = SidebarPanel.TokenizeCode(codeInside);

        Assert.DoesNotContain(spansInside, s => s.Type == TokenType.Comment);
        Assert.Contains(spansInside, s => s.Type == TokenType.Literal && codeInside.Substring(s.Start, s.Length) == "\"hello \\\"world\\\" // comment\"");

        string codeOutside = "string s = \"hello \\\"world\\\"\"; // real comment";
        var spansOutside = SidebarPanel.TokenizeCode(codeOutside);

        Assert.Contains(spansOutside, s => s.Type == TokenType.Comment && codeOutside.Substring(s.Start, s.Length) == "// real comment");
    }

    [Fact]
    public void TokenizeCode_CPreprocessorDirectives_NotClassifiedAsFullLineComments()
    {
        string codeInc = "#include <Arduino.h>";
        var spansInc = SidebarPanel.TokenizeCode(codeInc);

        Assert.DoesNotContain(spansInc, s => s.Type == TokenType.Comment && s.Length == codeInc.Length);
        Assert.Contains(spansInc, s => s.Type == TokenType.Keyword && codeInc.Substring(s.Start, s.Length) == "include");

        string codeDef = "#define MAX 10";
        var spansDef = SidebarPanel.TokenizeCode(codeDef);

        Assert.DoesNotContain(spansDef, s => s.Type == TokenType.Comment && s.Length == codeDef.Length);
        Assert.Contains(spansDef, s => s.Type == TokenType.Keyword && codeDef.Substring(s.Start, s.Length) == "define");
        Assert.Contains(spansDef, s => s.Type == TokenType.Literal && codeDef.Substring(s.Start, s.Length) == "10");

        string codePragma = "#pragma once";
        var spansPragma = SidebarPanel.TokenizeCode(codePragma);
        Assert.Contains(spansPragma, s => s.Type == TokenType.Keyword && codePragma.Substring(s.Start, s.Length) == "pragma");
    }

    [Fact]
    public void TokenizeCode_CPreprocessorDirectiveWithInlineComment_IdentifiesCommentPart()
    {
        string code = "#define MAX 10 // maximum limit";
        var spans = SidebarPanel.TokenizeCode(code);

        Assert.Contains(spans, s => s.Type == TokenType.Keyword && code.Substring(s.Start, s.Length) == "define");
        Assert.Contains(spans, s => s.Type == TokenType.Comment && code.Substring(s.Start, s.Length) == "// maximum limit");
    }

    [Fact]
    public void TokenizeCode_SingleQuoteLiterals_HandlesHashesAndSlashes()
    {
        string codeCharHash = "char c = '#';";
        var spans1 = SidebarPanel.TokenizeCode(codeCharHash);
        Assert.DoesNotContain(spans1, s => s.Type == TokenType.Comment);
        Assert.Contains(spans1, s => s.Type == TokenType.Literal && codeCharHash.Substring(s.Start, s.Length) == "'#'");

        string codeCharEscaped = "char c = '\\''; // comment";
        var spans2 = SidebarPanel.TokenizeCode(codeCharEscaped);
        Assert.Contains(spans2, s => s.Type == TokenType.Comment && codeCharEscaped.Substring(s.Start, s.Length) == "// comment");
    }

    [Fact]
    public void TokenizeCode_CancelledToken_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        string code = "int a = 1;\nint b = 2;\nint c = 3;\n";
        Assert.Throws<OperationCanceledException>(() => SidebarPanel.TokenizeCode(code, cts.Token));
    }

    [Fact]
    public void TokenizeCode_NullOrEmptyCode_ReturnsEmptyList()
    {
        Assert.Empty(SidebarPanel.TokenizeCode(null!));
        Assert.Empty(SidebarPanel.TokenizeCode(string.Empty));
    }

    [Fact]
    public void TokenizeCode_ComplexNestedStringWithSlashesAndHashes_NotClassifiedAsComment()
    {
        string code = "const char* s = \"str // # not comment\";";
        var spans = SidebarPanel.TokenizeCode(code);

        Assert.DoesNotContain(spans, s => s.Type == TokenType.Comment);
        Assert.Contains(spans, s => s.Type == TokenType.Keyword && code.Substring(s.Start, s.Length) == "const");
        Assert.Contains(spans, s => s.Type == TokenType.Literal && code.Substring(s.Start, s.Length) == "\"str // # not comment\"");
    }

    [Fact]
    public void TokenizeCode_EscapedQuotesAndSlashesInStrings_HandledCorrectly()
    {
        string code1 = "string s = \"a \\\"b\\\" // c\";";
        var spans1 = SidebarPanel.TokenizeCode(code1);

        Assert.DoesNotContain(spans1, s => s.Type == TokenType.Comment);
        Assert.Contains(spans1, s => s.Type == TokenType.Literal && code1.Substring(s.Start, s.Length) == "\"a \\\"b\\\" // c\"");

        string code2 = "string s = \"foo\\\\\\\\\"; // comment";
        var spans2 = SidebarPanel.TokenizeCode(code2);

        Assert.Contains(spans2, s => s.Type == TokenType.Comment && code2.Substring(s.Start, s.Length) == "// comment");
    }

    [Fact]
    public void TokenizeCode_AllCPreprocessorDirectives_NotClassifiedAsComments()
    {
        string[] directives = new[]
        {
            "#include <stdio.h>",
            "#define FOO 1",
            "#pragma once",
            "#ifdef FOO",
            "#ifndef BAR",
            "#endif",
            "#else",
            "#elif BAZ",
            "#undef FOO",
            "#if 1",
            "#error failed",
            "#line 100"
        };

        foreach (var code in directives)
        {
            var spans = SidebarPanel.TokenizeCode(code);
            Assert.DoesNotContain(spans, s => s.Type == TokenType.Comment && s.Length == code.Length);
        }
    }

    [Fact]
    public void TokenizeCode_PreprocessorDirectiveWithNestedStrings_PreservesStringAndComment()
    {
        string code = "#define STR \"hello // # world\" // real comment";
        var spans = SidebarPanel.TokenizeCode(code);

        Assert.Contains(spans, s => s.Type == TokenType.Keyword && code.Substring(s.Start, s.Length) == "define");
        Assert.Contains(spans, s => s.Type == TokenType.Literal && code.Substring(s.Start, s.Length) == "\"hello // # world\"");
        Assert.Contains(spans, s => s.Type == TokenType.Comment && code.Substring(s.Start, s.Length) == "// real comment");
    }

    [Fact]
    public void TokenizeCode_WithMaxSpans_StopsEarlyAtMaxSpans()
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < 200; i++)
        {
            sb.AppendLine($"const uint8_t byte_{i} = 0x{i:X2}; // byte comment {i}");
        }
        string code = sb.ToString();

        // Tokenize without limit
        var allSpans = SidebarPanel.TokenizeCode(code);
        Assert.True(allSpans.Count > 500, $"Expected >500 spans, got {allSpans.Count}");

        // Tokenize with limit of 50
        var limitedSpans = SidebarPanel.TokenizeCode(code, maxSpans: 50);
        Assert.True(limitedSpans.Count <= 50, $"Expected <= 50 spans, got {limitedSpans.Count}");
        Assert.Equal(50, limitedSpans.Count);
    }

    [Fact]
    public void TokenizeCode_ArrayLiterals_MergesAdjacentLiteralsOnSameLine()
    {
        string code = "const uint8_t data[] = { 0x01, 0x02, 0x03, 0x04 }; // data comment";
        var spans = SidebarPanel.TokenizeCode(code);

        // Should have Keyword(const), Keyword(uint8_t), Identifier(data), merged Literal(0x01, 0x02, 0x03, 0x04), Comment(// data comment)
        Assert.Equal(5, spans.Count);
        Assert.Contains(spans, s => s.Type == TokenType.Keyword && code.Substring(s.Start, s.Length) == "const");
        Assert.Contains(spans, s => s.Type == TokenType.Keyword && code.Substring(s.Start, s.Length) == "uint8_t");
        Assert.Contains(spans, s => s.Type == TokenType.Identifier && code.Substring(s.Start, s.Length) == "data");
        Assert.Contains(spans, s => s.Type == TokenType.Literal && code.Substring(s.Start, s.Length) == "0x01, 0x02, 0x03, 0x04");
        Assert.Contains(spans, s => s.Type == TokenType.Comment && code.Substring(s.Start, s.Length) == "// data comment");
    }

    [Fact]
    public void TokenizeCode_NonArrayLiterals_DoesNotMergeAcrossIdentifiersOrOperators()
    {
        string code = "int a = 1, b = 2;";
        var spans = SidebarPanel.TokenizeCode(code);

        // 1 and 2 should remain distinct because 'b' is an identifier in between
        Assert.Contains(spans, s => s.Type == TokenType.Literal && code.Substring(s.Start, s.Length) == "1");
        Assert.Contains(spans, s => s.Type == TokenType.Literal && code.Substring(s.Start, s.Length) == "2");
        Assert.Contains(spans, s => s.Type == TokenType.Identifier && code.Substring(s.Start, s.Length) == "b");
    }
}

