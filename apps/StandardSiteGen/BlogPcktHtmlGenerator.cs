using System.Net;
using System.Text;

/// <summary>
/// Generates HTML for BlogPckt content.
/// </summary>
static class BlogPcktHtmlGenerator
{
    public static string Generate(SiteStandard.Document document, BlogPckt.Content content)
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

        if (content.Items is not null)
        {
            foreach (var item in content.Items)
            {
                RenderBlock(html, item);
            }
        }

        html.AppendLine("</article>");
        html.AppendLine("</body>");
        html.AppendLine("</html>");
        return html.ToString();
    }

    static void RenderBlock(StringBuilder html, System.Text.Json.JsonElement element)
    {
        var type = element.TryGetProperty("$type", out var tp) ? tp.GetString() : null;
        switch (type)
        {
            case BlogPckt.Block.Text.TypeId:
                var text = BlogPckt.Block.Text.FromJson(element);
                if (text is not null)
                {
                    html.AppendLine($"<p>{RenderRichText(text.Plaintext, text.Facets)}</p>");
                }

                break;

            case BlogPckt.Block.Heading.TypeId:
                var heading = BlogPckt.Block.Heading.FromJson(element);
                if (heading is not null)
                {
                    var level = Math.Clamp(heading.Level ?? 1, 1, 6);
                    html.AppendLine($"<h{level}>{RenderRichText(heading.Plaintext, heading.Facets)}</h{level}>");
                }

                break;

            case BlogPckt.Block.CodeBlock.TypeId:
                var code = BlogPckt.Block.CodeBlock.FromJson(element);
                if (code is not null)
                {
                    var langAttr = !string.IsNullOrEmpty(code.Language)
                        ? $" class=\"language-{WebUtility.HtmlEncode(code.Language)}\""
                        : string.Empty;
                    html.AppendLine($"<pre><code{langAttr}>{WebUtility.HtmlEncode(code.Plaintext)}</code></pre>");
                }

                break;

            case BlogPckt.Block.Image.TypeId:
                var image = BlogPckt.Block.Image.FromJson(element);
                if (image?.Attrs is not null)
                {
                    var attrs = image.Attrs;
                    var altAttr = attrs.Alt is not null
                        ? $" alt=\"{WebUtility.HtmlEncode(attrs.Alt)}\""
                        : " alt=\"\"";
                    var titleAttr = attrs.Title is not null
                        ? $" title=\"{WebUtility.HtmlEncode(attrs.Title)}\""
                        : string.Empty;
                    html.AppendLine($"<figure>");
                    html.AppendLine($"<img src=\"{WebUtility.HtmlEncode(attrs.Src)}\"{altAttr}{titleAttr}>");
                    if (attrs.Title is not null)
                    {
                        html.AppendLine($"<figcaption>{WebUtility.HtmlEncode(attrs.Title)}</figcaption>");
                    }

                    html.AppendLine("</figure>");
                }

                break;

            case BlogPckt.Block.BulletList.TypeId:
                var bulletList = BlogPckt.Block.BulletList.FromJson(element);
                if (bulletList is not null)
                {
                    RenderBulletList(html, bulletList);
                }

                break;

            case BlogPckt.Block.OrderedList.TypeId:
                var orderedList = BlogPckt.Block.OrderedList.FromJson(element);
                if (orderedList is not null)
                {
                    RenderOrderedList(html, orderedList);
                }

                break;

            case BlogPckt.Block.Blockquote.TypeId:
                var blockquote = BlogPckt.Block.Blockquote.FromJson(element);
                if (blockquote is not null)
                {
                    html.AppendLine("<blockquote>");
                    foreach (var item in blockquote.Content)
                    {
                        RenderTypedContent(html, item, inline: false);
                    }

                    html.AppendLine("</blockquote>");
                }

                break;

            case BlogPckt.Block.Table.TypeId:
                var table = BlogPckt.Block.Table.FromJson(element);
                if (table is not null)
                {
                    RenderTable(html, table);
                }

                break;

            case BlogPckt.Block.HorizontalRule.TypeId:
                html.AppendLine("<hr>");
                break;

            case BlogPckt.Block.HardBreak.TypeId:
                html.AppendLine("<br>");
                break;

            case BlogPckt.Block.Website.TypeId:
                var website = BlogPckt.Block.Website.FromJson(element);
                if (website is not null)
                {
                    html.AppendLine("<div class=\"website-embed\">");
                    html.AppendLine($"<a href=\"{WebUtility.HtmlEncode(website.Src)}\">{WebUtility.HtmlEncode(website.Title ?? website.Src)}</a>");
                    if (website.Description is not null)
                    {
                        html.AppendLine($"<p>{WebUtility.HtmlEncode(website.Description)}</p>");
                    }

                    html.AppendLine("</div>");
                }

                break;

            case BlogPckt.Block.BlueskyEmbed.TypeId:
                var bskyEmbed = BlogPckt.Block.BlueskyEmbed.FromJson(element);
                if (bskyEmbed?.PostRef is not null)
                {
                    html.AppendLine($"<div class=\"bluesky-embed\" data-uri=\"{WebUtility.HtmlEncode(bskyEmbed.PostRef.Uri.ToString())}\">");
                    html.AppendLine("<p><a href=\"https://bsky.app\">View on Bluesky</a></p>");
                    html.AppendLine("</div>");
                }

                break;

            case BlogPckt.Block.Iframe.TypeId:
                var iframe = BlogPckt.Block.Iframe.FromJson(element);
                if (iframe is not null)
                {
                    var heightAttr = iframe.Height.HasValue ? $" height=\"{iframe.Height.Value}\"" : string.Empty;
                    html.AppendLine($"<iframe src=\"{WebUtility.HtmlEncode(iframe.Url)}\"{heightAttr} frameborder=\"0\" allowfullscreen></iframe>");
                }

                break;

            case BlogPckt.Block.Gallery.TypeId:
                var gallery = BlogPckt.Block.Gallery.FromJson(element);
                if (gallery is not null)
                {
                    html.AppendLine($"<div class=\"gallery\" data-ref=\"{WebUtility.HtmlEncode(gallery.Ref.ToString())}\"></div>");
                }

                break;

            case BlogPckt.Block.Mention.TypeId:
                var mention = BlogPckt.Block.Mention.FromJson(element);
                if (mention is not null)
                {
                    html.AppendLine($"<span class=\"mention\" data-did=\"{WebUtility.HtmlEncode(mention.Did.ToString())}\">@{WebUtility.HtmlEncode(mention.Handle.ToString())}</span>");
                }

                break;

            case BlogPckt.Block.TaskList.TypeId:
                var taskList = BlogPckt.Block.TaskList.FromJson(element);
                if (taskList is not null)
                {
                    RenderTaskList(html, taskList);
                }

                break;
        }
    }

    static void RenderTypedContent(StringBuilder html, object content, bool inline)
    {
        switch (content)
        {
            case BlogPckt.Block.Text text:
                if (inline)
                {
                    html.Append(RenderRichText(text.Plaintext, text.Facets));
                }
                else
                {
                    html.AppendLine($"<p>{RenderRichText(text.Plaintext, text.Facets)}</p>");
                }

                break;
            case BlogPckt.Block.BulletList bulletList:
                RenderBulletList(html, bulletList);
                break;
            case BlogPckt.Block.OrderedList orderedList:
                RenderOrderedList(html, orderedList);
                break;
        }
    }

    static void RenderBulletList(StringBuilder html, BlogPckt.Block.BulletList list)
    {
        html.AppendLine("<ul>");
        foreach (var item in list.Content)
        {
            html.Append("<li>");
            foreach (var c in item.Content)
            {
                RenderTypedContent(html, c, inline: true);
            }

            html.AppendLine("</li>");
        }

        html.AppendLine("</ul>");
    }

    static void RenderOrderedList(StringBuilder html, BlogPckt.Block.OrderedList list)
    {
        var startAttr = list.Start.HasValue && list.Start.Value != 1
            ? $" start=\"{list.Start.Value}\""
            : string.Empty;
        html.AppendLine($"<ol{startAttr}>");
        foreach (var item in list.Content)
        {
            html.Append("<li>");
            foreach (var c in item.Content)
            {
                RenderTypedContent(html, c, inline: true);
            }

            html.AppendLine("</li>");
        }

        html.AppendLine("</ol>");
    }

    static void RenderTable(StringBuilder html, BlogPckt.Block.Table table)
    {
        html.AppendLine("<table>");
        foreach (var row in table.Content)
        {
            html.AppendLine("<tr>");
            foreach (var cell in row.Content)
            {
                switch (cell)
                {
                    case BlogPckt.Block.TableHeader header:
                        var thColspan = header.Colspan is > 1 ? $" colspan=\"{header.Colspan.Value}\"" : string.Empty;
                        var thRowspan = header.Rowspan is > 1 ? $" rowspan=\"{header.Rowspan.Value}\"" : string.Empty;
                        html.Append($"<th{thColspan}{thRowspan}>");
                        foreach (var c in header.Content)
                        {
                            if (c is BlogPckt.Block.Text t)
                            {
                                html.Append(RenderRichText(t.Plaintext, t.Facets));
                            }
                        }

                        html.AppendLine("</th>");
                        break;
                    case BlogPckt.Block.TableCell tableCell:
                        var tdColspan = tableCell.Colspan is > 1 ? $" colspan=\"{tableCell.Colspan.Value}\"" : string.Empty;
                        var tdRowspan = tableCell.Rowspan is > 1 ? $" rowspan=\"{tableCell.Rowspan.Value}\"" : string.Empty;
                        html.Append($"<td{tdColspan}{tdRowspan}>");
                        foreach (var c in tableCell.Content)
                        {
                            if (c is BlogPckt.Block.Text t)
                            {
                                html.Append(RenderRichText(t.Plaintext, t.Facets));
                            }
                        }

                        html.AppendLine("</td>");
                        break;
                }
            }

            html.AppendLine("</tr>");
        }

        html.AppendLine("</table>");
    }

    static void RenderTaskList(StringBuilder html, BlogPckt.Block.TaskList taskList)
    {
        html.AppendLine("<ul class=\"task-list\">");
        foreach (var item in taskList.Content)
        {
            var checkedAttr = item.Checked ? " checked disabled" : " disabled";
            html.Append($"<li><input type=\"checkbox\"{checkedAttr}> ");
            foreach (var c in item.Content)
            {
                if (c is BlogPckt.Block.Text t)
                {
                    html.Append(RenderRichText(t.Plaintext, t.Facets));
                }
            }

            html.AppendLine("</li>");
        }

        html.AppendLine("</ul>");
    }

    static string RenderRichText(string plaintext, List<BlogPckt.Richtext.Facet>? facets)
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
                        BlogPckt.Richtext.FacetBold => $"<strong>{encoded}</strong>",
                        BlogPckt.Richtext.FacetItalic => $"<em>{encoded}</em>",
                        BlogPckt.Richtext.FacetCode => $"<code>{encoded}</code>",
                        BlogPckt.Richtext.FacetStrikethrough => $"<s>{encoded}</s>",
                        BlogPckt.Richtext.FacetUnderline => $"<u>{encoded}</u>",
                        BlogPckt.Richtext.FacetHighlight => $"<mark>{encoded}</mark>",
                        BlogPckt.Richtext.FacetLink link => $"<a href=\"{WebUtility.HtmlEncode(link.Uri)}\">{encoded}</a>",
                        BlogPckt.Richtext.FacetDidMention didMention => $"<span class=\"mention\" data-did=\"{WebUtility.HtmlEncode(didMention.Did.ToString())}\">{encoded}</span>",
                        BlogPckt.Richtext.FacetAtMention atMention => $"<a href=\"{WebUtility.HtmlEncode(atMention.AtURI)}\">{encoded}</a>",
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
