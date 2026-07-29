using UnityEngine;

namespace Reflash.Game
{
    /// <summary>
    /// Gets a PNG out of a texture the game never meant to be read.
    ///
    /// Almost every texture the game ships has `isReadable` false, which means GetPixels throws and EncodeToPNG
    /// returns nothing. The way around it is the GPU: blit into a RenderTexture, read THAT back into a fresh
    /// readable Texture2D, and encode that. It costs one full-size readback, which is why the caller is expected to
    /// do it once and keep the bytes.
    /// </summary>
    internal static class TextureIO
    {
        /// <summary>
        /// One sprite as a PNG, cropped to the region it actually occupies.
        ///
        /// A sprite is very often a rectangle inside an atlas, and encoding its whole texture would hand back the
        /// sheet with every other sprite on it. The crop is done on the GPU side of the readback, so it costs the
        /// sprite rather than the atlas.
        /// </summary>
        internal static byte[] EncodeSpritePng(Sprite sprite)
        {
            if (sprite == null || sprite.texture == null) return null;

            Rect region = sprite.textureRect;
            int width = Mathf.Max(1, Mathf.RoundToInt(region.width));
            int height = Mathf.Max(1, Mathf.RoundToInt(region.height));

            // A texture the game built at runtime - a mugshot, a product icon - is already readable, and copying
            // its pixels is both cheaper and EXACTLY what it holds. The GPU detour below has to sample and re-store
            // every pixel, and in a linearly lit project that round trip came back visibly darker: the shader
            // converts sRGB bytes to linear on the way in, and a texture that is not flagged sRGB gets that
            // conversion applied to values that were never linear. Reading directly cannot be wrong that way.
            byte[] copied = Copy(sprite.texture, region, width, height);
            if (copied != null) return copied;

            RenderTexture rt = null;
            RenderTexture previous = RenderTexture.active;
            Texture2D readable = null;

            try
            {
                Texture source = sprite.texture;

                rt = RenderTexture.GetTemporary(source.width, source.height, 0,
                                                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(source, rt);

                RenderTexture.active = rt;

                readable = new Texture2D(width, height, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(region.x, region.y, width, height), 0, 0);
                readable.Apply(false, false);

                if (Blank(readable.GetPixels32())) return null;

                return ImageConversion.EncodeToPNG(readable);
            }
            catch (Exception e)
            {
                Core.Log.Warning($"[Reflash] encoding a sprite failed: {e.Message}");
                return null;
            }
            finally
            {
                RenderTexture.active = previous;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (readable != null) UnityEngine.Object.Destroy(readable);
            }
        }

        /// <summary>
        /// The sprite's own pixels, straight out of the texture. Null when the texture will not be read - which is
        /// the normal case for anything shipped in an asset bundle, and why the GPU path exists at all.
        /// </summary>
        private static byte[] Copy(Texture2D source, Rect region, int width, int height)
        {
            if (source == null || !source.isReadable) return null;

            Texture2D cropped = null;

            try
            {
                // GetPixels32/SetPixels32, never GetPixels/SetPixels.
                //
                // The Color overloads convert: Unity turns stored bytes into linear floats on the way out and back
                // on the way in, and whether each step happens depends on a flag a runtime-generated texture does
                // not necessarily carry. Get that wrong in one direction and every mugshot comes out muddy and
                // dark, which is exactly what happened. The Color32 pair copies bytes and asks no questions.
                // GetPixels32 has no cropping overload, so the whole texture comes back and the sprite's own
                // rectangle is lifted out of it by hand. A mugshot IS the whole texture anyway; only an atlas
                // pays for this, and an atlas is exactly what must not be handed over whole.
                Color32[] all = source.GetPixels32();
                var cut = new Color32[width * height];

                int left = Mathf.RoundToInt(region.x);
                int bottom = Mathf.RoundToInt(region.y);

                for (int y = 0; y < height; y++)
                {
                    int from = (bottom + y) * source.width + left;
                    Array.Copy(all, from, cut, y * width, width);
                }

                if (Blank(cut)) return null;

                cropped = new Texture2D(width, height, TextureFormat.RGBA32, false);
                cropped.SetPixels32(cut);
                cropped.Apply(false, false);

                return ImageConversion.EncodeToPNG(cropped);
            }
            catch
            {
                // isReadable can still be true for a texture whose rect does not fit; fall through to the GPU.
                return null;
            }
            finally
            {
                if (cropped != null) UnityEngine.Object.Destroy(cropped);
            }
        }

        /// <summary>
        /// Whether every pixel is the same, which means there is no picture here yet.
        ///
        /// The game renders NPC mugshots into their textures some time after a save loads, and a texture read before
        /// that comes back one flat colour - usually black. Encoding it produced a valid PNG of nothing, the caller
        /// cached it as "done", and seven of eighteen contacts kept a black hole where their face should be for the
        /// rest of the session. Answering "not yet" instead lets the caller come round again.
        ///
        /// Sampled rather than walked: a mugshot is a few hundred pixels square, and a picture that is genuinely
        /// there differs from its first pixel long before the thousandth sample.
        ///
        /// The stride is a PRIME, and that is the whole trick. A stride of "length / 1024" lands on a round number
        /// for a 512x512 texture - exactly one row - so every sample fell in the same column, that column happened
        /// to be transparent edge, and seven perfectly good app icons were declared empty. A prime stride cannot
        /// divide the row width, so the walk drifts across the picture instead of down one line of it.
        /// </summary>
        private static bool Blank(Color32[] pixels)
        {
            if (pixels == null || pixels.Length == 0) return true;

            const int stride = 1093;
            const int samples = 1024;

            Color32 first = pixels[0];

            for (int n = 1, i = stride % pixels.Length; n < samples; n++, i = (i + stride) % pixels.Length)
            {
                Color32 p = pixels[i];
                if (p.r != first.r || p.g != first.g || p.b != first.b || p.a != first.a) return false;
            }

            return true;
        }

        internal static byte[] EncodePng(Texture source)
        {
            if (source == null) return null;

            RenderTexture rt = null;
            RenderTexture previous = RenderTexture.active;
            Texture2D readable = null;

            try
            {
                rt = RenderTexture.GetTemporary(source.width, source.height, 0,
                                                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(source, rt);

                RenderTexture.active = rt;

                readable = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                readable.Apply(false, false);

                return ImageConversion.EncodeToPNG(readable);
            }
            catch (Exception e)
            {
                Core.Log.Warning($"[Reflash] encoding a texture failed: {e.Message}");
                return null;
            }
            finally
            {
                RenderTexture.active = previous;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (readable != null) UnityEngine.Object.Destroy(readable);
            }
        }
    }
}
