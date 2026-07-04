using ScreenTrans.Query;
using UserControl = System.Windows.Controls.UserControl;
using Button = System.Windows.Controls.Button;
using TreeView = System.Windows.Controls.TreeView;
using TreeViewItem = System.Windows.Controls.TreeViewItem;
using StackPanel = System.Windows.Controls.StackPanel;
using TextBlock = System.Windows.Controls.TextBlock;
using Border = System.Windows.Controls.Border;
using Orientation = System.Windows.Controls.Orientation;
using Grid = System.Windows.Controls.Grid;
using ColumnDefinition = System.Windows.Controls.ColumnDefinition;
using GridLength = System.Windows.GridLength;
using GridUnitType = System.Windows.GridUnitType;
using Thickness = System.Windows.Thickness;
using CornerRadius = System.Windows.CornerRadius;
using Visibility = System.Windows.Visibility;
using RoutedEventArgs = System.Windows.RoutedEventArgs;
using RoutedPropertyChangedEventArgs = System.Windows.RoutedPropertyChangedEventArgs<object>;
using TextTrimming = System.Windows.TextTrimming;
using VerticalAlignment = System.Windows.VerticalAlignment;
using FrameworkElement = System.Windows.FrameworkElement;
using UIElement = System.Windows.UIElement;
using Point = System.Windows.Point;
using DataObject = System.Windows.DataObject;
using DragDrop = System.Windows.DragDrop;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MouseButtonState = System.Windows.Input.MouseButtonState;
using Cursors = System.Windows.Input.Cursors;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxResult = System.Windows.MessageBoxResult;
using MessageBoxImage = System.Windows.MessageBoxImage;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace ScreenTrans.Present;

/// <summary>
/// 我的筆記分頁（Issue #34）：左側**多層資料夾樹**（標準 <see cref="TreeView"/>，可新增子夾／更名／刪除、
/// 拖曳移動節點如檔案總管、防成環由 <see cref="NotesStore"/> 保證）、右側選取夾之條目（拖曳排序、播音／檢視／刪除）。
/// 每次變更即落地 notes.json；語音以 provider 委派取用。
/// </summary>
public partial class NotesPage : UserControl
{
    private const string FmtFolder = "ScreenTrans.NoteFolderId";
    private const string FmtEntry = "ScreenTrans.NoteEntryId";

    private readonly NotesStore _store;
    private readonly Func<ISpeechService?> _speech;
    private NotesData _data;

    public event Action<NoteEntry>? ViewRequested;

    private TreeViewItem? _pressItem;
    private Point _pressPoint;
    private NoteEntry? _entryDrag;
    private Point _entryStart;

    public NotesPage(NotesStore store, Func<ISpeechService?> speechProvider)
    {
        InitializeComponent();
        _store = store;
        _speech = speechProvider;
        _data = _store.LoadEnsured();

        AddBtn.Click += (_, _) => OnAddFolder(sub: false);
        AddSubBtn.Click += (_, _) => OnAddFolder(sub: true);
        RenameBtn.Click += OnRename;
        DeleteBtn.Click += OnDelete;
        FolderTree.SelectedItemChanged += OnFolderSelected;
        FolderTree.Drop += OnTreeBackgroundDrop;    // 拖到空白處 → 移到頂層
        FolderTree.DragOver += (_, e) => { e.Effects = DragDropEffects.Move; e.Handled = true; };

        BuildTree();
    }

    public void Reload()
    {
        _data = _store.LoadEnsured();
        BuildTree();
    }

    private NoteFolder? Selected => (FolderTree.SelectedItem as TreeViewItem)?.Tag as NoteFolder;

    // ---- 樹建置 ----

    private void BuildTree()
    {
        var keepId = Selected?.Id;
        FolderTree.Items.Clear();
        foreach (var f in _data.Folders)
        {
            FolderTree.Items.Add(MakeItem(f));
        }
        var target = keepId is null ? null : FindItem(FolderTree.Items, keepId);
        (target ?? FirstItem())?.SetValue(TreeViewItem.IsSelectedProperty, true);
        RenderFolder();
    }

    private TreeViewItem MakeItem(NoteFolder f)
    {
        var item = new TreeViewItem
        {
            Header = $"📁 {f.Name}　{f.Entries.Count}",
            Tag = f,
            IsExpanded = true,
            AllowDrop = true,
        };
        item.PreviewMouseLeftButtonDown += OnItemMouseDown;
        item.PreviewMouseMove += OnItemMouseMove;
        item.Drop += OnItemDrop;
        item.DragOver += (_, e) => { e.Effects = DragDropEffects.Move; e.Handled = true; };
        foreach (var sub in f.Folders)
        {
            item.Items.Add(MakeItem(sub));
        }
        return item;
    }

