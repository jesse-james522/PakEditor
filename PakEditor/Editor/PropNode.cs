using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PakEditor.Editor;

public class PropNode : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name     { get; init; } = string.Empty;
    public string TypeName { get; init; } = string.Empty;
    public bool   IsLeaf   { get; init; }
    public bool   IsReadOnly => !IsLeaf || WriteFn is null;

    public ObservableCollection<PropNode> Children { get; } = new();

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set { _isExpanded = value; Notify(); }
    }

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

    public Action<string>? WriteFn  { get; init; }
    internal Action?       OnDirty  { get; set; }

    internal void WireChildren(Action onDirty)
    {
        OnDirty = onDirty;
        foreach (var child in Children)
            child.WireChildren(onDirty);
    }

    private void Notify([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
