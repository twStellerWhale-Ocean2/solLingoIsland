using LingoIsland.Present;
using Xunit;

namespace LingoIsland.Tests;

/// <summary>影片擷取頁 YouTube 影片 ID 解析（VideoCapturePage.ExtractVideoId，spec#2）：各連結形式與 11 碼 ID、無效輸入回 null。</summary>
public class VideoIdTests
{
    [Theory]
    [InlineData("dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=42s", "dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/embed/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/shorts/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("  https://youtu.be/dQw4w9WgXcQ  ", "dQw4w9WgXcQ")]
    public void ExtractVideoId_ValidForms_ReturnsId(string input, string expected)
        => Assert.Equal(expected, VideoCapturePage.ExtractVideoId(input));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("not a youtube link")]
    [InlineData("https://example.com/watch?v=tooshort")]
    public void ExtractVideoId_Invalid_ReturnsNull(string? input)
        => Assert.Null(VideoCapturePage.ExtractVideoId(input));
}
