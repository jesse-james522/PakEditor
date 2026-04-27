using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PakEditor.Packer;

/// <summary>
/// Thin wrapper around retoc.exe.
/// Retoc handles both IoStore→Legacy (to-legacy) and Legacy→IoStore (to-zen) conversions.
/// </summary>
public static class RetocService
{
    // Default path bundled with the app; can be overridden.
    public static string RetocExePath { get; set; } = Path.Combine(
        AppContext.BaseDirectory, "Retoc", "retoc.exe");

    public sealed class RetocResult
    {
        public bool Success { get; init; }
        public string Output  { get; init; } = string.Empty;
        public string Errors  { get; init; } = string.Empty;
        public int    ExitCode { get; init; }
    }

    // ── Public helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Converts an IoStore container (.utoc/.ucas) or directory of .utoc files
    /// to legacy .uasset/.uexp format in <paramref name="outputDir"/>.
    /// </summary>
    public static Task<RetocResult> ToLegacyAsync(
        string   input,
        string   outputDir,
        string?  aesKey          = null,
        string?  engineVersion   = null,
        string?  filter          = null,
        IProgress<string>? progress = null,
        CancellationToken  ct    = default)
    {
        var args = BuildArgs("to-legacy", input, outputDir,
            aesKey, engineVersion, filter, verbose: true);
        return RunAsync(args, progress, ct);
    }

    /// <summary>
    /// Converts a directory of legacy .uasset/.uexp files (or a .pak)
    /// to an IoStore container (.utoc/.ucas).
    /// </summary>
    public static Task<RetocResult> ToZenAsync(
        string   input,
        string   outputUtoc,
        string   engineVersion,          // required for to-zen
        string?  aesKey        = null,
        IProgress<string>? progress = null,
        CancellationToken  ct  = default)
    {
        var args = BuildArgs("to-zen", input, outputUtoc,
            aesKey, engineVersion, filter: null, verbose: true);
        return RunAsync(args, progress, ct);
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    private static string BuildArgs(
        string  command,
        string  input,
        string  output,
        string? aesKey,
        string? version,
        string? filter,
        bool    verbose)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(aesKey))
            sb.Append($"--aes-key \"{aesKey}\" ");
        sb.Append($"{command} ");
        if (!string.IsNullOrWhiteSpace(version))
            sb.Append($"--version {version} ");
        if (!string.IsNullOrWhiteSpace(filter))
            sb.Append($"--filter \"{filter}\" ");
        if (verbose)
            sb.Append("-v ");
        sb.Append($"\"{input}\" \"{output}\"");
        return sb.ToString();
    }

    private static async Task<RetocResult> RunAsync(
        string             args,
        IProgress<string>? progress,
        CancellationToken  ct)
    {
        if (!File.Exists(RetocExePath))
            return new RetocResult
            {
                Success = false,
                Errors  = $"retoc.exe not found at: {RetocExePath}"
            };

        var psi = new ProcessStartInfo(RetocExePath, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        };

        var outSb = new StringBuilder();
        var errSb = new StringBuilder();

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            outSb.AppendLine(e.Data);
            progress?.Report(e.Data);
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            errSb.AppendLine(e.Data);
            progress?.Report("[ERR] " + e.Data);
        };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        await proc.WaitForExitAsync(ct).ConfigureAwait(false);

        return new RetocResult
        {
            Success  = proc.ExitCode == 0,
            Output   = outSb.ToString(),
            Errors   = errSb.ToString(),
            ExitCode = proc.ExitCode,
        };
    }
}
