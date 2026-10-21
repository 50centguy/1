using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;

namespace BorderRepair.RepairProps.Cosmetics
{
    public static class CosmeticCatalog
    {
        const int MaxImages = 16;
        const int MaxDimension = 2048;
        const int MaxLifetimeImports = 32;
        const long MaxImportedPixels = 16L * 1024 * 1024;
        const long MaxBytes = 8L * 1024 * 1024;
        static readonly List<Texture2D> images = new List<Texture2D>();
        static readonly IReadOnlyList<Texture2D> view = images.AsReadOnly();
        static readonly List<Texture2D> builtins = new List<Texture2D>();
        // Retired versions remain owned until Dispose: undo history may still reference them.
        static readonly Dictionary<string, Texture2D> imported =
            new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        static Texture2D selected;
        static bool initialized;
        static long importedPixels;
        static string feedback = "";

        public static IReadOnlyList<Texture2D> Images { get { EnsureLoaded(); return view; } }
        public static string Folder { get { return Path.Combine(Application.persistentDataPath, "CosmeticImages"); } }
        public static string Feedback { get { return feedback; } }
        public static Texture2D Selected
        {
            get { EnsureLoaded(); return selected; }
            set { EnsureLoaded(); selected = value; }
        }

        static void EnsureLoaded()
        {
            if (!initialized) Reload();
        }

        public static void Reload()
        {
            initialized = true;
            var previous = selected;
            if (builtins.Count == 0)
            {
                builtins.Add(CreatePattern(0, "警示补丁"));
                builtins.Add(CreatePattern(1, "几何标记"));
                builtins.Add(CreatePattern(2, "金属面板"));
            }

            var next = new List<Texture2D>(builtins);
            if (previous != null && !next.Contains(previous) && imported.ContainsValue(previous)) next.Add(previous);
            var errors = new List<string>();
            int skipped = 0;
            bool scanned = false;
            try
            {
                Directory.CreateDirectory(Folder);
                var paths = new List<string>();
                foreach (var path in Directory.EnumerateFiles(Folder, "*", SearchOption.TopDirectoryOnly))
                {
                    var extension = Path.GetExtension(path);
                    if (string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase)) paths.Add(path);
                }
                paths.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (var path in paths)
                {
                    if (next.Count >= MaxImages) { skipped++; continue; }
                    Texture2D texture = null;
                    try
                    {
                        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                            throw new InvalidDataException("不支持链接文件");
                        int width, height;
                        byte[] bytes;
                        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                        {
                            if (stream.Length == 0 || stream.Length > MaxBytes)
                                throw new InvalidDataException("文件须为 1 字节至 8 MB");
                            ReadDimensions(stream, Path.GetExtension(path), out width, out height);
                            stream.Position = 0;
                            bytes = new byte[(int)stream.Length];
                            int offset = 0;
                            while (offset < bytes.Length)
                            {
                                int count = stream.Read(bytes, offset, bytes.Length - offset);
                                if (count == 0) throw new InvalidDataException("文件读取不完整");
                                offset += count;
                            }
                        }
                        string key;
                        using (var hash = SHA256.Create())
                            key = path + "\n" + Convert.ToBase64String(hash.ComputeHash(bytes));
                        if (imported.TryGetValue(key, out texture))
                        {
                            if (!next.Contains(texture)) next.Add(texture);
                            continue;
                        }
                        if (imported.Count >= MaxLifetimeImports)
                            throw new InvalidDataException("本次会话已达 32 张导入上限");
                        long pixels = (long)width * height;
                        if (pixels > MaxImportedPixels - importedPixels)
                            throw new InvalidDataException("本次会话图片像素预算已满（64 MB）");
                        texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                        if (!ImageConversion.LoadImage(texture, bytes, true))
                            throw new InvalidDataException("图片解码失败");
                        if (texture.width != width || texture.height != height)
                            throw new InvalidDataException("图片尺寸与文件头不一致");
                        texture.name = Path.GetFileNameWithoutExtension(path);
                        texture.wrapMode = TextureWrapMode.Repeat;
                        texture.filterMode = FilterMode.Bilinear;
                        texture.hideFlags = HideFlags.DontSave;
                        next.Add(texture);
                        imported.Add(key, texture);
                        importedPixels += pixels;
                    }
                    catch (Exception ex) when (IsFileError(ex))
                    {
                        if (texture != null && !imported.ContainsValue(texture)) Release(texture);
                        errors.Add(Path.GetFileName(path) + "：" + ex.Message);
                    }
                }
                scanned = true;
            }
            catch (Exception ex) when (IsFileError(ex))
            {
                errors.Add("图片目录无法读取：" + ex.Message);
            }

            if (!scanned)
            {
                // Keep the last usable catalog when the directory is temporarily inaccessible.
                foreach (var image in images)
                    if (!next.Contains(image) && next.Count < MaxImages && image != null) next.Add(image);
            }
            images.Clear();
            images.AddRange(next);
            selected = previous != null && images.Contains(previous) ? previous : images[0];
            feedback = "图案 " + images.Count + "/" + MaxImages + "；会话导入 " + imported.Count + "/32";
            if (skipped > 0) feedback += "；超出上限 " + skipped + " 张";
            if (errors.Count > 0) feedback += "\n" + string.Join("\n", errors.ToArray());
        }

