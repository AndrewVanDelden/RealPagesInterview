using System.Text.Json;
using Agent.Common;
using Agent.Composition;
using Agent.Decisions;
using Agent.Domain;
using Agent.Ingest;
using Agent.Orchestration;
using Agent.Safety;
using Microsoft.Extensions.Configuration;

string? inputPath = GetOption(args, "--input");
string? outputPath = GetOption(args, "--output");
string composerName = GetOption(args, "--composer") ?? "template";
string? diagnosticsPath = GetOption(args, "--diagnostics");

if (inputPath is null || outputPath is null)
{
    Console.Error.WriteLine("Usage: --input <file.jsonl> --output <file.jsonl> [--composer template|openai] [--diagnostics <file.jsonl>]");
    return 1;
}

IConfiguration configuration = new ConfigurationBuilder()
    .AddUserSecrets<Program>(optional: true)
    .AddEnvironmentVariables()
    .Build();

var templateFallback = new TemplateMessageComposer();
IMessageComposer baseComposer = composerName switch
{
    "template" => templateFallback,
    "openai" => BuildOpenAiComposer(configuration),
    _ => throw new ArgumentException($"Unknown composer '{composerName}'. Expected 'template' or 'openai'."),
};

IMessageComposer composer = new ValidatingMessageComposer(baseComposer, new SafetyValidator(), templateFallback);

var agent = new LeasingMessageAgent(
    new ConsentGate(),
    new ChannelSelector(),
    composer,
    new SafetyValidator(),
    new SendScheduler(),
    new NextActionPlanner());

IReadOnlyList<ProspectCase> cases;
using (var inputReader = new StreamReader(inputPath))
{
    cases = new JsonlRecordReader().ReadAll(inputReader);
}

var outputLines = new List<string>();
var diagnosticsLines = new List<string>();

foreach (ProspectCase prospectCase in cases)
{
    AgentRunResult result = await agent.RunAsync(prospectCase);
    outputLines.Add(JsonSerializer.Serialize(result.Output, AgentJsonOptions.Default));

    if (diagnosticsPath is not null)
    {
        diagnosticsLines.Add(JsonSerializer.Serialize(
            new { TaskId = prospectCase.TaskId, Diagnostics = result.Diagnostics },
            AgentJsonOptions.Default));
    }
}

await File.WriteAllLinesAsync(outputPath, outputLines);

if (diagnosticsPath is not null)
{
    await File.WriteAllLinesAsync(diagnosticsPath, diagnosticsLines);
}

return 0;

static string? GetOption(string[] cliArgs, string name)
{
    int index = Array.IndexOf(cliArgs, name);
    return index >= 0 && index + 1 < cliArgs.Length ? cliArgs[index + 1] : null;
}

static IMessageComposer BuildOpenAiComposer(IConfiguration configuration)
{
    string apiKey = configuration["OpenAI:ApiKey"]
        ?? throw new InvalidOperationException(
            "OpenAI:ApiKey is not configured. Set it with: dotnet user-secrets set \"OpenAI:ApiKey\" \"<key>\" --project src/Agent.Cli");
    string model = configuration["OpenAI:Model"] ?? "gpt-4o-mini";

    var httpClient = new HttpClient();
    var completionClient = new OpenAiCompletionClient(httpClient, apiKey, model);
    return new OpenAiMessageComposer(completionClient);
}
