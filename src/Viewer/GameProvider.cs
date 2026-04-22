using CUE4Parse.Compression;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;

namespace Viewer;

public class GameProvider : IDisposable
{
    public DefaultFileProvider? Provider { get; private set; }
    public bool IsInitialized { get; private set; }
    public string? MappingsWarning { get; private set; }

    /// <summary>
    /// Full initialization sequence:
    ///   1. Oodle + Zlib-ng helpers (download on first run, cache next to exe)
    ///   2. Create DefaultFileProvider and scan pak index
    ///   3. Submit AES key
    ///   4. Load .usmap mappings (non-fatal if missing/corrupt)
    /// Progress is reported as (0–100, message) for display in the UI.
    /// </summary>
    public async Task InitializeAsync(
        string pakDirectory,
        EGame ueVersion,
        string aesKey,
        string? mappingsPath,
        IProgress<(int Percent, string Message)>? progress = null,
        CancellationToken ct = default)
    {
        IsInitialized = false;
        MappingsWarning = null;

        progress?.Report((5, "Preparing compression helpers…"));
        await InitCompressionHelpersAsync(progress, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        progress?.Report((25, "Creating file provider…"));
        var provider = new DefaultFileProvider(
            pakDirectory,
            SearchOption.TopDirectoryOnly,
            new VersionContainer(ueVersion),
            StringComparer.OrdinalIgnoreCase);

        progress?.Report((30, "Scanning pak index…"));
        provider.Initialize();
        ct.ThrowIfCancellationRequested();

        progress?.Report((50, "Submitting AES key…"));
        if (!string.IsNullOrWhiteSpace(aesKey))
            provider.SubmitKey(new FGuid(), new FAesKey(aesKey));

        progress?.Report((65, "Loading mappings…"));
        if (!string.IsNullOrWhiteSpace(mappingsPath) && File.Exists(mappingsPath))
        {
            try
            {
                provider.MappingsContainer = new FileUsmapTypeMappingsProvider(mappingsPath);
            }
            catch (Exception ex)
            {
                MappingsWarning = $"Mappings could not be loaded: {ex.Message}. " +
                    "Unversioned properties may be unavailable.";
            }
        }
        else if (!string.IsNullOrWhiteSpace(mappingsPath))
        {
            MappingsWarning = $"Mappings file not found: {mappingsPath}";
        }

        Provider = provider;
        IsInitialized = true;
        progress?.Report((100, "Ready"));
    }

    /// <summary>
    /// Returns all file keys from the provider, suitable for populating the asset tree.
    /// Keys are virtual paths as CUE4Parse sees them (no extension stripping needed here).
    /// </summary>
    public IEnumerable<string> GetAllFiles()
    {
        EnsureInitialized();
        return Provider!.Files.Keys;
    }

    /// <summary>
    /// Loads a package and returns its exports.
    /// The path should include the file extension as it appears in the tree.
    /// Returns null if the path is not a loadable package type.
    /// </summary>
    public async Task<PackageContents?> LoadPackageAsync(string virtualPath, CancellationToken ct = default)
    {
        EnsureInitialized();

        var withoutExt = StripPackageExtension(virtualPath);
        if (withoutExt is null) return null;

        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var pkg = Provider!.LoadPackage(withoutExt);
            ct.ThrowIfCancellationRequested();

            var exports = pkg.GetExports().ToList();
            var imports = pkg switch
            {
                Package legacyPkg => legacyPkg.ImportMap.Select(i => i.ToString()),
                IoPackage ioPkg   => ioPkg.ImportMap.Select(i => i.ToString()),
                _                 => Enumerable.Empty<string>()
            };
            return new PackageContents(exports, imports.ToList());
        }, ct).ConfigureAwait(false);
    }

    private static readonly HashSet<string> PackageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".uasset", ".umap", ".uexp"
    };

    private static string? StripPackageExtension(string path)
    {
        var ext = Path.GetExtension(path);
        return PackageExtensions.Contains(ext) ? path[..^ext.Length] : null;
    }

    public void Dispose()
    {
        Provider?.Dispose();
        Provider = null;
        IsInitialized = false;
    }

    private void EnsureInitialized()
    {
        if (!IsInitialized || Provider is null)
            throw new InvalidOperationException("GameProvider has not been initialized.");
    }

    private static async Task InitCompressionHelpersAsync(
        IProgress<(int Percent, string Message)>? progress,
        CancellationToken ct)
    {
        var baseDir = AppContext.BaseDirectory;

        if (OodleHelper.Instance is null)
        {
            var oodlePath = Path.Combine(baseDir, OodleHelper.OodleFileName);
            if (!File.Exists(oodlePath))
                progress?.Report((7, "Downloading Oodle (one-time)…"));
            try
            {
                await OodleHelper.InitializeAsync(oodlePath).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Non-fatal: assets compressed with Oodle will fail to decompress,
                // but the provider itself will still load.
                System.Diagnostics.Debug.WriteLine($"Oodle init failed: {ex.Message}");
            }
        }

        ct.ThrowIfCancellationRequested();

        if (ZlibHelper.Instance is null)
        {
            var zlibPath = Path.Combine(baseDir, ZlibHelper.DllName);
            if (!File.Exists(zlibPath))
                progress?.Report((15, "Downloading Zlib-ng (one-time)…"));
            try
            {
                await ZlibHelper.InitializeAsync(zlibPath).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Zlib init failed: {ex.Message}");
            }
        }
    }
}
