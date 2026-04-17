using Automation.Claude;

namespace Automation.Tests.Claude;

public class ClassifierParseTests
{
    [Fact]
    public void ParsesStrictJsonReadyVerdict()
    {
        var stdout = """
        {
          "verdict": "ready",
          "summary": "Fix the null-pointer in UserService.",
          "questions": [],
          "likely_files": ["src/UserService.cs"]
        }
        """;

        var result = Classifier.ParseVerdictJson(stdout);

        Assert.Equal(Verdict.Ready, result.ParsedVerdict);
        Assert.Equal("Fix the null-pointer in UserService.", result.Summary);
        Assert.Empty(result.Questions);
        Assert.Contains("src/UserService.cs", result.LikelyFiles);
    }

    [Fact]
    public void ParsesNotReadyWithQuestions()
    {
        var stdout = """
        Some prose from claude before the block.
        {
          "verdict": "not_ready",
          "summary": "Unclear acceptance criteria.",
          "questions": ["Which environment?", "What's the expected failure mode?"],
          "likely_files": []
        }
        Trailing prose from claude.
        """;

        var result = Classifier.ParseVerdictJson(stdout);

        Assert.Equal(Verdict.NotReady, result.ParsedVerdict);
        Assert.Equal(2, result.Questions.Count);
    }

    [Fact]
    public void RejectsUnknownVerdict()
    {
        var stdout = """{"verdict": "maybe", "summary": "", "questions": [], "likely_files": []}""";
        Assert.Throws<InvalidDataException>(() => Classifier.ParseVerdictJson(stdout));
    }

    [Fact]
    public void ThrowsWhenNoJsonPresent()
    {
        var stdout = "Sorry, I cannot comply.";
        Assert.Throws<InvalidDataException>(() => Classifier.ParseVerdictJson(stdout));
    }

    [Fact]
    public void HandlesNestedBracesInsideStrings()
    {
        var stdout = """
        {
          "verdict": "ready",
          "summary": "Config change: {feature_flag} should toggle behavior.",
          "questions": [],
          "likely_files": ["config.json"]
        }
        """;

        var result = Classifier.ParseVerdictJson(stdout);
        Assert.Equal(Verdict.Ready, result.ParsedVerdict);
        Assert.Contains("{feature_flag}", result.Summary);
    }
}
