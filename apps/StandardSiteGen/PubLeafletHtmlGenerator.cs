using System.Net;
using System.Text;

/// <summary>
/// Generates HTML for PubLeaflet content.
/// </summary>
static class PubLeafletHtmlGenerator
{
    public static string Generate(SiteStandard.Document document, PubLeaflet.Content content)
    {
        var html = new StringBuilder();
        html.AppendLine("<!DOCTYPE html>");
        html.AppendLine("<html lang=\"en\">");
        html.AppendLine("<head>");
        html.AppendLine("<meta charset=\"utf-8\">");
        html.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        html.AppendLine($"<title>{WebUtility.HtmlEncode(document.Title)}</title>");
        if (document.Description is not null)
        {
            html.AppendLine($"<meta name=\"description\" content=\"{WebUtility.HtmlEncode(document.Description)}\">");
        }

        html.AppendLine("</head>");
        html.AppendLine("<body>");
        html.AppendLine("<article>");
        html.AppendLine("<header>");
        html.AppendLine($"<h1>{WebUtility.HtmlEncode(document.Title)}</h1>");
        html.AppendLine($"<time datetime=\"{document.PublishedAt:yyyy-MM-ddTHH:mm:ssZ}\">{document.PublishedAt:MMMM d, yyyy}</time>");
        if (document.Tags is { Count: > 0 })
        {
            html.Append("<div class=\"tags\">");
            foreach (var tag in document.Tags)
            {
                html.Append($"<span class=\"tag\">{WebUtility.HtmlEncode(tag)}</span> ");
            }

            html.AppendLine("</div>");
        }

        html.AppendLine("</header>");

        foreach (var page in content.Pages)
        {
            switch (page)
            {
                case PubLeaflet.Pages.LinearDocument linearDoc:
                    foreach (var block in linearDoc.Blocks)
                    {
                        RenderLeafletBlock(html, block.Block, block.Alignment);
                    }

                    break;
                case PubLeaflet.Pages.Canvas canvas:
                    foreach (var block in canvas.Blocks)
                    {
                        RenderLeafletBlock(html, block.Block, alignment: null);
                    }

                    break;
            }
        }

        html.AppendLine("</article>");
        html.AppendLine("</body>");
        html.AppendLine("</html>");
        return html.ToString();
    }

