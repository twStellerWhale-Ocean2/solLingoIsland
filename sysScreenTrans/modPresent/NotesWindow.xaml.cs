using ScreenTrans.Query;
using Window = System.Windows.Window;
using Grid = System.Windows.Controls.Grid;
using ColumnDefinition = System.Windows.Controls.ColumnDefinition;
using Button = System.Windows.Controls.Button;
using ListBox = System.Windows.Controls.ListBox;
using ListBoxItem = System.Windows.Controls.ListBoxItem;
using StackPanel = System.Windows.Controls.StackPanel;
using TextBlock = System.Windows.Controls.TextBlock;
using Border = System.Windows.Controls.Border;
using Orientation = System.Windows.Controls.Orientation;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using SelectionChangedEventArgs = System.Windows.Controls.SelectionChangedEventArgs;
using GridLength = System.Windows.GridLength;
using GridUnitType = System.Windows.GridUnitType;
using Thickness = System.Windows.Thickness;
using CornerRadius = System.Windows.CornerRadius;
using Visibility = System.Windows.Visibility;
using RoutedEventArgs = System.Windows.RoutedEventArgs;
using FontWeights = System.Windows.FontWeights;
using TextTrimming = System.Windows.TextTrimming;
using VerticalAlignment = System.Windows.VerticalAlignment;
using FrameworkElement = System.Windows.FrameworkElement;
using UIElement = System.Windows.UIElement;
using Point = System.Windows.Point;
using DataObject = System.Windows.DataObject;
using DragDrop = System.Windows.DragDrop;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MouseButtonState = System.Windows.Input.MouseButtonState;
using Cursors = System.Windows.Input.Cursors;
using Key = System.Windows.Input.Key;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxResult = System.Windows.MessageBoxResult;
using MessageBoxImage = System.Windows.MessageBoxImage;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace ScreenTrans.Present;

/// <summary>
/// 我的筆記視窗（[modPresent模組] 我的筆記檢視契約，design ＜III.C.(C)＞ 我的筆記頁，spec#7）：
/// 左側自訂資料夾（新增／更名／刪除），右側該夾條目垂直堆疊（新在上、前端 ≡ 拖曳握把可上下排序）；
/// 單筆重聽、檢視（<see cref="ViewRequested"/> 由呼叫端開結果卡片、重用三欄詳情/發音）、刪除、右鍵移到他夾。
/// <b>非結果視窗</b>；語音以 provider 委派取用；每次變更即落地 notes.json。
/// </summary>
public partial class NotesWindow : Window
{
    private readonly NotesStore _store;
    private readonly Func<ISpeechService?> _speech;
    private NotesData _data;

    public event Action<NoteEntry>? ViewRequested;

    private NoteEntry? _dragEntry;
    private Point _dragStart;

    public NotesWindow(NotesStore store, Func<ISpeechService?> speechProvider)
    {
        InitializeComponent();
        _store = store;
        _speech = speechProvider;
        _data = _store.LoadEnsured();

        AddFolderBtn.Click += OnAddFolder;
        RenameFolderBtn.Click += OnRenameFolder;
        DeleteFolderBtn.Click += OnDeleteFolder;
        EntryPanel.AllowDrop = true;
        EntryPanel.DragOver += (_, e) => { e.Effects = DragDropEffects.Move; e.Handled = true; };
        EntryPanel.Drop += OnEntryDrop;

        BuildFolders();
    }

    /// <summary>重讀並重建（供收藏新增後外部刷新）。</summary>
    public void Reload()
    {
        _data = _store.LoadEnsured();
        BuildFolders();
    }

    private NoteFolder? Selected => (FolderList.SelectedItem as ListBoxItem)?.Tag as NoteFolder;

    private void BuildFolders()
    {
        var keepId = Selected?.Id;
        FolderList.SelectionChanged -= OnFolderChanged;
        FolderList.Items.Clear();
        foreach (var f in _data.Folders)
        {
            FolderList.Items.Add(new ListBoxItem { Content = FolderItem(f), Tag = f, Padding = new Thickness(4) });
        }
        FolderList.SelectionChanged += OnFolderChanged;

        int idx = 0;
        if (keepId is not null)
        {
            idx = Math.Max(0, _data.Folders.FindIndex(f => f.Id == keepId));
        }
        FolderList.SelectedIndex = _data.Folders.Count > 0 ? idx : -1;
        RenderFolder();
    }

    private void OnFolderChanged(object? sender, SelectionChangedEventArgs e) => RenderFolder();

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

    // ---- 資料夾操作 ----

