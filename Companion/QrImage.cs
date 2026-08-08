using UnityEngine;

namespace Reflash.Companion
{
    /// <summary>
    /// A pairing URL as a PNG, for the connect app to draw with <c>&lt;img src="s1://qr"&gt;</c>.
    ///
    /// Two conversions in here are easy to get wrong and both produce a code that looks fine and does not scan:
    ///
    ///   * QrLite hands back 0xAARRGGBB. Color32 is R,G,B,A in that byte order - so this is a reordering, not a
    ///     cast.
    ///   * SetPixels32 fills bottom-up, and the QR matrix is top-down. Without flipping the rows the code comes out
    ///     mirrored vertically, which no scanner accepts.
    /// </summary>
    internal static class QrImage
    {
        private const int Scale = 6;
        private const int Quiet = 4;

        private static string _cachedUrl = "";
        private static byte[] _cachedPng;

        /// <summary>
        /// The PNG for a URL, or null when it could not be encoded - QrLite covers versions 1 to 10, about 271
        /// bytes, and a pairing URL is around 50, so a failure here means something is wrong rather than long.
        ///
        /// Cached by URL: the connect app asks on every state read, and re-encoding plus re-compressing a PNG four
        /// times a second would be real work for a picture that has not changed.
        /// </summary>
        internal static byte[] For(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            if (_cachedPng != null && _cachedUrl == url) return _cachedPng;

            try
            {
                bool[,] matrix = QrLite.Encode(url);
                if (matrix == null)
                {
                    Core.Log.Warning($"the pairing URL does not fit in a QR code ({url.Length} chars).");
                    return null;
                }

                uint[] argb = QrLite.ToArgb(matrix, out int dim, Scale, Quiet);
                if (argb == null || dim <= 0) return null;

                var pixels = new Color32[argb.Length];
                for (int y = 0; y < dim; y++)
                {
                    // Row flip: source row 0 is the top, destination row 0 is the bottom.
                    int source = y * dim;
                    int destination = (dim - 1 - y) * dim;

                    for (int x = 0; x < dim; x++)
                    {
                        uint c = argb[source + x];
                        pixels[destination + x] = new Color32(
                            (byte)((c >> 16) & 0xFF),   // r
                            (byte)((c >> 8) & 0xFF),    // g
                            (byte)(c & 0xFF),           // b
                            (byte)((c >> 24) & 0xFF));  // a
                    }
                }

                var texture = new Texture2D(dim, dim, TextureFormat.RGBA32, false);
                texture.SetPixels32(pixels);
                texture.Apply(false, false);

                byte[] png = ImageConversion.EncodeToPNG(texture);
                UnityEngine.Object.Destroy(texture);

                _cachedUrl = url;
                _cachedPng = png;
                return png;
            }
            catch (Exception e)
            {
                Core.Log.Warning($"building the QR code failed: {e.Message}");
                return null;
            }
        }
    }
}
