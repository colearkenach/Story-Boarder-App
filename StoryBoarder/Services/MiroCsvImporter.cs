using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using StoryBoarder.Models;

namespace StoryBoarder.Services;

/// <summary>
/// Turns a Miro "Export to CSV" file into a board. Miro writes one text item per row and
/// separates frames with blank rows, so each block becomes a frame titled with its first row
/// and the remaining rows become sticky notes laid out in a grid inside it.
/// </summary>
public static class MiroCsvImporter
{
    private const double SmallNote = 200, MediumNote = 260, LargeNote = 340;
    private const double NoteGap = 24;
    private const double FramePadding = 48;
    private const double FrameGap = 200;
    private const double TargetAspect = 1.8; // board width / height, roughly a widescreen window
    private const int MaxColumns = 6;

    private static readonly Regex ListStart = new(@"^(?<head>[^:]*:)?\s*(?<marker>[-*])\s+");
    private static readonly Regex Label = new(@"^(?<label>[^:\n]{1,24}):\s");
    private static readonly Regex LostLineBreak = new(" {3,}");

    private sealed record Section(string Title, List<string> Notes);

    public static BoardData Import(string path) => FromCsv(File.ReadAllText(path));

    public static BoardData FromCsv(string csv)
    {
        var sections = ReadSections(csv.TrimStart('﻿'));
        var labelColors = ColorsForRepeatedLabels(sections.SelectMany(s => s.Notes));
        var laidOut = sections.Select(s => LayoutSection(s, labelColors)).ToList();

        // Wrap frames into rows sized so the whole board comes out roughly widescreen.
        var area = laidOut.Sum(items => (items[0].Width + FrameGap) * (items[0].Height + FrameGap));
        var maxRowWidth = Math.Max(Math.Sqrt(area * TargetAspect), laidOut.Select(items => items[0].Width).DefaultIfEmpty(0).Max());

        var board = new BoardData();
        double x = 0, y = 0, rowHeight = 0;
        foreach (var items in laidOut)
        {
            var frame = items[0];
            if (x > 0 && x + frame.Width > maxRowWidth)
            {
                x = 0;
                y += rowHeight + FrameGap;
                rowHeight = 0;
            }
            foreach (var item in items)
            {
                item.X += x;
                item.Y += y;
            }
            board.Items.AddRange(items);
            x += frame.Width + FrameGap;
            rowHeight = Math.Max(rowHeight, frame.Height);
        }
        return board;
    }

    private static List<Section> ReadSections(string csv)
    {
        var sections = new List<Section>();
        var lines = new List<string>();
        // The extra empty record at the end flushes the last block.
        foreach (var record in ParseCsv(csv).Append(new List<string>()))
        {
            var text = RecordText(record);
            if (text.Length > 0)
            {
                lines.Add(text);
            }
            else if (lines.Count > 0)
            {
                sections.Add(new Section(lines[0], lines.Skip(1).ToList()));
                lines.Clear();
            }
        }
        return sections;
    }

    // Returns the frame followed by its notes, positioned relative to the frame's top-left corner.
    private static List<BoardItemData> LayoutSection(Section section, Dictionary<string, string> labelColors)
    {
        var frame = new BoardItemData { Kind = ItemKind.Frame, Text = section.Title, Color = Palette.Frame };
        var items = new List<BoardItemData> { frame };

        var columns = Math.Clamp((int)Math.Ceiling(Math.Sqrt(section.Notes.Count)), 1, MaxColumns);
        var maxRowWidth = columns * (SmallNote + NoteGap) - NoteGap;
        double x = 0, y = 0, rowHeight = 0, contentWidth = 0;
        foreach (var text in section.Notes)
        {
            var size = NoteSize(text);
            if (x > 0 && x + size > maxRowWidth)
            {
                x = 0;
                y += rowHeight + NoteGap;
                rowHeight = 0;
            }
            items.Add(new BoardItemData
            {
                Kind = ItemKind.Note,
                Text = text,
                Color = NoteColor(text, labelColors),
                X = FramePadding + x,
                Y = FramePadding + y,
                Width = size,
                Height = size,
            });
            x += size + NoteGap;
            rowHeight = Math.Max(rowHeight, size);
            contentWidth = Math.Max(contentWidth, x - NoteGap);
        }

        frame.Width = Math.Max(contentWidth + 2 * FramePadding, 400);
        frame.Height = Math.Max(y + rowHeight + 2 * FramePadding, 300);
        return items;
    }

    private static double NoteSize(string text) =>
        (text.Length + 25 * text.Count(c => c == '\n')) switch
        {
            <= 70 => SmallNote,
            <= 180 => MediumNote,
            _ => LargeNote,
        };

    private static string NoteColor(string text, Dictionary<string, string> labelColors)
    {
        var match = Label.Match(text);
        return match.Success && labelColors.TryGetValue(match.Groups["label"].Value, out var color)
            ? color
            : Palette.DefaultNote;
    }

    // Notes that start with the same label ("Class:", "Power:", ...) share a color so related notes stand out.
    private static Dictionary<string, string> ColorsForRepeatedLabels(IEnumerable<string> notes) =>
        notes.Select(n => Label.Match(n))
            .Where(m => m.Success)
            .Select(m => m.Groups["label"].Value)
            .GroupBy(label => label)
            .Where(g => g.Count() > 1)
            .Select((g, i) => (Label: g.Key, Color: Palette.Accents[i % Palette.Accents.Length]))
            .ToDictionary(p => p.Label, p => p.Color);

    // Joins a row's cells into one note. Miro sometimes repeats a link in a second column.
    private static string RecordText(List<string> cells)
    {
        var text = "";
        foreach (var cell in cells.Select(Clean).Where(c => c.Length > 0))
        {
            if (!text.Contains(cell)) text = text.Length == 0 ? cell : text + "\n" + cell;
        }
        return text;
    }

    private static string Clean(string raw)
    {
        var text = WebUtility.HtmlDecode(raw).Replace("\r\n", "\n").Replace(' ', ' ').Trim();
        // Miro wraps some multi-line notes in single quotes.
        if (text.Length >= 2 && text[0] == '\'' && text[^1] == '\'') text = text[1..^1].Trim();
        // Paragraph breaks come out as runs of spaces.
        text = LostLineBreak.Replace(text, "\n").Replace("  ", " ");
        return RestoreBullets(text);
    }

    // Miro flattens bullet lists to "- one - two - three"; put each item back on its own line.
    private static string RestoreBullets(string text)
    {
        var match = ListStart.Match(text);
        if (!match.Success) return text;

        var separator = $" {match.Groups["marker"].Value} ";
        var items = text[match.Length..].Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var bullets = string.Join("\n", items.Select(item => "• " + item));
        var head = match.Groups["head"].Value.Trim();
        return head.Length > 0 ? head + "\n" + bullets : bullets;
    }

    private static IEnumerable<List<string>> ParseCsv(string csv)
    {
        var record = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < csv.Length; i++)
        {
            var c = csv[i];
            if (inQuotes)
            {
                if (c != '"') field.Append(c);
                else if (i + 1 < csv.Length && csv[i + 1] == '"') { field.Append('"'); i++; }
                else inQuotes = false;
            }
            else if (c == '"') inQuotes = true;
            else if (c == ',') { record.Add(field.ToString()); field.Clear(); }
            else if (c == '\n')
            {
                record.Add(field.ToString());
                field.Clear();
                yield return record;
                record = new List<string>();
            }
            else if (c != '\r') field.Append(c);
        }
        if (field.Length > 0 || record.Count > 0)
        {
            record.Add(field.ToString());
            yield return record;
        }
    }
}
