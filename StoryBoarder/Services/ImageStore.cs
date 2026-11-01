using System.IO;
using System.Security.Cryptography;
using System.Windows.Media.Imaging;

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
    private static readonly Dictionary<string, BitmapSource> Decoded = new();

    /// <summary>Adds a picture file and returns its id. Throws if WPF can't read the bytes as an image.</summary>
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

    public static BitmapSource? Get(string? id) => id is not null ? Decoded.GetValueOrDefault(id) : null;

    private static BitmapSource Decode(byte[] bytes)
    {
        var probe = BitmapFrame.Create(new MemoryStream(bytes), BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = new MemoryStream(bytes);
        if (probe.PixelWidth >= probe.PixelHeight && probe.PixelWidth > MaxDecodeSize) image.DecodePixelWidth = MaxDecodeSize;
        else if (probe.PixelHeight > MaxDecodeSize) image.DecodePixelHeight = MaxDecodeSize;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
