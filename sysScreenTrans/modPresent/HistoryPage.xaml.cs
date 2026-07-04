using ScreenTrans.Query;
using UserControl = System.Windows.Controls.UserControl;
using Button = System.Windows.Controls.Button;
using ListBoxItem = System.Windows.Controls.ListBoxItem;
using StackPanel = System.Windows.Controls.StackPanel;
using TextBlock = System.Windows.Controls.TextBlock;
using Border = System.Windows.Controls.Border;
using Orientation = System.Windows.Controls.Orientation;
using Grid = System.Windows.Controls.Grid;
using ColumnDefinition = System.Windows.Controls.ColumnDefinition;
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
using UIElement = System.Windows.UIElement;
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
/// 查詢歷史分頁（Issue #34；原 HistoryWindow 內容移入為 UserControl）：左依日期分組、右該日條目
/// （前端「＋筆記」加入我的筆記、尾端 播音／檢視／刪除、頂部清除全部）。非結果視窗、由主視窗分頁承載。
/// </summary>
public partial class HistoryPage : UserControl
{
    private readonly HistoryStore _store;
    private readonly Func<ISpeechService?> _speech;

    public event Action<HistoryEntry>? ViewRequested;
    public event Action<HistoryEntry>? AddToNotesRequested;

    private sealed record DateGroup(DateTime Date, List<HistoryEntry> Entries);

    public HistoryPage(HistoryStore store, Func<ISpeechService?> speechProvider)
    {
        InitializeComponent();
        _store = store;
        _speech = speechProvider;
        ClearAllBtn.Click += OnClearAll;
        Reload();
    }

    public void Reload()
    {
        DateTime? keep = (DateList.SelectedItem as ListBoxItem)?.Tag is DateGroup sel ? sel.Date : null;

        var groups = _store.Load()
            .GroupBy(e => e.Timestamp.ToLocalTime().Date)
            .OrderByDescending(g => g.Key)
            .Select(g => new DateGroup(g.Key, g.ToList()))
            .ToList();

        DateList.SelectionChanged -= OnDateChanged;
        DateList.Items.Clear();
        foreach (var g in groups)
        {
            DateList.Items.Add(new ListBoxItem { Content = DateItem(g), Tag = g, Padding = new Thickness(4) });
        }
        DateList.SelectionChanged += OnDateChanged;

        bool any = groups.Count > 0;
        EmptyHint.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        ClearAllBtn.IsEnabled = any;
        if (!any)
        {
            EntryPanel.Children.Clear();
            return;
        }
        int idx = keep is null ? 0 : Math.Max(0, groups.FindIndex(g => g.Date == keep));
        DateList.SelectedIndex = idx;
    }

    private void OnDateChanged(object? sender, SelectionChangedEventArgs e) => RenderSelected();

    private void RenderSelected()
    {
        EntryPanel.Children.Clear();
        if ((DateList.SelectedItem as ListBoxItem)?.Tag is not DateGroup g)
        {
            return;
        }
        foreach (var entry in g.Entries)
        {
            EntryPanel.Children.Add(EntryRow(entry));
        }
    }

    private void OnClearAll(object? sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("確定清除全部查詢歷史？此動作無法復原。", "清除全部",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
        {
            return;
        }
        _store.Clear();
        Reload();
    }

    private static StackPanel DateItem(DateGroup g)
    {
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock
        {
            Text = g.Date.ToString("yyyy-MM-dd"),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#3A2C33"),
        });
        sp.Children.Add(new TextBlock { Text = $"{g.Entries.Count} 筆", FontSize = 11, Foreground = Brush("#8A5A6D") });
        return sp;
    }

    private UIElement EntryRow(HistoryEntry entry)
    {
        var card = new Border
        {
            Background = Brush("#FFFFFF"),
            BorderBrush = Brush("#F4C2D0"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 10, 10, 10),
            Margin = new Thickness(0, 0, 0, 8),
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var addNote = ActionButton("＋筆記", "加入我的筆記", "#2F6F4A", "#E9F6EE", "#C9E6D3",
            () => AddToNotesRequested?.Invoke(entry));
        addNote.Margin = new Thickness(0, 0, 8, 0);
        Grid.SetColumn(addNote, 0);
        grid.Children.Add(addNote);

        var textCol = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        textCol.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(entry.Original) ? "（未偵測到英文文字）" : entry.Original,
            FontSize = 14,
            Foreground = Brush("#3A2C33"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        textCol.Children.Add(new TextBlock
        {
            Text = entry.Timestamp.ToLocalTime().ToString("HH:mm"),
            FontSize = 11.5,
            Foreground = Brush("#9A6A82"),
            Margin = new Thickness(0, 3, 0, 0),
        });
        Grid.SetColumn(textCol, 1);
        grid.Children.Add(textCol);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        actions.Children.Add(ActionButton("▶", "播音（英文原句）", "#2F6FED", "#F0F6FF", "#CFE0FB",
            () => _speech()?.Speak(entry.Original, "en-US", stopPrevious: true)));
        actions.Children.Add(ActionButton("檢視", "開啟中英詳情", "#4A4A4A", "#F5F5F5", "#DCDCDC",
            () => ViewRequested?.Invoke(entry)));
        actions.Children.Add(ActionButton("刪除", "自歷史移除此筆", "#B23B3B", "#FDF2F2", "#F0D2D2",
            () => { _store.Delete(entry.Id); Reload(); }));
        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);

        card.Child = grid;
        return card;
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