    private void OnAddFolder(object? sender, RoutedEventArgs e)
    {
        var name = Microsoft.VisualBasic.Interaction.InputBox("資料夾名稱：", "新增資料夾", "新資料夾");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }
        var f = NotesStore.AddFolder(_data, name);
        _store.Save(_data);
        BuildFolders();
        SelectFolder(f.Id);
    }

    private void OnRenameFolder(object? sender, RoutedEventArgs e)
    {
        var f = Selected;
        if (f is null)
        {
            return;
        }
        var name = Microsoft.VisualBasic.Interaction.InputBox("新名稱：", "更名資料夾", f.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }
        NotesStore.RenameFolder(_data, f.Id, name);
        _store.Save(_data);
        BuildFolders();
    }

    private void OnDeleteFolder(object? sender, RoutedEventArgs e)
    {
        var f = Selected;
        if (f is null)
        {
            return;
        }
        var msg = f.Entries.Count > 0
            ? $"刪除資料夾「{f.Name}」及其中 {f.Entries.Count} 筆筆記？此動作無法復原。"
            : $"刪除資料夾「{f.Name}」？";
        if (MessageBox.Show(msg, "刪除資料夾", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
        {
            return;
        }
        NotesStore.RemoveFolder(_data, f.Id);
        _store.Save(_data);
        BuildFolders();
    }

    private void SelectFolder(string id)
    {
        for (int i = 0; i < FolderList.Items.Count; i++)
        {
            if ((FolderList.Items[i] as ListBoxItem)?.Tag is NoteFolder f && f.Id == id)
            {
                FolderList.SelectedIndex = i;
                return;
            }
        }
    }

    // ---- 條目版型 ----

    private static StackPanel FolderItem(NoteFolder f)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new TextBlock
        {
            Text = "📁 " + f.Name,
            FontSize = 13,
            Foreground = Brush("#1B1B1B"),
        });
        sp.Children.Add(new TextBlock
        {
            Text = "  " + f.Entries.Count,
            FontSize = 11,
            Foreground = Brush("#8A8A8A"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        return sp;
    }

    private Border EntryRow(NoteEntry entry)
    {
        var card = new Border
        {
            Background = Brush("#FFFFFF"),
            BorderBrush = Brush("#E6E6E6"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 10, 10, 10),
            Margin = new Thickness(0, 0, 0, 8),
            Tag = entry,
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                       // 拖曳握把
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });  // 文字
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                       // 動作

        // 拖曳握把
        var handle = new TextBlock
        {
            Text = "≡",
            FontSize = 16,
            Foreground = Brush("#B8B8B8"),
            Cursor = Cursors.SizeNS,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 8, 0),
            ToolTip = "拖曳調整順序",
        };
        handle.PreviewMouseLeftButtonDown += (_, e) => { _dragEntry = entry; _dragStart = e.GetPosition(null); };
        handle.PreviewMouseMove += OnHandleMove;
        Grid.SetColumn(handle, 0);
        grid.Children.Add(handle);

        var text = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(entry.Original) ? "（無內容）" : entry.Original,
            FontSize = 14,
            Foreground = Brush("#1B1B1B"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };
        actions.Children.Add(ActionButton("▶", "播音（英文原句）", "#2F6FED", "#F0F6FF", "#CFE0FB",
            () => _speech()?.Speak(entry.Original, "en-US", stopPrevious: true)));
        actions.Children.Add(ActionButton("檢視", "開啟中英詳情", "#4A4A4A", "#F5F5F5", "#DCDCDC",
            () => ViewRequested?.Invoke(entry)));
        actions.Children.Add(ActionButton("刪除", "自筆記移除此筆", "#B23B3B", "#FDF2F2", "#F0D2D2",
            () => { NotesStore.RemoveEntry(_data, entry.Id); _store.Save(_data); BuildFolders(); }));
        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);

        card.Child = grid;
        card.ContextMenu = MoveMenu(entry);
        return card;
    }

    /// <summary>右鍵「移到」其他資料夾（跨夾歸類）。</summary>
    private ContextMenu MoveMenu(NoteEntry entry)
    {
        var menu = new ContextMenu();
        var head = new MenuItem { Header = "移到資料夾", IsEnabled = false };
        menu.Items.Add(head);
        foreach (var f in _data.Folders)
        {
            if (Selected is not null && f.Id == Selected.Id)
            {
                continue; // 略過所在夾
            }
            var target = f;
            var mi = new MenuItem { Header = f.Name };
            mi.Click += (_, _) => { NotesStore.MoveEntry(_data, entry.Id, target.Id); _store.Save(_data); BuildFolders(); };
            menu.Items.Add(mi);
        }
        return menu;
    }

    // ---- 拖曳排序 ----

    private void OnHandleMove(object? sender, MouseEventArgs e)
    {
        if (_dragEntry is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }
        var p = e.GetPosition(null);
        if (Math.Abs(p.X - _dragStart.X) < 6 && Math.Abs(p.Y - _dragStart.Y) < 6)
        {
            return; // 未達拖曳閾值
        }
        var moving = _dragEntry;
        DragDrop.DoDragDrop(EntryPanel, new DataObject(typeof(NoteEntry), moving), DragDropEffects.Move);
        _dragEntry = null;
    }

    private void OnEntryDrop(object? sender, DragEventArgs e)
    {
        var f = Selected;
        if (f is null || e.Data.GetData(typeof(NoteEntry)) is not NoteEntry moving)
        {
            return;
        }
        int from = f.Entries.FindIndex(x => x.Id == moving.Id);
        if (from < 0)
        {
            return;
        }
        int to = TargetIndex(e.GetPosition(EntryPanel).Y);
        NotesStore.Reorder(f, from, to);
        _store.Save(_data);
        RenderFolder();
    }

    /// <summary>依放下之 Y 座標，落點取第一個「中線在游標下方」的列索引；否則置末。</summary>
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

    private static SolidColorBrush Brush(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex));

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
        base.OnKeyDown(e);
    }
}
