using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace IterationRoom.EditorTools
{
    // EVERY PICTURE THE GAME OWNS IS DRAWN HERE, at build time, into a `Texture2D` - the wall test
    // card, the noise and wear maps, and the whole icon set. Nothing in this project ships a
    // painted asset, so a new pictogram is a method rather than a file. A data texture takes no
    // gamma curve and no block compression (CLAUDE.md 3).
    //
    // Split out of `SceneBuilder.cs` (2026-09-02), which had reached 25,000 lines. One class,
    // many files - see the note over the `partial` keyword there. Everything private in any part
    // is reachable from every other part, so nothing about the build changed when this moved.
    public static partial class SceneBuilder
    {

        // Surface detail, generated rather than sourced, so the project stays reproducible from
        // scripts like everything else here.
        //
        // The room's walls and slabs are perfectly uniform flat colour, which is the other half of
        // why it read as a whitebox: real lights now sweep across them, but there is no micro-relief
        // for that light to catch, so every surface still shades as one continuous gradient. A fine
        // normal map gives the light something to break up on - the difference between "painted
        // white" and "a painted white wall".
        //
        // Multi-octave value noise, made *tileable* by wrapping the lattice at each octave's period
        // (see Hash). Mathf.PerlinNoise is not tileable and would seam at every repeat.
        // The picture every wall panel shows once it fails: seven colour bars with a dark band
        // under them carrying the word ERROR.
        //
        // A TEST CARD rather than a warning sign, and that is the whole idea. A sign is something a
        // room PUTS UP, and a facility still capable of putting a sign up has not failed. This is
        // the image a display shows when it has nothing left to show - so a room built entirely out
        // of displays does not report the fault, it becomes it.
        //
        // 126 x 100 for seven exact 18px bars, uncompressed and point-filtered: block colour with
        // hard edges is precisely what DXT smears, and a soft test card is a poster of one.
        private static Texture2D MakeTestCardTexture(string name)
        {
            const int w = 126, h = 100;
            const int bandHeight = 30;          // the dark strip along the bottom
            const int bars = 7;

            // The broadcast order, brightest to darkest by luma - which is what makes a row of them
            // read as a test card rather than as a row of coloured squares.
            Color[] bar =
            {
                Color.white,
                new Color(1f, 1f, 0f),
                new Color(0f, 1f, 1f),
                new Color(0f, 1f, 0f),
                new Color(1f, 0f, 1f),
                new Color(1f, 0f, 0f),
                new Color(0f, 0f, 1f),
            };

            Color band = new Color(0.04f, 0.04f, 0.045f);   // GrooveDark, like every dark plate here
            Color ink = new Color(1f, 0.13f, 0.11f);        // and the same red as every display

            Color[] px = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    px[y * w + x] = y < bandHeight
                        ? band
                        : bar[Mathf.Clamp(x * bars / w, 0, bars - 1)];
                }
            }

            // ERROR, drawn from a 5x7 bitmap because there is no font rasteriser to hand when the
            // target is a Texture2D. Three glyphs is all five letters need.
            const int glyphScale = 3;
            int textWidth = (5 * 5 + 4) * glyphScale;       // five glyphs, four one-pixel gaps
            int originX = (w - textWidth) / 2;
            int originY = (bandHeight - 7 * glyphScale) / 2;
            string word = "ERROR";
            for (int c = 0; c < word.Length; c++)
                BlitGlyph(px, w, h, word[c], originX + c * 6 * glyphScale, originY, glyphScale, ink);

            return WriteTexture(name, w, h, px, TextureWrapMode.Repeat, FilterMode.Point);
        }

        // Rows top-to-bottom, so a glyph reads the right way up when it is written into a texture
        // whose y = 0 is the BOTTOM.
        private static void BlitGlyph(Color[] px, int w, int h, char c, int x0, int y0, int scale, Color ink)
        {
            string[] rows;
            switch (c)
            {
                case 'E': rows = new[] { "11111", "10000", "10000", "11110", "10000", "10000", "11111" }; break;
                case 'R': rows = new[] { "11110", "10001", "10001", "11110", "10100", "10010", "10001" }; break;
                case 'O': rows = new[] { "01110", "10001", "10001", "10001", "10001", "10001", "01110" }; break;
                default: return;
            }

            for (int row = 0; row < rows.Length; row++)
            {
                for (int col = 0; col < 5; col++)
                {
                    if (rows[row][col] != '1') continue;
                    for (int sy = 0; sy < scale; sy++)
                    {
                        for (int sx = 0; sx < scale; sx++)
                        {
                            int x = x0 + col * scale + sx;
                            int y = y0 + (rows.Length - 1 - row) * scale + sy;
                            if (x < 0 || x >= w || y < 0 || y >= h) continue;
                            px[y * w + x] = ink;
                        }
                    }
                }
            }
        }

        // Monochrome grain, tiled several times across a panel and scrolled a whole texture at a
        // time between frames. Deterministic through System.Random rather than UnityEngine.Random,
        // which BuildOnsets depends on being left alone.
        private static Texture2D MakeStaticTexture(string name, int size)
        {
            var rng = new System.Random(20260812);
            Color[] px = new Color[size * size];
            for (int i = 0; i < px.Length; i++)
            {
                // Weighted toward the dark end. Even static is mostly black - an even spread comes
                // out as flat grey at any distance and stops reading as noise at all.
                float v = (float)rng.NextDouble();
                v *= v;
                px[i] = new Color(v, v, v);
            }

            return WriteTexture(name, size, size, px, TextureWrapMode.Repeat, FilterMode.Point);
        }

        private static Texture2D WriteTexture(string name, int w, int h, Color[] pixels,
                                              TextureWrapMode wrap, FilterMode filter)
        {
            string path = $"{TexturesDir}/{name}.png";

            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels(pixels);
            tex.Apply();

            Directory.CreateDirectory(TexturesDir);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.wrapMode = wrap;
                importer.filterMode = filter;
                // NPOT sizes are rescaled to the nearest power of two by default, which would turn
                // 126 x 100 into 128 x 128 and put the seven exact bars back on fractional pixels.
                importer.npotScale = TextureImporterNPOTScale.None;
                // Block colour with hard edges is the worst case for DXT, and this is nothing else.
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // HOW SHINY THE SURFACE IS, VARYING ACROSS IT - the other half of what the normal map does.
        //
        // **PERFECTLY UNIFORM ROUGHNESS IS ONE OF THE STRONGEST TELLS THAT SOMETHING IS RENDERED.**
        // `ApplySurfaceDetail` gives the walls a normal map, so light already breaks up on the
        // micro-relief - but it also hands them ONE smoothness number, so every square metre of the
        // building reflects exactly as sharply as every other. Real paint does not: it is duller
        // where it has been touched, cleaned, or run down a wall, and the eye reads that unevenness
        // long before it can name it.
        //
        // **LOW FREQUENCY ON PURPOSE, and that is the whole difference between this and the normal
        // map.** That one wants near-pixel grain, because it is standing in for plaster texture. This
        // wants broad soft patches the size of a hand or a body, because it is standing in for wear -
        // fine noise in roughness reads as sparkle, which is the opposite of the intent.
        //
        // **IT ONLY EVER DULLS.** URP multiplies (`specGloss.a *= _Smoothness` in LitInput.hlsl), so
        // the alpha here is a fraction of the material's authored smoothness and 1.0 means "as
        // authored". Nothing can come out glossier than the number somebody chose, which keeps this a
        // detail pass rather than a second place the surface is defined.
        //
        // R CARRIES METALLIC, because the same texture supplies both and a zero red channel would
        // silently un-metal anything this is applied to. Read off the material rather than assumed.
        private static Texture2D MakeSmoothnessMap(string name, int size, float metallic, float floor)
        {
            string path = $"{TexturesDir}/{name}.png";

            float[] wear = new float[size * size];
            // Two coarse octaves only - 4 and 12 periods across the tile. The normal map's finest is
            // 64; going anywhere near that here is what turns wear into glitter.
            AddNoiseOctave(wear, size, 4, 0.65f, 4801);
            AddNoiseOctave(wear, size, 12, 0.35f, 5779);

            float min = float.MaxValue, max = float.MinValue;
            foreach (float v in wear) { if (v < min) min = v; if (v > max) max = v; }
            float span = Mathf.Max(0.0001f, max - min);

            Color[] pixels = new Color[size * size];
            for (int i = 0; i < pixels.Length; i++)
            {
                // Normalised to the noise's ACTUAL range rather than an assumed one, so `floor` means
                // the same depth of dulling whatever the octave weights above are changed to.
                float t = (wear[i] - min) / span;
                pixels[i] = new Color(metallic, 0f, 0f, Mathf.Lerp(floor, 1f, t));
            }

            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            tex.SetPixels(pixels);
            tex.Apply();

            Directory.CreateDirectory(TexturesDir);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                // **sRGB OFF.** This is data, not a picture - a gamma curve applied to a roughness
                // value is a different roughness. The same reason the normal map is created with
                // `linear: true`.
                importer.sRGBTexture = false;
                // **AND ALPHA MUST SURVIVE THE IMPORT.** The smoothness lives in it, and Unity will
                // happily compress a texture whose alpha it thinks is unused.
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = false;
                // **AND IT MUST NOT BE BLOCK-COMPRESSED.** DXT5 stores alpha in 4x4 blocks, and the
                // smoothness lives in alpha - so a smooth low-frequency field gets quantised per
                // block and the walls come out wearing a faint grid of small squares, visible across
                // a room. Reported from play as "연한 회색 사각형들", 2026-08-25.
                //
                // Same rule the HUD icons already carry for the same reason ("block compression
                // frays something that is nothing but alpha"), which this map did not inherit
                // because it goes through a different importer path. **A DATA texture is not a
                // picture: it gets no gamma curve and no block compression.**
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.anisoLevel = 4;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Texture2D MakeNoiseNormalMap(string name, int size, float bumpStrength)
        {
            string path = $"{TexturesDir}/{name}.png";

            float[] height = new float[size * size];
            // Coarsest octave is a broad undulation like a skimmed wall; finest is near-pixel grain.
            int[] periods = { 8, 16, 32, 64 };
            float[] weights = { 0.5f, 0.28f, 0.15f, 0.07f };
            for (int o = 0; o < periods.Length; o++)
                AddNoiseOctave(height, size, periods[o], weights[o], 1000 + o * 977);

            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Central differences, wrapped, so the derived normals tile with the heights.
                    float dx = height[WrapIndex(x - 1, y, size)] - height[WrapIndex(x + 1, y, size)];
                    float dy = height[WrapIndex(x, y - 1, size)] - height[WrapIndex(x, y + 1, size)];
                    Vector3 n = new Vector3(dx * bumpStrength, dy * bumpStrength, 1f).normalized;
                    pixels[y * size + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
                }
            }

            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            tex.SetPixels(pixels);
            tex.Apply();

            Directory.CreateDirectory(TexturesDir);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                // Without NormalMap the PNG imports as a colour texture and the shader reads the
                // raw RGB as a normal, which tilts every surface toward +X/+Y.
                importer.textureType = TextureImporterType.NormalMap;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.anisoLevel = 4;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static void AddNoiseOctave(float[] height, int size, int period, float weight, int seed)
        {
            float cell = (float)size / period;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float fx = x / cell, fy = y / cell;
                    int x0 = Mathf.FloorToInt(fx), y0 = Mathf.FloorToInt(fy);
                    float tx = SmoothStep(fx - x0), ty = SmoothStep(fy - y0);

                    float bottom = Mathf.Lerp(LatticeHash(x0, y0, period, seed), LatticeHash(x0 + 1, y0, period, seed), tx);
                    float top = Mathf.Lerp(LatticeHash(x0, y0 + 1, period, seed), LatticeHash(x0 + 1, y0 + 1, period, seed), tx);
                    height[y * size + x] += Mathf.Lerp(bottom, top, ty) * weight;
                }
            }
        }

        private static float SmoothStep(float t) => t * t * (3f - 2f * t);

        private static int WrapIndex(int x, int y, int size)
        {
            x = ((x % size) + size) % size;
            y = ((y % size) + size) % size;
            return y * size + x;
        }

        // The lattice coordinate wraps at `period`, which is precisely what makes each octave -
        // and therefore the whole map - tile seamlessly.
        private static float LatticeHash(int x, int y, int period, int seed)
        {
            x = ((x % period) + period) % period;
            y = ((y % period) + period) % period;
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + seed * 1274126177;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0x7fffffff) / (float)0x7fffffff;
            }
        }

        // A tiny software rasteriser for the HUD's glyphs. They are drawn from code for the same
        // reason the wall grain and the narration are: everything here has to rebuild from a
        // script, and a folder of sourced PNGs was exactly the part that could not.
        //
        // Shapes are predicates over a 0..1 square with the origin bottom-left, matching Unity's
        // texture coordinates so a row index needs no flipping. Coverage is supersampled 4x4, which
        // is the whole reason a 128px disc has a clean edge rather than a staircase.
        private sealed class IconCanvas
        {
            private const int Supersample = 4;

            private readonly int size;
            private readonly float[] coverage;

            public IconCanvas(int size)
            {
                this.size = size;
                coverage = new float[size * size];
            }

            // sign -1 cuts back out of what is already drawn - the hole in a key's bow, the hollow
            // inside the mouse outline. Predicates compose, so an intersection is just &&.
            public void Shape(System.Func<Vector2, bool> inside, float sign = 1f)
            {
                float step = 1f / (size * Supersample);
                const float samples = Supersample * Supersample;

                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float hits = 0f;
                        for (int sy = 0; sy < Supersample; sy++)
                            for (int sx = 0; sx < Supersample; sx++)
                            {
                                Vector2 p = new Vector2(
                                    (x * Supersample + sx + 0.5f) * step,
                                    (y * Supersample + sy + 0.5f) * step);
                                if (inside(p)) hits++;
                            }

                        if (hits == 0f) continue;
                        int i = y * size + x;
                        coverage[i] = Mathf.Clamp01(coverage[i] + sign * (hits / samples));
                    }
                }
            }

            public void Disc(Vector2 centre, float radius, float sign = 1f) =>
                Shape(p => (p - centre).sqrMagnitude <= radius * radius, sign);

            // A THICK LINE WITH ROUND ENDS - the distance from a point to a segment, thresholded.
            // Everything curved in these icons used to be a stack of overlapping `Bar`s stepped along
            // the shape, which is why the axe read as a hammer and the question mark as a broken ring:
            // a staircase of rectangles has corners the eye finds before it finds the curve. One
            // swept disc has none.
            public void Capsule(Vector2 a, Vector2 b, float radius, float sign = 1f)
            {
                Vector2 ab = b - a;
                float lenSqr = Mathf.Max(1e-6f, ab.sqrMagnitude);
                Shape(p =>
                {
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSqr);
                    return (p - (a + ab * t)).sqrMagnitude <= radius * radius;
                }, sign);
            }

            // A STROKED ARC, as a chain of capsules. Enough segments that the joins disappear at the
            // 128px these are authored at; fewer and the curve polygonises again.
            public void Arc(Vector2 centre, float radius, float fromDegrees, float toDegrees,
                            float thickness, float sign = 1f)
            {
                const int steps = 28;
                Vector2 previous = Vector2.zero;
                for (int i = 0; i <= steps; i++)
                {
                    float a = Mathf.Deg2Rad * Mathf.Lerp(fromDegrees, toDegrees, i / (float)steps);
                    Vector2 at = centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
                    if (i > 0) Capsule(previous, at, thickness, sign);
                    previous = at;
                }
            }

            public void Ring(Vector2 centre, float outer, float inner, float sign = 1f) =>
                Shape(p =>
                {
                    float sqr = (p - centre).sqrMagnitude;
                    return sqr <= outer * outer && sqr >= inner * inner;
                }, sign);

            // Described by centre, half extents and an angle rather than by four corners, so the
            // needle and its handle can share one diagonal.
            public void Bar(Vector2 centre, Vector2 halfExtents, float degrees = 0f, float sign = 1f)
            {
                float rad = -degrees * Mathf.Deg2Rad;
                float cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);

                Shape(p =>
                {
                    Vector2 d = p - centre;
                    // Un-rotate the sample rather than rotating the rectangle: an axis-aligned
                    // containment test is the only one that stays trivially correct.
                    Vector2 local = new Vector2(d.x * cos - d.y * sin, d.x * sin + d.y * cos);
                    return Mathf.Abs(local.x) <= halfExtents.x && Mathf.Abs(local.y) <= halfExtents.y;
                }, sign);
            }

            // The same glyph as an OPAQUE picture, for the ones that go on an object in the world
            // rather than in the HUD. Alpha is not an option there: a lit surface with the shape in
            // its alpha is a white card, because nothing in this project reads alpha off an opaque
            // material. The coverage becomes ink over paper instead.
            public Texture2D ToOpaqueTexture(string name, Color paper, Color ink)
            {
                Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = name };
                Color[] pixels = new Color[size * size];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.Lerp(paper, ink, coverage[i]);
                tex.SetPixels(pixels);
                tex.Apply();
                return tex;
            }

            // White, with the whole shape carried in alpha: the HUD tints these through
            // Image.color, and a glyph with baked-in colour could not be recoloured to match.
            public Texture2D ToTexture(string name)
            {
                Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = name };
                Color[] pixels = new Color[size * size];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(1f, 1f, 1f, coverage[i]);
                tex.SetPixels(pixels);
                tex.Apply();
                return tex;
            }
        }

        // The same glyph as a world texture: opaque, mipmapped, and imported as a plain texture
        // rather than a sprite. Mipmaps matter here where they do not in the HUD - these are read at
        // four metres across a room, and an unmipped glyph at that distance is a shimmering mess.
        private static Texture2D SaveSymbolTexture(IconCanvas canvas, string name)
            => SaveSymbolTexture(canvas, name, SymbolPaper, SymbolInk);

        // The same, with the two values named rather than assumed. The cube room's inclusion wants
        // them INVERTED - a near-black core carrying a pale glyph - because it is read through glass
        // in a white room, where ink-on-paper is a white card inside a white cube seen against a
        // white wall. Which of the two is the glyph never changes; only which one is bright does.
        private static Texture2D SaveSymbolTexture(IconCanvas canvas, string name, Color paper, Color ink)
        {
            if (!Directory.Exists(SymbolsDir)) Directory.CreateDirectory(SymbolsDir);

            string path = $"{SymbolsDir}/{name}.png";
            Texture2D tex = canvas.ToOpaqueTexture(name, paper, ink);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Default;
                importer.alphaIsTransparency = false;
                importer.mipmapEnabled = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // A lit material with a picture on it, which is the first one in this building - every other
        // surface here is a flat colour. `_BaseMap` AND `mainTexture` are both set because URP reads
        // the first and a good deal of Unity's own tooling still reads the second.
        private static Material MakeSymbolMaterial(string name, Texture2D map)
        {
            string path = $"{MaterialsDir}/{name}.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(OpaqueShader());
                AssetDatabase.CreateAsset(mat, path);
            }

            mat.shader = OpaqueShader();
            mat.color = Color.white;
            mat.SetTexture("_BaseMap", map);
            mat.mainTexture = map;
            // Matte. A glyph is being read, and a specular highlight across it is the one thing that
            // stops it being readable from an angle.
            SetSmoothness(mat, 0.08f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Sprite SaveSprite(IconCanvas canvas, string name)
        {
            string path = $"{IconsDir}/{name}.png";
            Texture2D tex = canvas.ToTexture(name);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Bilinear;
                importer.wrapMode = TextureWrapMode.Clamp;
                // Uncompressed: these are a few KB each, and block compression frays the edge of a
                // glyph that is nothing but alpha.
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        // The pin, as a silhouette: a stubby grip and a long needle on one diagonal. Drawn on the
        // diagonal rather than upright because upright it reads as a nail, and because a HUD slot
        // is square - a diagonal uses the corners.
        // A pail seen side on: a tapered body with a handle over it. Drawn rather than rendered, like
        // every other icon here.
        // A RUBBER DUCK IN SILHOUETTE. Body, head, bill, and a notch bitten out for the eye - a duck
        // is one of the few shapes that survives being reduced to an outline, which is exactly why it
        // is the object a pictogram can name without a caption.
        private static Sprite DuckIcon()
        {
            var icon = new IconCanvas(128);
            Vector2 mid = new Vector2(0.5f, 0.46f);

            // The body: a big disc with a tail bar, which reads as a duck sitting on water.
            icon.Disc(mid + new Vector2(-0.02f, -0.06f), 0.235f);
            icon.Bar(mid + new Vector2(-0.245f, 0.010f), new Vector2(0.090f, 0.055f), 22f);

            // Head and neck.
            icon.Disc(mid + new Vector2(0.150f, 0.195f), 0.135f);
            icon.Bar(mid + new Vector2(0.115f, 0.075f), new Vector2(0.075f, 0.100f), 0f);

            // The bill, forward and slightly down.
            icon.Bar(mid + new Vector2(0.305f, 0.170f), new Vector2(0.090f, 0.042f), -8f);

            // The eye, cut back OUT of the head - a hole reads at icon size where a dot does not.
            icon.Disc(mid + new Vector2(0.170f, 0.235f), 0.038f, -1f);
            return SaveSprite(icon, "icon_duck");
        }

        // A BEACH BALL: a disc with two curved panel seams. The seams are the whole of it - without
        // them this is a circle, and a circle in a row of objects reads as "any ball" or as a full
        // stop.
        private static Sprite BeachBallIcon()
        {
            var icon = new IconCanvas(128);
            Vector2 mid = new Vector2(0.5f, 0.5f);

            icon.Disc(mid, 0.330f);
            // Two seams, cut out rather than drawn on, each built from short bars stepped across the
            // face so they bow like meridians on a sphere.
            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = -5; i <= 5; i++)
                {
                    float t = i / 5f;
                    float y = t * 0.300f;
                    // The bow: widest at the equator, meeting at the poles.
                    float x = side * 0.150f * Mathf.Cos(t * Mathf.PI * 0.5f);
                    icon.Bar(mid + new Vector2(x, y), new Vector2(0.026f, 0.036f), 0f, -1f);
                }
            }
            return SaveSprite(icon, "icon_beachball");
        }

        // A CUBE, SEEN CORNER-ON: a hexagon with a Y cut into it.
        //
        // That is the whole of the shape, and it is why this reads as a cube where the previous two
        // attempts did not. A square with panels implied on it is a square; a hexagon divided by a Y
        // is three rhombi meeting at a corner, which the eye resolves as a box with no other cue at
        // all - it is the same drawing everybody makes of a cube by hand.
        //
        // THE Y IS CUT OUT, not drawn on. These icons are a single colour with an alpha mask, so a
        // hole is the only mark available; it shows through as whatever is behind, which is the white
        // line asked for.
        private static Sprite CubeIcon()
        {
            var icon = new IconCanvas(128);
            Vector2 mid = new Vector2(0.5f, 0.5f);
            const float radius = 0.345f;      // corner to centre
            const float line = 0.026f;        // half-width of the white lines

            // A pointy-top hexagon is the intersection of three slabs, at 0, 60 and 120 degrees, each
            // an apothem wide. Written that way rather than as six edges because three inequalities
            // cannot disagree about a corner.
            float apothem = radius * Mathf.Cos(30f * Mathf.Deg2Rad);
            icon.Shape(pt =>
            {
                Vector2 d = pt - mid;
                for (int k = 0; k < 3; k++)
                {
                    float a = Mathf.Deg2Rad * (k * 60f);
                    if (Mathf.Abs(d.x * Mathf.Cos(a) + d.y * Mathf.Sin(a)) > apothem) return false;
                }
                return true;
            });

            // The three arms: up to the top corner, and down to the two lower ones. Those are the
            // edges where the three visible faces meet.
            foreach (float degrees in new[] { 90f, 210f, 330f })
            {
                float a = Mathf.Deg2Rad * degrees;
                Vector2 corner = mid + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
                icon.Capsule(mid, corner, line, -1f);
            }
            return SaveSprite(icon, "icon_cube");
        }

        // A BUCKET WITH WATER IN IT, which has to be distinguishable from the empty one at a glance
        // because the two weigh 1.8 and 12.0 - the biggest single fact in room2-7. Same silhouette as
        // `BucketIcon` with a waterline across it and the pail below it cut hollow, so the difference
        // is a FILL rather than a decoration.
        private static Sprite FullBucketIcon()
        {
            var icon = new IconCanvas(128);
            Vector2 mid = new Vector2(0.5f, 0.46f);

            icon.Bar(mid + new Vector2(0f, 0.145f), new Vector2(0.360f, 0.055f), 0f);
            icon.Bar(mid + new Vector2(0f, 0.055f), new Vector2(0.330f, 0.130f), 0f);
            icon.Bar(mid + new Vector2(0f, -0.070f), new Vector2(0.280f, 0.130f), 0f);
            icon.Bar(mid + new Vector2(0f, -0.165f), new Vector2(0.240f, 0.070f), 0f);

            icon.Bar(mid + new Vector2(-0.175f, 0.255f), new Vector2(0.032f, 0.150f), 0f);
            icon.Bar(mid + new Vector2(0.175f, 0.255f), new Vector2(0.032f, 0.150f), 0f);
            icon.Bar(mid + new Vector2(0f, 0.325f), new Vector2(0.360f, 0.032f), 0f);

            // THE WATERLINE, and then the pail below it hollowed out. Cutting the body away under the
            // line leaves a filled band at the top and an outline beneath: at icon size that reads as
            // "there is something in this one" far better than shading it darker would.
            icon.Bar(mid + new Vector2(0f, 0.078f), new Vector2(0.300f, 0.026f), 0f, -1f);
            icon.Bar(mid + new Vector2(0f, -0.085f), new Vector2(0.245f, 0.130f), 0f, -1f);
            return SaveSprite(icon, "icon_bucket_full");
        }

        // A QUESTION MARK, built rather than typed. The signs in this building are ICONS - a glyph
        // from the UI font among them would read as a different kind of object, and the one thing
        // this sign must not do is look like it is explaining itself in words.
        //
        // STROKED ALONG ITS OWN PATH rather than assembled from a ring with a bite taken out of it.
        // That is how the first version was built and it showed: the cut left a squared-off end where
        // the hook should taper into the stem, and the join read as a break. A question mark is one
        // continuous line, so it is drawn as one - an arc from the lower left of the bowl round the
        // top and down the right, then a short curve into the tail, then the dot.
        private static Sprite QuestionIcon()
        {
            var icon = new IconCanvas(128);
            Vector2 mid = new Vector2(0.5f, 0.5f);
            const float stroke = 0.062f;

            // THE BOWL: three-quarters of a circle, opening at the bottom left. 200 degrees round to
            // -20 leaves the tail pointing down and inward, which is where the stem picks it up.
            Vector2 bowl = mid + new Vector2(0f, 0.215f);
            icon.Arc(bowl, 0.175f, 200f, -20f, stroke);

            // THE STEM, from where the bowl ends, curving in to the centre line. Two segments rather
            // than one: a question mark's tail is not straight, and the bend is what stops it reading
            // as a lollipop.
            float endRad = Mathf.Deg2Rad * -20f;
            Vector2 bowlEnd = bowl + new Vector2(Mathf.Cos(endRad), Mathf.Sin(endRad)) * 0.175f;
            Vector2 waist = mid + new Vector2(0.055f, -0.020f);
            Vector2 tail = mid + new Vector2(0.005f, -0.145f);
            icon.Capsule(bowlEnd, waist, stroke);
            icon.Capsule(waist, tail, stroke);

            // THE DOT, clear of the tail by its own width - touching, it reads as an exclamation.
            icon.Disc(mid + new Vector2(0.005f, -0.310f), 0.078f);
            return SaveSprite(icon, "icon_question");
        }

        // PLUS AND EQUALS. Two bars and a cross - trivial, and here rather than as text for the same
        // reason the question mark is.
        private static Sprite PlusIcon()
        {
            var icon = new IconCanvas(128);
            Vector2 mid = new Vector2(0.5f, 0.5f);
            icon.Bar(mid, new Vector2(0.300f, 0.062f), 0f);
            icon.Bar(mid, new Vector2(0.062f, 0.300f), 0f);
            return SaveSprite(icon, "icon_plus");
        }

        private static Sprite EqualsIcon()
        {
            var icon = new IconCanvas(128);
            Vector2 mid = new Vector2(0.5f, 0.5f);
            icon.Bar(mid + new Vector2(0f, 0.115f), new Vector2(0.300f, 0.058f), 0f);
            icon.Bar(mid + new Vector2(0f, -0.115f), new Vector2(0.300f, 0.058f), 0f);
            return SaveSprite(icon, "icon_equals");
        }

        private static Sprite BucketIcon()
        {
            var icon = new IconCanvas(128);
            Vector2 mid = new Vector2(0.5f, 0.46f);

            // The body, as a stack of narrowing bars - the taper is what makes it a bucket rather than
            // a tin, and it is the only part of the silhouette that survives at 58px.
            icon.Bar(mid + new Vector2(0f, 0.145f), new Vector2(0.360f, 0.055f), 0f);
            icon.Bar(mid + new Vector2(0f, 0.055f), new Vector2(0.330f, 0.130f), 0f);
            icon.Bar(mid + new Vector2(0f, -0.070f), new Vector2(0.280f, 0.130f), 0f);
            icon.Bar(mid + new Vector2(0f, -0.165f), new Vector2(0.240f, 0.070f), 0f);

            // The handle, an arc built from two uprights and a span across the top.
            icon.Bar(mid + new Vector2(-0.175f, 0.255f), new Vector2(0.032f, 0.150f), 0f);
            icon.Bar(mid + new Vector2(0.175f, 0.255f), new Vector2(0.032f, 0.150f), 0f);
            icon.Bar(mid + new Vector2(0f, 0.325f), new Vector2(0.360f, 0.032f), 0f);
            return SaveSprite(icon, "icon_bucket");
        }

        private static Sprite PinIcon()
        {
            var icon = new IconCanvas(128);
            Vector2 along = new Vector2(0.7071f, 0.7071f);
            Vector2 mid = new Vector2(0.5f, 0.5f);
            // Everything is placed by distance along that one diagonal, so the parts stay in line
            // however they are resized.
            System.Func<float, Vector2> at = t => mid + along * t;

            // The needle is deliberately fatter than the real pin is. This is drawn at 128px and
            // displayed at 58: a truly slim needle lands on one screen pixel and disappears.
            icon.Bar(at(0.235f), new Vector2(0.030f, 0.185f), -45f);
            icon.Bar(at(-0.15f), new Vector2(0.058f, 0.19f), -45f);   // grip
            icon.Disc(at(-0.34f), 0.058f);                            // rounded butt
            icon.Disc(at(0.045f), 0.045f);                            // ferrule, where the two meet
            return SaveSprite(icon, "icon_pin");
        }

        // A MIRROR: the round pane, its stand, and two glints across the glass.
        //
        // The glints are not decoration - a ring on a stem is a hand mirror, a lollipop or a road
        // sign, and every one of those is a plausible thing to be carrying in this building. Two
        // parallel bars across the inside is the one mark that says the circle is REFLECTIVE, and it
        // is the same mark a mirror gets in every pictogram set for that reason.
        //
        // Both bars stay inside the ring's inner radius: they are placed by offsetting perpendicular
        // to their own direction, so the pair stays parallel and centred however the angle is retuned.
        private static Sprite MirrorIcon()
        {
            var icon = new IconCanvas(128);
            Vector2 mid = new Vector2(0.5f, 0.585f);

            icon.Ring(mid, 0.275f, 0.212f);

            const float glintDegrees = -35f;
            float rad = -glintDegrees * Mathf.Deg2Rad;
            Vector2 perp = new Vector2(Mathf.Cos(rad), -Mathf.Sin(rad));

            icon.Bar(mid - perp * 0.075f, new Vector2(0.024f, 0.145f), glintDegrees);
            icon.Bar(mid + perp * 0.075f, new Vector2(0.024f, 0.085f), glintDegrees);

            // The stem runs up into the ring rather than stopping at it - butted against the outside
            // it reads as a circle balanced on a stick.
            icon.Bar(new Vector2(0.5f, 0.235f), new Vector2(0.026f, 0.085f));
            icon.Bar(new Vector2(0.5f, 0.155f), new Vector2(0.125f, 0.028f));
            return SaveSprite(icon, "icon_mirror");
        }

        // ---- the figure pictograms: one body, six poses --------------------------------------------
        //
        // The calibration wall used to caption its controls in words (MOVE, JUMP, SPRINT...). These
        // replace them, which is the same move the Room2 sign makes and for the same reason: the game
        // has no text elsewhere and a player who cannot read English should still be able to start it.
        //
        // ONE body plan, six poses, and that is the point rather than tidiness. Six figures each drawn
        // to look right on its own is how a set of pictograms ends up looking like six pictograms from
        // six different sets - the same head size, limb length and thickness across all of them is what
        // makes them read as one person doing different things.
        private const float FigureLimb = 0.075f;    // limb thickness, fat enough to survive 128px
        private const float FigureHead = 0.088f;    // head radius
        private const float FigureThigh = 0.20f;
        private const float FigureShin = 0.19f;
        private const float FigureUpperArm = 0.16f;
        private const float FigureForearm = 0.15f;

        // Draws a limb segment from `from` in `direction` and returns its far end, so segments chain
        // into a bent limb. Angles are given in degrees CLOCKWISE FROM STRAIGHT DOWN, because every
        // limb here hangs off a joint and down is the rest pose.
        private static Vector2 FigureBone(IconCanvas icon, Vector2 from, float degrees, float length)
        {
            float rad = degrees * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Sin(rad), -Mathf.Cos(rad));
            Vector2 to = from + dir * length;
            // Bar takes half-extents and a rotation; the sign convention is the pin icon's.
            icon.Bar((from + to) * 0.5f, new Vector2(FigureLimb * 0.5f, length * 0.5f),
                     -Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg);
            // Joint discs at every hinge. Without them a bent limb shows the corner of one bar poking
            // past the other and the elbow reads as a break.
            icon.Disc(to, FigureLimb * 0.5f);
            return to;
        }

        // Head, spine and a joint at each end. Returns the shoulder and hip so the caller can hang
        // limbs off them, which is the only thing any of the poses differ by.
        private static (Vector2 shoulder, Vector2 hip) FigureTorso(IconCanvas icon, Vector2 hip,
                                                                   float spineLength, float leanDegrees)
        {
            float rad = leanDegrees * Mathf.Deg2Rad;
            Vector2 up = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
            Vector2 shoulder = hip + up * spineLength;

            icon.Bar((hip + shoulder) * 0.5f, new Vector2(FigureLimb * 0.55f, spineLength * 0.5f),
                     -leanDegrees);
            icon.Disc(hip, FigureLimb * 0.5f);
            icon.Disc(shoulder, FigureLimb * 0.5f);
            // The head sits off the shoulder along the same lean, so a leaning figure's head leads.
            icon.Disc(shoulder + up * (FigureHead + 0.035f), FigureHead);
            return (shoulder, hip);
        }

        // MOVE. A mid-stride walk: one leg forward and planted, one trailing, arms opposing. Upright,
        // because upright against the run's lean is what tells the two apart at a glance - the stride
        // widths alone are too similar at this size.
        private static Sprite FigureWalkIcon()
        {
            var icon = new IconCanvas(128);
            var (shoulder, hip) = FigureTorso(icon, new Vector2(0.47f, 0.44f), 0.26f, 2f);
            FigureBone(icon, FigureBone(icon, hip, 26f, FigureThigh), 12f, FigureShin);
            FigureBone(icon, FigureBone(icon, hip, -22f, FigureThigh), -6f, FigureShin);
            FigureBone(icon, FigureBone(icon, shoulder, -20f, FigureUpperArm), -44f, FigureForearm);
            FigureBone(icon, FigureBone(icon, shoulder, 18f, FigureUpperArm), 40f, FigureForearm);
            return SaveSprite(icon, "icon_figure_walk");
        }

        // SPRINT. The same body leaning into it, with a longer stride and arms driving harder. The
        // LEAN is what reads as speed; a running figure drawn upright reads as a wider walk.
        private static Sprite FigureRunIcon()
        {
            var icon = new IconCanvas(128);
            var (shoulder, hip) = FigureTorso(icon, new Vector2(0.44f, 0.45f), 0.26f, 20f);
            FigureBone(icon, FigureBone(icon, hip, 48f, FigureThigh), 20f, FigureShin);
            FigureBone(icon, FigureBone(icon, hip, -34f, FigureThigh), -74f, FigureShin);
            FigureBone(icon, FigureBone(icon, shoulder, -52f, FigureUpperArm), -104f, FigureForearm);
            FigureBone(icon, FigureBone(icon, shoulder, 46f, FigureUpperArm), 96f, FigureForearm);
            return SaveSprite(icon, "icon_figure_run");
        }

        // CROUCH. Knees folded hard, hip dropped, spine tipped forward to balance over the feet. The
        // dropped hip is the whole silhouette: a figure with bent knees at standing height reads as
        // someone about to jump.
        private static Sprite FigureCrouchIcon()
        {
            var icon = new IconCanvas(128);
            var (shoulder, hip) = FigureTorso(icon, new Vector2(0.47f, 0.31f), 0.24f, 16f);
            FigureBone(icon, FigureBone(icon, hip, 58f, FigureThigh * 0.92f), -30f, FigureShin * 0.92f);
            FigureBone(icon, FigureBone(icon, hip, 30f, FigureThigh * 0.92f), -52f, FigureShin * 0.92f);
            FigureBone(icon, FigureBone(icon, shoulder, 30f, FigureUpperArm), 74f, FigureForearm * 0.9f);
            return SaveSprite(icon, "icon_figure_crouch");
        }

        // JUMP. Both feet off the floor and tucked the same way, arms up. Symmetry is what separates it
        // from the walk - a stride says one foot is down, and two matching legs say neither is.
        private static Sprite FigureJumpIcon()
        {
            var icon = new IconCanvas(128);
            var (shoulder, hip) = FigureTorso(icon, new Vector2(0.5f, 0.46f), 0.25f, 0f);
            FigureBone(icon, FigureBone(icon, hip, 30f, FigureThigh * 0.85f), 66f, FigureShin * 0.85f);
            FigureBone(icon, FigureBone(icon, hip, -30f, FigureThigh * 0.85f), -66f, FigureShin * 0.85f);
            FigureBone(icon, FigureBone(icon, shoulder, 156f, FigureUpperArm), 168f, FigureForearm * 0.85f);
            FigureBone(icon, FigureBone(icon, shoulder, -156f, FigureUpperArm), -168f, FigureForearm * 0.85f);
            // The floor it has left. Without it the pose is just a figure with odd legs; with it there
            // is a gap under the feet, and the gap is the jump.
            icon.Bar(new Vector2(0.5f, 0.055f), new Vector2(0.30f, 0.022f));
            return SaveSprite(icon, "icon_figure_jump");
        }

        // INTERACT. A figure with one arm out to a panel on the wall - the panel is what makes it
        // "press this" rather than "wave". Drawn as one of the room's own wall cells, because that is
        // literally what E is pressed at.
        private static Sprite FigurePressIcon()
        {
            var icon = new IconCanvas(128);
            var (shoulder, hip) = FigureTorso(icon, new Vector2(0.36f, 0.42f), 0.26f, 4f);
            FigureBone(icon, FigureBone(icon, hip, 12f, FigureThigh), 4f, FigureShin);
            FigureBone(icon, FigureBone(icon, hip, -14f, FigureThigh), -4f, FigureShin);
            // The reaching arm, straight out and level, ending at the panel.
            FigureBone(icon, FigureBone(icon, shoulder, 88f, FigureUpperArm), 92f, FigureForearm);
            FigureBone(icon, FigureBone(icon, shoulder, -14f, FigureUpperArm), -8f, FigureForearm);

            // Panel, and a groove down its left edge so it reads as set into a wall.
            icon.Bar(new Vector2(0.85f, 0.60f), new Vector2(0.075f, 0.135f));
            icon.Bar(new Vector2(0.745f, 0.60f), new Vector2(0.012f, 0.155f), 0f, -1f);
            return SaveSprite(icon, "icon_figure_press");
        }

        // LOOK AROUND. A head seen FROM ABOVE with an arc over it - the only one of the six not drawn
        // from the side, because a head turning is invisible in profile. The arc is an annulus segment
        // with a head on each end, which is a turn in both directions and therefore a look rather than
        // a glance.
        private static Sprite FigureLookIcon()
        {
            var icon = new IconCanvas(128);
            Vector2 centre = new Vector2(0.5f, 0.35f);

            icon.Disc(centre, 0.15f);
            // Nose, so the head has a facing and the arc has something to be turning.
            icon.Bar(new Vector2(centre.x, centre.y + 0.175f), new Vector2(0.035f, 0.045f));

            // The sweep: a ring, kept to its upper half and to the outside of the head.
            icon.Shape(p =>
            {
                float r = (p - centre).magnitude;
                return r > 0.245f && r < 0.315f && p.y > centre.y + 0.02f;
            });
            // Arrowheads on both ends of that arc. Placed on the ring at +-58 degrees from straight up.
            for (int s = -1; s <= 1; s += 2)
            {
                float rad = 58f * s * Mathf.Deg2Rad;
                Vector2 tip = centre + new Vector2(Mathf.Sin(rad), Mathf.Cos(rad)) * 0.28f;
                icon.Shape(p => (p - tip).magnitude < 0.075f && Vector2.Dot(p - tip, new Vector2(Mathf.Cos(rad) * s, -Mathf.Sin(rad) * s)) > 0f);
            }
            return SaveSprite(icon, "icon_figure_look");
        }

        // TAB. Not one of the six body poses - switching what is in the hand moves no limb a figure
        // could show - so this is the one calibration-wall control captioned with an object glyph
        // instead: two arrows chasing each other round a ring, the ordinary mark for "cycle through".
        // Built the same way the look icon's turn arc is, a ring cut to an arc with a half-disc
        // arrowhead at its leading end, just carried all the way round instead of stopping at a head.
        private static Sprite CycleIcon()
        {
            var icon = new IconCanvas(128);
            Vector2 centre = new Vector2(0.5f, 0.5f);
            const float outer = 0.36f, inner = 0.275f;
            const float mid = (outer + inner) * 0.5f;

            // Two arcs, opposite halves of the ring, each a little short of a true half so its
            // arrowhead has clear ring under it rather than overlapping the far arc.
            float[] starts = { 15f, 195f };
            float[] ends = { 165f, 345f };

            for (int i = 0; i < 2; i++)
            {
                float from = starts[i], to = ends[i];
                icon.Shape(p =>
                {
                    Vector2 d = p - centre;
                    float r = d.magnitude;
                    if (r < inner || r > outer) return false;
                    float deg = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
                    if (deg < 0f) deg += 360f;
                    return deg >= from && deg <= to;
                });

                // The arrowhead sits at the arc's leading end - `to`, since the sweep runs clockwise -
                // a half disc cut along the tangent so it points the way the ring is turning.
                float tipRad = to * Mathf.Deg2Rad;
                Vector2 tip = centre + new Vector2(Mathf.Sin(tipRad), Mathf.Cos(tipRad)) * mid;
                float tangentRad = (to + 90f) * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Sin(tangentRad), Mathf.Cos(tangentRad));
                icon.Shape(p => (p - tip).magnitude < 0.085f && Vector2.Dot(p - tip, dir) > 0f);
            }
            return SaveSprite(icon, "icon_cycle");
        }

        // A balloon: egg-shaped body, knot, short string. Wider at the top than a circle and narrowed
        // to the knot, because a plain disc with a string under it reads as a lollipop.
        private static Sprite BalloonIcon()
        {
            var icon = new IconCanvas(128);
            Vector2 centre = new Vector2(0.5f, 0.60f);
            icon.Shape(p =>
            {
                float dx = (p.x - centre.x) / 0.285f;
                // Narrower below the middle: that taper is the whole silhouette.
                float ry = p.y < centre.y ? 0.30f : 0.255f;
                float dy = (p.y - centre.y) / ry;
                return dx * dx + dy * dy <= 1f;
            });
            icon.Bar(new Vector2(0.5f, 0.285f), new Vector2(0.036f, 0.030f));   // knot
            icon.Bar(new Vector2(0.5f, 0.175f), new Vector2(0.014f, 0.085f));   // string
            return SaveSprite(icon, "icon_balloon");
        }

        // The same balloon a moment later: a scrap of skin still on the knot, and shards going out.
        // The shards are what carries it - a torn remnant on its own reads as a damaged balloon
        // rather than one that has just gone.
        private static Sprite BalloonBurstIcon()
        {
            var icon = new IconCanvas(128);
            Vector2 centre = new Vector2(0.5f, 0.56f);

            // Eight shards at uneven lengths. Even ones read as a sun, which is a different sign.
            float[] degrees = { 12f, 52f, 88f, 126f, 168f, 212f, 258f, 312f };
            float[] lengths = { 0.20f, 0.14f, 0.22f, 0.15f, 0.19f, 0.13f, 0.17f, 0.15f };
            for (int i = 0; i < degrees.Length; i++)
            {
                float rad = degrees[i] * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
                // Placed at half its own length out from the gap, so the shards do not meet in the
                // middle - the hole is what says the balloon is gone.
                Vector2 at = centre + dir * (0.105f + lengths[i] * 0.5f);
                icon.Bar(at, new Vector2(0.026f, lengths[i] * 0.5f), -degrees[i] + 90f);
            }

            icon.Bar(new Vector2(0.5f, 0.285f), new Vector2(0.036f, 0.030f));   // knot, still there
            icon.Bar(new Vector2(0.5f, 0.175f), new Vector2(0.014f, 0.085f));   // string
            return SaveSprite(icon, "icon_balloon_burst");
        }

        // A plain right-pointing arrow, for the pictogram rows. Shaft plus a solid head built from a
        // half-plane test rather than three bars, so the point cannot come out blunt at 128px.
        private static Sprite ArrowRightIcon()
        {
            var icon = new IconCanvas(128);
            icon.Bar(new Vector2(0.40f, 0.5f), new Vector2(0.24f, 0.055f));
            icon.Shape(p =>
            {
                if (p.x < 0.60f || p.x > 0.90f) return false;
                // Half-width shrinks to nothing at the tip.
                float half = Mathf.Lerp(0.20f, 0f, (p.x - 0.60f) / 0.30f);
                return Mathf.Abs(p.y - 0.5f) <= half;
            });
            return SaveSprite(icon, "icon_arrow_right");
        }

        // The key's silhouette at HUD size: ring bow, shaft, two teeth off one side only. A
        // symmetrical bit would read as a cross. Drawn rather than rendered from gold_key.glb,
        // because at 58px the model's bit resolves to a smudge - the same call the pin icon makes.
        private static Sprite KeyIcon()
        {
            var icon = new IconCanvas(128);
            icon.Ring(new Vector2(0.5f, 0.72f), 0.20f, 0.095f);
            icon.Bar(new Vector2(0.5f, 0.35f), new Vector2(0.045f, 0.20f));
            icon.Bar(new Vector2(0.60f, 0.26f), new Vector2(0.07f, 0.042f));
            icon.Bar(new Vector2(0.60f, 0.155f), new Vector2(0.07f, 0.042f));
            return SaveSprite(icon, "icon_key");
        }

        // The grey disc both control hints sit on.
        private static Sprite HintDiscSprite()
        {
            var icon = new IconCanvas(128);
            icon.Disc(new Vector2(0.5f, 0.5f), 0.48f);
            return SaveSprite(icon, "icon_hint_disc");
        }

        // A mouse seen from above, outlined, with the left button filled - which is the entire
        // message. Built from one capsule distance function so the outline, the hollow and the
        // button are all the same shape at three insets, and none of them can drift apart.
        private static Sprite MouseLeftIcon()
        {
            var icon = new IconCanvas(128);
            const float radius = 0.215f;
            const float topY = 0.655f, bottomY = 0.345f;

            System.Func<Vector2, float> toSpine = p =>
                (p - new Vector2(0.5f, Mathf.Clamp(p.y, bottomY, topY))).magnitude;

            icon.Shape(p => toSpine(p) <= radius);
            icon.Shape(p => toSpine(p) <= radius - 0.042f, -1f);
            icon.Shape(p => toSpine(p) <= radius - 0.072f && p.x < 0.484f && p.y > 0.60f);
            return SaveSprite(icon, "icon_mouse_left");
        }

        // The same mouse with no button filled - the calibration page's "look around" row, where
        // highlighting a button would say "click", which is the one thing that row does not mean.
        // The two seams are what keep it from reading as a plain capsule.
        private static Sprite MouseIcon()
        {
            var icon = new IconCanvas(128);
            const float radius = 0.215f;
            const float topY = 0.655f, bottomY = 0.345f;

            System.Func<Vector2, float> toSpine = p =>
                (p - new Vector2(0.5f, Mathf.Clamp(p.y, bottomY, topY))).magnitude;

            icon.Shape(p => toSpine(p) <= radius);
            icon.Shape(p => toSpine(p) <= radius - 0.042f, -1f);
            const float hollow = radius - 0.042f;
            icon.Shape(p => toSpine(p) <= hollow && Mathf.Abs(p.y - 0.605f) < 0.017f);
            icon.Shape(p => toSpine(p) <= hollow && p.y > 0.605f && Mathf.Abs(p.x - 0.5f) < 0.017f);
            return SaveSprite(icon, "icon_mouse");
        }

        // A speaker with sound coming off it - "turn your volume up", over the calibration room's
        // BEGIN button. The PA and every room's audio cues are half of what the facility tells the
        // player, and this is the last screen before the loop's clock starts, so it is the one place
        // a muted or silent tab can still be caught rather than discovered mid-iteration.
        private static Sprite VolumeIcon()
        {
            var icon = new IconCanvas(128);

            // The body: a plain box, standing in for the speaker cabinet.
            icon.Bar(new Vector2(0.28f, 0.5f), new Vector2(0.07f, 0.11f));
            // The cone, widening away from the box - the same tapered half-plane test the right
            // arrow's head uses, just wider so it reads as a speaker rather than an arrow.
            icon.Shape(p =>
            {
                if (p.x < 0.28f || p.x > 0.46f) return false;
                float half = Mathf.Lerp(0.11f, 0.22f, (p.x - 0.28f) / 0.18f);
                return Mathf.Abs(p.y - 0.5f) <= half;
            });

            // Three sound waves fanning out to the right, each a band of an arc rather than a full
            // ring so they read as coming FROM the cone rather than surrounding it.
            Vector2 mouth = new Vector2(0.46f, 0.5f);
            for (int i = 0; i < 3; i++)
            {
                float r = 0.13f + i * 0.085f;
                const float thickness = 0.026f;
                icon.Shape(p =>
                {
                    Vector2 d = p - mouth;
                    float dist = d.magnitude;
                    if (dist < r - thickness || dist > r + thickness) return false;
                    float deg = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                    return Mathf.Abs(deg) <= 55f;
                });
            }
            return SaveSprite(icon, "icon_volume");
        }
    }
}