        public static void OpenFolder()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                Application.OpenURL(new Uri(Folder).AbsoluteUri);
                feedback = "已打开图片目录";
            }
            catch (Exception ex) when (IsFileError(ex) || ex is UriFormatException)
            {
                feedback = "图片目录打开失败：" + ex.Message;
            }
        }

        public static void Dispose()
        {
            foreach (var texture in builtins) Release(texture);
            foreach (var texture in imported.Values) Release(texture);
            builtins.Clear();
            imported.Clear();
            importedPixels = 0;
            images.Clear();
            selected = null;
            // Reading Images after disposal must not allocate another catalog.
            initialized = true;
            feedback = "图案已释放";
        }

        static bool IsFileError(Exception ex)
        {
            return ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException ||
                ex is NotSupportedException || ex is System.Security.SecurityException || ex is UnityException;
        }

        static void Release(Texture2D texture)
        {
            if (texture == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(texture);
            else UnityEngine.Object.DestroyImmediate(texture);
        }

        static int Byte(Stream stream)
        {
            int value = stream.ReadByte();
            if (value < 0) throw new InvalidDataException("文件头不完整");
            return value;
        }

        static int UInt16(Stream stream) { return (Byte(stream) << 8) | Byte(stream); }

        static uint UInt32(Stream stream)
        {
            return ((uint)Byte(stream) << 24) | ((uint)Byte(stream) << 16) |
                ((uint)Byte(stream) << 8) | (uint)Byte(stream);
        }

        static void ValidateSize(uint width, uint height)
        {
            if (width == 0 || height == 0 || width > MaxDimension || height > MaxDimension)
                throw new InvalidDataException("图片宽高须为 1 至 2048 像素");
        }

        static void ReadDimensions(Stream stream, string extension, out int width, out int height)
        {
            width = height = 0;
            if (string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase))
            {
                byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
                foreach (var value in signature)
                    if (Byte(stream) != value) throw new InvalidDataException("PNG 文件头无效");
                if (UInt32(stream) != 13 || UInt32(stream) != 0x49484452)
                    throw new InvalidDataException("PNG 缺少 IHDR");
                uint w = UInt32(stream), h = UInt32(stream);
                ValidateSize(w, h);
                // Complete IHDR data and CRC must exist before handing bytes to the decoder.
                for (int i = 0; i < 9; i++) Byte(stream);
                width = (int)w;
                height = (int)h;
                return;
            }

            if (Byte(stream) != 0xff || Byte(stream) != 0xd8)
                throw new InvalidDataException("JPEG 文件头无效");
            while (stream.Position < stream.Length)
            {
                if (Byte(stream) != 0xff) throw new InvalidDataException("JPEG 标记无效");
                int marker;
                do { marker = Byte(stream); } while (marker == 0xff);
                if (marker == 0x01) continue;
                if (marker == 0x00 || marker == 0xd8 || marker == 0xd9 || (marker >= 0xd0 && marker <= 0xd7))
                    throw new InvalidDataException("JPEG 标记顺序无效");
                int length = UInt16(stream);
                long end = stream.Position + length - 2;
                if (length < 2 || end > stream.Length) throw new InvalidDataException("JPEG 段不完整");
                bool frame = marker >= 0xc0 && marker <= 0xcf && marker != 0xc4 && marker != 0xc8 && marker != 0xcc;
                if (frame)
                {
                    if (width != 0 || length < 8) throw new InvalidDataException("JPEG 帧头无效");
                    if (Byte(stream) != 8) throw new InvalidDataException("仅支持 8 位 JPEG");
                    uint h = (uint)UInt16(stream), w = (uint)UInt16(stream);
                    ValidateSize(w, h);
                    int components = Byte(stream);
                    if (components == 0 || length != 8 + 3 * components)
                        throw new InvalidDataException("JPEG 颜色分量无效");
                    width = (int)w;
                    height = (int)h;
                }
                if (marker == 0xda)
                {
                    if (width == 0 || length < 6) throw new InvalidDataException("JPEG 缺少有效尺寸");
                    return;
                }
                stream.Position = end;
            }
            throw new InvalidDataException("JPEG 缺少扫描数据");
        }

        static Texture2D CreatePattern(int pattern, string name)
        {
            const int size = 128;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    Color32 color;
                    if (pattern == 0)
                    {
                        bool edge = x < 6 || y < 6 || x >= size - 6 || y >= size - 6;
                        color = edge || ((x + y) / 16) % 2 == 0
                            ? new Color32(35, 39, 37, 255) : new Color32(238, 192, 48, 255);
                    }
                    else if (pattern == 1)
                    {
                        int diamond = Math.Abs(x - 64) + Math.Abs(y - 64);
                        color = diamond < 48 && diamond > 29
                            ? new Color32(227, 71, 64, 255) : new Color32(0, 0, 0, 0);
                        if (Math.Abs(x - 64) < 5 && Math.Abs(y - 64) < 18)
                            color = new Color32(240, 244, 238, 255);
                    }
                    else
                    {
                        int grain = ((x * 17 + y * 7) % 9) - 4;
                        byte shade = (byte)(150 + grain + (y % 16 == 0 ? -18 : 0));
                        color = new Color32(shade, (byte)(shade + 6), (byte)(shade + 8), 255);
                        bool bolt = (x % 64 - 8) * (x % 64 - 8) + (y % 64 - 8) * (y % 64 - 8) < 12;
                        if (bolt) color = new Color32(62, 68, 69, 255);
                    }
                    pixels[y * size + x] = color;
                }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.name = name;
            texture.hideFlags = HideFlags.DontSave;
            texture.wrapMode = TextureWrapMode.Repeat;
            texture.filterMode = FilterMode.Bilinear;
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }
    }
}
