using System.Text.Json;

namespace LocalLizard.LocalLLM.Tools.Tools;

/// <summary>
/// Run predefined read-only shell commands from an allowlist.
/// Late-binding: allowlist is loaded from a JSON file at construction time.
/// </summary>
public sealed class RunShellTool : ITool
{
    private readonly string _allowlistPath;
    private HashSet<string>? _cachedAllowlist;
    private DateTime _lastLoad = DateTime.MinValue;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public string Name => "run_shell";

    public string Description =>
        "Run a predefined diagnostic command. Argument: command (one of the allowed commands). " +
        "Only read-only diagnostics are permitted. Example: command=df -h";

    public RunShellTool() : this("/shared/projects/local-lizard/config/shell-allowlist.json") { }

    public RunShellTool(string allowlistPath)
    {
        _allowlistPath = allowlistPath;
    }

    public async Task<string> RunAsync(JsonElement arguments, CancellationToken ct)
    {
        // Parse command from arguments JSON
        string? command = null;
        if (arguments.TryGetProperty("command", out var cmdEl))
            command = cmdEl.GetString();

        if (string.IsNullOrWhiteSpace(command))
            return "Error: run_shell requires a command argument. Example: command=df -h";

        var allowlist = await LoadAllowlistAsync(ct);
        if (!allowlist.Contains(command))
        {
            return $"Command not in allowlist. Allowed commands:\n" +
                   string.Join("\n", allowlist.Select(c => $"  {c}"));
        }

        return await ExecuteAsync(command, ct);
    }

    private readonly object _cacheLock = new();

    private async Task<HashSet<string>> LoadAllowlistAsync(CancellationToken ct)
    {
        // Fast path: cache still fresh
        if (_cachedAllowlist is not null)
        {
            lock (_cacheLock)
            {
                if (_cachedAllowlist is not null && (DateTime.UtcNow - _lastLoad) < CacheDuration)
                    return _cachedAllowlist;
            }
        }

        var defaults = new HashSet<string>(StringComparer.Ordinal)
        {
            "df -h",
            "free -h",
            "uptime",
            "who",
            "ps aux --sort=-%mem | head -10",
            "ls -la /shared/projects/local-lizard/",
        };

        HashSet<string>? loaded = null;
        try
        {
            if (File.Exists(_allowlistPath))
            {
                var json = await File.ReadAllTextAsync(_allowlistPath, ct);
                var custom = JsonSerializer.Deserialize<List<string>>(json);
                if (custom is not null && custom.Count > 0)
                {
                    loaded = new HashSet<string>(custom, StringComparer.Ordinal);
                }
            }
        }
        catch
        {
            // Fall back to defaults on any error
        }

        lock (_cacheLock)
        {
            _cachedAllowlist = loaded ?? defaults;
            _lastLoad = DateTime.UtcNow;
            return _cachedAllowlist;
        }
    }

    private static async Task<string> ExecuteAsync(string command, CancellationToken ct)
    {
        try
        {
            // Security: run through bash -c but trust is enforced by allowlist check above
            var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"-c {EscapeArg(command)}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            if (process is null)
                return "Error: failed to start process.";

            // Timeout after 15 seconds to prevent runaway commands
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            var timeoutToken = cts.Token;

            var outputTask = process.StandardOutput.ReadToEndAsync(timeoutToken);
            var errorTask = process.StandardError.ReadToEndAsync(timeoutToken);

            // Wait for process exit with timeout
            var completed = await Task.WhenAny(
                Task.Run(() => process.WaitForExit(), timeoutToken),
                Task.Delay(TimeSpan.FromSeconds(15), timeoutToken));

            string output, error;
            if (completed.IsCompletedSuccessfully && process.HasExited)
            {
                output = outputTask.IsCompletedSuccessfully ? await outputTask : "";
                error = errorTask.IsCompletedSuccessfully ? await errorTask : "";
            }
            else
            {
                try { process.Kill(true); } catch { }
                return "Command timed out after 15 seconds.";
            }

            var result = output.Trim();
            if (!string.IsNullOrEmpty(error))
                result += "\n\nstderr:\n" + error.Trim();

            return string.IsNullOrWhiteSpace(result) ? "(no output)" : ToolCallParser.TruncateResult(result);
        }
        catch (OperationCanceledException)
        {
            return "Command timed out.";
        }
        catch (Exception ex)
        {
            return $"Error executing command: {ex.Message}";
        }
    }

    private static string EscapeArg(string arg)
        => $"'{arg.Replace("'", "'\\''")}'";
}
