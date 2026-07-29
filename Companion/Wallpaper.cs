using Reflash.Game;
using Il2CppScheduleOne.DevUtilities;
using UnityEngine;
using UnityEngine.UI;

namespace Reflash.Companion
{
    /// <summary>
    /// The phone's own wallpaper, as one PNG the size of the screen.
    ///
    /// Vanilla builds it out of two layers: a flat grey <c>Background</c> with a single enormous engraved sprite
    /// stretched over it, five thousand canvas units wide against a screen of six hundred and fifty-five. The
    /// screen therefore shows a narrow slice of the middle of that picture, and the pattern reads as big sweeping
    /// arcs rather than as anything tileable.
    ///
    /// So it is composited here rather than approximated in CSS. The first attempt was two crossing gradients,
    /// which is a fine texture and is not this one - the arcs are the thing you notice, and a diagonal weave says
    /// "some page" where the real one says "that phone".
    ///
    /// Handing over one flat picture also survives a browser that has decided to darken the page: a background
    /// COLOUR gets inverted to near black, an IMAGE is only dimmed.
    /// </summary>
    internal static class Wallpaper
    {
        /// <summary>The screen in CSS pixels, doubled so it still looks drawn rather than blown up on a phone.</summary>
        private const int Width = 800;
        private const int Height = 1418;

        private static byte[] _png;
        private static bool _missing;

        /// <summary>
        /// The wallpaper, or null while the phone has not built itself yet. Main thread only: it reads the scene
        /// and pulls a texture back off the GPU.
        /// </summary>
        internal static byte[] Png()
        {
            if (_png != null || _missing) return _png;

            try
            {
                var home = PlayerSingleton<Il2CppScheduleOne.UI.Phone.HomeScreen>.Instance;
                if (home == null) return null;

                Transform background = home.transform.Find("Background");
                if (background == null) { Give("the home screen has no Background"); return null; }

                var behind = background.GetComponent<Image>();
                Transform weaveNode = background.Find("Image");
                var weave = weaveNode == null ? null : weaveNode.GetComponent<Image>();

                if (behind == null || weave == null || weave.sprite == null)
                {
                    Give("the wallpaper layers are not where they were");
                    return null;
                }

                // GetComponent, never a cast. Under IL2CPP a managed cast of a Transform wrapper to RectTransform
                // throws even though the object IS one - the wrapper does not carry the derived type.
                RectTransform backRect = background.GetComponent<RectTransform>();
                RectTransform weaveRect = weaveNode.GetComponent<RectTransform>();
                if (backRect == null || weaveRect == null) { Give("the wallpaper layers have no rect"); return null; }

                _png = Composite(behind, weave, backRect, weaveRect);
                if (_png == null) return null;

                Core.Log.Msg($"[Reflash] wallpaper ready ({_png.Length / 1024} KB).");
                return _png;
            }
            catch (Exception e)
            {
                Give($"building the wallpaper failed: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Stop trying, and say why once. A missing wallpaper is not worth a retry every time a device asks for the
        /// home screen, and the fallback - the flat colour underneath it - is perfectly usable.
        /// </summary>
        private static void Give(string why)
        {
            _missing = true;
            Core.Log.Warning($"[Reflash] {why} - the companion falls back to a plain background.");
        }

        private static byte[] Composite(Image behind, Image weave, RectTransform backRect, RectTransform weaveRect)
        {
            Color32[] source = TextureIO.SpritePixels(weave.sprite, out int sw, out int sh);
            if (source == null) return null;

            Vector2 back = backRect.rect.size;
            Vector2 over = weaveRect.rect.size;
            Vector2 shift = weaveRect.anchoredPosition;

            if (back.x <= 0 || back.y <= 0 || over.x <= 0 || over.y <= 0) return null;

            Color32 baseColour = behind.color;
            Color tint = weave.color;

            var pixels = new Color32[Width * Height];

            for (int y = 0; y < Height; y++)
            {
                // Unity holds a texture bottom row first, and so does the array this hands back, so y counts up
                // from the bottom of the screen in both.
                float canvasY = (y + 0.5f) / Height * back.y - back.y * 0.5f;
                float overY = canvasY - shift.y;

                for (int x = 0; x < Width; x++)
                {
                    float canvasX = (x + 0.5f) / Width * back.x - back.x * 0.5f;
                    float overX = canvasX - shift.x;

                    Color32 result = baseColour;

                    // Outside the stretched sprite there is only the flat colour, which is what vanilla shows above
                    // and below it as well.
                    float u = overX / over.x + 0.5f;
                    float v = overY / over.y + 0.5f;

                    if (u >= 0f && u < 1f && v >= 0f && v < 1f)
                    {
                        int sx = Mathf.Clamp((int)(u * sw), 0, sw - 1);
                        int sy = Mathf.Clamp((int)(v * sh), 0, sh - 1);

                        Color32 texel = source[sy * sw + sx];

                        // Exactly what the UI shader does: tint the sprite, then lay it over what is behind it.
                        float a = texel.a / 255f * tint.a;
                        if (a > 0f)
                            result = new Color32(
                                Mix(baseColour.r, texel.r * tint.r, a),
                                Mix(baseColour.g, texel.g * tint.g, a),
                                Mix(baseColour.b, texel.b * tint.b, a),
                                255);
                    }

                    pixels[y * Width + x] = result;
                }
            }

            return TextureIO.EncodePixels(pixels, Width, Height);
        }

        private static byte Mix(byte under, float over, float alpha) =>
            (byte)Mathf.Clamp(Mathf.RoundToInt(under * (1f - alpha) + over * alpha), 0, 255);
    }
}