    static void RenderLeafletBlock(StringBuilder html, object block, string? alignment)
    {
        var alignStyle = alignment switch
        {
            PubLeaflet.Pages.LinearDocumentTextAlignCenterToken.Value => " style=\"text-align:center\"",
            PubLeaflet.Pages.LinearDocumentTextAlignRightToken.Value => " style=\"text-align:right\"",
            PubLeaflet.Pages.LinearDocumentTextAlignJustifyToken.Value => " style=\"text-align:justify\"",
            _ => string.Empty,
        };

        switch (block)
        {
            case PubLeaflet.Blocks.Text text:
                html.AppendLine($"<p{alignStyle}>{RenderLeafletRichText(text.Plaintext, text.Facets)}</p>");
                break;

            case PubLeaflet.Blocks.Header header:
                var level = Math.Clamp(header.Level ?? 1, 1, 6);
                html.AppendLine($"<h{level}{alignStyle}>{RenderLeafletRichText(header.Plaintext, header.Facets)}</h{level}>");
                break;

            case PubLeaflet.Blocks.Code code:
                var langAttr = !string.IsNullOrEmpty(code.Language)
                    ? $" class=\"language-{WebUtility.HtmlEncode(code.Language)}\""
                    : string.Empty;
                html.AppendLine($"<pre><code{langAttr}>{WebUtility.HtmlEncode(code.Plaintext)}</code></pre>");
                break;

            case PubLeaflet.Blocks.Image image:
                var altAttr = image.Alt is not null
                    ? $" alt=\"{WebUtility.HtmlEncode(image.Alt)}\""
                    : " alt=\"\"";
                html.AppendLine("<figure>");
                html.AppendLine($"<img src=\"blob:{WebUtility.HtmlEncode(image.ImageValue.ToString())}\"{altAttr}>");
                if (image.Alt is not null)
                {
                    html.AppendLine($"<figcaption>{WebUtility.HtmlEncode(image.Alt)}</figcaption>");
                }

                html.AppendLine("</figure>");
                break;

            case PubLeaflet.Blocks.UnorderedList unorderedList:
                RenderLeafletUnorderedList(html, unorderedList);
                break;

            case PubLeaflet.Blocks.Blockquote blockquote:
                html.AppendLine("<blockquote>");
                html.AppendLine($"<p>{RenderLeafletRichText(blockquote.Plaintext, blockquote.Facets)}</p>");
                html.AppendLine("</blockquote>");
                break;

            case PubLeaflet.Blocks.Website website:
                html.AppendLine("<div class=\"website-embed\">");
                html.AppendLine($"<a href=\"{WebUtility.HtmlEncode(website.Src)}\">{WebUtility.HtmlEncode(website.Title ?? website.Src)}</a>");
                if (website.Description is not null)
                {
                    html.AppendLine($"<p>{WebUtility.HtmlEncode(website.Description)}</p>");
                }

                html.AppendLine("</div>");
                break;

            case PubLeaflet.Blocks.BskyPost bskyPost:
                html.AppendLine($"<div class=\"bluesky-embed\" data-uri=\"{WebUtility.HtmlEncode(bskyPost.PostRef.Uri.ToString())}\">");
                html.AppendLine("<p><a href=\"https://bsky.app\">View on Bluesky</a></p>");
                html.AppendLine("</div>");
                break;

            case PubLeaflet.Blocks.Iframe iframe:
                var heightAttr = iframe.Height.HasValue ? $" height=\"{iframe.Height.Value}\"" : string.Empty;
                html.AppendLine($"<iframe src=\"{WebUtility.HtmlEncode(iframe.Url)}\"{heightAttr} frameborder=\"0\" allowfullscreen></iframe>");
                break;

            case PubLeaflet.Blocks.HorizontalRule:
                html.AppendLine("<hr>");
                break;

            case PubLeaflet.Blocks.Math math:
                html.AppendLine($"<div class=\"math\">{WebUtility.HtmlEncode(math.Tex)}</div>");
                break;

            case PubLeaflet.Blocks.Button button:
                html.AppendLine($"<a class=\"button\" href=\"{WebUtility.HtmlEncode(button.Url)}\">{WebUtility.HtmlEncode(button.Text)}</a>");
                break;

            case PubLeaflet.Blocks.Page page:
                html.AppendLine($"<a class=\"page-link\" href=\"#{WebUtility.HtmlEncode(page.Id)}\">{WebUtility.HtmlEncode(page.Id)}</a>");
                break;

            case PubLeaflet.Blocks.Poll poll:
                html.AppendLine($"<div class=\"poll\" data-ref=\"{WebUtility.HtmlEncode(poll.PollRef.Uri.ToString())}\"></div>");
                break;
        }
    }

    static void RenderLeafletUnorderedList(StringBuilder html, PubLeaflet.Blocks.UnorderedList list)
    {
        html.AppendLine("<ul>");
        foreach (var item in list.Children)
        {
            html.Append("<li>");
            RenderLeafletListItemContent(html, item.Content);
            if (item.Children is { Count: > 0 })
            {
                html.AppendLine();
                html.AppendLine("<ul>");
                foreach (var child in item.Children)
                {
                    RenderLeafletListItemRecursive(html, child);
                }

                html.AppendLine("</ul>");
            }

            html.AppendLine("</li>");
        }

        html.AppendLine("</ul>");
    }

    static void RenderLeafletListItemRecursive(StringBuilder html, PubLeaflet.Blocks.UnorderedListListItem item)
    {
        html.Append("<li>");
        RenderLeafletListItemContent(html, item.Content);
        if (item.Children is { Count: > 0 })
        {
            html.AppendLine();
            html.AppendLine("<ul>");
            foreach (var child in item.Children)
            {
                RenderLeafletListItemRecursive(html, child);
            }

            html.AppendLine("</ul>");
        }

        html.AppendLine("</li>");
    }

