using System.Reflection;
using UserControl = System.Windows.Controls.UserControl;

namespace ScreenTrans.Present;

/// <summary>關於分頁（Issue #34）：程式名／版本／版權／聯絡 email／logo。純顯示。</summary>
public partial class AboutPage : UserControl
{
    public AboutPage()
    {
        InitializeComponent();
        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = "版本 v" + (ver is null ? "?" : $"{ver.Major}.{ver.Minor}.{ver.Build}");
    }
}
