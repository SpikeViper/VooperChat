﻿using Markdig;
using Microsoft.AspNetCore.Html;

namespace Valour.Web
{
    public static class MarkdownToHtml
    {
        public static HtmlString Faq;

        public static async Task LoadMarkdown()
        {
            var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().UseBootstrap().Build();
            
            var file = (await File.ReadAllLinesAsync("FAQ.md")).ToList();
            var headers = file.Where(x => x.StartsWith("## ")).ToList();

            file.Insert(2, "<table class=\"table\"><thead><tr><th>Content</th></tr></thead><tbody>");
            int i = 3;
            foreach(string raw in headers)
            {
                string header = raw[3..]; //removes ## 
                file.Insert(i, $"<tr><td><a href=\"#{header.ToLower().Replace(' ', '-')}\">{header}</a></td></tr>");
                i++;
            }
            file.Insert(i, "</tbody></table>");
            file.Insert(i + 1, "");

            var markdown = Markdown.ToHtml(string.Join('\n', file), pipeline);
            Faq = new HtmlString(markdown);
        }
    }
}
