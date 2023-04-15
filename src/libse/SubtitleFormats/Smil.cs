using Nikse.SubtitleEdit.Core.Common;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;

namespace Nikse.SubtitleEdit.Core.SubtitleFormats
{
    /// <summary>
    /// http://wam.inrialpes.fr/timesheets/annotations/video.html
    /// </summary>
    public class Smil : SubtitleFormat
    {
        private static Dictionary<string, IHtmlDocument> textDocCache =
            new Dictionary<string, IHtmlDocument>();
        public override string Extension => ".smil";

        public override string Name => "SMIL";

        public override bool IsMine(List<string> lines, string fileName)
        {
            if (!lines.Any() || !lines.First().Contains("http://www.w3.org/ns/SMIL"))
            {
                return false;
            }

            return base.IsMine(lines, fileName);
        }

        public override string ToText(Subtitle subtitle, string title)
        {
            var header =
                "<smil xmlns=\"http://www.w3.org/ns/SMIL\" xmlns:epub=\"http://www.idpf.org/2007/ops\" version=\"3.0\">"
                + Environment.NewLine
                + "\t<body>" + Environment.NewLine
                + $"\t\t<seq id=\"seq000001\" epub:textref=\"{subtitle.Misc["textFile"]}\">";
            
            var sb = new StringBuilder();
            sb.AppendLine(header);
            foreach (var p in subtitle.Paragraphs)
            {
                sb.AppendLine($"\t\t\t<par id=\"{p.Misc["id"]}\">\n" +
                              $"\t\t\t\t<text src=\"{p.Misc["textFile"]}\"/>\n"+
                              $"\t\t\t\t<audio src=\"{p.Misc["audioFile"]}\" " +
                              $"clipBegin=\"{EncodeTime(p.StartTime)}\" " +
                              $"clipEnd=\"{EncodeTime(p.EndTime)}\"/>\n" +
                              "\t\t\t</par>");
            }
            sb.AppendLine("\t\t</seq>");
            sb.AppendLine("\t</body>");
            sb.AppendLine("</smil>");
            return sb.ToString().Trim();
        }

        private static string EncodeTime(TimeCode time)
        {
            return $"{time.Hours:00}:{time.Minutes:00}:{time.Seconds:00}.{time.Milliseconds:000}";
        }

        public override void LoadSubtitle(Subtitle subtitle, List<string> lines, string fileName)
        {
            _errorCount = 0;

            var parNodes = PullParNodes(fileName, lines); 

            foreach (var parNode in parNodes)
            {
                var parId = parNode.Attributes["id"]?.TextContent;

                var textContent = PullText(parNode, fileName);

                if (textContent == null)
                {
                    throw new ApplicationException("Error pulling text content");
                }
                
                var audioContent = PullAudio(parNode);

                if (audioContent == null)
                {
                    throw new ApplicationException("Error pulling audio content");
                }
                
                if (audioContent.StartTime == null || audioContent.EndTime == null)
                {
                    throw new ApplicationException("Error pulling audio val");
                }
                
                var p = new Paragraph
                {
                    Text = textContent.Text,
                    StartTime = new TimeCode(audioContent.StartTime.Value),
                    EndTime = new TimeCode(audioContent.EndTime.Value),
                    Misc = new Dictionary<string,string>()
                    {
                        {"id", parId},
                        {"textFile", textContent.File},
                        {"audioFile", audioContent.File},
                    }
                };
                
                subtitle.Paragraphs.Add(p);
            }

            var firstPara = subtitle.Paragraphs.FirstOrDefault();
            
            if (firstPara != null)
            {
                var textFile = firstPara.Misc["textFile"]
                    .Split(new[] { '#' }, StringSplitOptions.RemoveEmptyEntries)
                    [0];
                subtitle.Misc.Add("textFile", textFile);
                
                var audioFile = firstPara.Misc["audioFile"];
                subtitle.Misc.Add("audioFile", audioFile);
            }

            // var index = 1;
            // foreach (var paragraph in subtitle.Paragraphs)
            // {
            //     var next = subtitle.GetParagraphOrDefault(index);
            //     if (next != null)
            //     {
            //         paragraph.EndTime.TotalMilliseconds = next.StartTime.TotalMilliseconds - 1;
            //     }
            //     // else if (paragraph.EndTime.TotalMilliseconds < 50)
            //     // {
            //     //     paragraph.EndTime.TotalMilliseconds = paragraph.StartTime.TotalMilliseconds + Utilities.GetOptimalDisplayMilliseconds(paragraph.Text);
            //     // }
            //     // if (paragraph.Duration.TotalMilliseconds > Configuration.Settings.General.SubtitleMaximumDisplayMilliseconds)
            //     // {
            //     //     paragraph.EndTime.TotalMilliseconds = paragraph.StartTime.TotalMilliseconds + Utilities.GetOptimalDisplayMilliseconds(paragraph.Text);
            //     // }
            //     index++;
            // }

            foreach (var p2 in subtitle.Paragraphs)
            {
                p2.Text = WebUtility.HtmlDecode(p2.Text);
            }

            subtitle.Renumber();
        }

