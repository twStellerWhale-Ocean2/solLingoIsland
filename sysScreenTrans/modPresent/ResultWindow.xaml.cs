using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ScreenTrans.Query;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Button = System.Windows.Controls.Button;
using Cursors = System.Windows.Input.Cursors;
using FontFamily = System.Windows.Media.FontFamily;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace ScreenTrans.Present;

/// <summary>
/// 浮動結果視窗（[runWi自訂Usr查看聆聽結果]、design ＜III.C.(C)＞ 查詢結果頁）：
/// 三區直排（原文／KK 音標／中譯）＋播放鈕；查詢中顯示進度、失敗顯示明確錯誤。
/// ESC 或點視窗外即關。位置暫置中，選區旁定位留 GUI 實測調校。
/// </summary>
public partial class ResultWindow : Window
{
    private ISpeechService? _speech;
    private string _originalForSpeech = "";
    private bool _isLoading;

    public ResultWindow()
    {
        InitializeComponent();
    }

    public void ShowLoading()
    {
        _isLoading = true;
        BodyPanel.Children.Clear();
        BodyPanel.Children.Add(new TextBlock
        {
            Text = "辨識翻譯中…",
            Foreground = Brush("#DFE1E5"),
            FontSize = 13,
        });
    }

    public void ShowResult(QueryResult r, ISpeechService speech)
    {
        _isLoading = false;
        _speech = speech;
        _originalForSpeech = r.Original;
        BodyPanel.Children.Clear();

        if (r.IsEmpty)
        {
            BodyPanel.Children.Add(new TextBlock
            {
                Text = "未偵測到英文文字",
                Foreground = Brush("#9AA0A6"),
                FontSize = 13,
            });
            return;
        }

        BodyPanel.Children.Add(Label("原文 ORIGINAL"));
        BodyPanel.Children.Add(Value(r.Original, "#FFFFFF", 14, bold: true));
        BodyPanel.Children.Add(Label("KK 音標 PHONETIC"));
        BodyPanel.Children.Add(Value(r.Phonetic, "#8AB4F8", 12, bold: false, font: "Georgia"));
        BodyPanel.Children.Add(Label("中譯 TRANSLATION"));
        BodyPanel.Children.Add(Value(r.Translation, "#E8EAED", 13, bold: false));

        var play = new Button
        {
            Content = "▶ 播放發音",
            Margin = new Thickness(0, 9, 0, 0),
            Padding = new Thickness(12, 4, 12, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = Brush("#2B3F63"),
            Foreground = Brush("#CFE0FF"),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
        };
        play.Click += (_, _) => _speech?.Speak(_originalForSpeech);
        BodyPanel.Children.Add(play);
    }

    public void ShowError(string message)
    {
        _isLoading = false;
        BodyPanel.Children.Clear();
        BodyPanel.Children.Add(new TextBlock
        {
            Text = "查詢失敗",
            Foreground = Brush("#F28B82"),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
        });
        BodyPanel.Children.Add(new TextBlock
        {
            Text = message,
            Foreground = Brush("#DFE1E5"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
        });
    }

    private static TextBlock Label(string t) => new()
    {
        Text = t,
        Foreground = Brush("#8AB4F8"),
        FontSize = 9,
        Margin = new Thickness(0, 6, 0, 2),
    };

    private static TextBlock Value(string t, string color, double size, bool bold, string? font = null)
    {
        var tb = new TextBlock
        {
            Text = t,
            Foreground = Brush(color),
            FontSize = size,
            TextWrapping = TextWrapping.Wrap,
        };
        if (bold)
        {
            tb.FontWeight = FontWeights.SemiBold;
        }
        if (font is not null)
        {
            tb.FontFamily = new FontFamily(font);
        }
        return tb;
    }

    private static SolidColorBrush Brush(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex));

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        // 查詢中不因失焦關閉（等結果）；有結果/錯誤後點視窗外即關
        if (!_isLoading)
        {
            Close();
        }
    }
}
