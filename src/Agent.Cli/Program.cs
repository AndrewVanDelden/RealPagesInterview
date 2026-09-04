using Agent.Cli;
using Microsoft.Extensions.Configuration;

IConfiguration configuration = new ConfigurationBuilder()
    .AddUserSecrets<Program>(optional: true)
    .AddEnvironmentVariables()
    .Build();

var runner = new CliRunner(configuration, Console.Out, Console.Error);
return await runner.RunAsync(args);
