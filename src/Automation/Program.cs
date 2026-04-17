using Automation.Actions;
using Automation.Claude;
using Automation.Config;
using Automation.GitHub;
using Automation.Logging;
using Automation.Loop;
using Automation.State;

var overrides = CliOverridesParser.Parse(args);

var repoRoot = FindRepoRoot() ?? Directory.GetCurrentDirectory();
var configPath = Path.Join(repoRoot, "config", "config.json");
if (!File.Exists(configPath))
{
    Console.Error.WriteLine($"config file not found: {configPath}");
    Console.Error.WriteLine("Copy config/config.example.json to config/config.json and edit.");
    return 2;
}

var baseConfig = AppConfigLoader.Load(configPath);
var config = AppConfigLoader.ApplyCliOverrides(baseConfig, overrides);

Directory.CreateDirectory(Path.Join(repoRoot, "logs"));
var logPath = Path.Join(repoRoot, "logs", $"automation-{DateTime.UtcNow:yyyy-MM-dd}.log");
using var logger = new JsonLineLogger(logPath);
logger.Info("startup", new
{
    dry_run = config.DryRun,
    run_once = overrides.RunOnce,
    jiggle_enabled = config.JiggleEnabled,
    watched_repos = config.WatchedRepos,
    poll_seconds = config.PollIntervalSeconds,
});

var promptsDir = Path.Join(repoRoot, "src", "Automation", "Prompts");
var classifierPrompt = await File.ReadAllTextAsync(Path.Join(promptsDir, "classifier.md"));
var implementerPrompt = await File.ReadAllTextAsync(Path.Join(promptsDir, "implementer.md"));
var prResponderPrompt = await File.ReadAllTextAsync(Path.Join(promptsDir, "pr_responder.md"));

var gh = new GhCli(config.GhBin);
var claude = new ClaudeCli(config.ClaudeBin);
var git = new GitClient(config.GitBin);
var issues = new IssueFetcher(gh);
var prs = new PrFetcher(gh, logger);
var classifier = new Classifier(claude, logger, classifierPrompt);
var implementer = new Implementer(claude, logger, implementerPrompt);
var prResponder = new PrResponder(claude, logger, prResponderPrompt);
var branchAndPr = new BranchAndPr(git, gh, implementer, logger, config.DryRun);
var draft = new DraftReply(Path.Join(repoRoot, "drafts"), logger, config.DryRun);
var ledger = new Ledger(Path.Join(repoRoot, "logs", "ledger.json"));
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

var orchestrator = new PollOrchestrator(
    config, logger, issues, prs, git, classifier, branchAndPr, prResponder, draft, ledger, http);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

JiggleTimer? jiggle = null;
if (config.JiggleEnabled && !config.DryRun)
    jiggle = new JiggleTimer(TimeSpan.FromSeconds(config.JiggleIntervalSeconds), logger);

try
{
    if (overrides.RunOnce)
    {
        await orchestrator.RunOnceAsync(cts.Token);
    }
    else
    {
        // Run one tick at startup, then every poll_interval_seconds.
        // PeriodicTimer keeps ticks serialized and propagates cancellation
        // cleanly — unlike System.Threading.Timer with an async void callback,
        // which would silently crash the process on an unhandled exception.
        try { await orchestrator.RunOnceAsync(cts.Token); }
        catch (Exception ex) { logger.Error("loop.tick_failed", new { error = ex.Message }); }

        using var periodic = new PeriodicTimer(TimeSpan.FromSeconds(config.PollIntervalSeconds));
        try
        {
            while (await periodic.WaitForNextTickAsync(cts.Token).ConfigureAwait(false))
            {
                try { await orchestrator.RunOnceAsync(cts.Token); }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { logger.Error("loop.tick_failed", new { error = ex.Message }); }
            }
        }
        catch (OperationCanceledException) { /* clean shutdown */ }
    }
}
finally
{
    jiggle?.Dispose();
    logger.Info("shutdown");
}
return 0;

static string? FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        if (File.Exists(Path.Join(dir.FullName, "CLAUDE.md")) &&
            File.Exists(Path.Join(dir.FullName, "demo.jpeg")))
            return dir.FullName;
        dir = dir.Parent;
    }
    // Fallback: assume CWD.
    return null;
}