        private IHtmlCollection<IElement> PullParNodes(string fileName, List<string> lines = null)
        {
            string html;
            if (lines != null && lines.Any())
            {
                html = string.Join(Environment.NewLine, lines);                
            }
            else
            {
                html = File.ReadAllText(fileName);
            }
            
            
            var parser = new HtmlParser();
            var doc = parser.ParseDocument(html);

            var parNodes = doc.QuerySelectorAll("par[id]");

            return parNodes;

        }

        public string PullFirstMedia(string smilFile)
        {
            var node = PullParNodes(smilFile).FirstOrDefault();

            var audioNode = PullAudio(node);

            var containingDir = Path.GetDirectoryName(Path.GetDirectoryName(smilFile));

            if (string.IsNullOrWhiteSpace(containingDir))
            {
                return null;
            }

            if (!audioNode.File.StartsWith("../"))
            {
                return null;
            }

            var audioPathVal = audioNode.File.Replace("../", string.Empty);

            var audioPath = Path.Combine(containingDir, audioPathVal);

            if (!File.Exists(audioPath))
            {
                return null;
            }

            return audioPath;

        }

        private AudioNodeContent PullAudio(IElement parNode)
        {
            var audioNode = parNode.QuerySelector("audio");
            if (audioNode == null)
            {
                return null;
            }

            var audioFile = audioNode.Attributes["src"]?.TextContent;
                
            var clipBegin = audioNode.Attributes["clipbegin"]?.TextContent?.FromTimeSpanString();
            var clipEnd = audioNode.Attributes["clipend"]?.TextContent?.FromTimeSpanString();

            if (clipBegin == null || clipEnd == null)
            {
                throw new ApplicationException("Error parsing clip begin or end");
            }

            return new AudioNodeContent()
            {
                File = audioFile,
                StartTime = clipBegin,
                EndTime = clipEnd,
            };
        }

        private TextNodeContent PullText(IElement parNode, string fileName)
        {
            var textNode = parNode.QuerySelector("text");
            if (textNode == null)
            {
                return null;
            }

            var filePath = textNode.Attributes["src"]?.TextContent;

            if (string.IsNullOrWhiteSpace(filePath))
            {
                return null;
            }

            var smilDir = Path.GetDirectoryName(fileName);

            if (string.IsNullOrWhiteSpace(smilDir))
            {
                return null;
            }

            var containingDir = Path.GetDirectoryName(smilDir);

            if (string.IsNullOrWhiteSpace(containingDir))
            {
                return null;
            }

            if (!filePath.StartsWith("../"))
            {
                return null;
            }

            var textPathVal = filePath.Replace("../", string.Empty);

            var split = textPathVal.Split(new[] { '#' }, StringSplitOptions.RemoveEmptyEntries);

            if (split.Length != 2)
            {
                throw new ApplicationException("Error parsing SMIL");
            }

            var textPathFile = split[0];

            var textPathId = split[1];

            var textPath = Path.Combine(containingDir, textPathFile);

            if (!File.Exists(textPath))
            {
                if (Path.GetDirectoryName(textPathFile) == "text")
                {
                    //possible sync_text - try that
                    textPath = Path.Combine(containingDir, "sync_text", Path.GetFileName(textPathFile));
                    if (!File.Exists(textPath))
                    {
                        return null;
                    }
                }
                else
                {
                    return null;
                }
            }

            IHtmlDocument textDoc;

            if (textDocCache.ContainsKey(textPath))
            {
                textDocCache.TryGetValue(textPath, out textDoc);
            }
            else
            {
                var textHtml = File.ReadAllText(textPath);

                var parser = new HtmlParser();
                textDoc = parser.ParseDocument(textHtml);
                textDocCache.Add(textPath, textDoc);
            }

            if (textDoc == null)
            {
                return null;
            }

            var node = textDoc.QuerySelector($"span[id='{textPathId}']");

            if (node == null)
            {
                throw new ApplicationException("Error locating text val");
            }

            return new TextNodeContent()
            {
                File = filePath,
                Text = node.TextContent,
            };
        }
    }

    internal class AudioNodeContent
    {
        public string File { get; set; }
        public TimeSpan? StartTime { get; set; }
        public TimeSpan? EndTime { get; set; }
    }
    
    internal class TextNodeContent
    {
        public string File { get; set; }
        public string Text { get; set; }
    }
}
