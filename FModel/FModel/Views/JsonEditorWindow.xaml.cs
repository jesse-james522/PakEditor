using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using FModel.Extensions;
using ICSharpCode.AvalonEdit.Document;
using UAssetAPI;
using UAssetAPI.Unversioned;

namespace FModel.Views;

public partial class JsonEditorWindow : AdonisUI.Controls.AdonisWindow
{
    private readonly string  _assetPath;   // path to .uasset in EditedAssets/
    private readonly string  _jsonPath;    // sidecar: <assetPath>.json
    private readonly UAsset  _asset;

    private CancellationTokenSource? _saveCts;
    private bool _applied;

    public JsonEditorWindow(string assetPath, UAsset asset)
    {
        _assetPath = assetPath;
        _jsonPath  = assetPath + ".json";
        _asset     = asset;
        InitializeComponent();
        TxtPath.Text = assetPath;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Editor.SyntaxHighlighting = AvalonExtensions.HighlighterSelector("json");

        // Load from sidecar if it exists, else serialize fresh.
        string json;
        if (File.Exists(_jsonPath))
        {
            try { json = File.ReadAllText(_jsonPath); }
            catch { json = _asset.SerializeJson(true); }
        }
        else
        {
            json = _asset.SerializeJson(true);
        }

        Editor.Document = new TextDocument(json);
        Editor.Document.TextChanged += OnTextChanged;
        SetStatus("Edit JSON then click Apply to write back to .uasset.");
    }

    // ── Autosave (debounced 800 ms) ───────────────────────────────────────────

    private void OnTextChanged(object? sender, EventArgs e)
    {
        _saveCts?.Cancel();
        _saveCts = new CancellationTokenSource();
        var token = _saveCts.Token;
        _ = Task.Run(async () =>
        {
            await Task.Delay(800, token);
            if (token.IsCancellationRequested) return;
            var text = await Dispatcher.InvokeAsync(() => Editor.Document.Text);
            try { await File.WriteAllTextAsync(_jsonPath, text, token); }
            catch { /* non-fatal */ }
        }, token);
    }

    // ── Apply ─────────────────────────────────────────────────────────────────

    private void BtnApply_Click(object sender, RoutedEventArgs e)
        => ApplyJson();

    private void ApplyJson()
    {
        SetStatus("Applying…");
        BtnApply.IsEnabled = false;
        try
        {
            var json  = Editor.Document.Text;
            var patch = UAsset.DeserializeJson(json);
            patch.Write(_assetPath);

            // Keep sidecar in sync with what was applied.
            try { File.WriteAllText(_jsonPath, json); } catch { }

            _applied = true;
            SetStatus($"Applied → {_assetPath}");
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}");
        }
        finally
        {
            BtnApply.IsEnabled = true;
        }
    }

    // ── Close ─────────────────────────────────────────────────────────────────

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _saveCts?.Cancel();

        if (Editor.Document == null || _applied) return;

        // Flush any pending autosave.
        try { File.WriteAllText(_jsonPath, Editor.Document.Text); } catch { }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void SetStatus(string msg) => TxtStatus.Text = msg;
}
