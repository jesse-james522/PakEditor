using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using CUE4Parse.UE4.Versions;
using FModel.Settings;
using PakEditor.Packer;

namespace FModel.ViewModels;

// ── One item in the asset checklist ──────────────────────────────────────────

public class CookAssetItem : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private bool _isChecked = true;
    public bool IsChecked
    {
        get => _isChecked;
        set { _isChecked = value; Notify(); }
    }

    /// <summary>Path relative to EditRoot, e.g. TheIsle/Content/…/BP_Foo.uasset</summary>
    public string RelativePath  { get; init; } = string.Empty;
    public string FullPath      { get; init; } = string.Empty;
    public string FileName      => Path.GetFileName(FullPath);
    public string LastModified  { get; init; } = string.Empty;

    private void Notify([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

// ── ViewModel ─────────────────────────────────────────────────────────────────

public class CookViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    // ── Paths ─────────────────────────────────────────────────────────────────

    public static string EditRoot =>
        Path.Combine(AppContext.BaseDirectory, "EditedAssets");

    // ── Asset list ────────────────────────────────────────────────────────────

    public ObservableCollection<CookAssetItem> Assets { get; } = new();

    // ── Output options ────────────────────────────────────────────────────────

    private string _outputName = "MyMod_P";
    public string OutputName
    {
        get => _outputName;
        set { _outputName = value; Notify(); }
    }

    private string _outputDirectory =
        Path.Combine(AppContext.BaseDirectory, "Cooked");
    public string OutputDirectory
    {
        get => _outputDirectory;
        set { _outputDirectory = value; Notify(); }
    }

    // ── Cook options ──────────────────────────────────────────────────────────

    private bool _deleteAfterCook;
    public bool DeleteAfterCook
    {
        get => _deleteAfterCook;
        set { _deleteAfterCook = value; Notify(); }
    }

    // ── Status ────────────────────────────────────────────────────────────────

    private string _status = "Refresh to load edited assets, then click Cook.";
    public string Status
    {
        get => _status;
        set { _status = value; Notify(); }
    }

    private bool _isCooking;
    public bool IsCooking
    {
        get => _isCooking;
        set { _isCooking = value; Notify(); NotifyCommands(); }
    }

    public bool HasAssets => Assets.Count > 0;

    // ── Commands ──────────────────────────────────────────────────────────────

    public SimpleCommand RefreshCommand       { get; }
    public SimpleCommand BrowseCommand        { get; }
    public SimpleCommand CookCommand          { get; }
    public SimpleCommand CheckAllCommand      { get; }
    public SimpleCommand UncheckAllCommand    { get; }
    public SimpleCommand DeleteSelectedCommand { get; }

    public CookViewModel()
    {
        RefreshCommand        = new SimpleCommand(_ => RefreshAssets());
        BrowseCommand         = new SimpleCommand(_ => BrowseOutput());
        CookCommand           = new SimpleCommand(_ => _ = CookAsync(),
                                    _ => !IsCooking && Assets.Any(a => a.IsChecked));
        CheckAllCommand       = new SimpleCommand(_ => SetAll(true));
        UncheckAllCommand     = new SimpleCommand(_ => SetAll(false));
        DeleteSelectedCommand = new SimpleCommand(_ => DeleteSelected(),
                                    _ => !IsCooking && Assets.Any(a => a.IsChecked));
    }

    // ── Refresh ───────────────────────────────────────────────────────────────

    public void RefreshAssets()
    {
        Assets.Clear();

        if (!Directory.Exists(EditRoot))
        {
            Status = $"No edited assets yet — EditedAssets folder doesn't exist at {EditRoot}";
            Notify(nameof(HasAssets));
            return;
        }

        foreach (var file in Directory.EnumerateFiles(EditRoot, "*.uasset", SearchOption.AllDirectories)
                                      .OrderBy(f => f))
        {
            var rel      = Path.GetRelativePath(EditRoot, file).Replace('\\', '/');
            var modified = File.GetLastWriteTime(file);
            var modStr   = modified > DateTime.MinValue
                ? modified.ToString("MMM d, HH:mm")
                : string.Empty;
            var item = new CookAssetItem { RelativePath = rel, FullPath = file, LastModified = modStr };

            // Re-evaluate commands whenever any item's checkbox changes.
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(CookAssetItem.IsChecked))
                    NotifyCommands();
            };

            Assets.Add(item);
        }

        Status = Assets.Count > 0
            ? $"{Assets.Count} edited asset(s) found. Select which to cook."
            : "No .uasset files found in EditedAssets folder.";

        Notify(nameof(HasAssets));
        NotifyCommands();
    }

    // ── Browse output ─────────────────────────────────────────────────────────

    private void BrowseOutput()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description            = "Select output folder for cooked mod",
            SelectedPath           = OutputDirectory,
            UseDescriptionForTitle = true,
        };
        if (dlg.ShowDialog() == DialogResult.OK)
            OutputDirectory = dlg.SelectedPath;
    }

    // ── Cook ─────────────────────────────────────────────────────────────────

    private async Task CookAsync()
    {
        var selected = Assets.Where(a => a.IsChecked).ToList();
        if (selected.Count == 0)
        {
            Status = "No assets selected.";
            return;
        }

        var name = OutputName.Trim();
        if (string.IsNullOrEmpty(name)) name = "MyMod_P";

        IsCooking = true;
        Status    = "Preparing staging directory…";

        var stagingDir = Path.Combine(Path.GetTempPath(), "PakEditor", "CookStaging",
                                      DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        bool cookSucceeded = false;

        try
        {
            // Copy selected assets (+ companion files) into staging dir.
            foreach (var item in selected)
            {
                var relBase = Path.ChangeExtension(item.RelativePath, null)
                                  .Replace('/', Path.DirectorySeparatorChar);

                foreach (var src in Directory.GetFiles(
                             Path.GetDirectoryName(item.FullPath)!,
                             Path.GetFileNameWithoutExtension(item.FullPath) + ".*"))
                {
                    var dst = Path.Combine(stagingDir, relBase + Path.GetExtension(src));
                    Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                    File.Copy(src, dst, overwrite: true);
                }
            }

            Directory.CreateDirectory(OutputDirectory);

            var outputUtoc = Path.Combine(OutputDirectory, name + ".utoc");
            var version    = MapEGameToRetocVersion(UserSettings.Default.CurrentDir.UeVersion);

            Status = $"Running retoc to-zen ({version})…";

            var result = await RetocService.ToZenAsync(
                input:         stagingDir,
                outputUtoc:    outputUtoc,
                engineVersion: version);

            if (result.Success)
            {
                cookSucceeded = true;
                Status = $"✓ Cooked → {outputUtoc}  (.utoc + .ucas)";
            }
            else
            {
                Status = $"retoc failed (exit {result.ExitCode}): {result.Errors.Trim()}";
            }
        }
        catch (Exception ex)
        {
            Status = $"Cook error: {ex.Message}";
        }
        finally
        {
            // Clean up temp staging dir.
            try { if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, true); }
            catch { /* ignore */ }

            IsCooking = false;
        }

        // Delete from EditedAssets after a successful cook if the option is set.
        if (cookSucceeded && DeleteAfterCook)
        {
            int deleted = 0;
            foreach (var item in selected)
            {
                try
                {
                    // Delete .uasset + all companions (.uexp, .ubulk, etc.)
                    var stem = Path.ChangeExtension(item.FullPath, null);
                    foreach (var f in Directory.GetFiles(
                                 Path.GetDirectoryName(item.FullPath)!,
                                 Path.GetFileNameWithoutExtension(item.FullPath) + ".*"))
                    {
                        File.Delete(f);
                        deleted++;
                    }
                }
                catch { /* ignore per-file errors */ }
            }
            Status += $"  Deleted {deleted} file(s) from EditedAssets.";
            RefreshAssets();
        }
    }

    // ── Delete selected ───────────────────────────────────────────────────────

    private void DeleteSelected()
    {
        var toDelete = Assets.Where(a => a.IsChecked).ToList();
        if (toDelete.Count == 0) return;

        var answer = System.Windows.MessageBox.Show(
            $"Permanently delete {toDelete.Count} edited asset(s) from disk?\n\n" +
            string.Join('\n', toDelete.Select(a => "  - " + a.RelativePath)),
            "Delete Edited Assets",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (answer != System.Windows.MessageBoxResult.Yes) return;

        int deleted = 0;
        foreach (var item in toDelete)
        {
            try
            {
                foreach (var f in Directory.GetFiles(
                             Path.GetDirectoryName(item.FullPath)!,
                             Path.GetFileNameWithoutExtension(item.FullPath) + ".*"))
                {
                    File.Delete(f);
                    deleted++;
                }
            }
            catch { /* ignore per-file errors */ }
        }

        Status = $"Deleted {deleted} file(s) from EditedAssets.";
        RefreshAssets();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void SetAll(bool check)
    {
        foreach (var a in Assets) a.IsChecked = check;
        NotifyCommands();
    }

    private void NotifyCommands()
    {
        CookCommand.RaiseCanExecuteChanged();
        DeleteSelectedCommand.RaiseCanExecuteChanged();
    }

    private void Notify([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

    private static string MapEGameToRetocVersion(EGame game) => game switch
    {
        EGame.GAME_UE5_6 or EGame.GAME_UE5_7 => "UE5_6",
        EGame.GAME_UE5_5  => "UE5_5",
        EGame.GAME_UE5_4  => "UE5_4",
        EGame.GAME_UE5_3  => "UE5_3",
        EGame.GAME_UE5_2  => "UE5_2",
        EGame.GAME_UE5_1  => "UE5_1",
        EGame.GAME_UE5_0  => "UE5_0",
        EGame.GAME_UE4_27 => "UE4_27",
        EGame.GAME_UE4_26 => "UE4_26",
        EGame.GAME_UE4_25 => "UE4_25",
        _                 => "UE5_6",
    };
}

// ── Minimal ICommand wrapper ──────────────────────────────────────────────────

public class SimpleCommand : System.Windows.Input.ICommand
{
    private readonly Action<object?>     _execute;
    private readonly Func<object?, bool>? _canExecute;

    public event EventHandler? CanExecuteChanged;

    public SimpleCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute    = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? p) => _canExecute?.Invoke(p) ?? true;
    public void Execute(object? p)    => _execute(p);
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
