using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface;

namespace AetherLove.Widgets;

/// <summary>What a dialog is for; drives which chrome the window grows.</summary>
internal enum FileDialogMode
{
    OpenFile,
    OpenFiles,
    PickFolder,
    SaveFile,
    SaveFolder,
}

/// <summary>The house file picker: Dalamud's <c>FileDialogManager</c> API on a dialog that fixes
/// its gaps. Image thumbnails and a live preview pane, one globally remembered folder, starred
/// favorites, quick links (game screenshots included), an editable breadcrumb path, back/up
/// history, search, sort, a themed look, and a window that keeps its size. Same filter grammar
/// ("Label{.png,.jpg}" or bare ".png,.jpg") and the same method set (open/save, file/folder,
/// multi-select, modal), so any call site written against Dalamud's manager drops in.</summary>
public sealed class AetherFileDialogManager
{
    private AetherFileDialog? _dialog;
    private Action<bool, string>? _callback;
    private Action<bool, List<string>>? _multiCallback;

    /// <summary>Extra sidebar entries a host wants everywhere (the plugin adds the game's
    /// screenshot folder). Static by design: quick links are facts about the machine, not about
    /// one picker instance. Labels are LOCALIZATION KEYS, resolved at draw time so a language
    /// switch mid-session renames them like everything else.</summary>
    public static readonly List<(string LabelKey, string Path, FontAwesomeIcon Icon)> ExtraQuickLinks = [];

    public void OpenFileDialog(string title, string filters, Action<bool, string> callback)
        => Open(new AetherFileDialog(FileDialogMode.OpenFile, title, filters), callback, null);

    /// <summary><paramref name="selectionCountMax"/> caps how many files may be picked; zero or
    /// less means unlimited. The callback's list is in the order the user selected.</summary>
    public void OpenFileDialog(string title, string filters, Action<bool, List<string>> callback,
        int selectionCountMax, string? startPath = null, bool isModal = false)
        => Open(new AetherFileDialog(FileDialogMode.OpenFiles, title, filters,
            maxSelection: selectionCountMax, startPath: startPath, isModal: isModal), null, callback);

    public void OpenFolderDialog(string title, Action<bool, string> callback)
        => Open(new AetherFileDialog(FileDialogMode.PickFolder, title, ""), callback, null);

    public void OpenFolderDialog(string title, Action<bool, string> callback, string? startPath, bool isModal = false)
        => Open(new AetherFileDialog(FileDialogMode.PickFolder, title, "",
            startPath: startPath, isModal: isModal), callback, null);

    public void SaveFileDialog(string title, string filters, string defaultFileName, string defaultExtension,
        Action<bool, string> callback)
        => Open(new AetherFileDialog(FileDialogMode.SaveFile, title, filters,
            defaultName: defaultFileName, defaultExtension: defaultExtension), callback, null);

    public void SaveFileDialog(string title, string filters, string defaultFileName, string defaultExtension,
        Action<bool, string> callback, string? startPath, bool isModal = false)
        => Open(new AetherFileDialog(FileDialogMode.SaveFile, title, filters,
            defaultName: defaultFileName, defaultExtension: defaultExtension,
            startPath: startPath, isModal: isModal), callback, null);

    public void SaveFolderDialog(string title, string defaultFolderName, Action<bool, string> callback)
        => Open(new AetherFileDialog(FileDialogMode.SaveFolder, title, "",
            defaultName: defaultFolderName), callback, null);

    public void SaveFolderDialog(string title, string defaultFolderName, Action<bool, string> callback,
        string? startPath, bool isModal = false)
        => Open(new AetherFileDialog(FileDialogMode.SaveFolder, title, "",
            defaultName: defaultFolderName, startPath: startPath, isModal: isModal), callback, null);

    private void Open(AetherFileDialog dialog, Action<bool, string>? single, Action<bool, List<string>>? multi)
    {
        _dialog = dialog;
        _callback = single;
        _multiCallback = multi;
    }

    public void Reset()
    {
        _dialog = null;
        _callback = null;
        _multiCallback = null;
    }

    public void Draw()
    {
        if (_dialog is not { } dialog)
        {
            return;
        }
        if (!dialog.Draw())
        {
            var paths = dialog.ResultPaths;
            var single = _callback;
            var multi = _multiCallback;
            Reset();
            single?.Invoke(paths is { Count: > 0 }, paths is { Count: > 0 } ? paths[0] : string.Empty);
            multi?.Invoke(paths is { Count: > 0 }, paths ?? []);
        }
    }
}