    private TreeViewItem? FirstItem() => FolderTree.Items.Count > 0 ? (TreeViewItem)FolderTree.Items[0]! : null;

    private static TreeViewItem? FindItem(System.Windows.Controls.ItemCollection items, string id)
    {
        foreach (TreeViewItem it in items)
        {
            if (it.Tag is NoteFolder f && f.Id == id)
            {
                return it;
            }
            var sub = FindItem(it.Items, id);
            if (sub is not null)
            {
                return sub;
            }
        }
        return null;
    }

    private void OnFolderSelected(object? sender, RoutedPropertyChangedEventArgs e) => RenderFolder();

    private void RenderFolder()
    {
        EntryPanel.Children.Clear();
        var f = Selected;
        bool any = f is not null && f.Entries.Count > 0;
        EmptyHint.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        if (f is null)
        {
            return;
        }
        foreach (var entry in f.Entries)
        {
            EntryPanel.Children.Add(EntryRow(entry));
        }
    }

    private void Persist() { _store.Save(_data); BuildTree(); }

    // ---- 資料夾操作 ----

    private void OnAddFolder(bool sub)
    {
        var name = Microsoft.VisualBasic.Interaction.InputBox(sub ? "子資料夾名稱：" : "資料夾名稱：",
            sub ? "新增子資料夾" : "新增資料夾", "新資料夾");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }
        NoteFolder? created;
        if (sub)
        {
            var parent = Selected;
            if (parent is null) { return; }
            created = NotesStore.AddSubFolder(_data, parent.Id, name);
        }
        else
        {
            created = NotesStore.AddFolder(_data, name);
        }
        _store.Save(_data);
        BuildTree();
        if (created is not null)
        {
            FindItem(FolderTree.Items, created.Id)?.SetValue(TreeViewItem.IsSelectedProperty, true);
        }
    }

    private void OnRename(object? sender, RoutedEventArgs e)
    {
        var f = Selected;
        if (f is null) { return; }
        var name = Microsoft.VisualBasic.Interaction.InputBox("新名稱：", "更名資料夾", f.Name);
        if (string.IsNullOrWhiteSpace(name)) { return; }
        NotesStore.RenameFolder(_data, f.Id, name);
        Persist();
    }

    private void OnDelete(object? sender, RoutedEventArgs e)
    {
        var f = Selected;
        if (f is null) { return; }
        var msg = (f.Entries.Count > 0 || f.Folders.Count > 0)
            ? $"刪除資料夾「{f.Name}」及其子夾與 {f.Entries.Count} 筆筆記？此動作無法復原。"
            : $"刪除資料夾「{f.Name}」？";
        if (MessageBox.Show(msg, "刪除資料夾", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
        {
            return;
        }
        NotesStore.RemoveFolder(_data, f.Id);
        Persist();
    }

    // ---- 資料夾/條目拖曳移動（節點移動如檔案總管；防環由 store） ----

    private void OnItemMouseDown(object sender, MouseButtonEventArgs e)
    {
        _pressItem = sender as TreeViewItem;
        _pressPoint = e.GetPosition(null);
    }

    private void OnItemMouseMove(object sender, MouseEventArgs e)
    {
        if (_pressItem is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }
        var p = e.GetPosition(null);
        if (Math.Abs(p.X - _pressPoint.X) < 6 && Math.Abs(p.Y - _pressPoint.Y) < 6)
        {
            return;
        }
        if (_pressItem.Tag is NoteFolder f)
        {
            var moving = _pressItem;
            _pressItem = null;
            DragDrop.DoDragDrop(moving, new DataObject(FmtFolder, f.Id), DragDropEffects.Move);
        }
    }

    private void OnItemDrop(object sender, DragEventArgs e)
    {
        if ((sender as TreeViewItem)?.Tag is not NoteFolder target)
        {
            return;
        }
        if (e.Data.GetDataPresent(FmtFolder) && e.Data.GetData(FmtFolder) is string fid)
        {
            NotesStore.MoveFolder(_data, fid, target.Id); // 防移入自身/子孫由 store 保證
            Persist();
        }
        else if (e.Data.GetDataPresent(FmtEntry) && e.Data.GetData(FmtEntry) is string eid)
        {
            NotesStore.MoveEntry(_data, eid, target.Id);
            Persist();
        }
        e.Handled = true;
    }

    private void OnTreeBackgroundDrop(object? sender, DragEventArgs e)
    {
        if (e.Handled)
        {
            return; // 已由某節點處理
        }
        if (e.Data.GetDataPresent(FmtFolder) && e.Data.GetData(FmtFolder) is string fid)
        {
            NotesStore.MoveFolder(_data, fid, null); // 移到頂層
            Persist();
        }
    }

    // ---- 條目版型與排序 ----

    private UIElement EntryRow(NoteEntry entry)
    {
        var card = new Border
        {
            Background = Brush("#FFFFFF"),
            BorderBrush = Brush("#F4C2D0"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 10, 10, 10),
            Margin = new Thickness(0, 0, 0, 8),
            AllowDrop = true,
        };
        card.Drop += OnEntryPanelDrop;
        card.DragOver += (_, e) => { e.Effects = DragDropEffects.Move; e.Handled = true; };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var handle = new TextBlock
        {
            Text = "≡",
            FontSize = 16,
            Foreground = Brush("#C77D9A"),
            Cursor = Cursors.SizeNS,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 8, 0),
            ToolTip = "拖曳排序／拖到左側資料夾移動",
        };
        handle.PreviewMouseLeftButtonDown += (_, ev) => { _entryDrag = entry; _entryStart = ev.GetPosition(null); };
        handle.PreviewMouseMove += OnEntryHandleMove;
        Grid.SetColumn(handle, 0);
        grid.Children.Add(handle);

        var text = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(entry.Original) ? "（無內容）" : entry.Original,
            FontSize = 14,
            Foreground = Brush("#3A2C33"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        actions.Children.Add(ActionButton("▶", "播音（英文原句）", "#2F6FED", "#F0F6FF", "#CFE0FB",
            () => _speech()?.Speak(entry.Original, "en-US", stopPrevious: true)));
        actions.Children.Add(ActionButton("檢視", "開啟中英詳情", "#4A4A4A", "#F5F5F5", "#DCDCDC",
            () => ViewRequested?.Invoke(entry)));
        actions.Children.Add(ActionButton("刪除", "自筆記移除此筆", "#B23B3B", "#FDF2F2", "#F0D2D2",
            () => { NotesStore.RemoveEntry(_data, entry.Id); Persist(); }));
        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);

        card.Child = grid;
        card.Tag = entry;
        return card;
    }

    private void OnEntryHandleMove(object sender, MouseEventArgs e)
    {
        if (_entryDrag is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }
        var p = e.GetPosition(null);
        if (Math.Abs(p.X - _entryStart.X) < 6 && Math.Abs(p.Y - _entryStart.Y) < 6)
        {
            return;
        }
        var moving = _entryDrag;
        _entryDrag = null;
        DragDrop.DoDragDrop(EntryPanel, new DataObject(FmtEntry, moving.Id), DragDropEffects.Move);
    }

    // 條目落在另一條目上 → 同夾排序（落點在該列之前/後由 Y 中線判定）
    private void OnEntryPanelDrop(object sender, DragEventArgs e)
    {
        var f = Selected;
        if (f is null || !e.Data.GetDataPresent(FmtEntry) || e.Data.GetData(FmtEntry) is not string eid)
        {
            return;
        }
        int from = f.Entries.FindIndex(x => x.Id == eid);
        if (from < 0)
        {
            return; // 來自他夾之條目落在此清單 → 交由資料夾 drop 處理移動，此處不排序
        }
        int to = TargetIndex(e.GetPosition(EntryPanel).Y);
        NotesStore.Reorder(f, from, to);
        _store.Save(_data);
        RenderFolder();
        e.Handled = true;
    }

    private int TargetIndex(double y)
    {
        for (int i = 0; i < EntryPanel.Children.Count; i++)
        {
            var c = (FrameworkElement)EntryPanel.Children[i];
            double top = c.TranslatePoint(new Point(0, 0), EntryPanel).Y;
            if (y < top + c.ActualHeight / 2)
            {
                return i;
            }
        }
        return Math.Max(0, EntryPanel.Children.Count - 1);
    }

    private static Button ActionButton(string content, string tip, string fg, string bg, string border, Action onClick)
    {
        var btn = new Button
        {
            Content = content,
            ToolTip = tip,
            Foreground = Brush(fg),
            Background = Brush(bg),
            BorderBrush = Brush(border),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(6, 0, 0, 0),
            FontSize = 12.5,
            Cursor = Cursors.Hand,
        };
        btn.Click += (_, _) => onClick();
        return btn;
    }

    private static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
}
