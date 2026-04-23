using CUE4Parse.Compression;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using Newtonsoft.Json;

namespace Viewer;

public enum PreviewResult { Json, HeavyAsset, Unsupported, Error }

public record PackagePreview(
    PreviewResult Result,
    string? Json = null,
    IReadOnlyList<string>? Imports = null,
    string? ErrorMessage = null);

public class GameProvider : IDisposable
{
    public DefaultFileProvider? Provider { get; private set; }
    public bool IsInitialized { get; private set; }
    public string? MappingsWarning { get; private set; }

    private static readonly HashSet<string> HeavyExportClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "StaticMesh", "SkeletalMesh", "SkeletalMeshLODSettings",
        "Texture2D", "TextureCube", "TextureRenderTarget2D", "VolumeTexture",
        "SoundWave", "SoundCue", "MetaSoundSource",
        "AnimSequence", "AnimMontage", "AnimComposite", "BlendSpace",
        "AnimBlueprint", "AnimBlueprintGeneratedClass",
        "World",
        "Material", "MaterialInstanceConstant", "MaterialInstanceDynamic", "MaterialFunction",
    };

    private static readonly HashSet<string> PackageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".uasset", ".umap", ".uexp"
    };

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

    public IEnumerable<string> GetAllFiles()
    {
        EnsureInitialized();
        return Provider!.Files.Keys;
    }

    /// <summary>
    /// Loads a package and returns syntax-colourable JSON plus its import list.
    /// If previewHeavyAssets is false and the package is a mesh/texture/sound/etc.,
    /// returns HeavyAsset instead of deserialising.
    /// </summary>
    public async Task<PackagePreview> LoadPreviewAsync(
        string virtualPath,
        bool previewHeavyAssets,
        CancellationToken ct = default)
    {
        EnsureInitialized();

        var withoutExt = StripPackageExtension(virtualPath);
        if (withoutExt is null)
            return new PackagePreview(PreviewResult.Unsupported);

        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            var pkg = Provider!.LoadPackage(withoutExt);
            ct.ThrowIfCancellationRequested();

            var exports = pkg.GetExports().ToList();

            if (!previewHeavyAssets && exports.Count > 0)
            {
                var firstClass = exports[0].GetType().Name.TrimStart('U');
                if (HeavyExportClasses.Contains(firstClass))
                    return new PackagePreview(PreviewResult.HeavyAsset);
            }

            ct.ThrowIfCancellationRequested();

            var json = JsonConvert.SerializeObject(exports, Formatting.Indented);

            var imports = pkg switch
            {
                Package legacyPkg => legacyPkg.ImportMap.Select(i => i.ToString()).ToList(),
                IoPackage ioPkg   => ioPkg.ImportMap.Select(i => i.ToString()).ToList(),
                _                 => (IReadOnlyList<string>)[]
            };

            return new PackagePreview(PreviewResult.Json, json, imports);
        }, ct).ConfigureAwait(false);
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

    private static string? StripPackageExtension(string path)
    {
        var ext = Path.GetExtension(path);
        return PackageExtensions.Contains(ext) ? path[..^ext.Length] : null;
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
            try { await OodleHelper.InitializeAsync(oodlePath).ConfigureAwait(false); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Oodle init failed: {ex.Message}"); }
        }

        ct.ThrowIfCancellationRequested();

        if (ZlibHelper.Instance is null)
        {
            var zlibPath = Path.Combine(baseDir, ZlibHelper.DllName);
            if (!File.Exists(zlibPath))
                progress?.Report((15, "Downloading Zlib-ng (one-time)…"));
            try { await ZlibHelper.InitializeAsync(zlibPath).ConfigureAwait(false); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Zlib init failed: {ex.Message}"); }
        }
    }
}