/// <summary>One open dialog. Draw returns false on the frame it finishes; <see cref="ResultPaths"/>
/// then carries the picked path(s), or null for a cancel.</summary>
internal sealed class AetherFileDialog
{
    private sealed record Entry(string Path, string Name, bool IsDir, long Size, DateTime ModifiedUtc, string Ext);

    private sealed record Filter(string Label, HashSet<string> Extensions);

    private readonly FileDialogMode _mode;
    private readonly string _title;
    private readonly List<Filter> _filters;
    private readonly int _maxSelection;
    private readonly bool _isModal;
    private readonly string _defaultExtension;
    private int _filterIndex;

    private string _dir;
    private readonly List<string> _back = [];
    private readonly List<string> _forward = [];

    private List<Entry>? _entries;
    private string _listedDir = "";
    private string _listError = "";
    private string _search = "";
    private string? _selected;
    private readonly List<string> _multi = [];
    private string _saveName;
    private bool _editPath;
    private string _pathEdit = "";
    private bool _newFolder;
    private string _newFolderName = "";
    private bool _focusPathEdit;
    private bool _focusNewFolder;
    private bool _open = true;
    private bool _modalOpened;
    private double _lastClickTime;
    private string _lastClickPath = "";
    private int _lastClickIndex = -1;

    public List<string>? ResultPaths { get; private set; }

    public AetherFileDialog(FileDialogMode mode, string title, string filters, int maxSelection = 0,
        string? startPath = null, bool isModal = false, string defaultName = "", string defaultExtension = "")
    {
        _mode = mode;
        _title = title;
        _filters = ParseFilters(filters);
        _maxSelection = maxSelection;
        _isModal = isModal;
        _saveName = defaultName;
        _defaultExtension = defaultExtension;
        var cfg = UiHost.Configuration.FilePicker;
        _dir = startPath is { Length: > 0 } && Directory.Exists(startPath)
            ? startPath
            : Directory.Exists(cfg.LastFolder)
                ? cfg.LastFolder
                : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures) is { Length: > 0 } pictures
                    && Directory.Exists(pictures)
                    ? pictures
                    : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private bool FoldersOnly => _mode is FileDialogMode.PickFolder or FileDialogMode.SaveFolder;

    /// <summary>Dalamud's grammar: comma-separated entries, each either a bare extension or a
    /// "Label{.a,.b}" group. ".*" passes everything.</summary>
    private static List<Filter> ParseFilters(string filters)
    {
        var result = new List<Filter>();
        foreach (Match m in Regex.Matches(filters ?? string.Empty, @"[^,{}]+(\{[^{}]*\})?"))
        {
            var whole = m.Value.Trim();
            var brace = whole.IndexOf('{');
            if (brace >= 0)
            {
                var label = whole[..brace].Trim();
                var exts = whole[(brace + 1)..].TrimEnd('}')
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(e => e.ToLowerInvariant())
                    .ToHashSet();
                result.Add(new Filter(label.Length > 0 ? label : string.Join(", ", exts), exts));
            }
            else if (whole.Length > 0)
            {
                result.Add(new Filter(whole, whole == ".*" ? [] : [whole.ToLowerInvariant()]));
            }
        }
        return result;
    }

    private bool PassesFilter(Entry e)
    {
        if (e.IsDir)
        {
            return true;
        }
        if (_filters.Count == 0)
        {
            return true;
        }
        var f = _filters[Math.Clamp(_filterIndex, 0, _filters.Count - 1)];
        return f.Extensions.Count == 0 || f.Extensions.Contains(e.Ext);
    }

    private static bool LooksLikeImage(string ext) =>
        ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp" or ".gif";

    private void NavigateTo(string path, bool pushHistory = true)
    {
        if (!Directory.Exists(path))
        {
            return;
        }
        if (pushHistory && _dir.Length > 0 && !string.Equals(path, _dir, StringComparison.OrdinalIgnoreCase))
        {
            _back.Add(_dir);
            _forward.Clear();
        }
        _dir = path;
        _selected = null;
        _multi.Clear();
        _lastClickIndex = -1;
        _search = "";
        _editPath = false;
        _newFolder = false;
    }

