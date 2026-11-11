using System.Security.Cryptography;
using Avalonia.Media.Imaging;

namespace StoryBoarder.Services;

/// <summary>
/// Picture files used on boards, keyed by a hash of their bytes. Items refer to pictures by id so undo
/// snapshots stay small; the bytes are only written out when a board is saved or copied.
/// </summary>
public static class ImageStore
{
    // Longest side, in pixels, that pictures are decoded at for display. Saved files keep the original.
    private const int MaxDecodeSize = 2048;

    private static readonly Dictionary<string, byte[]> Files = new();
    private static readonly Dictionary<string, Bitmap> Decoded = new();

    /// <summary>Adds a picture file and returns its id. Throws <see cref="FormatException"/> if it isn't a picture.</summary>
    public static string Add(byte[] bytes)
    {
        var id = Convert.ToHexString(SHA256.HashData(bytes))[..16].ToLowerInvariant();
        if (!Files.ContainsKey(id))
        {
            Decoded[id] = Decode(bytes);
            Files[id] = bytes;
        }
        return id;
    }

    public static byte[]? GetBytes(string id) => Files.GetValueOrDefault(id);

    public static Bitmap? Get(string? id) => id is not null ? Decoded.GetValueOrDefault(id) : null;

    private static Bitmap Decode(byte[] bytes)
    {
        try
        {
            var bitmap = new Bitmap(new MemoryStream(bytes));
            var size = bitmap.PixelSize;
            if (Math.Max(size.Width, size.Height) <= MaxDecodeSize) return bitmap;
            bitmap.Dispose();
            return size.Width >= size.Height
                ? Bitmap.DecodeToWidth(new MemoryStream(bytes), MaxDecodeSize)
                : Bitmap.DecodeToHeight(new MemoryStream(bytes), MaxDecodeSize);
        }
        catch (Exception e)
        {
            throw new FormatException("The file isn't a picture this app can read.", e);
        }
    }
}
