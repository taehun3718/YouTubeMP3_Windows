namespace YouTubeMP3.Windows.Models;

public sealed class VideoItem
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Channel { get; init; } = "";
    public string Duration { get; init; } = "";
    public double? DurationSeconds { get; init; }
    public string ThumbnailUrl => $"https://i.ytimg.com/vi/{Id}/mqdefault.jpg";
    public string WatchUrl => $"https://www.youtube.com/watch?v={Id}";
}
