using System.Text;
using LuxMap.Modules.Notifications;

namespace LuxMap.Shared.Tests;

/// <summary>BE-27 — the text a notice keeps must always be encodable, or the business save it rides on fails.</summary>
public class NotifierTests
{
    [Fact]
    public void Clipping_never_splits_an_emoji_across_the_cut()
    {
        // "aaaa" + 😀 (two UTF-16 units) + more: a cut at 6 keeps 5 units, which would end on the high surrogate.
        var text = "aaaa\U0001F600 tail";

        var clipped = Notifier.Clip(text, 6);

        Assert.Equal("aaaa…", clipped);
        Assert.True(clipped.Length <= 6);
        _ = new UTF8Encoding(false, throwOnInvalidBytes: true).GetBytes(clipped);
    }

    [Theory]
    [InlineData("short", 10, "short")]
    [InlineData("exactly10!", 10, "exactly10!")]
    [InlineData("eleven chars", 10, "eleven ch…")]
    public void Text_within_the_limit_is_kept_and_longer_text_ends_with_an_ellipsis(string text, int max, string expected)
        => Assert.Equal(expected, Notifier.Clip(text, max));
}
