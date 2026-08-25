using CliWrap;
using CliWrap.Buffered;

namespace MattKotsenas.AppHost;

internal interface ICommandRunner
{
    Task<CommandOutput> RunAsync(
        string command,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken);
}

internal sealed record CommandOutput(
    int ExitCode,
    string StandardOutput,
    string StandardError);

internal sealed class CliWrapCommandRunner : ICommandRunner
{
    public async Task<CommandOutput> RunAsync(
        string command,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var result = await Cli.Wrap(command)
            .WithArguments(arguments)
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync(cancellationToken);

        return new CommandOutput(
            result.ExitCode,
            result.StandardOutput,
            result.StandardError);
    }
}
