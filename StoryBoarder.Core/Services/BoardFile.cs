using System.Text.Json;
using System.Text.Json.Serialization;
using StoryBoarder.Models;

namespace StoryBoarder.Services;

/// <summary>Reads and writes boards as JSON (.storyboard files).</summary>
public static class BoardFile
{
    public static string Serialize(BoardData board) => JsonSerializer.Serialize(board, BoardJson.Default.BoardData);

    public static BoardData Deserialize(string json) =>
        JsonSerializer.Deserialize(json, BoardJson.Default.BoardData) ?? new BoardData();

    public static string SerializeSession(SessionData session) =>
        JsonSerializer.Serialize(session, BoardJson.Default.SessionData);

    public static SessionData? DeserializeSession(string json) =>
        JsonSerializer.Deserialize(json, BoardJson.Default.SessionData);

    /// <summary>
    /// Reads a .storyboard board or a Miro CSV export. Android's file picker doesn't always keep file
    /// extensions, so anything that isn't clearly a CSV but looks like JSON is read as a board.
    /// </summary>
    public static (BoardData Board, bool IsCsv) Read(string fileName, string content)
    {
        var isCsv = Path.GetExtension(fileName).Equals(".csv", StringComparison.OrdinalIgnoreCase)
            || !content.TrimStart('﻿', ' ', '\t', '\r', '\n').StartsWith('{');
        return (isCsv ? MiroCsvImporter.FromCsv(content) : Deserialize(content), isCsv);
    }
}

/// <summary>The open board as it was when the app last went into the background (phones and tablets).</summary>
public sealed class SessionData
{
    public BoardData Board { get; set; } = new();
    public string DocName { get; set; } = "Untitled";
    /// <summary>Lets the app write back to the file it was opened from or saved to.</summary>
    public string? FileBookmark { get; set; }
    public bool IsDirty { get; set; }
}

// Generated at build time, so saving and loading still work when the Android build trims unused code.
[JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(BoardData))]
[JsonSerializable(typeof(SessionData))]
internal partial class BoardJson : JsonSerializerContext;
