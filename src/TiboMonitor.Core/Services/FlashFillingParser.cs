using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using TiboMonitor.Core.Models;

namespace TiboMonitor.Core.Services;

public static partial class FlashFillingParser
{
    public static IReadOnlyList<XPost> ParsePostsFromHtml(string html, string account)
    {
        var cardMatches = CardStartRegex().Matches(html);
        if (cardMatches.Count == 0)
        {
            throw new InvalidDataException("实时镜像中没有找到 tweet-card，页面结构可能已经变化。");
        }

        var posts = new List<XPost>();
        for (var index = 0; index < cardMatches.Count; index++)
        {
            var match = cardMatches[index];
            var end = index + 1 < cardMatches.Count ? cardMatches[index + 1].Index : html.Length;
            var card = html[match.Index..end];
            var headerIndex = card.LastIndexOf("<div class=\"tweet-header\">", StringComparison.Ordinal);
            if (headerIndex < 0)
            {
                continue;
            }

            var ownPostSection = card[headerIndex..];
            var usernameMatch = UsernameRegex().Match(ownPostSection);
            var timeMatch = TimeRegex().Match(ownPostSection);
            var textMatch = TextRegex().Match(ownPostSection);
            if (!usernameMatch.Success || !timeMatch.Success || !textMatch.Success ||
                !string.Equals(usernameMatch.Groups["username"].Value, account, StringComparison.OrdinalIgnoreCase) ||
                !DateTime.TryParseExact(
                    timeMatch.Groups["time"].Value,
                    "yyyy-MM-dd HH:mm:ss",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var localTime))
            {
                continue;
            }

            var text = DecodeText(textMatch.Groups["text"].Value);
            var id = match.Groups["id"].Value;
            var localUnspecified = DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified);
            var createdAt = new DateTimeOffset(localUnspecified, TimeZoneInfo.Local.GetUtcOffset(localUnspecified));
            posts.Add(new XPost
            {
                Id = id,
                Text = text,
                CreatedAt = createdAt,
                Url = $"https://x.com/{account}/status/{id}",
                Type = DetermineType(card, text)
            });
        }

        if (posts.Count == 0)
        {
            throw new InvalidDataException("实时镜像存在卡片，但没有可解析的目标账号动态。");
        }

        return posts
            .GroupBy(post => post.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(post => post.Id, SnowflakeIdComparer.Instance)
            .ToList();
    }

    private static PostType DetermineType(string card, string text)
    {
        if (text.TrimStart().StartsWith('@'))
        {
            return PostType.Reply;
        }

        return card.Contains("quoted-tweet", StringComparison.OrdinalIgnoreCase)
            ? PostType.Quote
            : PostType.Original;
    }

    private static string DecodeText(string html)
    {
        var withLineBreaks = BreakRegex().Replace(html, "\n");
        var withoutTags = TagRegex().Replace(withLineBreaks, string.Empty);
        return WebUtility.HtmlDecode(withoutTags).Trim();
    }

    [GeneratedRegex("<div\\s+class=[\\\"']tweet-card[\\\"'][^>]*?/thread/(?<id>\\d+)[\\\"'][^>]*>", RegexOptions.Singleline | RegexOptions.IgnoreCase, 2000)]
    private static partial Regex CardStartRegex();

    [GeneratedRegex("<span\\s+class=[\\\"']username[\\\"']>\\s*@(?<username>[^<\\s]+)\\s*</span>", RegexOptions.Singleline | RegexOptions.IgnoreCase, 1000)]
    private static partial Regex UsernameRegex();

    [GeneratedRegex("<span\\s+class=[\\\"']tweet-time[\\\"']>\\s*(?<time>\\d{4}-\\d{2}-\\d{2}\\s+\\d{2}:\\d{2}:\\d{2})\\s*</span>", RegexOptions.Singleline | RegexOptions.IgnoreCase, 1000)]
    private static partial Regex TimeRegex();

    [GeneratedRegex("<div\\s+class=[\\\"']tweet-text[\\\"']>(?<text>.*?)</div>", RegexOptions.Singleline | RegexOptions.IgnoreCase, 1000)]
    private static partial Regex TextRegex();

    [GeneratedRegex("<br\\s*/?>", RegexOptions.IgnoreCase, 1000)]
    private static partial Regex BreakRegex();

    [GeneratedRegex("<[^>]+>", RegexOptions.Singleline, 1000)]
    private static partial Regex TagRegex();
}
