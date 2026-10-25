// Физическая модель (.phy): выпуклые тела (solid), привязанные к костям. Только для отображения.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace SourceSDKHub
{
    internal class PhySolid
    {
        public int Index;
        public string BoneName = "";
        public int Bone = -1;
        public float[] V = new float[0];      // x,y,z в единицах Source
        public int[] Tris = new int[0];       // индексы вершин
        public float[] BMin = new float[3], BMax = new float[3];   // границы в системе кости
    }

    internal class PhyData
    {
        public readonly List<PhySolid> Solids = new List<PhySolid>();

        public static PhyData Load(string path, AnimFile skeleton)
        {
            byte[] d = VtfLoader.ReadShared(path);
            if (d.Length < 32) throw new InvalidDataException(Loc.T("Короткий .phy"));
            int headerSize = BitConverter.ToInt32(d, 0);
            int count = BitConverter.ToInt32(d, 8);
            if (headerSize < 16 || headerSize > 64 || count < 0 || count > 512) throw new InvalidDataException(Loc.T("Неизвестный формат .phy"));
            PhyData r = new PhyData();
            int cur = headerSize;
            for (int s = 0; s < count; s++)
            {
                int size = BitConverter.ToInt32(d, cur);
                int start = cur + 4;
                if (size <= 0 || start + size > d.Length) break;
                PhySolid solid = new PhySolid();
                solid.Index = s;
                try { ReadSolid(d, start, size, solid); } catch { }
                r.Solids.Add(solid);
                cur = start + size;
            }

            // текстовая секция: solid { "index" "0" "name" "bone" ... }
            if (cur < d.Length)
            {
                string text = Encoding.ASCII.GetString(d, cur, d.Length - cur);
                foreach (Match m in Regex.Matches(text, "\"index\"\\s+\"(\\d+)\"\\s+\"name\"\\s+\"([^\"]*)\"", RegexOptions.IgnoreCase))
                {
                    int idx = int.Parse(m.Groups[1].Value);
                    foreach (PhySolid ps in r.Solids)
                        if (ps.Index == idx) ps.BoneName = m.Groups[2].Value;
                }
            }
            foreach (PhySolid ps in r.Solids)
            {
                for (int b = 0; b < skeleton.Bones.Length; b++)
                    if (string.Equals(skeleton.Bones[b].Name, ps.BoneName, StringComparison.OrdinalIgnoreCase)) { ps.Bone = b; break; }
                if (ps.Bone < 0)
                    for (int b = 0; b < skeleton.Bones.Length; b++)
                        if (skeleton.Bones[b].PhysBone == ps.Index) { ps.Bone = b; break; }
                if (ps.Bone < 0) ps.Bone = 0;
            }
            return r;
        }

        private static void ReadSolid(byte[] d, int start, int size, PhySolid solid)
        {
            // заголовок VPHY (28 байт), затем compactsurface
            if (d[start] != 'V' || d[start + 1] != 'P' || d[start + 2] != 'H' || d[start + 3] != 'Y') return;
            int surf = start + 28;
            int rootOff = BitConverter.ToInt32(d, surf + 32);
            int root = surf + rootOff;
            List<float> verts = new List<float>();
            List<int> tris = new List<int>();
            Dictionary<int, int> remap = new Dictionary<int, int>();
            Walk(d, root, surf, start + size, verts, tris, remap, 0);
            solid.V = verts.ToArray();
            solid.Tris = tris.ToArray();
            for (int c = 0; c < 3; c++) { solid.BMin[c] = float.MaxValue; solid.BMax[c] = float.MinValue; }
            for (int i = 0; i < solid.V.Length; i++)
            {
                int c = i % 3;
                if (solid.V[i] < solid.BMin[c]) solid.BMin[c] = solid.V[i];
                if (solid.V[i] > solid.BMax[c]) solid.BMax[c] = solid.V[i];
            }
        }

        private static void Walk(byte[] d, int node, int surf, int limit, List<float> verts, List<int> tris, Dictionary<int, int> remap, int depth)
        {
            if (depth > 64 || node < 0 || node + 28 > limit) return;
            int right = BitConverter.ToInt32(d, node);
            int ledgeOff = BitConverter.ToInt32(d, node + 4);
            if (right == 0)
            {
                ReadLedge(d, node + ledgeOff, limit, verts, tris, remap);
                return;
            }
            Walk(d, node + 28, surf, limit, verts, tris, remap, depth + 1);
            Walk(d, node + right, surf, limit, verts, tris, remap, depth + 1);
        }

        private static void ReadLedge(byte[] d, int ledge, int limit, List<float> verts, List<int> tris, Dictionary<int, int> remap)
        {
            if (ledge < 0 || ledge + 16 > limit) return;
            int pointOff = BitConverter.ToInt32(d, ledge);
            int nTris = BitConverter.ToInt16(d, ledge + 12);
            int points = ledge + pointOff;
            for (int t = 0; t < nTris; t++)
            {
                int tb = ledge + 16 + t * 16;
                if (tb + 16 > limit) return;
                for (int e = 0; e < 3; e++)
                {
                    uint edge = BitConverter.ToUInt32(d, tb + 4 + e * 4);
                    int pi = (int)(edge & 0xFFFF);
                    int key = points * 31 + pi;
                    int vi;
                    if (!remap.TryGetValue(key, out vi))
                    {
                        int po = points + pi * 16;
                        if (po + 12 > d.Length) return;
                        float x = BitConverter.ToSingle(d, po), y = BitConverter.ToSingle(d, po + 4), z = BitConverter.ToSingle(d, po + 8);
                        const float k = 1f / 0.0254f;
                        // вершины лежат в системе кости; оси IVP (x, y, z) -> Source (x, z, y) - проверено по вершинам меша
                        vi = verts.Count / 3;
                        verts.Add(x * k); verts.Add(z * k); verts.Add(y * k);
                        remap[key] = vi;
                    }
                    tris.Add(vi);
                }
            }
        }
    }
}