    static void RenderLeafletListItemContent(StringBuilder html, PubLeaflet.Blocks.IUnorderedListListItemContent content)
    {
        switch (content)
        {
            case PubLeaflet.Blocks.Text text:
                html.Append(RenderLeafletRichText(text.Plaintext, text.Facets));
                break;
            case PubLeaflet.Blocks.Header header:
                var level = Math.Clamp(header.Level ?? 1, 1, 6);
                html.Append($"<h{level}>{RenderLeafletRichText(header.Plaintext, header.Facets)}</h{level}>");
                break;
            case PubLeaflet.Blocks.Image image:
                var altAttr = image.Alt is not null
                    ? $" alt=\"{WebUtility.HtmlEncode(image.Alt)}\""
                    : " alt=\"\"";
                html.Append($"<img src=\"blob:{WebUtility.HtmlEncode(image.ImageValue.ToString())}\"{altAttr}>");
                break;
        }
    }

    static string RenderLeafletRichText(string plaintext, List<PubLeaflet.Richtext.Facet>? facets)
    {
        if (string.IsNullOrEmpty(plaintext))
        {
            return string.Empty;
        }

        if (facets is null || facets.Count == 0)
        {
            return WebUtility.HtmlEncode(plaintext);
        }

        var utf8Bytes = Encoding.UTF8.GetBytes(plaintext);
        var sortedFacets = facets.OrderBy(f => f.Index.ByteStart).ToList();
        var sb = new StringBuilder();
        long currentByte = 0;

        foreach (var facet in sortedFacets)
        {
            var byteStart = Math.Min(facet.Index.ByteStart, utf8Bytes.Length);
            var byteEnd = Math.Min(facet.Index.ByteEnd, utf8Bytes.Length);

            if (byteStart < currentByte)
            {
                continue;
            }

            if (byteStart > currentByte)
            {
                var before = Encoding.UTF8.GetString(utf8Bytes, (int)currentByte, (int)(byteStart - currentByte));
                sb.Append(WebUtility.HtmlEncode(before));
            }

            if (byteEnd > byteStart)
            {
                var facetText = Encoding.UTF8.GetString(utf8Bytes, (int)byteStart, (int)(byteEnd - byteStart));
                var encoded = WebUtility.HtmlEncode(facetText);

                foreach (var feature in facet.Features)
                {
                    encoded = feature switch
                    {
                        PubLeaflet.Richtext.FacetBold => $"<strong>{encoded}</strong>",
                        PubLeaflet.Richtext.FacetItalic => $"<em>{encoded}</em>",
                        PubLeaflet.Richtext.FacetCode => $"<code>{encoded}</code>",
                        PubLeaflet.Richtext.FacetStrikethrough => $"<s>{encoded}</s>",
                        PubLeaflet.Richtext.FacetUnderline => $"<u>{encoded}</u>",
                        PubLeaflet.Richtext.FacetHighlight => $"<mark>{encoded}</mark>",
                        PubLeaflet.Richtext.FacetLink link => $"<a href=\"{WebUtility.HtmlEncode(link.Uri)}\">{encoded}</a>",
                        PubLeaflet.Richtext.FacetDidMention didMention => $"<span class=\"mention\" data-did=\"{WebUtility.HtmlEncode(didMention.Did.ToString())}\">{encoded}</span>",
                        PubLeaflet.Richtext.FacetAtMention atMention => $"<a href=\"{WebUtility.HtmlEncode(atMention.AtURI)}\">{encoded}</a>",
                        _ => encoded,
                    };
                }

                sb.Append(encoded);
            }

            currentByte = byteEnd;
        }

        if (currentByte < utf8Bytes.Length)
        {
            var remaining = Encoding.UTF8.GetString(utf8Bytes, (int)currentByte, utf8Bytes.Length - (int)currentByte);
            sb.Append(WebUtility.HtmlEncode(remaining));
        }

        return sb.ToString();
    }
}
