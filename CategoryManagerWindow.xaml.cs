using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CIE;

public partial class CategoryManagerWindow : Window
{
    private readonly List<string> _folders;
    private readonly IReadOnlyList<InstanceProfile> _profiles;
    private readonly IReadOnlyList<InstanceProfile> _selectedProfiles;
    private readonly string _iconDirectory;
    private bool _loading;

    public IReadOnlyList<string> FolderNames => _folders;

    public CategoryManagerWindow(List<string> folders, IReadOnlyList<InstanceProfile> profiles,
        IReadOnlyList<InstanceProfile> selectedProfiles, string iconDirectory)
    {
        InitializeComponent();
        _folders = folders;
        _profiles = profiles;
        _selectedProfiles = selectedProfiles;
        _iconDirectory = iconDirectory;
        Directory.CreateDirectory(_iconDirectory);
        SelectedVersionText.Text = selectedProfiles.Count == 0
            ? "管理分类文件夹；也可以先在已安装版本中多选，再一次归类。"
            : selectedProfiles.Count == 1
                ? $"正在管理版本：{selectedProfiles[0].DisplayName}"
                : $"已多选 {selectedProfiles.Count} 个版本，可将它们一起归入同一分类。";
        AssignHintText.Text = selectedProfiles.Count == 0
            ? "没有选中版本。可先建立或整理分类，然后返回列表多选版本并批量归类。"
            : selectedProfiles.Count == 1
                ? $"将“{selectedProfiles[0].DisplayName}”放入所选文件夹。"
                : $"将已选中的 {selectedProfiles.Count} 个版本批量放入同一文件夹。";
        RefreshFolderList();
        RefreshAssignmentChoices();
    }

    private void RefreshFolderList(string? selectName = null)
    {
        var entries = _folders.Select(name => new FolderEntry(name, LoadIcon(name) ?? DefaultFolderIcon())).ToList();
        FolderListBox.ItemsSource = entries;
        if (selectName is not null)
            FolderListBox.SelectedItem = entries.FirstOrDefault(item => item.Name == selectName);
        if (FolderListBox.SelectedItem is null && entries.Count > 0)
            FolderListBox.SelectedIndex = 0;
    }

    private void RefreshAssignmentChoices()
    {
        _loading = true;
        AssignFolderComboBox.ItemsSource = new[] { "未分类" }.Concat(_folders).ToList();
        var folders = _selectedProfiles.Select(profile => profile.VirtualFolder ?? "").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        AssignFolderComboBox.SelectedItem = folders.Count == 1 ? (string.IsNullOrEmpty(folders[0]) ? "未分类" : folders[0]) : null;
        AssignFolderComboBox.IsEnabled = _selectedProfiles.Count > 0;
        _loading = false;
    }

