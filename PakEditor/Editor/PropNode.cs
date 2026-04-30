using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using UAssetAPI.PropertyTypes.Objects;

namespace PakEditor.Editor;

public class PropNode : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name     { get; init; } = string.Empty;
    public string TypeName { get; init; } = string.Empty;
    public string Variant  { get; init; } = string.Empty;
    public bool   IsLeaf   { get; init; }

    // ── Metadata columns ─────────────────────────────────────────────────────
    public int  ArrayIndex { get; init; } = -1;
    public long Offset     { get; init; } = -1;
    public bool IsZero     { get; init; }

    /// <summary>Hex offset for display; empty when Offset is negative.</summary>
    public string OffsetDisplay => Offset < 0 ? string.Empty : $"0x{Offset:X}";

    /// <summary>Array index for display; empty when ArrayIndex is negative.</summary>
    public string ArrayIndexDisplay => ArrayIndex < 0 ? string.Empty : ArrayIndex.ToString();

    /// <summary>Back-reference to the raw PropertyData (used for array add/remove).</summary>
    internal PropertyData? SourcePD { get; init; }

    /// <summary>Set on array element nodes — the array that owns this element.</summary>
    internal ArrayPropertyData? ParentArray { get; set; }

    // ── Tree structure ────────────────────────────────────────────────────────
    public ObservableCollection<PropNode> Children { get; } = new();

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set { _isExpanded = value; Notify(); }
    }

    // ── Value 1 (primary) ────────────────────────────────────────────────────
    public bool IsReadOnly => !IsLeaf || WriteFn is null;

    private string _value = string.Empty;
    public string Value
    {
        get => _value;
        set
        {
            if (_value == value) return;
            _value = value;
            Notify();
            if (WriteFn is null) return;
            try { WriteFn(value); OnDirty?.Invoke(); }
            catch { /* bad input — leave text, no write-back */ }
        }
    }

    public Action<string>? WriteFn { get; init; }

    // ── Value 2 ──────────────────────────────────────────────────────────────
    public bool IsReadOnly2 => !IsLeaf || WriteFn2 is null;

    private string _v2 = string.Empty;
    public string V2
    {
        get => _v2;
        set
        {
            if (_v2 == value) return;
            _v2 = value;
            Notify();
            if (WriteFn2 is null) return;
            try { WriteFn2(value); OnDirty?.Invoke(); }
            catch { }
        }
    }

    public Action<string>? WriteFn2 { get; init; }

    // ── Value 3 ──────────────────────────────────────────────────────────────
    public bool IsReadOnly3 => !IsLeaf || WriteFn3 is null;

    private string _v3 = string.Empty;
    public string V3
    {
        get => _v3;
        set
        {
            if (_v3 == value) return;
            _v3 = value;
            Notify();
            if (WriteFn3 is null) return;
            try { WriteFn3(value); OnDirty?.Invoke(); }
            catch { }
        }
    }

    public Action<string>? WriteFn3 { get; init; }

    // ── Value 4 ──────────────────────────────────────────────────────────────
    public bool IsReadOnly4 => !IsLeaf || WriteFn4 is null;

    private string _v4 = string.Empty;
    public string V4
    {
        get => _v4;
        set
        {
            if (_v4 == value) return;
            _v4 = value;
            Notify();
            if (WriteFn4 is null) return;
            try { WriteFn4(value); OnDirty?.Invoke(); }
            catch { }
        }
    }

    public Action<string>? WriteFn4 { get; init; }

    // ── Dirty wiring ─────────────────────────────────────────────────────────
    internal Action? OnDirty { get; set; }

    internal void WireChildren(Action onDirty)
    {
        OnDirty = onDirty;
        foreach (var child in Children)
            child.WireChildren(onDirty);
    }

    private void Notify([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
