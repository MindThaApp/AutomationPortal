using System.Text.RegularExpressions;

namespace AutomationPortal.Actions.Content.Utilities;

/// <summary>Strips tracking parameters from links (utm_*, fbclid, gclid …) and keeps the ones the page needs.</summary>
public static class LinkCleaner
{
    private static readonly HashSet<string> Always = new(StringComparer.OrdinalIgnoreCase)
    {
        "fbclid", "gclid", "gclsrc", "dclid", "gbraid", "wbraid", "msclkid", "mc_eid", "mc_cid", "igshid", "igsh", "ref_src", "ref_url",
        "yclid", "twclid", "ttclid", "li_fat_id", "_ga", "_gl", "_hsenc", "_hsmi", "hsctatracking", "mkt_tok", "vero_id", "vero_conv",
        "oly_enc_id", "oly_anon_id", "wickedid", "s_cid", "icid", "cmpid", "ncid", "__s", "mbid", "rb_clickid", "sr_share", "share_id",
        "fb_action_ids", "fb_action_types", "fb_source", "fb_ref", "trk", "trkinfo", "trackingid", "lipi", "midtoken", "utm_id",
        "ck_subscriber_id", "_openstat", "pk_campaign", "pk_kwd", "piwik_campaign", "piwik_kwd", "campaign_id", "ad_id", "adset_id",
    };

    private static readonly HashSet<string> AmazonOnly = new(StringComparer.OrdinalIgnoreCase)
        { "tag", "ref", "ref_", "content-id", "qid", "sr", "crid", "sprefix", "dib", "dib_tag", "linkcode", "linkid", "camp", "creative" };

    private static bool IsAmazon(string host) =>
        IsHost(host, "amazon.com", "amazon.co.uk", "amazon.de", "amazon.fr", "amazon.es", "amazon.it", "amazon.ca", "amazon.com.au", "amazon.in", "amazon.co.jp", "amazon.nl", "amazon.se", "amazon.pl");

    private static bool IsHost(string host, params string[] names) =>
        names.Any(n => host.Equals(n, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + n, StringComparison.OrdinalIgnoreCase));

    private static bool ShouldRemove(string name, string host)
    {
        if (name.StartsWith("utm_", StringComparison.OrdinalIgnoreCase)) return true;
        if (Always.Contains(name)) return true;
        if (name.Equals("si", StringComparison.OrdinalIgnoreCase) && IsHost(host, "youtube.com", "youtu.be", "spotify.com", "music.youtube.com")) return true;
        if (name.Equals("feature", StringComparison.OrdinalIgnoreCase) && IsHost(host, "youtube.com", "youtu.be")) return true;
        if ((name.Equals("s", StringComparison.OrdinalIgnoreCase) || name.Equals("t", StringComparison.OrdinalIgnoreCase)) && IsHost(host, "twitter.com", "x.com")) return true;
        if (IsAmazon(host) && (AmazonOnly.Contains(name) || name.StartsWith("pf_rd_", StringComparison.OrdinalIgnoreCase)
                                || name.StartsWith("pd_rd_", StringComparison.OrdinalIgnoreCase))) return true;
        if (name.Equals("ref", StringComparison.OrdinalIgnoreCase) && IsHost(host, "reddit.com", "github.com", "medium.com", "producthunt.com")) return true;
        if (name.Equals("ref_source", StringComparison.OrdinalIgnoreCase) && IsHost(host, "reddit.com")) return true;
        if (name.Equals("source", StringComparison.OrdinalIgnoreCase) && IsHost(host, "medium.com")) return true;
        if (name.Equals("spm", StringComparison.OrdinalIgnoreCase) && IsHost(host, "aliexpress.com", "taobao.com", "tmall.com", "1688.com")) return true;
        return false;
    }

    /// <summary>Returns the link without tracking parameters. Anything that isn't a web link is returned unchanged.</summary>
    public static Uri Clean(Uri uri)
    {
        if (!uri.IsAbsoluteUri || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) return uri;
        return new Uri(Clean(uri.OriginalString), UriKind.Absolute);
    }

    public static string Clean(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) return url;
        var host = uri.Host;
        var s = url;

        var hash = s.IndexOf('#');
        var fragment = hash >= 0 ? s[hash..] : "";
        if (hash >= 0) s = s[..hash];

        var q = s.IndexOf('?');
        var main = q >= 0 ? s[..q] : s;
        var query = q >= 0 ? s[(q + 1)..] : "";

        // Amazon puts tracking in the path: /dp/B000/ref=sr_1_1
        if (IsAmazon(host))
            main = Regex.Replace(main, @"/ref=[^/?#]*", "", RegexOptions.IgnoreCase);

        if (query.Length > 0)
        {
            var kept = query.Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Where(pair =>
                {
                    var eq = pair.IndexOf('=');
                    var name = Uri.UnescapeDataString(eq >= 0 ? pair[..eq] : pair);
                    return !ShouldRemove(name, host);
                }).ToList();
            query = string.Join('&', kept);
        }
        return main + (query.Length > 0 ? "?" + query : "") + fragment;
    }
}