    private void FolderListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FolderListBox.SelectedItem is not FolderEntry entry) return;
        FolderNameTextBox.Text = entry.Name;
        IconPreview.Source = entry.Icon ?? DefaultFolderIcon();
    }

    private void AssignFolderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _selectedProfiles.Count == 0 || AssignFolderComboBox.SelectedItem is not string folder) return;
        var target = folder == "未分类" ? "" : folder;
        foreach (var profile in _selectedProfiles) profile.VirtualFolder = target;
    }

    private void CreateFolder_Click(object sender, RoutedEventArgs e)
    {
        var name = FolderNameTextBox.Text.Trim();
        if (!ValidateName(name)) return;
        if (_folders.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "已经有同名分类了。", "分类管理", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _folders.Add(name);
        RefreshFolderList(name);
        RefreshAssignmentChoices();
        AssignFolderComboBox.SelectedItem = name;
    }

    private void RenameFolder_Click(object sender, RoutedEventArgs e)
    {
        if (FolderListBox.SelectedItem is not FolderEntry selected) { ShowSelectFolder(); return; }
        var name = FolderNameTextBox.Text.Trim();
        if (!ValidateName(name)) return;
        if (!string.Equals(selected.Name, name, StringComparison.OrdinalIgnoreCase)
            && _folders.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "已经有同名分类了。", "分类管理", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var index = _folders.IndexOf(selected.Name);
        if (index < 0) return;
        _folders[index] = name;
        foreach (var profile in _profiles.Where(profile => profile.VirtualFolder == selected.Name))
            profile.VirtualFolder = name;
        var oldIcon = IconPath(selected.Name);
        var newIcon = IconPath(name);
        if (File.Exists(oldIcon) && !string.Equals(oldIcon, newIcon, StringComparison.OrdinalIgnoreCase))
            File.Move(oldIcon, newIcon, true);
        RefreshFolderList(name);
        RefreshAssignmentChoices();
    }

    private void DeleteFolder_Click(object sender, RoutedEventArgs e)
    {
        if (FolderListBox.SelectedItem is not FolderEntry selected) { ShowSelectFolder(); return; }
        if (MessageBox.Show(this,
            $"删除分类“{selected.Name}”？其中的版本会保留并移到“未分类”，游戏文件不会删除。",
            "删除分类", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _folders.Remove(selected.Name);
        foreach (var profile in _profiles.Where(profile => profile.VirtualFolder == selected.Name))
            profile.VirtualFolder = "";
        try { if (File.Exists(IconPath(selected.Name))) File.Delete(IconPath(selected.Name)); } catch { }
        FolderNameTextBox.Clear();
        IconPreview.Source = DefaultFolderIcon();
        RefreshFolderList();
        RefreshAssignmentChoices();
    }

    private void ChooseIcon_Click(object sender, RoutedEventArgs e)
    {
        if (FolderListBox.SelectedItem is not FolderEntry selected) { ShowSelectFolder(); return; }
        var dialog = new OpenFileDialog
        {
            Title = "选择文件夹图标",
            Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp|所有文件|*.*",
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var info = new FileInfo(dialog.FileName);
            if (info.Length > 20L * 1024 * 1024) throw new InvalidDataException("图片文件不能超过 20 MB。");
            using var input = File.OpenRead(dialog.FileName);
            var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames.FirstOrDefault() ?? throw new InvalidDataException("无法读取图片内容。");
            if (frame.PixelWidth > 4096 || frame.PixelHeight > 4096 || (long)frame.PixelWidth * frame.PixelHeight > 16_777_216)
                throw new InvalidDataException("图片尺寸不能超过 4096×4096 像素。");
            SaveCompressedIcon(frame, IconPath(selected.Name));
            RefreshFolderList(selected.Name);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"无法导入图片：{ex.Message}", "文件夹图标", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static void SaveCompressedIcon(BitmapSource source, string destination)
    {
        const int size = 96;
        var scale = Math.Min(1d, size / (double)Math.Max(source.PixelWidth, source.PixelHeight));
        var width = Math.Max(1, (int)Math.Round(source.PixelWidth * scale));
        var height = Math.Max(1, (int)Math.Round(source.PixelHeight * scale));
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            var image = new TransformedBitmap(source, new ScaleTransform(width / (double)source.PixelWidth, height / (double)source.PixelHeight));
            context.DrawImage(image, new Rect((size - width) / 2d, (size - height) / 2d, width, height));
        }
        var rendered = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rendered.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rendered));
        using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        encoder.Save(output);
    }

    private void ResetIcon_Click(object sender, RoutedEventArgs e)
    {
        if (FolderListBox.SelectedItem is not FolderEntry selected) { ShowSelectFolder(); return; }
        try { if (File.Exists(IconPath(selected.Name))) File.Delete(IconPath(selected.Name)); } catch { }
        RefreshFolderList(selected.Name);
    }

    private void Done_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private bool ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 40 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            MessageBox.Show(this, "请输入 1 到 40 个字符的有效分类名称。", "分类管理", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        return true;
    }

    private void ShowSelectFolder() =>
        MessageBox.Show(this, "请先选择一个分类文件夹。", "分类管理", MessageBoxButton.OK, MessageBoxImage.Information);

    private string IconPath(string name)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name))).ToLowerInvariant();
        return Path.Combine(_iconDirectory, hash + ".png");
    }

    private BitmapSource? LoadIcon(string name)
    {
        var path = IconPath(name);
        if (!File.Exists(path)) return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 48;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch { return null; }
    }

    internal static BitmapSource DefaultFolderIcon()
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(246, 190, 75)), null, new Rect(5, 12, 40, 29), 4, 4);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(255, 210, 102)), null, new Rect(5, 8, 20, 10), 4, 4);
        }
        var bitmap = new RenderTargetBitmap(50, 48, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private sealed record FolderEntry(string Name, BitmapSource? Icon);
}
