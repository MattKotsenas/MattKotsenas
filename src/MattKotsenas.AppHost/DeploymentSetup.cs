using System.Diagnostics;

using Aspire.Hosting.ApplicationModel;

namespace MattKotsenas.AppHost;

internal static class DeploymentSetup
{
    public static async Task<ExecuteCommandResult> ConfigureAsync(
        string repositoryRoot,
        ExecuteCommandContext context)
    {
        var scriptPath = Path.Combine(
            repositoryRoot,
            "scripts",
            "Configure-ContainerAppOidc.ps1");
        var startInfo = new ProcessStartInfo("pwsh")
        {
            WorkingDirectory = repositoryRoot,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start PowerShell.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        try
        {
            await process.WaitForExitAsync(context.CancellationToken);
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                // The process exited between the HasExited check and Kill.
            }

            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(outputTask, errorTask);

            return CommandResults.Canceled();
        }

        var output = await outputTask;
        var error = await errorTask;

        if (process.ExitCode == 0)
        {
            return CommandResults.Success(
                string.IsNullOrWhiteSpace(output)
                    ? "Container App deployment identity configured."
                    : output.Trim());
        }

        return CommandResults.Failure(
            string.IsNullOrWhiteSpace(error)
                ? "Deployment identity configuration failed."
                : error.Trim());
    }
}
