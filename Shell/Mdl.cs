// Загрузчик моделей Source: .mdl + .vvd + .vtx (LOD 0), бодигруппы, скины, материалы.
// Поддерживаются версии MDL 44-49 (Source 2007 ... Source 2013 / GMod / CS:GO-совместимые статические формы).

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SourceSDKHub
{
    internal class MdlSub
    {
        public int SkinRef;      // индекс в таблице скинов
        public int[] Index;      // индексы вершин (треугольники)
    }

    internal class MdlBodyModel
    {
        public string Name = "";
        public List<MdlSub> Subs = new List<MdlSub>();
        public int Triangles;
    }

    internal class MdlBodyPart
    {
        public string Name = "";
        public List<MdlBodyModel> Models = new List<MdlBodyModel>();
    }

    internal class MdlData
    {
        public string Path, Name;
        public int Version, Bones;
        public int VertexCount;
        public float[] Pos, Nrm, Uv;
        public float[] Weights;          // 3 веса на вершину
        public byte[] BoneIdx;           // 3 кости на вершину
        public byte[] NumBones;
        public AnimSet Anim;             // скелет и анимации (может быть null)
        public string AnimError;
        public PhyData Phy;              // физика (может быть null)
        public string PhyError;
        public List<MdlBodyPart> Parts = new List<MdlBodyPart>();
        public string[] TexNames = new string[0];
        public string[] CdTextures = new string[0];
        public int SkinRefs;
        public int[][] Skins = new int[0][];          // [семейство][skinref] -> номер текстуры
        public readonly List<string> Files = new List<string>();   // файлы модели (для автообновления)
    }

    internal static class MdlLoader
    {
        // Папки, в которых лежит каталог models (для поиска подключённых файлов анимаций).
        public static List<string> ModelRoots(string mdlPath, IEnumerable<string> extra)
        {
            List<string> r = new List<string>();
            string dir = System.IO.Path.GetDirectoryName(mdlPath);
            for (int i = 0; i < 10 && !string.IsNullOrEmpty(dir); i++)
            {
                if (Directory.Exists(System.IO.Path.Combine(dir, "models"))) r.Add(dir);
                dir = System.IO.Path.GetDirectoryName(dir);
            }
            if (extra != null)
                foreach (string e in extra)
                {
                    if (string.IsNullOrEmpty(e) || !Directory.Exists(e)) continue;
                    string root = System.IO.Path.GetFileName(e.TrimEnd('\\')).Equals("materials", StringComparison.OrdinalIgnoreCase)
                        ? System.IO.Path.GetDirectoryName(e.TrimEnd('\\')) : e;
                    if (!r.Contains(root)) r.Add(root);
                }
            return r;
        }

        private static int I(byte[] d, int o) { return BitConverter.ToInt32(d, o); }
        private static int U16(byte[] d, int o) { return BitConverter.ToUInt16(d, o); }

        private static string Str(byte[] d, int o)
        {
            if (o < 0 || o >= d.Length) return "";
            int e = o;
            while (e < d.Length && d[e] != 0) e++;
            return Encoding.UTF8.GetString(d, o, e - o);
        }

        public static MdlData Load(string path, IEnumerable<string> matRoots, IEnumerable<string> modelRoots, IEnumerable<string> extraAnims)
        {
            byte[] d = VtfLoader.ReadShared(path);
            if (d.Length < 240 || d[0] != 'I' || d[1] != 'D' || d[2] != 'S' || d[3] != 'T')
                throw new InvalidDataException(Loc.T("Это не файл модели Source (.mdl)."));
            MdlData m = new MdlData();
            m.Path = path;
            m.Version = I(d, 4);
            if (m.Version < 44 || m.Version > 49)
                throw new InvalidDataException(Loc.T("Версия MDL ") + m.Version + Loc.T(" не поддерживается (нужна 44–49)."));
            m.Name = Str(d, 12);
            m.Bones = I(d, 156);
            int texCount = I(d, 204), texIdx = I(d, 208);
            int cdCount = I(d, 212), cdIdx = I(d, 216);
            int skinRefs = I(d, 220), skinFam = I(d, 224), skinIdx = I(d, 228);
            int bpCount = I(d, 232), bpIdx = I(d, 236);

            m.TexNames = new string[texCount];
            for (int i = 0; i < texCount; i++)
            {
                int b = texIdx + i * 64;
                m.TexNames[i] = Str(d, b + I(d, b));
            }
            m.CdTextures = new string[cdCount];
            for (int i = 0; i < cdCount; i++) m.CdTextures[i] = Str(d, I(d, cdIdx + i * 4));
            m.SkinRefs = skinRefs;
            m.Skins = new int[Math.Max(1, skinFam)][];
            for (int f = 0; f < m.Skins.Length; f++)
            {
                m.Skins[f] = new int[skinRefs];
                for (int r = 0; r < skinRefs; r++)
                    m.Skins[f][r] = skinFam > 0 ? (short)U16(d, skinIdx + (f * skinRefs + r) * 2) : r;
            }

            // --- VVD ---
            string stem = path.Substring(0, path.Length - System.IO.Path.GetExtension(path).Length);
            string vvdPath = stem + ".vvd";
            if (!File.Exists(vvdPath)) throw new FileNotFoundException(Loc.T("Рядом с моделью нет файла ") + System.IO.Path.GetFileName(vvdPath));
            byte[] v = VtfLoader.ReadShared(vvdPath);
            if (v.Length < 64 || v[0] != 'I' || v[1] != 'D' || v[2] != 'S' || v[3] != 'V') throw new InvalidDataException(Loc.T("Повреждён .vvd"));
            int lod0 = I(v, 16);
            int nFix = I(v, 48), fixStart = I(v, 52), vStart = I(v, 56);
            List<int> order = new List<int>();   // индексы исходных вершин в порядке LOD 0
            if (nFix == 0) for (int i = 0; i < lod0; i++) order.Add(i);
            else
                for (int f = 0; f < nFix; f++)
                {
                    int fl = I(v, fixStart + f * 12), src = I(v, fixStart + f * 12 + 4), cnt = I(v, fixStart + f * 12 + 8);
                    if (fl < 0) continue;
                    for (int i = 0; i < cnt; i++) order.Add(src + i);
                }
            int vc = order.Count;
            m.VertexCount = vc;
            m.Pos = new float[vc * 3]; m.Nrm = new float[vc * 3]; m.Uv = new float[vc * 2];
            m.Weights = new float[vc * 3]; m.BoneIdx = new byte[vc * 3]; m.NumBones = new byte[vc];
            for (int i = 0; i < vc; i++)
            {
                int o = vStart + order[i] * 48;
                if (o + 48 > v.Length) throw new InvalidDataException(Loc.T(".vvd обрезан"));
                for (int k = 0; k < 3; k++) { m.Weights[i * 3 + k] = BitConverter.ToSingle(v, o + k * 4); m.BoneIdx[i * 3 + k] = v[o + 12 + k]; }
                m.NumBones[i] = v[o + 15];
                m.Pos[i * 3] = BitConverter.ToSingle(v, o + 16);
                m.Pos[i * 3 + 1] = BitConverter.ToSingle(v, o + 20);
                m.Pos[i * 3 + 2] = BitConverter.ToSingle(v, o + 24);
                m.Nrm[i * 3] = BitConverter.ToSingle(v, o + 28);
                m.Nrm[i * 3 + 1] = BitConverter.ToSingle(v, o + 32);
                m.Nrm[i * 3 + 2] = BitConverter.ToSingle(v, o + 36);
                m.Uv[i * 2] = BitConverter.ToSingle(v, o + 40);
                m.Uv[i * 2 + 1] = BitConverter.ToSingle(v, o + 44);
            }

            // --- VTX ---
            string vtxPath = null;
            foreach (string ext in new string[] { ".dx90.vtx", ".dx80.vtx", ".sw.vtx" })
                if (File.Exists(stem + ext)) { vtxPath = stem + ext; break; }
            if (vtxPath == null) throw new FileNotFoundException(Loc.T("Рядом с моделью нет файла .dx90.vtx"));
            byte[] x = VtfLoader.ReadShared(vtxPath);
            if (x.Length < 36) throw new InvalidDataException(Loc.T("Повреждён .vtx"));
            int bpOff = I(x, 32);       // смещение массива bodypart (числом их заголовок не используем - берём из .mdl)
            int sgSize = m.Version >= 49 ? 33 : 25;

            for (int bp = 0; bp < bpCount; bp++)
            {
                int bpBase = bpIdx + bp * 16;
                MdlBodyPart part = new MdlBodyPart();
                part.Name = Str(d, bpBase + I(d, bpBase));
                int nModels = I(d, bpBase + 4), modelIdx = I(d, bpBase + 12);
                int xbp = bpOff + bp * 8;
                int xModelOff = xbp + I(x, xbp + 4);
                for (int mo = 0; mo < nModels; mo++)
                {
                    int mb = bpBase + modelIdx + mo * 148;
                    MdlBodyModel bm = new MdlBodyModel();
                    bm.Name = Str(d, mb);
                    int nMeshes = I(d, mb + 72), meshIdx = I(d, mb + 76), vtxStart = I(d, mb + 84) / 48;
                    part.Models.Add(bm);
                    if (nMeshes == 0) continue;

                    int xm = xModelOff + mo * 8;
                    int xLodOff = xm + I(x, xm + 4);
                    int xMeshCount = I(x, xLodOff), xMeshOff = xLodOff + I(x, xLodOff + 4);
                    for (int k = 0; k < nMeshes && k < xMeshCount; k++)
                    {
                        int mm = mb + meshIdx + k * 116;
                        int material = I(d, mm), meshVertOff = I(d, mm + 12);
                        int xh = xMeshOff + k * 9;
                        int nGroups = I(x, xh), groupOff = xh + I(x, xh + 4);
                        List<int> tris = new List<int>();
                        for (int g = 0; g < nGroups; g++)
                        {
                            int gb = groupOff + g * sgSize;
                            int nVerts = I(x, gb), vOff = gb + I(x, gb + 4);
                            int nIdx = I(x, gb + 8), iOff = gb + I(x, gb + 12);
                            int nStrips = I(x, gb + 16), sOff = gb + I(x, gb + 20);
                            for (int s = 0; s < nStrips; s++)
                            {
                                int sb = sOff + s * 27;
                                int sIdxCount = I(x, sb), sIdxOff = I(x, sb + 4), sFlags = x[sb + 18];
                                Action<int, int, int> add = delegate(int a, int b, int c)
                                {
                                    int ga = vtxStart + meshVertOff + U16(x, vOff + a * 9 + 4);
                                    int gbv = vtxStart + meshVertOff + U16(x, vOff + b * 9 + 4);
                                    int gc = vtxStart + meshVertOff + U16(x, vOff + c * 9 + 4);
                                    if (ga < 0 || gbv < 0 || gc < 0 || ga >= vc || gbv >= vc || gc >= vc) return;
                                    if (ga == gbv || gbv == gc || ga == gc) return;
                                    tris.Add(ga); tris.Add(gbv); tris.Add(gc);
                                };
                                if ((sFlags & 1) != 0)
                                {
                                    for (int t = 0; t + 2 < sIdxCount; t += 3)
                                        add(U16(x, iOff + (sIdxOff + t) * 2), U16(x, iOff + (sIdxOff + t + 1) * 2), U16(x, iOff + (sIdxOff + t + 2) * 2));
                                }
                                else if ((sFlags & 2) != 0)
                                {
                                    for (int t = 0; t + 2 < sIdxCount; t++)
                                    {
                                        int a = U16(x, iOff + (sIdxOff + t) * 2), b = U16(x, iOff + (sIdxOff + t + 1) * 2), c = U16(x, iOff + (sIdxOff + t + 2) * 2);
                                        if ((t & 1) == 0) add(a, b, c); else add(b, a, c);
                                    }
                                }
                            }
                        }
                        if (tris.Count == 0) continue;
                        MdlSub sub = new MdlSub();
                        sub.SkinRef = material;
                        sub.Index = tris.ToArray();
                        bm.Subs.Add(sub);
                        bm.Triangles += tris.Count / 3;
                    }
                }
                m.Parts.Add(part);
            }
            m.Files.Add(path); m.Files.Add(vvdPath); m.Files.Add(vtxPath);

            // скелет, анимации (включая $includemodel) и физика - необязательные части
            try
            {
                AnimFile main = AnimFile.Load(path);
                List<string> mr = new List<string>();
                if (modelRoots != null) mr.AddRange(modelRoots);
                if (matRoots != null) mr.AddRange(matRoots);     // корень игры часто нужен и для materials, и для models
                List<string> extraList = extraAnims == null ? null : new List<string>(extraAnims);
                m.Anim = AnimSet.Build(main, ModelRoots(path, mr), extraList);
                foreach (AnimFile af in m.Anim.Files) if (!m.Files.Contains(af.Path)) m.Files.Add(af.Path);
                string phy = stem + ".phy";
                if (File.Exists(phy))
                {
                    try { m.Phy = PhyData.Load(phy, main); m.Files.Add(phy); }
                    catch (Exception ex) { m.PhyError = ex.Message; }
                }
            }
            catch (Exception ex) { m.AnimError = ex.Message; }
            return m;
        }
    }

    // ------------------------------------------------------------------
    // Материалы
    // ------------------------------------------------------------------
    internal class MatInfo
    {
        public string TexName;
        public string VmtPath, VtfPath, Shader = "";
        public bool AlphaTest, Translucent, NoCull;
        public float[] Tint = new float[] { 1f, 1f, 1f };
        public VtfImage Image;
        public string Error;
        public readonly List<string> Files = new List<string>();   // vmt/vtf для автообновления

        public bool Ok { get { return Image != null; } }
    }

    internal static class MatResolver
    {
        // Корни поиска: папки выше модели, содержащие materials, + пользовательские пути.
        public static List<string> Roots(string mdlPath, IEnumerable<string> extra)
        {
            List<string> r = new List<string>();
            string dir = System.IO.Path.GetDirectoryName(mdlPath);
            for (int i = 0; i < 10 && !string.IsNullOrEmpty(dir); i++)
            {
                if (Directory.Exists(System.IO.Path.Combine(dir, "materials"))) r.Add(dir);
                dir = System.IO.Path.GetDirectoryName(dir);
            }
            if (extra != null)
                foreach (string e in extra)
                {
                    if (string.IsNullOrEmpty(e) || !Directory.Exists(e)) continue;
                    // можно указать как корень игры, так и саму папку materials
                    string root = System.IO.Path.GetFileName(e.TrimEnd('\\')).Equals("materials", StringComparison.OrdinalIgnoreCase)
                        ? System.IO.Path.GetDirectoryName(e.TrimEnd('\\')) : e;
                    if (!r.Contains(root)) r.Add(root);
                }
            return r;
        }

        public static MatInfo Resolve(string texName, string[] cdTextures, List<string> roots, int maxDim)
        {
            MatInfo mi = new MatInfo();
            mi.TexName = texName;
            if (roots.Count == 0) { mi.Error = Loc.T("Не найдена папка materials рядом с моделью"); return mi; }

            string vmtFound = null, rootFound = null;
            foreach (string cd in cdTextures.Length > 0 ? cdTextures : new string[] { "" })
            {
                string rel = (cd + texName).Replace('/', '\\').TrimStart('\\');
                foreach (string root in roots)
                {
                    string f = System.IO.Path.Combine(root, "materials\\" + rel + ".vmt");
                    if (File.Exists(f)) { vmtFound = f; rootFound = root; break; }
                }
                if (vmtFound != null) break;
            }

            string baseTex = null;
            if (vmtFound != null)
            {
                mi.VmtPath = vmtFound;
                mi.Files.Add(vmtFound);
                try
                {
                    VmtData vd = VmtData.Load(vmtFound, roots);
                    mi.Shader = vd.Shader;
                    mi.AlphaTest = vd.Flag("$alphatest");
                    mi.Translucent = vd.Flag("$translucent");
                    mi.NoCull = vd.Flag("$nocull");
                    float[] c = vd.Color("$color2") ?? vd.Color("$color");
                    if (c != null) mi.Tint = c;
                    baseTex = vd.Get("$basetexture");
                }
                catch (Exception ex) { mi.Error = Loc.T("Не удалось прочитать VMT: ") + ex.Message; }
            }
            else
            {
                // нет VMT - пробуем одноимённый VTF
                foreach (string cd in cdTextures.Length > 0 ? cdTextures : new string[] { "" })
                    baseTex = baseTex ?? (cd + texName);
                mi.Error = Loc.T("VMT не найден: materials\\") + (cdTextures.Length > 0 ? cdTextures[0] : "") + texName + ".vmt";
            }

            if (baseTex != null && baseTex.Trim().Length > 0)
            {
                string rel = baseTex.Trim().Replace('/', '\\').TrimStart('\\');
                foreach (string root in rootFound != null ? Order(roots, rootFound) : roots)
                {
                    string f = System.IO.Path.Combine(root, "materials\\" + rel + ".vtf");
                    if (!File.Exists(f)) continue;
                    mi.VtfPath = f;
                    mi.Files.Add(f);
                    try { mi.Image = VtfLoader.Load(f, maxDim); mi.Error = null; }
                    catch (Exception ex) { mi.Error = "VTF: " + ex.Message; }
                    break;
                }
                if (mi.VtfPath == null && mi.Error == null) mi.Error = Loc.T("Текстура не найдена: materials\\") + rel + ".vtf";
            }
            else if (mi.Error == null) mi.Error = Loc.T("В VMT нет $basetexture");
            return mi;
        }

        private static List<string> Order(List<string> roots, string first)
        {
            List<string> r = new List<string>();
            r.Add(first);
            foreach (string s in roots) if (s != first) r.Add(s);
            return r;
        }
    }
}
