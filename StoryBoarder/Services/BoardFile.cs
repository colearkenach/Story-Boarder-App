using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using StoryBoarder.Models;

namespace StoryBoarder.Services;

/// <summary>Reads and writes boards as JSON (.storyboard files).</summary>
public static class BoardFile
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(BoardData board) => JsonSerializer.Serialize(board, Options);

    public static BoardData Deserialize(string json) =>
        JsonSerializer.Deserialize<BoardData>(json, Options) ?? new BoardData();

    public static BoardData Load(string path) => Deserialize(File.ReadAllText(path));

    public static void Save(string path, BoardData board) => File.WriteAllText(path, Serialize(board));
}