    private void EnsureListed()
    {
        var cfg = UiHost.Configuration.FilePicker;
        if (_entries is not null && _listedDir == $"{_dir}|{cfg.ShowHidden}|{cfg.SortField}|{cfg.SortDescending}")
        {
            return;
        }
        _listedDir = $"{_dir}|{cfg.ShowHidden}|{cfg.SortField}|{cfg.SortDescending}";
        _listError = "";
        var list = new List<Entry>();
        try
        {
            var info = new DirectoryInfo(_dir);
            foreach (var d in info.EnumerateDirectories())
            {
                if (!cfg.ShowHidden && (d.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0)
                {
                    continue;
                }
                list.Add(new Entry(d.FullName, d.Name, true, 0, d.LastWriteTimeUtc, ""));
            }
            if (!FoldersOnly)
            {
                foreach (var f in info.EnumerateFiles())
                {
                    if (!cfg.ShowHidden && (f.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0)
                    {
                        continue;
                    }
                    list.Add(new Entry(f.FullName, f.Name, false, f.Length, f.LastWriteTimeUtc,
                        f.Extension.ToLowerInvariant()));
                }
            }
        }
        catch (Exception ex)
        {
            _listError = ex.Message;
        }

        Comparison<Entry> by = cfg.SortField switch
        {
            1 => (a, b) => a.ModifiedUtc.CompareTo(b.ModifiedUtc),
            2 => (a, b) => a.Size.CompareTo(b.Size),
            _ => (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase),
        };
        list.Sort((a, b) =>
        {
            // Folders stay above files whatever the sort says; inside each half the sort rules.
            if (a.IsDir != b.IsDir)
            {
                return a.IsDir ? -1 : 1;
            }
            var c = by(a, b);
            return cfg.SortDescending ? -c : c;
        });
        _entries = list;
    }

    private IEnumerable<Entry> Visible()
    {
        EnsureListed();
        foreach (var e in _entries!)
        {
            if (!PassesFilter(e))
            {
                continue;
            }
            if (_search.Length > 0 && !e.Name.Contains(_search, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            yield return e;
        }
    }

    private void Finish(List<string>? result)
    {
        ResultPaths = result;
        _open = false;
        var cfg = UiHost.Configuration.FilePicker;
        cfg.LastFolder = _dir;
        UiHost.Configuration.Save();
    }

    /// <summary>What OK would return right now, or null while OK should be disabled.</summary>
    private List<string>? Confirmable()
    {
        switch (_mode)
        {
            case FileDialogMode.OpenFile:
                return _selected is not null ? [_selected] : null;
            case FileDialogMode.OpenFiles:
                return _multi.Count > 0 ? [.. _multi] : null;
            case FileDialogMode.PickFolder:
                // A selected folder wins; with none, the folder being looked at is the answer.
                return [_selected ?? _dir];
            case FileDialogMode.SaveFile:
            {
                var name = _saveName.Trim();
                if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                {
                    return null;
                }
                if (Path.GetExtension(name).Length == 0 && _defaultExtension.Length > 0)
                {
                    name += _defaultExtension.StartsWith('.') ? _defaultExtension : "." + _defaultExtension;
                }
                return [Path.Combine(_dir, name)];
            }
            default:
            {
                var folder = _saveName.Trim();
                return folder.Length > 0 && folder.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
                    ? [Path.Combine(_dir, folder)]
                    : null;
            }
        }
    }

    /// <summary>Draws the window; false the frame the dialog is done (picked or cancelled).</summary>
    public bool Draw()
    {
        if (!_open)
        {
            return false;
        }
        var cfg = UiHost.Configuration.FilePicker;
        var defaultSize = new Vector2(
            cfg.WindowW > 200f ? cfg.WindowW : 860f,
            cfg.WindowH > 200f ? cfg.WindowH : 540f);
        ImGui.SetNextWindowSize(defaultSize, ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(560f, 360f), new Vector2(float.MaxValue, float.MaxValue));

        var t = ThemeService.Current;
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, t.Accent with { W = 0.55f });
        ImGui.PushStyleColor(ImGuiCol.Button, t.Accent with { W = 0.22f });
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, t.Accent with { W = 0.42f });
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, t.Accent with { W = 0.60f });
        ImGui.PushStyleColor(ImGuiCol.Header, t.Accent with { W = 0.30f });
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, t.Accent with { W = 0.42f });
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, t.Accent with { W = 0.55f });
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6f);

        bool began;
        var label = $"{_title}###aetherFilePicker";
        if (_isModal)
        {
            if (!_modalOpened)
            {
                _modalOpened = true;
                ImGui.OpenPopup(label);
            }
            began = ImGui.BeginPopupModal(label, ref _open, ImGuiWindowFlags.None);
        }
        else
        {
            began = ImGui.Begin(label, ref _open, ImGuiWindowFlags.NoCollapse);
        }
        try
        {
            if (!began)
            {
                return _open;
            }
            var size = ImGui.GetWindowSize();
            if (MathF.Abs(size.X - cfg.WindowW) > 1f || MathF.Abs(size.Y - cfg.WindowH) > 1f)
            {
                cfg.WindowW = size.X;
                cfg.WindowH = size.Y;
            }

            DrawToolbar(cfg);
            var footer = ImGui.GetFrameHeightWithSpacing() + ImGui.GetStyle().ItemSpacing.Y * 2f;
            var body = ImGui.GetContentRegionAvail().Y - footer;
            DrawSidebar(cfg, body);
            ImGui.SameLine();
            DrawBody(cfg, body);
            DrawFooter();

            if (ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows))
            {
                if (ImGui.IsKeyPressed(ImGuiKey.Escape))
                {
                    Finish(null);
                }
                else if (ImGui.IsKeyPressed(ImGuiKey.Enter) && !ImGui.IsAnyItemActive()
                    && Confirmable() is { } byKey)
                {
                    Finish(byKey);
                }
                else if (ImGui.IsKeyPressed(ImGuiKey.Backspace) && !ImGui.IsAnyItemActive())
                {
                    GoUp();
                }
            }
        }
        finally
        {
            if (_isModal && began)
            {
                ImGui.EndPopup();
            }
            else if (!_isModal)
            {
                ImGui.End();
            }
            ImGui.PopStyleVar();
            ImGui.PopStyleColor(7);
        }

        // Closed by the title bar X: a cancel, through the same door as every other exit.
        if (!_open && ResultPaths is null)
        {
            Finish(null);
            return false;
        }
        return _open;
    }

    private void GoUp()
    {
        if (Path.GetDirectoryName(_dir) is { Length: > 0 } parent)
        {
            NavigateTo(parent);
        }
    }

    private void DrawToolbar(Config.FilePickerConfig cfg)
    {
        using (UiHost.PluginInterface.UiBuilder.IconFontHandle.Push())
        {
            if (ImGui.Button(FontAwesomeIcon.ArrowLeft.ToIconString()) && _back.Count > 0)
            {
                _forward.Add(_dir);
                var target = _back[^1];
                _back.RemoveAt(_back.Count - 1);
                NavigateTo(target, pushHistory: false);
            }
            ImGui.SameLine();
            if (ImGui.Button(FontAwesomeIcon.ArrowRight.ToIconString()) && _forward.Count > 0)
            {
                _back.Add(_dir);
                var target = _forward[^1];
                _forward.RemoveAt(_forward.Count - 1);
                NavigateTo(target, pushHistory: false);
            }
            ImGui.SameLine();
            if (ImGui.Button(FontAwesomeIcon.ArrowUp.ToIconString()))
            {
                GoUp();
            }
            ImGui.SameLine();
            var starred = cfg.Favorites.Contains(_dir, StringComparer.OrdinalIgnoreCase);
            if (ImGui.Button((starred ? FontAwesomeIcon.Star : FontAwesomeIcon.StarHalfAlt).ToIconString()))
            {
                if (starred)
                {
                    cfg.Favorites.RemoveAll(f => string.Equals(f, _dir, StringComparison.OrdinalIgnoreCase));
                }
                else
                {
                    cfg.Favorites.Add(_dir);
                }
                UiHost.Configuration.Save();
            }
            HoverTip(Loc.T("picker.tip_star"));
            ImGui.SameLine();
            if (ImGui.Button(FontAwesomeIcon.FolderPlus.ToIconString()))
            {
                _newFolder = !_newFolder;
                _newFolderName = "";
                _focusNewFolder = _newFolder;
            }
            ImGui.SameLine();
            if (ImGui.Button((cfg.GridView ? FontAwesomeIcon.List : FontAwesomeIcon.Th).ToIconString()))
            {
                cfg.GridView = !cfg.GridView;
            }
        }

        // Sort picker and hidden toggle ride the right edge of the toolbar row.
        ImGui.SameLine();
        var sortLabels = new[] { Loc.T("picker.sort_name"), Loc.T("picker.sort_date"), Loc.T("picker.sort_size") };
        var sortLabel = sortLabels[Math.Clamp(cfg.SortField, 0, 2)] + (cfg.SortDescending ? " ↓" : " ↑");
        ImGui.SetNextItemWidth(150f);
        if (ImGui.BeginCombo("##pickerSort", sortLabel))
        {
            for (var i = 0; i < sortLabels.Length; i++)
            {
                if (ImGui.Selectable(sortLabels[i], cfg.SortField == i))
                {
                    if (cfg.SortField == i)
                    {
                        cfg.SortDescending = !cfg.SortDescending;
                    }
                    else
                    {
                        cfg.SortField = i;
                        cfg.SortDescending = false;
                    }
                }
            }
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        var hidden = cfg.ShowHidden;
        if (ImGui.Checkbox(Loc.T("picker.show_hidden"), ref hidden))
        {
            cfg.ShowHidden = hidden;
        }

        // The search field takes what is left of the row.
        ImGui.SameLine();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        if (ImGui.IsKeyPressed(ImGuiKey.F) && ImGui.GetIO().KeyCtrl)
        {
            ImGui.SetKeyboardFocusHere();
        }
        ImGui.InputTextWithHint("##pickerSearch", Loc.T("picker.search_hint"), ref _search, 128);

        DrawBreadcrumbs();
        if (_newFolder)
        {
            DrawNewFolderRow();
        }
    }

    private void DrawBreadcrumbs()
    {
        if (_editPath)
        {
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 30f);
            if (_focusPathEdit)
            {
                _focusPathEdit = false;
                ImGui.SetKeyboardFocusHere();
            }
            if (ImGui.InputText("##pickerPath", ref _pathEdit, 512, ImGuiInputTextFlags.EnterReturnsTrue))
            {
                NavigateTo(_pathEdit.Trim());
                _editPath = false;
            }
            ImGui.SameLine();
            using (UiHost.PluginInterface.UiBuilder.IconFontHandle.Push())
            {
                if (ImGui.SmallButton(FontAwesomeIcon.Times.ToIconString()))
                {
                    _editPath = false;
                }
            }
            return;
        }

        var parts = _dir.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var run = _dir.StartsWith('/') ? "/" : "";
        for (var i = 0; i < parts.Length; i++)
        {
            run = i == 0 && parts[0].EndsWith(':') ? parts[0] + Path.DirectorySeparatorChar
                : Path.Combine(run, parts[i]);
            if (i > 0)
            {
                ImGui.SameLine(0f, 2f);
                ImGui.TextDisabled("›");
                ImGui.SameLine(0f, 2f);
            }
            if (ImGui.SmallButton($"{parts[i]}##crumb{i}"))
            {
                NavigateTo(run);
            }
        }
        ImGui.SameLine();
        using (UiHost.PluginInterface.UiBuilder.IconFontHandle.Push())
        {
            if (ImGui.SmallButton(FontAwesomeIcon.Pen.ToIconString()))
            {
                _editPath = true;
                _pathEdit = _dir;
                _focusPathEdit = true;
            }
        }
        HoverTip(Loc.T("picker.tip_edit_path"));
    }

    private void DrawNewFolderRow()
    {
        ImGui.SetNextItemWidth(260f);
        if (_focusNewFolder)
        {
            _focusNewFolder = false;
            ImGui.SetKeyboardFocusHere();
        }
        var commit = ImGui.InputTextWithHint("##pickerNewFolder", Loc.T("picker.new_folder_hint"),
            ref _newFolderName, 64, ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();
        commit |= ImGui.Button(Loc.T("picker.new_folder_create"));
        if (commit && _newFolderName.Trim() is { Length: > 0 } name
            && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0)
        {
            try
            {
                var created = Directory.CreateDirectory(Path.Combine(_dir, name));
                _newFolder = false;
                NavigateTo(created.FullName);
            }
            catch (Exception ex)
            {
                _listError = ex.Message;
            }
        }
    }

    private void DrawSidebar(Config.FilePickerConfig cfg, float height)
    {
        if (!ImGui.BeginChild("##pickerSide", new Vector2(180f, height), true))
        {
            ImGui.EndChild();
            return;
        }

        void Link(FontAwesomeIcon icon, string label, string path)
        {
            if (path.Length == 0 || !Directory.Exists(path))
            {
                return;
            }
            var here = string.Equals(path, _dir, StringComparison.OrdinalIgnoreCase);
            using (UiHost.PluginInterface.UiBuilder.IconFontHandle.Push())
            {
                ImGui.TextUnformatted(icon.ToIconString());
            }
            ImGui.SameLine();
            if (ImGui.Selectable($"{label}##{path}", here))
            {
                NavigateTo(path);
            }
        }

        ImGui.TextDisabled(Loc.T("picker.quick_links"));
        Link(FontAwesomeIcon.Desktop, Loc.T("picker.place_desktop"),
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
        Link(FontAwesomeIcon.File, Loc.T("picker.place_documents"),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        Link(FontAwesomeIcon.Download, Loc.T("picker.place_downloads"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
        Link(FontAwesomeIcon.Image, Loc.T("picker.place_pictures"),
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
        foreach (var (labelKey, path, icon) in AetherFileDialogManager.ExtraQuickLinks)
        {
            Link(icon, Loc.T(labelKey), path);
        }

        if (cfg.Favorites.Count > 0)
        {
            ImGui.Spacing();
            ImGui.TextDisabled(Loc.T("picker.favorites"));
            string? remove = null;
            foreach (var fav in cfg.Favorites)
            {
                Link(FontAwesomeIcon.Star, Path.GetFileName(fav.TrimEnd(Path.DirectorySeparatorChar)) is { Length: > 0 } leaf ? leaf : fav, fav);
                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                {
                    remove = fav;
                }
            }
            if (remove is not null)
            {
                cfg.Favorites.Remove(remove);
                UiHost.Configuration.Save();
            }
        }

        ImGui.Spacing();
        ImGui.TextDisabled(Loc.T("picker.drives"));
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.IsReady)
                {
                    Link(FontAwesomeIcon.Hdd, drive.Name, drive.RootDirectory.FullName);
                }
            }
        }
        catch (Exception)
        {
            // A drive enumeration hiccup costs the section, never the picker.
        }
        ImGui.EndChild();
    }

    private void DrawBody(Config.FilePickerConfig cfg, float height)
    {
        // The preview pane appears only when an image is selected; the list keeps the width
        // otherwise, which is what a picker full of folders wants.
        var previewFor = _selected is not null && !FoldersOnly
            && LooksLikeImage(Path.GetExtension(_selected).ToLowerInvariant())
            ? _selected
            : null;
        var previewW = previewFor is not null ? MathF.Min(320f, ImGui.GetContentRegionAvail().X * 0.38f) : 0f;
        var listW = ImGui.GetContentRegionAvail().X - (previewW > 0f ? previewW + ImGui.GetStyle().ItemSpacing.X : 0f);

        if (ImGui.BeginChild("##pickerList", new Vector2(listW, height), true))
        {
            if (_listError.Length > 0)
            {
                ImGui.TextColored(UiColors.Danger, _listError);
            }
            var visible = Visible().ToList();
            if (visible.Count == 0 && _listError.Length == 0)
            {
                ImGui.TextDisabled(Loc.T("picker.empty"));
            }
            else if (cfg.GridView)
            {
                DrawGrid(visible);
            }
            else
            {
                DrawRows(visible);
            }
        }
        ImGui.EndChild();

        if (previewFor is null)
        {
            return;
        }
        ImGui.SameLine();
        if (ImGui.BeginChild("##pickerPreview", new Vector2(previewW, height), true))
        {
            var wrap = UiHost.TextureProvider.GetFromFile(previewFor).GetWrapOrDefault();
            var avail = ImGui.GetContentRegionAvail();
            if (wrap is not null)
            {
                var fit = MathF.Min((avail.X - 4f) / wrap.Width, (avail.Y * 0.7f) / wrap.Height);
                fit = MathF.Min(fit, 1.5f);
                var w = wrap.Width * fit;
                var h = wrap.Height * fit;
                ImGui.SetCursorPosX((avail.X - w) * 0.5f + ImGui.GetCursorPosX());
                ImGui.Image(wrap.Handle, new Vector2(w, h));
                ImGui.Spacing();
                ImGui.TextDisabled($"{wrap.Width} x {wrap.Height}");
            }
            else
            {
                ImGui.TextDisabled(Loc.T("picker.preview_loading"));
            }
            ImGui.PushTextWrapPos();
            ImGui.TextUnformatted(Path.GetFileName(previewFor));
            ImGui.PopTextWrapPos();
            try
            {
                var info = new FileInfo(previewFor);
                ImGui.TextDisabled($"{SizeText(info.Length)}  ·  {info.LastWriteTime:yyyy-MM-dd HH:mm}");
            }
            catch (Exception)
            {
            }
        }
        ImGui.EndChild();
    }

    private bool IsPicked(Entry e) => _mode == FileDialogMode.OpenFiles
        ? _multi.Contains(e.Path, StringComparer.OrdinalIgnoreCase)
        : _selected == e.Path;

    private void DrawGrid(List<Entry> visible)
    {
        const float Cell = 96f;
        const float Gap = 8f;
        var perRow = Math.Max(1, (int)((ImGui.GetContentRegionAvail().X + Gap) / (Cell + Gap)));
        var textH = ImGui.GetTextLineHeight();
        for (var i = 0; i < visible.Count; i++)
        {
            var e = visible[i];
            if (i % perRow != 0)
            {
                ImGui.SameLine(0f, Gap);
            }
            ImGui.BeginGroup();
            var tl = ImGui.GetCursorScreenPos();
            var picked = ImGui.Selectable($"##cell{e.Path}", IsPicked(e),
                ImGuiSelectableFlags.None, new Vector2(Cell, Cell + textH + 4f));
            var dl = ImGui.GetWindowDrawList();
            var thumb = new Vector2(tl.X, tl.Y);
            if (e.IsDir)
            {
                IconDraw.AddCentered(dl, FontAwesomeIcon.Folder, Cell * 0.45f,
                    thumb + new Vector2(Cell * 0.5f, Cell * 0.5f),
                    ImGui.GetColorU32(ThemeService.Current.AccentLight with { W = 0.85f }));
            }
            else if (LooksLikeImage(e.Ext)
                && UiHost.TextureProvider.GetFromFile(e.Path).GetWrapOrDefault() is { } wrap)
            {
                var fit = MathF.Min(Cell / wrap.Width, Cell / wrap.Height);
                var w = wrap.Width * fit;
                var h = wrap.Height * fit;
                var at = thumb + new Vector2((Cell - w) * 0.5f, (Cell - h) * 0.5f);
                dl.AddImageRounded(wrap.Handle, at, at + new Vector2(w, h),
                    Vector2.Zero, Vector2.One, 0xFFFFFFFF, 6f);
            }
            else
            {
                IconDraw.AddCentered(dl, IconFor(e.Ext), Cell * 0.4f,
                    thumb + new Vector2(Cell * 0.5f, Cell * 0.5f),
                    ImGui.GetColorU32(UiColors.Muted));
            }
            var name = e.Name;
            var nameW = ImGui.CalcTextSize(name).X;
            while (nameW > Cell && name.Length > 4)
            {
                name = name[..^4] + "…";
                nameW = ImGui.CalcTextSize(name).X;
            }
            dl.AddText(thumb + new Vector2((Cell - nameW) * 0.5f, Cell + 2f),
                ImGui.GetColorU32(UiColors.Body), name);
            ImGui.EndGroup();
            HandleActivate(visible, i, picked);
        }
    }

    private void DrawRows(List<Entry> visible)
    {
        for (var i = 0; i < visible.Count; i++)
        {
            var e = visible[i];
            using (UiHost.PluginInterface.UiBuilder.IconFontHandle.Push())
            {
                ImGui.TextColored(
                    e.IsDir ? ThemeService.Current.AccentLight : UiColors.Muted,
                    (e.IsDir ? FontAwesomeIcon.Folder : IconFor(e.Ext)).ToIconString());
            }
            ImGui.SameLine();
            var picked = ImGui.Selectable($"{e.Name}##row{e.Path}", IsPicked(e));
            if (!e.IsDir)
            {
                ImGui.SameLine(ImGui.GetContentRegionAvail().X - 150f);
                ImGui.TextDisabled(SizeText(e.Size));
                ImGui.SameLine(ImGui.GetContentRegionAvail().X - 90f);
                ImGui.TextDisabled(e.ModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd"));
            }
            HandleActivate(visible, i, picked);
        }
    }

    /// <summary>Click selects, double-click opens: a folder navigates, a file confirms. Multi-select
    /// adds Ctrl+click toggling and Shift+click ranges over the visible order.</summary>
    private void HandleActivate(List<Entry> visible, int index, bool picked)
    {
        if (!picked)
        {
            return;
        }
        var e = visible[index];
        var now = ImGui.GetTime();
        var doubled = _lastClickPath == e.Path && now - _lastClickTime < 0.35;
        _lastClickPath = e.Path;
        _lastClickTime = now;

        if (e.IsDir)
        {
            if (FoldersOnly)
            {
                _selected = e.Path;
            }
            if (doubled)
            {
                NavigateTo(e.Path);
            }
            _lastClickIndex = index;
            return;
        }

        if (_mode == FileDialogMode.OpenFiles)
        {
            var io = ImGui.GetIO();
            if (io.KeyShift && _lastClickIndex >= 0)
            {
                var from = Math.Min(_lastClickIndex, index);
                var to = Math.Max(_lastClickIndex, index);
                for (var i = from; i <= to; i++)
                {
                    if (!visible[i].IsDir)
                    {
                        AddToMulti(visible[i].Path);
                    }
                }
            }
            else if (io.KeyCtrl)
            {
                if (_multi.RemoveAll(p => string.Equals(p, e.Path, StringComparison.OrdinalIgnoreCase)) == 0)
                {
                    AddToMulti(e.Path);
                }
            }
            else
            {
                _multi.Clear();
                AddToMulti(e.Path);
            }
            _selected = e.Path;
            _lastClickIndex = index;
            if (doubled && _multi.Count > 0)
            {
                Finish([.. _multi]);
            }
            return;
        }

        if (_mode == FileDialogMode.SaveFile)
        {
            // Clicking an existing file adopts its name, the way every native save dialog does.
            _selected = e.Path;
            _saveName = e.Name;
            _lastClickIndex = index;
            return;
        }

        _selected = e.Path;
        _lastClickIndex = index;
        if (doubled)
        {
            Finish([e.Path]);
        }
    }

    private void AddToMulti(string path)
    {
        if (_multi.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }
        if (_maxSelection > 0 && _multi.Count >= _maxSelection)
        {
            return;
        }
        _multi.Add(path);
    }

    private void DrawFooter()
    {
        ImGui.Separator();
        var okLabel = _mode is FileDialogMode.SaveFile or FileDialogMode.SaveFolder
            ? Loc.T("picker.save")
            : Loc.T("picker.open");
        var cancelLabel = Loc.T("common.cancel");
        var buttonsW = ImGui.CalcTextSize(okLabel).X + ImGui.CalcTextSize(cancelLabel).X + 220f;

        if (_mode is FileDialogMode.SaveFile or FileDialogMode.SaveFolder)
        {
            // The name being saved is an input, not a status line.
            ImGui.SetNextItemWidth(MathF.Max(160f, ImGui.GetContentRegionAvail().X - buttonsW));
            ImGui.InputTextWithHint("##pickerSaveName",
                Loc.T(_mode == FileDialogMode.SaveFile ? "picker.file_name_hint" : "picker.new_folder_hint"),
                ref _saveName, 128);
        }
        else
        {
            var status = _mode switch
            {
                FileDialogMode.OpenFiles when _multi.Count > 0 =>
                    string.Format(Loc.T("picker.selected_count"), _multi.Count),
                FileDialogMode.PickFolder => _selected ?? _dir,
                _ => _selected is not null ? Path.GetFileName(_selected) : "",
            };
            ImGui.TextDisabled(status.Length > 0 ? status : Loc.T("picker.nothing_selected"));
        }

        // The filter combo and the two buttons ride the right edge.
        ImGui.SameLine(MathF.Max(0f, ImGui.GetContentRegionAvail().X - buttonsW) + ImGui.GetCursorPosX());
        if (_filters.Count > 1)
        {
            ImGui.SetNextItemWidth(160f);
            if (ImGui.BeginCombo("##pickerFilter", _filters[Math.Clamp(_filterIndex, 0, _filters.Count - 1)].Label))
            {
                for (var i = 0; i < _filters.Count; i++)
                {
                    if (ImGui.Selectable(_filters[i].Label, i == _filterIndex))
                    {
                        _filterIndex = i;
                        _selected = null;
                        _multi.Clear();
                    }
                }
                ImGui.EndCombo();
            }
            ImGui.SameLine();
        }

        var confirmable = Confirmable();
        using (ImRaii.Disabled(confirmable is null))
        {
            if (ImGui.Button(okLabel, new Vector2(96f, 0f)))
            {
                Finish(confirmable);
            }
        }
        ImGui.SameLine();
        if (ImGui.Button(cancelLabel, new Vector2(96f, 0f)))
        {
            Finish(null);
        }
    }

    private static FontAwesomeIcon IconFor(string ext) => ext switch
    {
        ".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp" or ".gif" => FontAwesomeIcon.FileImage,
        ".mp3" or ".ogg" or ".wav" or ".flac" => FontAwesomeIcon.FileAudio,
        ".mp4" or ".mkv" or ".webm" or ".avi" => FontAwesomeIcon.FileVideo,
        ".zip" or ".rar" or ".7z" => FontAwesomeIcon.FileArchive,
        ".txt" or ".md" or ".json" or ".xml" or ".log" => FontAwesomeIcon.FileAlt,
        _ => FontAwesomeIcon.File,
    };

    private static string SizeText(long bytes) => bytes switch
    {
        >= 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024 * 1024):0.0} GB",
        >= 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.0} MB",
        >= 1024 => $"{bytes / 1024.0:0.0} KB",
        _ => $"{bytes} B",
    };

    private static void HoverTip(string text)
    {
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(text);
        }
    }
}
