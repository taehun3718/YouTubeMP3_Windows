using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using YouTubeMP3.Windows.Models;
using YouTubeMP3.Windows.Services;

namespace YouTubeMP3.Windows;

public partial class MainWindow : Window
{
    private readonly YouTubeService _service = new();
    private readonly ObservableCollection<VideoItem> _videos = new();
    private VideoItem? _selected;
    private string? _savedFile;
    private string _lastQuery = "";
    private int _requestedCount = 20;
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();
        VideoList.ItemsSource = _videos;
    }

    private async void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        _lastQuery = QueryText.Text.Trim();
        if (_lastQuery.Length == 0)
        {
            SearchStatus.Text = "검색어를 입력하세요.";
            return;
        }

        _requestedCount = 20;
        _videos.Clear();
        VideoList.SelectedItem = null;
        await SearchAsync();
    }

    private async void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        _requestedCount += 20;
        await SearchAsync();
    }

    private async Task SearchAsync()
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        SearchButton.IsEnabled = false;
        MoreButton.IsEnabled = false;
        SearchStatus.Text = "검색 중... (첫 실행 시 yt-dlp 다운로드 및 검증)";

        try
        {
            var results = await _service.SearchAsync(_lastQuery, _requestedCount);
            var known = new HashSet<string>(_videos.Select(video => video.Id));
            foreach (var video in results)
            {
                if (known.Add(video.Id))
                {
                    _videos.Add(video);
                }
            }

            SearchStatus.Text = $"검색 결과 {_videos.Count}개";
            MoreButton.Visibility = _videos.Count > 0 && results.Count >= _requestedCount
                ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception exception)
        {
            SearchStatus.Text = $"검색 실패: {exception.Message}";
            MoreButton.Visibility = _videos.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        finally
        {
            _busy = false;
            SearchButton.IsEnabled = true;
            MoreButton.IsEnabled = true;
        }
    }

    private void QueryText_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SearchButton_Click(sender, e);
        }
    }

    private async void VideoList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selected = VideoList.SelectedItem as VideoItem;
        DetailPanel.Visibility = _selected is null ? Visibility.Collapsed : Visibility.Visible;
        EmptyLabel.Visibility = _selected is null ? Visibility.Visible : Visibility.Collapsed;
        RightsCheck.IsChecked = false;
        ResultStatus.Text = "";
        ProgressPanel.Visibility = Visibility.Collapsed;
        OpenFolderButton.Visibility = Visibility.Collapsed;
        OpenFileButton.Visibility = Visibility.Collapsed;
        _savedFile = null;

        if (_selected is null)
        {
            return;
        }

        VideoTitle.Text = _selected.Title;
        VideoChannel.Text = _selected.Channel;
        var videoId = _selected.Id;

        try
        {
            await PreviewBrowser.EnsureCoreWebView2Async();

            // An embed URL loaded directly has no referring page and YouTube may return error 153.
            // Navigate to the regular watch page instead. Never spoof a Referer header.
            if (_selected?.Id == videoId)
            {
                PreviewBrowser.CoreWebView2.Navigate(
                    $"https://www.youtube.com/watch?v={Uri.EscapeDataString(videoId)}");
            }
        }
        catch (Exception exception)
        {
            ResultStatus.Text = "미리보기를 열 수 없습니다: " + exception.Message +
                " (YouTube 원본 열기 버튼을 사용하세요.)";
        }
    }

    private void RightsCheck_Changed(object sender, RoutedEventArgs e)
    {
        UpdateConvertButton();
    }

    private void UpdateConvertButton()
    {
        if (ConvertButton is not null)
        {
            ConvertButton.IsEnabled = !_busy && _selected is not null && RightsCheck.IsChecked == true;
        }
    }

    private async void ConvertButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _selected is null || RightsCheck.IsChecked != true)
        {
            return;
        }

        var video = _selected;
        var bitrate = int.Parse(((ComboBoxItem)BitrateCombo.SelectedItem).Tag.ToString()!);
        _busy = true;
        ConvertButton.IsEnabled = false;
        SearchButton.IsEnabled = false;
        MoreButton.IsEnabled = false;
        ProgressPanel.Visibility = Visibility.Visible;
        DownloadProgress.Value = 0;
        ConversionProgress.Value = 0;
        DownloadLabel.Text = "파일 다운로드 0%";
        ConversionLabel.Text = "MP3 변환 (PCM → 인코딩) 0%";
        PhaseText.Text = "변환 준비 중... (필요한 도구 자동 설치)";
        ResultStatus.Text = "";
        OpenFolderButton.Visibility = Visibility.Collapsed;
        OpenFileButton.Visibility = Visibility.Collapsed;
        _savedFile = null;

        var download = new Progress<double>(fraction =>
        {
            var percent = Math.Clamp((int)Math.Round(fraction * 100), 0, 100);
            DownloadProgress.Value = percent;
            DownloadLabel.Text = $"파일 다운로드 {percent}%";
            PhaseText.Text = percent == 100 ? "PCM 디코딩 및 MP3 인코딩 중..." : "오디오 다운로드 중...";
        });

        var conversion = new Progress<double>(fraction =>
        {
            var percent = Math.Clamp((int)Math.Round(fraction * 100), 0, 100);
            ConversionProgress.Value = percent;
            ConversionLabel.Text = $"MP3 변환 (PCM → 인코딩) {percent}%";
            if (percent == 100)
            {
                PhaseText.Text = "MP3 저장 완료";
            }
        });

        try
        {
            _savedFile = await _service.DownloadAndConvertAsync(video, bitrate, download, conversion);
            ResultStatus.Text = "완료: " + _savedFile;
            OpenFolderButton.Visibility = Visibility.Visible;
            OpenFileButton.Visibility = Visibility.Visible;
        }
        catch (Exception exception)
        {
            ResultStatus.Text = "변환 실패: " + exception.Message;
        }
        finally
        {
            _busy = false;
            SearchButton.IsEnabled = true;
            MoreButton.IsEnabled = true;
            UpdateConvertButton();
        }
    }

    private void OpenVideo_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is not null)
        {
            OpenUrl(_selected.WatchUrl);
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_savedFile is not null)
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_savedFile}\"")
            {
                UseShellExecute = true
            });
        }
    }

    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        if (_savedFile is not null)
        {
            OpenUrl(_savedFile);
        }
    }

    private static void OpenUrl(string target)
    {
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    }
}
