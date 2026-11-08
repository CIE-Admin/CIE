using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace CIE;

public partial class ReleaseNotesWindow : Window
{
    public ReleaseNotesWindow(IReadOnlyList<LauncherReleaseNote> releases, bool isAnnouncement, bool isBlueTheme, bool isVioletTheme)
    {
        InitializeComponent();
        Title = isAnnouncement ? "CIE 版本更新公告" : "CIE 历史更新日志";
        HeadingText.Text = isAnnouncement ? "CIE 更新公告" : "历史更新日志";
        SubheadingText.Text = isAnnouncement && releases.Count > 0
            ? $"版本 {releases[0].Version} · 本次更新内容"
            : "按发布日期从新到旧排列";
        CloseButton.Content = isAnnouncement ? "我知道了" : "关闭";
        ReleaseItemsControl.ItemsSource = releases;

        var colors = isVioletTheme
            ? new Dictionary<string, string>
            {
                ["AppBackgroundBrush"] = "#F8F4FB", ["CardBrush"] = "#FFFFFF", ["SurfaceBrush"] = "#F3EBF7",
                ["BorderBrushColor"] = "#D8C6E0", ["AccentBrush"] = "#7C3FA1", ["PrimaryTextBrush"] = "#35213F",
                ["SecondaryTextBrush"] = "#654F70", ["MutedDimBrush"] = "#786582"
            }
            : isBlueTheme
            ? new Dictionary<string, string>
            {
                ["AppBackgroundBrush"] = "#F3F8FC", ["CardBrush"] = "#FFFFFF", ["SurfaceBrush"] = "#EFF7FC",
                ["BorderBrushColor"] = "#C4DCEB", ["AccentBrush"] = "#287FAE", ["PrimaryTextBrush"] = "#19384E",
                ["SecondaryTextBrush"] = "#496579", ["MutedDimBrush"] = "#6B8293"
            }
            : new Dictionary<string, string>
            {
                ["AppBackgroundBrush"] = "#F4F4F4", ["CardBrush"] = "#FFFFFF", ["SurfaceBrush"] = "#F1F1F1",
                ["BorderBrushColor"] = "#CCCCCC", ["AccentBrush"] = "#111111", ["PrimaryTextBrush"] = "#111111",
                ["SecondaryTextBrush"] = "#444444", ["MutedDimBrush"] = "#666666"
            };

        foreach (var (key, color) in colors)
            Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}