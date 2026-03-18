using HaPcRemote.Service.Services;
using Shouldly;

namespace HaPcRemote.Service.Tests.Services;

public class CliRunnerTests
{
    [Fact]
    public async Task RunAsync_MissingExe_ThrowsFileNotFoundException()
    {
        var fakePath = Path.Combine(Path.GetTempPath(), "nonexistent-tool.exe");
        var runner = new CliRunner();

        var ex = await Should.ThrowAsync<FileNotFoundException>(
            () => runner.RunAsync(fakePath, []));

        ex.FileName.ShouldBe(fakePath);
    }

    [Fact]
    public async Task RunAsync_MissingExe_IncludesPathInMessage()
    {
        var fakePath = Path.Combine(Path.GetTempPath(), "does-not-exist.exe");
        var runner = new CliRunner();

        var ex = await Should.ThrowAsync<FileNotFoundException>(
            () => runner.RunAsync(fakePath, ["--list"]));

        ex.Message.ShouldContain(fakePath);
    }

    // ── SplitCsvLine tests ────────────────────────────────────────────

    [Fact]
    public void SplitCsvLine_SimpleFields_SplitsCorrectly()
    {
        var result = CsvParser.SplitCsvLine("a,b,c");

        result.ShouldBe(new[] { "a", "b", "c" });
    }

    [Fact]
    public void SplitCsvLine_QuotedField_RemovesQuotes()
    {
        var result = CsvParser.SplitCsvLine("\"hello world\",b");

        result.ShouldBe(new[] { "hello world", "b" });
    }

    [Fact]
    public void SplitCsvLine_QuotedFieldWithComma_PreservesComma()
    {
        var result = CsvParser.SplitCsvLine("\"a,b\",c");

        result.ShouldBe(new[] { "a,b", "c" });
    }

    [Fact]
    public void SplitCsvLine_EscapedQuote_PreservesQuote()
    {
        var result = CsvParser.SplitCsvLine("\"he said \"\"hi\"\"\",b");

        result.ShouldBe(new[] { "he said \"hi\"", "b" });
    }

    [Fact]
    public void SplitCsvLine_EmptyFields_ReturnsEmptyStrings()
    {
        var result = CsvParser.SplitCsvLine("a,,c,");

        result.ShouldBe(new[] { "a", "", "c", "" });
    }

    [Fact]
    public void SplitCsvLine_SingleField_ReturnsSingleElement()
    {
        var result = CsvParser.SplitCsvLine("hello");

        result.ShouldBe(new[] { "hello" });
    }

    [Fact]
    public void SplitCsvLine_EmptyString_ReturnsSingleEmptyElement()
    {
        var result = CsvParser.SplitCsvLine("");

        result.ShouldBe(new[] { "" });
    }
}
