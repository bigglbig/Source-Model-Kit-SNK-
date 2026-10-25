// Декодер VTF (текстуры Source) и разбор VMT (материалы).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace SourceSDKHub
{
    internal class VtfImage
    {
        public int Width, Height;
        public byte[] Rgba;       // Width*Height*4, строки сверху вниз (как в файле)
        public bool HasAlpha;
    }

    internal static class VtfLoader
    {
        private static int ImageSize(int fmt, int w, int h)
        {
            switch (fmt)
            {
                case 13: case 20: return Math.Max(1, (w + 3) / 4) * Math.Max(1, (h + 3) / 4) * 8;
                case 14: case 15: return Math.Max(1, (w + 3) / 4) * Math.Max(1, (h + 3) / 4) * 16;
                case 0: case 1: case 11: case 12: case 16: case 23: case 26: return w * h * 4;
                case 2: case 3: case 9: case 10: return w * h * 3;
                case 4: case 6: case 17: case 18: case 19: case 21: case 22: return w * h * 2;
                case 5: case 7: case 8: return w * h;
                case 24: case 25: return w * h * 8;
            }
            return -1;
        }

        // maxDim - если текстура больше, берётся уменьшенный mip-уровень.
        public static VtfImage Load(string path, int maxDim)
        {
            byte[] d = ReadShared(path);
            if (d.Length < 64 || d[0] != 'V' || d[1] != 'T' || d[2] != 'F') throw new InvalidDataException(Loc.T("Не VTF-файл"));
            int vmaj = BitConverter.ToInt32(d, 4), vmin = BitConverter.ToInt32(d, 8);
            int hdr = BitConverter.ToInt32(d, 12);
            int w = BitConverter.ToUInt16(d, 16), h = BitConverter.ToUInt16(d, 18);
            int flags = BitConverter.ToInt32(d, 20);
            int frames = Math.Max(1, (int)BitConverter.ToUInt16(d, 24));
            int fmt = BitConverter.ToInt32(d, 52);
            int mips = Math.Max(1, (int)d[56]);
            int lowFmt = BitConverter.ToInt32(d, 57);
            int lowW = d[61], lowH = d[62];
            int faces = (flags & 0x4000) != 0 ? 6 : 1;
            if (ImageSize(fmt, 1, 1) < 0) throw new InvalidDataException(Loc.T("Формат VTF ") + fmt + Loc.T(" не поддерживается"));

            int dataStart = hdr;
            bool resources = vmaj > 7 || (vmaj == 7 && vmin >= 3);
            if (resources)
            {
                int n = BitConverter.ToInt32(d, 68);
                dataStart = -1;
                for (int i = 0; i < n && 80 + i * 8 + 8 <= d.Length; i++)
                {
                    int e = 80 + i * 8;
                    if (d[e] == 0x30 && d[e + 1] == 0 && d[e + 2] == 0) { dataStart = BitConverter.ToInt32(d, e + 4); break; }
                }
                if (dataStart < 0) throw new InvalidDataException(Loc.T("В VTF нет данных изображения"));
            }
            else if (lowFmt >= 0 && lowW > 0 && lowH > 0) dataStart += ImageSize(lowFmt, lowW, lowH);

            // выбираем mip-уровень
            int level = 0;
            while (level < mips - 1 && Math.Max(Math.Max(1, w >> level), Math.Max(1, h >> level)) > maxDim) level++;

            long off = dataStart;
            for (int m = mips - 1; m > level; m--)
                off += (long)ImageSize(fmt, Math.Max(1, w >> m), Math.Max(1, h >> m)) * frames * faces;
            int lw = Math.Max(1, w >> level), lh = Math.Max(1, h >> level);
            int size = ImageSize(fmt, lw, lh);
            if (off + size > d.Length) throw new InvalidDataException(Loc.T("VTF повреждён (данные обрезаны)"));

            VtfImage img = new VtfImage();
            img.Width = lw; img.Height = lh;
            img.Rgba = new byte[lw * lh * 4];
            Decode(d, (int)off, fmt, lw, lh, img.Rgba);
            for (int i = 3; i < img.Rgba.Length; i += 4) if (img.Rgba[i] != 255) { img.HasAlpha = true; break; }
            return img;
        }

        // Файл может быть ещё открыт редактором - читаем с разрешением совместного доступа.
        public static byte[] ReadShared(string path)
        {
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                byte[] b = new byte[fs.Length];
                int read = 0;
                while (read < b.Length) { int r = fs.Read(b, read, b.Length - read); if (r <= 0) break; read += r; }
                return b;
            }
        }

        private static void Put(byte[] o, int i, int r, int g, int b, int a)
        {
            o[i] = (byte)r; o[i + 1] = (byte)g; o[i + 2] = (byte)b; o[i + 3] = (byte)a;
        }

        private static void Decode(byte[] s, int p, int fmt, int w, int h, byte[] o)
        {
            int n = w * h;
            switch (fmt)
            {
                case 13: case 20: case 14: case 15: DecodeDxt(s, p, fmt, w, h, o); return;
                case 0: for (int i = 0; i < n; i++) Put(o, i * 4, s[p + i * 4], s[p + i * 4 + 1], s[p + i * 4 + 2], s[p + i * 4 + 3]); return;
                case 1: for (int i = 0; i < n; i++) Put(o, i * 4, s[p + i * 4 + 3], s[p + i * 4 + 2], s[p + i * 4 + 1], s[p + i * 4]); return;
                case 2: for (int i = 0; i < n; i++) Put(o, i * 4, s[p + i * 3], s[p + i * 3 + 1], s[p + i * 3 + 2], 255); return;
                case 3: for (int i = 0; i < n; i++) Put(o, i * 4, s[p + i * 3 + 2], s[p + i * 3 + 1], s[p + i * 3], 255); return;
                case 11: for (int i = 0; i < n; i++) Put(o, i * 4, s[p + i * 4 + 1], s[p + i * 4 + 2], s[p + i * 4 + 3], s[p + i * 4]); return;
                case 12: for (int i = 0; i < n; i++) Put(o, i * 4, s[p + i * 4 + 2], s[p + i * 4 + 1], s[p + i * 4], s[p + i * 4 + 3]); return;
                case 16: for (int i = 0; i < n; i++) Put(o, i * 4, s[p + i * 4 + 2], s[p + i * 4 + 1], s[p + i * 4], 255); return;
                case 5: for (int i = 0; i < n; i++) Put(o, i * 4, s[p + i], s[p + i], s[p + i], 255); return;
                case 8: for (int i = 0; i < n; i++) Put(o, i * 4, 255, 255, 255, s[p + i]); return;
                case 6: for (int i = 0; i < n; i++) Put(o, i * 4, s[p + i * 2], s[p + i * 2], s[p + i * 2], s[p + i * 2 + 1]); return;
                case 22: for (int i = 0; i < n; i++) Put(o, i * 4, s[p + i * 2], s[p + i * 2 + 1], 128, 255); return;
                case 4:
                    for (int i = 0; i < n; i++) { int v = s[p + i * 2] | (s[p + i * 2 + 1] << 8); Put(o, i * 4, (v >> 11) * 255 / 31, ((v >> 5) & 63) * 255 / 63, (v & 31) * 255 / 31, 255); }
                    return;
                case 17:
                    for (int i = 0; i < n; i++) { int v = s[p + i * 2] | (s[p + i * 2 + 1] << 8); Put(o, i * 4, (v & 31) * 255 / 31, ((v >> 5) & 63) * 255 / 63, (v >> 11) * 255 / 31, 255); }
                    return;
                case 19:
                    for (int i = 0; i < n; i++) { int v = s[p + i * 2] | (s[p + i * 2 + 1] << 8); Put(o, i * 4, ((v >> 8) & 15) * 17, ((v >> 4) & 15) * 17, (v & 15) * 17, ((v >> 12) & 15) * 17); }
                    return;
                case 21:
                    for (int i = 0; i < n; i++) { int v = s[p + i * 2] | (s[p + i * 2 + 1] << 8); Put(o, i * 4, ((v >> 10) & 31) * 255 / 31, ((v >> 5) & 31) * 255 / 31, (v & 31) * 255 / 31, (v >> 15) * 255); }
                    return;
                case 18:
                    for (int i = 0; i < n; i++) { int v = s[p + i * 2] | (s[p + i * 2 + 1] << 8); Put(o, i * 4, ((v >> 10) & 31) * 255 / 31, ((v >> 5) & 31) * 255 / 31, (v & 31) * 255 / 31, 255); }
                    return;
                case 25:
                    for (int i = 0; i < n; i++) Put(o, i * 4, s[p + i * 8 + 1], s[p + i * 8 + 3], s[p + i * 8 + 5], s[p + i * 8 + 7]);
                    return;
                case 24: // HDR half-float: грубо переводим в LDR
                    for (int i = 0; i < n; i++)
                    {
                        int[] c = new int[4];
                        for (int k = 0; k < 4; k++) c[k] = (int)Math.Max(0, Math.Min(255, HalfToFloat(BitConverter.ToUInt16(s, p + i * 8 + k * 2)) * 255f));
                        Put(o, i * 4, c[0], c[1], c[2], 255);
                    }
                    return;
                default:
                    for (int i = 0; i < n; i++) Put(o, i * 4, 255, 0, 255, 255);
                    return;
            }
        }

        private static float HalfToFloat(ushort hf)
        {
            int sign = (hf >> 15) & 1, exp = (hf >> 10) & 31, man = hf & 1023;
            float v;
            if (exp == 0) v = (float)(man / 1024.0 * Math.Pow(2, -14));
            else if (exp == 31) v = 65504f;
            else v = (float)((1 + man / 1024.0) * Math.Pow(2, exp - 15));
            return sign == 1 ? -v : v;
        }

        private static void DecodeDxt(byte[] s, int p, int fmt, int w, int h, byte[] o)
        {
            int bw = Math.Max(1, (w + 3) / 4), bh = Math.Max(1, (h + 3) / 4);
            int blockSize = (fmt == 13 || fmt == 20) ? 8 : 16;
            int[] col = new int[16];     // rgba упакованные
            int[] alpha = new int[16];
            byte[] pal = new byte[16];   // 4 цвета * 4
            for (int by = 0; by < bh; by++)
                for (int bx = 0; bx < bw; bx++)
                {
                    int bp = p + (by * bw + bx) * blockSize;
                    int cp = (fmt == 13 || fmt == 20) ? bp : bp + 8;

                    // альфа
                    if (fmt == 14)
                    {
                        for (int i = 0; i < 16; i++)
                        {
                            int b = s[bp + i / 2];
                            alpha[i] = ((i & 1) == 0 ? b & 15 : b >> 4) * 17;
                        }
                    }
                    else if (fmt == 15)
                    {
                        int a0 = s[bp], a1 = s[bp + 1];
                        int[] ap = new int[8];
                        ap[0] = a0; ap[1] = a1;
                        if (a0 > a1) for (int i = 1; i < 7; i++) ap[i + 1] = ((7 - i) * a0 + i * a1) / 7;
                        else
                        {
                            for (int i = 1; i < 5; i++) ap[i + 1] = ((5 - i) * a0 + i * a1) / 5;
                            ap[6] = 0; ap[7] = 255;
                        }
                        long bits = 0;
                        for (int i = 0; i < 6; i++) bits |= (long)s[bp + 2 + i] << (8 * i);
                        for (int i = 0; i < 16; i++) alpha[i] = ap[(int)((bits >> (3 * i)) & 7)];
                    }
                    else for (int i = 0; i < 16; i++) alpha[i] = 255;

                    // цвета
                    int c0 = s[cp] | (s[cp + 1] << 8), c1 = s[cp + 2] | (s[cp + 3] << 8);
                    int r0 = ((c0 >> 11) & 31) * 255 / 31, g0 = ((c0 >> 5) & 63) * 255 / 63, b0 = (c0 & 31) * 255 / 31;
                    int r1 = ((c1 >> 11) & 31) * 255 / 31, g1 = ((c1 >> 5) & 63) * 255 / 63, b1 = (c1 & 31) * 255 / 31;
                    pal[0] = (byte)r0; pal[1] = (byte)g0; pal[2] = (byte)b0; pal[3] = 255;
                    pal[4] = (byte)r1; pal[5] = (byte)g1; pal[6] = (byte)b1; pal[7] = 255;
                    bool four = c0 > c1 || fmt == 14 || fmt == 15;
                    if (four)
                    {
                        pal[8] = (byte)((2 * r0 + r1) / 3); pal[9] = (byte)((2 * g0 + g1) / 3); pal[10] = (byte)((2 * b0 + b1) / 3); pal[11] = 255;
                        pal[12] = (byte)((r0 + 2 * r1) / 3); pal[13] = (byte)((g0 + 2 * g1) / 3); pal[14] = (byte)((b0 + 2 * b1) / 3); pal[15] = 255;
                    }
                    else
                    {
                        pal[8] = (byte)((r0 + r1) / 2); pal[9] = (byte)((g0 + g1) / 2); pal[10] = (byte)((b0 + b1) / 2); pal[11] = 255;
                        pal[12] = 0; pal[13] = 0; pal[14] = 0; pal[15] = (byte)(fmt == 20 ? 0 : 255);
                    }
                    uint idx = BitConverter.ToUInt32(s, cp + 4);
                    for (int py = 0; py < 4; py++)
                        for (int px = 0; px < 4; px++)
                        {
                            int x = bx * 4 + px, y = by * 4 + py;
                            if (x >= w || y >= h) continue;
                            int ci = (int)((idx >> (2 * (py * 4 + px))) & 3);
                            int oi = (y * w + x) * 4;
                            o[oi] = pal[ci * 4]; o[oi + 1] = pal[ci * 4 + 1]; o[oi + 2] = pal[ci * 4 + 2];
                            int a = alpha[py * 4 + px];
                            o[oi + 3] = (byte)(fmt == 13 || fmt == 20 ? pal[ci * 4 + 3] : a);
                        }
                }
        }
    }

    // ------------------------------------------------------------------
    // VMT
    // ------------------------------------------------------------------
    internal class VmtData
    {
        public string Shader = "";
        public readonly Dictionary<string, string> Params = new Dictionary<string, string>();

        public string Get(string key)
        {
            string v;
            return Params.TryGetValue(key.ToLowerInvariant(), out v) ? v : null;
        }

        public bool Flag(string key)
        {
            string v = Get(key);
            if (v == null) return false;
            v = v.Trim();
            return v.Length > 0 && v != "0" && !v.Equals("false", StringComparison.OrdinalIgnoreCase);
        }

        // [r g b] или {r g b}: значения 0..1, либо {0..255}
        public float[] Color(string key)
        {
            string v = Get(key);
            if (v == null) return null;
            bool bytes = v.Contains("{");
            string[] p = v.Trim('[', ']', '{', '}', ' ').Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            float[] c = new float[3];
            for (int i = 0; i < 3; i++)
            {
                float f;
                if (i >= p.Length) c[i] = c.Length > 0 && i > 0 ? c[0] : 1f;
                else if (!float.TryParse(p[i], NumberStyles.Float, CultureInfo.InvariantCulture, out f)) c[i] = 1f;
                else c[i] = bytes ? f / 255f : f;
            }
            return c;
        }

        // include "materials/..." в шейдере patch
        public static VmtData Load(string path, List<string> roots)
        {
            return Load(path, roots, 0);
        }

        private static VmtData Load(string path, List<string> roots, int depth)
        {
            string text = Encoding.UTF8.GetString(VtfLoader.ReadShared(path));
            List<string> t = Tokenize(text);
            VmtData v = new VmtData();
            if (t.Count == 0) return v;
            v.Shader = t[0];
            int pos = 1;
            if (pos < t.Count && t[pos] == "{") pos++;
            Dictionary<string, string> top = new Dictionary<string, string>();
            Dictionary<string, Dictionary<string, string>> blocks = new Dictionary<string, Dictionary<string, string>>();
            ParseBlock(t, ref pos, top, blocks);

            if (v.Shader.Equals("patch", StringComparison.OrdinalIgnoreCase) && depth < 4)
            {
                string inc;
                if (top.TryGetValue("include", out inc))
                {
                    string rel = inc.Replace('/', '\\').TrimStart('\\');
                    foreach (string root in roots)
                    {
                        string f = System.IO.Path.Combine(root, rel);
                        if (!File.Exists(f)) continue;
                        VmtData baseV = Load(f, roots, depth + 1);
                        v.Shader = baseV.Shader;
                        foreach (KeyValuePair<string, string> kv in baseV.Params) v.Params[kv.Key] = kv.Value;
                        break;
                    }
                }
                foreach (string blk in new string[] { "insert", "replace" })
                {
                    Dictionary<string, string> b;
                    if (blocks.TryGetValue(blk, out b)) foreach (KeyValuePair<string, string> kv in b) v.Params[kv.Key] = kv.Value;
                }
            }
            else foreach (KeyValuePair<string, string> kv in top) v.Params[kv.Key] = kv.Value;
            return v;
        }

        private static void ParseBlock(List<string> t, ref int pos, Dictionary<string, string> kv,
            Dictionary<string, Dictionary<string, string>> blocks)
        {
            while (pos < t.Count)
            {
                string k = t[pos++];
                if (k == "}") return;
                if (k == "{") { SkipBlock(t, ref pos); continue; }
                if (pos >= t.Count) return;
                string val = t[pos];
                if (val == "{")
                {
                    pos++;
                    Dictionary<string, string> sub = new Dictionary<string, string>();
                    Dictionary<string, Dictionary<string, string>> nested = new Dictionary<string, Dictionary<string, string>>();
                    ParseBlock(t, ref pos, sub, nested);
                    blocks[k.ToLowerInvariant()] = sub;
                }
                else { pos++; kv[k.ToLowerInvariant()] = val; }
            }
        }

        private static void SkipBlock(List<string> t, ref int pos)
        {
            int depth = 1;
            while (pos < t.Count && depth > 0)
            {
                string s = t[pos++];
                if (s == "{") depth++; else if (s == "}") depth--;
            }
        }

        private static List<string> Tokenize(string s)
        {
            List<string> r = new List<string>();
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (char.IsWhiteSpace(c) || c == '﻿') { i++; continue; }
                if (c == '/' && i + 1 < s.Length && s[i + 1] == '/') { while (i < s.Length && s[i] != '\n') i++; continue; }
                if (c == '{' || c == '}') { r.Add(c.ToString()); i++; continue; }
                if (c == '"')
                {
                    int j = s.IndexOf('"', i + 1);
                    if (j < 0) j = s.Length;
                    r.Add(s.Substring(i + 1, j - i - 1));
                    i = j + 1;
                    continue;
                }
                int st = i;
                while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] != '{' && s[i] != '}' && s[i] != '"') i++;
                r.Add(s.Substring(st, i - st));
            }
            return r;
        }
    }
}
