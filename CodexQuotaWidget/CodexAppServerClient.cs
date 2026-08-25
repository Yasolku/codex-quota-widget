using System.Diagnostics;
using System.Text.Json;

namespace CodexQuotaWidget;

internal sealed class CodexAppServerClient
{
    private static readonly string[] CandidateNames = ["codex.exe", "codex.cmd", "codex"];

    public async Task<QuotaSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        var executable = FindCodex();
        if (executable is null)
            return QuotaSnapshot.Unavailable("未找到可启动的 Codex CLI。请先安装或登录 Codex。右键可使用演示模式。");

        Process? process = null;
        Task<string>? stderrDrain = null;
        try
        {
            process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = "app-server --stdio",
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                }
            };
            process.Start();
            stderrDrain = process.StandardError.ReadToEndAsync();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));

            await SendAsync(process, new { id = 1, method = "initialize", @params = new { clientInfo = new { name = "codex-quota-widget", title = "Codex Quota Widget", version = "3.0.1" } } });
            await ReadResponseAsync(process, 1, timeout.Token);
            await SendAsync(process, new { method = "initialized", @params = new { } });
            await SendAsync(process, new { id = 2, method = "account/rateLimits/read", @params = new { } });
            using var response = await ReadResponseAsync(process, 2, timeout.Token);
            return Parse(response.RootElement);
        }
        catch (OperationCanceledException) { return QuotaSnapshot.Unavailable("读取限额超时，请确认 Codex 已登录。"); }
        catch (Exception ex) { return QuotaSnapshot.Unavailable($"读取失败：{ex.Message}"); }
        finally
        {
            if (process is not null)
            {
                TryStop(process);
                if (stderrDrain is not null)
                {
                    try { await stderrDrain.WaitAsync(TimeSpan.FromSeconds(1)); } catch { }
                }
                process.Dispose();
            }
        }
    }

    private static async Task SendAsync(Process process, object payload)
    {
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(payload));
        await process.StandardInput.FlushAsync();
    }

    private static async Task<JsonDocument> ReadResponseAsync(Process process, int id, CancellationToken token)
    {
        while (true)
        {
            var line = await process.StandardOutput.ReadLineAsync(token) ?? throw new InvalidOperationException("Codex app-server 已退出。");
            JsonDocument? doc = null;
            try { doc = JsonDocument.Parse(line); } catch { }
            if (doc is null) continue;
            if (doc.RootElement.TryGetProperty("id", out var idNode) && idNode.ValueKind == JsonValueKind.Number && idNode.GetInt32() == id)
                return doc;
            doc.Dispose();
        }
    }

    private static QuotaSnapshot Parse(JsonElement root)
    {
        if (root.TryGetProperty("error", out var error)) return QuotaSnapshot.Unavailable(error.ToString());
        var result = root.TryGetProperty("result", out var r) ? r : root;
        var windows = new List<UsageWindow>();
        FindWindows(result, windows);
        var weekly = windows.Where(w => w.Name.Contains("week", StringComparison.OrdinalIgnoreCase) || w.Name.Contains("/10080", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(w => w.ResetsAt).FirstOrDefault();
        var fiveHour = windows.Where(w => w.Name.Contains("5h", StringComparison.OrdinalIgnoreCase)
                || w.Name.Contains("5 hour", StringComparison.OrdinalIgnoreCase)
                || w.Name.Contains("/300", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(w => w.ResetsAt).FirstOrDefault();
        var expiries = new List<DateTimeOffset>();
        FindExpiryDates(result, expiries);
        var credits = FindCreditCount(result);
        return new(weekly, fiveHour, credits, expiries.Distinct().Order().ToList(), DateTimeOffset.Now,
            weekly is null && fiveHour is null ? "Codex 未返回可识别的 5 小时或每周限额。" : null);
    }

    private static void FindWindows(JsonElement node, List<UsageWindow> output, string path = "")
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            double? used = null, remaining = null, minutes = null;
            DateTimeOffset? reset = null;
            string name = path;
            foreach (var p in node.EnumerateObject())
            {
                var key = p.Name.ToLowerInvariant();
                if (key.Contains("used") && key.Contains("percent") && p.Value.TryGetDouble(out var u)) used = u;
                if (key.Contains("remaining") && key.Contains("percent") && p.Value.TryGetDouble(out var rem)) remaining = rem;
                if (key.Contains("window") && (key.Contains("minute") || key.Contains("mins")) && p.Value.TryGetDouble(out var min)) minutes = min;
                if (key.Contains("reset") && TryDate(p.Value, out var dt)) reset = dt;
                if ((key is "name" or "label" or "limit_name") && p.Value.ValueKind == JsonValueKind.String) name += "/" + p.Value.GetString();
            }
            if ((used.HasValue || remaining.HasValue) && reset.HasValue)
            {
                var pct = remaining ?? 100 - used!.Value;
                var windowName = minutes.HasValue ? $"{name}/{minutes:0}" : name;
                output.Add(new(windowName, Math.Clamp(pct, 0, 100), reset));
            }
            foreach (var p in node.EnumerateObject()) FindWindows(p.Value, output, path + "/" + p.Name);
        }
        else if (node.ValueKind == JsonValueKind.Array)
            foreach (var item in node.EnumerateArray()) FindWindows(item, output, path);
    }

    private static void FindExpiryDates(JsonElement node, List<DateTimeOffset> output, string path = "")
    {
        if (node.ValueKind == JsonValueKind.Object)
            foreach (var p in node.EnumerateObject())
            {
                var next = path + "/" + p.Name.ToLowerInvariant();
                if ((next.Contains("credit") || next.Contains("reset")) && next.Contains("expir") && TryDate(p.Value, out var dt)) output.Add(dt);
                FindExpiryDates(p.Value, output, next);
            }
        else if (node.ValueKind == JsonValueKind.Array)
            foreach (var item in node.EnumerateArray()) FindExpiryDates(item, output, path);
    }

    private static int FindCreditCount(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Object)
            foreach (var p in node.EnumerateObject())
            {
                var key = p.Name.ToLowerInvariant();
                if ((key.Contains("reset") || key.Contains("credit")) && (key.Contains("count") || key.Contains("available")) && p.Value.TryGetInt32(out var count)) return count;
                var nested = FindCreditCount(p.Value); if (nested > 0) return nested;
            }
        else if (node.ValueKind == JsonValueKind.Array)
            foreach (var item in node.EnumerateArray()) { var nested = FindCreditCount(item); if (nested > 0) return nested; }
        return 0;
    }

    private static bool TryDate(JsonElement value, out DateTimeOffset date)
    {
        if (value.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(value.GetString(), out date)) return true;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var n))
        {
            try { date = n > 10_000_000_000 ? DateTimeOffset.FromUnixTimeMilliseconds(n) : DateTimeOffset.FromUnixTimeSeconds(n); return true; } catch { }
        }
        date = default; return false;
    }

    private static string? FindCodex()
    {
        var configured = Environment.GetEnvironmentVariable("CODEX_CLI_PATH");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;
        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin", "codex.exe");
        if (File.Exists(local)) return local;
        var localBin = Path.GetDirectoryName(local)!;
        try
        {
            if (Directory.Exists(localBin))
            {
                var versioned = Directory.EnumerateFiles(localBin, "codex.exe", SearchOption.AllDirectories)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();
                if (versioned is not null) return versioned;
            }
        }
        catch { }
        var command = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator)
            .SelectMany(dir => CandidateNames.Select(name => Path.Combine(dir.Trim(), name)))
            .FirstOrDefault(File.Exists);
        if (command is not null) return command;
        try
        {
            return Process.GetProcessesByName("Codex")
                .Select(p => { try { return p.MainModule?.FileName; } catch { return null; } })
                .Where(p => p is not null)
                .OrderBy(p => p!.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();
        }
        catch { return null; }
    }

    private static void TryStop(Process process)
    {
        try { process.StandardInput.Close(); if (!process.WaitForExit(500)) process.Kill(true); } catch { }
    }
}
