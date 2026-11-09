namespace SimpleRay.Core.Profiles;

/// <summary>
/// What a pasted / scanned / loaded text contains: subscription URLs (plain http(s) or another
/// client's import link), and the rest — server links, a WireGuard .conf, Xray JSON or a
/// subscription body — to parse into servers. <see cref="HappEncrypted"/> flags a
/// happ://crypt… link, which only Happ can open.
/// </summary>
public sealed record ImportPlan(IReadOnlyList<string> SubscriptionUrls, string ServerText, bool HappEncrypted)
{
    public bool IsEmpty => SubscriptionUrls.Count == 0 && string.IsNullOrWhiteSpace(ServerText) && !HappEncrypted;
}

/// <summary>Sorts imported text into subscriptions vs servers, so one "Add" handles any input.</summary>
public static class ImportClassifier
{
    public static ImportPlan Classify(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new ImportPlan(Array.Empty<string>(), "", false);

        // Whole-document formats: never split them line by line.
        var trimmed = text.Trim();
        if (XrayJsonParser.LooksLikeJson(trimmed) || WireGuardConfParser.LooksLikeConf(trimmed))
            return new ImportPlan(Array.Empty<string>(), text, false);

        var urls = new List<string>();
        var serverLines = new List<string>();
        bool encrypted = false;
        foreach (var rawLine in text.Replace("\r", "\n").Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
                continue;
            // A server link first: its own URL-ish shape must never be taken for a subscription.
            if (ShareLinkParser.TryParse(line, out _))
            {
                serverLines.Add(line);
                continue;
            }
            switch (SubscriptionLink.Normalize(line, out var url))
            {
                case SubscriptionLinkStatus.Ok:
                    if (!urls.Contains(url))
                        urls.Add(url);
                    break;
                case SubscriptionLinkStatus.HappEncrypted:
                    encrypted = true;
                    break;
                default:
                    serverLines.Add(line); // base64 body, #metadata, anything the parser may know
                    break;
            }
        }
        return new ImportPlan(urls, string.Join("\n", serverLines), encrypted);
    }
}
