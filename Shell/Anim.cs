// Скелет, анимации и секвенции моделей Source (MDL 44-49): разбор, декодирование и расчёт поз.
// Поддержаны: сжатые анимации (RLE, Quaternion48/64, Vector48), секции длинных анимаций,
// бленды секвенций (1D/2D по поз-параметрам), $includemodel (внешние файлы анимаций), аддитивные анимации.
// Не поддержаны: внешние блоки .ani, IK, процедурные кости, движение (movement).

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SourceSDKHub
{
    internal class AnimBone
    {
        public string Name = "";
        public int Parent = -1, PhysBone;
        public float[] Pos = new float[3], Quat = new float[4], Rot = new float[3];
        public float[] PosScale = new float[3], RotScale = new float[3];
        public float[] PoseToBone = new float[12];
    }

    internal class AnimDesc
    {
        public string Name = "";
        public float Fps = 30;
        public int Flags, NumFrames, Block, Index, SectionIndex, SectionFrames, Base;
    }

    internal class SeqDesc
    {
        public string Name = "", Activity = "";
        public int Flags;
        public int[] Blends = new int[0];
        public int GroupX = 1, GroupY = 1, ParamX = -1, ParamY = -1;
        public float StartX, EndX, StartY, EndY;
        public float[] WeightList;                 // веса костей секвенции (null - нет)
        public bool AllOnes = true;                // weightlist состоит из одних единиц (то есть не ограничивает кости)
        public float FadeIn, FadeOut;
        public List<AutoLayer> Layers = new List<AutoLayer>();
    }

    internal class AutoLayer
    {
        public int Seq, Flags;
        public float Start, Peak, Tail, End;
    }

    // Слой, накладываемый поверх основной секвенции (только для просмотра)
    internal class LayerSpec
    {
        public SeqRef Seq;
        public float Weight = 1f;
        public int Mask;            // 0 - weightlist секвенции, 1 - все кости, 2 - выбранная кость и её потомки
        public int Root = -1;
        public int Mode;            // 0 - авто, 1 - смешивать, 2 - добавлять (минус кадр 0)
        public bool Sync = true;    // фаза совпадает с основной секвенцией
        public float Cycle;
    }

    internal class PoseParam
    {
        public string Name = "";
        public float Start, End, Loop;
    }

    internal class HitBox
    {
        public int Bone, Group;
        public float[] Min = new float[3], Max = new float[3];
        public string Name = "";
    }

    internal class HitSet
    {
        public string Name = "";
        public List<HitBox> Boxes = new List<HitBox>();
    }

    internal class AnimFile
    {
        public string Path = "", ModelName = "";
        public byte[] D;
        public int Version;
        public AnimBone[] Bones = new AnimBone[0];
        public AnimDesc[] Anims = new AnimDesc[0];
        public SeqDesc[] Seqs = new SeqDesc[0];
        public PoseParam[] Params = new PoseParam[0];
        public List<string> Includes = new List<string>();
        public List<HitSet> HitSets = new List<HitSet>();

        public static int I(byte[] d, int o) { return BitConverter.ToInt32(d, o); }
        public static float F(byte[] d, int o) { return BitConverter.ToSingle(d, o); }
        public static int U16(byte[] d, int o) { return BitConverter.ToUInt16(d, o); }

        public static string Str(byte[] d, int o)
        {
            if (o < 0 || o >= d.Length) return "";
            int e = o;
            while (e < d.Length && d[e] != 0) e++;
            return Encoding.UTF8.GetString(d, o, e - o);
        }

        public static AnimFile Load(string path)
        {
            byte[] d = VtfLoader.ReadShared(path);
            if (d.Length < 400 || d[0] != 'I' || d[1] != 'D' || d[2] != 'S' || d[3] != 'T')
                throw new InvalidDataException(Loc.T("Это не файл модели Source (.mdl)."));
            AnimFile a = new AnimFile();
            a.Path = path; a.D = d; a.Version = I(d, 4);
            if (a.Version < 44 || a.Version > 49)
                throw new InvalidDataException(Loc.T("Версия MDL ") + a.Version + Loc.T(" не поддерживается (нужна 44–49)."));
            a.ModelName = Str(d, 12);

            int nb = I(d, 156), bi = I(d, 160);
            a.Bones = new AnimBone[Math.Max(0, nb)];
            for (int i = 0; i < a.Bones.Length; i++)
            {
                int b = bi + i * 216;
                AnimBone bone = new AnimBone();
                bone.Name = Str(d, b + I(d, b));
                bone.Parent = I(d, b + 4);
                for (int k = 0; k < 3; k++) { bone.Pos[k] = F(d, b + 32 + k * 4); bone.Rot[k] = F(d, b + 60 + k * 4); bone.PosScale[k] = F(d, b + 72 + k * 4); bone.RotScale[k] = F(d, b + 84 + k * 4); }
                for (int k = 0; k < 4; k++) bone.Quat[k] = F(d, b + 44 + k * 4);
                for (int k = 0; k < 12; k++) bone.PoseToBone[k] = F(d, b + 96 + k * 4);
                bone.PhysBone = I(d, b + 172);
                a.Bones[i] = bone;
            }

            int nh = I(d, 172), hi = I(d, 176);
            for (int s = 0; s < nh && s < 8; s++)
            {
                int sb = hi + s * 12;
                HitSet hs = new HitSet();
                hs.Name = Str(d, sb + I(d, sb));
                int cnt = I(d, sb + 4), idx = I(d, sb + 8);
                for (int k = 0; k < cnt && k < 512; k++)
                {
                    int hb = sb + idx + k * 68;
                    HitBox h = new HitBox();
                    h.Bone = I(d, hb); h.Group = I(d, hb + 4);
                    for (int c = 0; c < 3; c++) { h.Min[c] = F(d, hb + 8 + c * 4); h.Max[c] = F(d, hb + 20 + c * 4); }
                    h.Name = Str(d, hb + I(d, hb + 32));
                    hs.Boxes.Add(h);
                }
                a.HitSets.Add(hs);
            }

            int nan = I(d, 180), ai = I(d, 184);
            a.Anims = new AnimDesc[Math.Max(0, nan)];
            for (int i = 0; i < a.Anims.Length; i++)
            {
                int o = ai + i * 100;
                AnimDesc ad = new AnimDesc();
                ad.Base = o;
                ad.Name = Str(d, o + I(d, o + 4));
                ad.Fps = F(d, o + 8); if (ad.Fps <= 0) ad.Fps = 30;
                ad.Flags = I(d, o + 12); ad.NumFrames = Math.Max(1, I(d, o + 16));
                ad.Block = I(d, o + 52); ad.Index = I(d, o + 56);
                ad.SectionIndex = I(d, o + 80); ad.SectionFrames = I(d, o + 84);
                a.Anims[i] = ad;
            }

            int npp = I(d, 300), ppi = I(d, 304);
            a.Params = new PoseParam[Math.Max(0, Math.Min(npp, 64))];
            for (int i = 0; i < a.Params.Length; i++)
            {
                int o = ppi + i * 20;
                PoseParam p = new PoseParam();
                p.Name = Str(d, o + I(d, o)); p.Start = F(d, o + 8); p.End = F(d, o + 12); p.Loop = F(d, o + 16);
                a.Params[i] = p;
            }

            int nsq = I(d, 188), si = I(d, 192);
            a.Seqs = new SeqDesc[Math.Max(0, nsq)];
            for (int i = 0; i < a.Seqs.Length; i++)
            {
                int o = si + i * 212;
                SeqDesc s = new SeqDesc();
                s.Name = Str(d, o + I(d, o + 4));
                s.Activity = Str(d, o + I(d, o + 8));
                s.Flags = I(d, o + 12);
                int nbl = I(d, o + 56), abi = I(d, o + 60);
                s.GroupX = Math.Max(1, I(d, o + 68)); s.GroupY = Math.Max(1, I(d, o + 72));
                s.ParamX = I(d, o + 76); s.ParamY = I(d, o + 80);
                s.StartX = F(d, o + 84); s.StartY = F(d, o + 88); s.EndX = F(d, o + 92); s.EndY = F(d, o + 96);
                s.FadeIn = F(d, o + 104); s.FadeOut = F(d, o + 108);
                int nal = I(d, o + 148), ali = I(d, o + 152), wli = I(d, o + 156);
                for (int k = 0; k < nal && k < 32; k++)
                {
                    int lo = o + ali + k * 24;
                    if (lo + 24 > d.Length) break;
                    AutoLayer al = new AutoLayer();
                    al.Seq = (short)U16(d, lo); al.Flags = I(d, lo + 4);
                    al.Start = F(d, lo + 8); al.Peak = F(d, lo + 12); al.Tail = F(d, lo + 16); al.End = F(d, lo + 20);
                    s.Layers.Add(al);
                }
                if (wli > 0 && nb > 0 && o + wli + nb * 4 <= d.Length)
                {
                    s.WeightList = new float[nb];
                    for (int k = 0; k < nb; k++)
                    {
                        s.WeightList[k] = F(d, o + wli + k * 4);
                        if (Math.Abs(s.WeightList[k] - 1f) > 1e-4f) s.AllOnes = false;
                    }
                }
                int count = Math.Min(Math.Max(0, nbl), 1024);
                s.Blends = new int[count];
                for (int k = 0; k < count; k++) s.Blends[k] = (short)U16(d, o + abi + k * 2);
                a.Seqs[i] = s;
            }

            int ninc = I(d, 336), inci = I(d, 340);
            for (int i = 0; i < ninc && i < 64; i++)
            {
                int o = inci + i * 8;
                string n = Str(d, o + I(d, o + 4));
                if (n.Length > 0) a.Includes.Add(n);
            }
            return a;
        }
    }

    // Отчёт о подключённых файлах анимаций (для окна «Привязки»)
    internal class IncludeInfo
    {
        public string Name = "", Path;       // Path == null - файл не найден
        public bool Extra;                   // добавлен пользователем вручную
        public int Seqs;
    }

    // Строка списка секвенций
    internal class SeqRef
    {
        public int File, Index;
        public string Name = "", Activity = "", Source = "";
        public SeqDesc Desc;
        public bool Looping, Additive, Missing;
        public int Frames;
        public float Fps = 30, Duration = 1;
    }

    // Набор: основная модель + подключённые ($includemodel) файлы анимаций
    internal class AnimSet
    {
        public readonly List<AnimFile> Files = new List<AnimFile>();
        public readonly List<int[]> Maps = new List<int[]>();       // кость файла -> кость основной модели
        public readonly List<SeqRef> Seqs = new List<SeqRef>();
        public readonly List<string> Warnings = new List<string>();
        public readonly List<IncludeInfo> Report = new List<IncludeInfo>();

        public AnimFile Main { get { return Files[0]; } }
        public int BoneCount { get { return Main.Bones.Length; } }

        public static AnimSet Build(AnimFile main, List<string> modelRoots, List<string> extraFiles)
        {
            AnimSet s = new AnimSet();
            s.Files.Add(main);
            Dictionary<string, int> byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < main.Bones.Length; i++) byName[main.Bones[i].Name] = i;
            s.Maps.Add(Identity(main.Bones.Length));
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            seen.Add(Path.GetFullPath(main.Path));
            s.AddIncludes(main, modelRoots, byName, seen, 0);
            if (extraFiles != null)
                foreach (string ef in extraFiles)
                {
                    if (string.IsNullOrEmpty(ef)) continue;
                    IncludeInfo ii = new IncludeInfo(); ii.Name = Path.GetFileName(ef); ii.Extra = true;
                    if (!File.Exists(ef)) { s.Report.Add(ii); continue; }
                    string full = Path.GetFullPath(ef);
                    if (!seen.Add(full)) { ii.Path = full; s.Report.Add(ii); continue; }
                    try { s.AddFile(ef, byName); ii.Path = full; }
                    catch (Exception ex) { s.Warnings.Add(Path.GetFileName(ef) + ": " + ex.Message); }
                    s.Report.Add(ii);
                }

            for (int f = 0; f < s.Files.Count; f++)
            {
                AnimFile af = s.Files[f];
                for (int i = 0; i < af.Seqs.Length; i++)
                {
                    SeqDesc sd = af.Seqs[i];
                    SeqRef r = new SeqRef();
                    r.File = f; r.Index = i; r.Desc = sd; r.Name = sd.Name; r.Activity = sd.Activity;
                    r.Source = f == 0 ? "" : Path.GetFileName(af.Path);
                    r.Looping = (sd.Flags & 1) != 0;
                    if (sd.Blends.Length > 0 && sd.Blends[0] >= 0 && sd.Blends[0] < af.Anims.Length)
                    {
                        AnimDesc ad = af.Anims[sd.Blends[0]];
                        r.Frames = ad.NumFrames; r.Fps = ad.Fps;
                        r.Duration = ad.NumFrames > 1 ? (ad.NumFrames - 1) / ad.Fps : 0f;
                        r.Additive = (ad.Flags & 4) != 0 || (sd.Flags & 4) != 0;
                        r.Missing = ad.Block != 0 && ad.SectionFrames == 0;
                    }
                    else r.Missing = true;
                    s.Seqs.Add(r);
                }
            }
            foreach (IncludeInfo ii in s.Report)
                if (ii.Path != null)
                    for (int f = 1; f < s.Files.Count; f++)
                        if (string.Equals(Path.GetFullPath(s.Files[f].Path), ii.Path, StringComparison.OrdinalIgnoreCase)) ii.Seqs = s.Files[f].Seqs.Length;
            return s;
        }

        private void AddFile(string path, Dictionary<string, int> mainBones)
        {
            AnimFile af = AnimFile.Load(path);
            int[] map = new int[af.Bones.Length];
            for (int i = 0; i < map.Length; i++)
            {
                int dst;
                map[i] = mainBones.TryGetValue(af.Bones[i].Name, out dst) ? dst : -1;
            }
            Files.Add(af); Maps.Add(map);
        }

        private static int[] Identity(int n)
        {
            int[] m = new int[n];
            for (int i = 0; i < n; i++) m[i] = i;
            return m;
        }

        private void AddIncludes(AnimFile f, List<string> roots, Dictionary<string, int> mainBones, HashSet<string> seen, int depth)
        {
            if (depth > 3) return;
            foreach (string inc in f.Includes)
            {
                string rel = inc.Replace('/', '\\').TrimStart('\\');
                string found = null;
                foreach (string root in roots)
                {
                    string c = Path.Combine(root, rel);
                    if (File.Exists(c)) { found = c; break; }
                }
                IncludeInfo ii = new IncludeInfo(); ii.Name = inc;
                if (found == null) { Warnings.Add(Loc.T("Не найден подключённый файл анимаций: ") + inc); Report.Add(ii); continue; }
                string full = Path.GetFullPath(found);
                ii.Path = full;
                Report.Add(ii);
                if (!seen.Add(full)) continue;
                try
                {
                    AddFile(found, mainBones);
                    AddIncludes(Files[Files.Count - 1], roots, mainBones, seen, depth + 1);
                }
                catch (Exception ex) { Warnings.Add(Path.GetFileName(found) + ": " + ex.Message); }
            }
        }

        // ------------------------------------------------------------------
        // Матрицы 3x4 (строки по 4 числа: [R | t])
        // ------------------------------------------------------------------
        public static void QuatToMat(float[] q, float[] m, int o)
        {
            float x = q[0], y = q[1], z = q[2], w = q[3];
            m[o + 0] = 1 - 2 * y * y - 2 * z * z; m[o + 1] = 2 * x * y - 2 * w * z; m[o + 2] = 2 * x * z + 2 * w * y;
            m[o + 4] = 2 * x * y + 2 * w * z; m[o + 5] = 1 - 2 * x * x - 2 * z * z; m[o + 6] = 2 * y * z - 2 * w * x;
            m[o + 8] = 2 * x * z - 2 * w * y; m[o + 9] = 2 * y * z + 2 * w * x; m[o + 10] = 1 - 2 * x * x - 2 * y * y;
        }

        // r = a * b  (3x4 * 3x4), r не должен совпадать с a или b
        public static void Mul(float[] a, int ao, float[] b, int bo, float[] r, int ro)
        {
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++)
                    r[ro + i * 4 + j] = a[ao + i * 4] * b[bo + j] + a[ao + i * 4 + 1] * b[bo + 4 + j] + a[ao + i * 4 + 2] * b[bo + 8 + j];
                r[ro + i * 4 + 3] = a[ao + i * 4] * b[bo + 3] + a[ao + i * 4 + 1] * b[bo + 7] + a[ao + i * 4 + 2] * b[bo + 11] + a[ao + i * 4 + 3];
            }
        }

        private static void AngleQuat(float ax, float ay, float az, float[] q)
        {
            float sr = (float)Math.Sin(ax * 0.5f), cr = (float)Math.Cos(ax * 0.5f);
            float sp = (float)Math.Sin(ay * 0.5f), cp = (float)Math.Cos(ay * 0.5f);
            float sy = (float)Math.Sin(az * 0.5f), cy = (float)Math.Cos(az * 0.5f);
            q[0] = sr * cp * cy - cr * sp * sy;
            q[1] = cr * sp * cy + sr * cp * sy;
            q[2] = cr * cp * sy - sr * sp * cy;
            q[3] = cr * cp * cy + sr * sp * sy;
        }

        private static void QMul(float[] a, float[] b, float[] r)
        {
            r[0] = a[3] * b[0] + a[0] * b[3] + a[1] * b[2] - a[2] * b[1];
            r[1] = a[3] * b[1] - a[0] * b[2] + a[1] * b[3] + a[2] * b[0];
            r[2] = a[3] * b[2] + a[0] * b[1] - a[1] * b[0] + a[2] * b[3];
            r[3] = a[3] * b[3] - a[0] * b[0] - a[1] * b[1] - a[2] * b[2];
        }

        private static float Half(int h)
        {
            int sign = (h >> 15) & 1, exp = (h >> 10) & 31, man = h & 1023;
            double v;
            if (exp == 0) v = man / 1024.0 * Math.Pow(2, -14);
            else if (exp == 31) v = 65504.0;
            else v = (1 + man / 1024.0) * Math.Pow(2, exp - 15);
            return (float)(sign == 1 ? -v : v);
        }

        private static float Extract(byte[] d, int abs, int frame, float scale)
        {
            int k = frame, p = abs;
            while (d[p + 1] <= k)
            {
                k -= d[p + 1];
                p += (d[p] + 1) * 2;
                if (p + 2 >= d.Length || d[p + 1] == 0) return 0;
            }
            if (d[p] > k) return (short)AnimFile.U16(d, p + (k + 1) * 2) * scale;
            return (short)AnimFile.U16(d, p + d[p] * 2) * scale;
        }

        // Локальные позы одной анимации на целом кадре. q[bone*4], p[bone*3] (по костям файла).
        private static void Decode(AnimFile f, AnimDesc a, int frame, float[] q, float[] p)
        {
            for (int b = 0; b < f.Bones.Length; b++)
            {
                for (int k = 0; k < 4; k++) q[b * 4 + k] = f.Bones[b].Quat[k];
                for (int k = 0; k < 3; k++) p[b * 3 + k] = f.Bones[b].Pos[k];
            }
            byte[] d = f.D;
            int off = ChainStart(f, a, ref frame);
            if (off < 0) return;
            float[] tq = new float[4], bq = new float[4];
            int guard = 0;
            while (off >= 0 && off + 4 <= d.Length && guard++ < 4096)
            {
                int bone = d[off], flags = d[off + 1], next = (short)AnimFile.U16(d, off + 2);
                if (bone < f.Bones.Length)
                {
                    AnimBone bd = f.Bones[bone];
                    bool delta = (flags & 0x10) != 0;
                    int cur = off + 4;
                    // вращение
                    if ((flags & 0x02) != 0)            // RAWROT: Quaternion48
                    {
                        int x = AnimFile.U16(d, cur), y = AnimFile.U16(d, cur + 2), zr = AnimFile.U16(d, cur + 4);
                        tq[0] = (x - 32768) * (1f / 32768f); tq[1] = (y - 32768) * (1f / 32768f); tq[2] = ((zr & 0x7FFF) - 16384) * (1f / 16384f);
                        float w2 = 1f - tq[0] * tq[0] - tq[1] * tq[1] - tq[2] * tq[2];
                        tq[3] = (float)Math.Sqrt(w2 > 0 ? w2 : 0); if ((zr & 0x8000) != 0) tq[3] = -tq[3];
                        Store(q, bone, tq, delta, bd);
                        cur += 6;
                    }
                    else if ((flags & 0x20) != 0)       // RAWROT2: Quaternion64
                    {
                        ulong v = BitConverter.ToUInt64(d, cur);
                        long x = (long)(v & 0x1FFFFF), y = (long)((v >> 21) & 0x1FFFFF), z = (long)((v >> 42) & 0x1FFFFF);
                        tq[0] = (float)((x - 1048576) * (1.0 / 1048576.5)); tq[1] = (float)((y - 1048576) * (1.0 / 1048576.5)); tq[2] = (float)((z - 1048576) * (1.0 / 1048576.5));
                        float w2 = 1f - tq[0] * tq[0] - tq[1] * tq[1] - tq[2] * tq[2];
                        tq[3] = (float)Math.Sqrt(w2 > 0 ? w2 : 0); if ((v >> 63) != 0) tq[3] = -tq[3];
                        Store(q, bone, tq, delta, bd);
                        cur += 8;
                    }
                    else if ((flags & 0x08) != 0)       // ANIMROT: RLE значения углов
                    {
                        float[] ang = new float[3];
                        for (int j = 0; j < 3; j++)
                        {
                            int vo = (short)AnimFile.U16(d, cur + j * 2);
                            float bas = delta ? 0f : bd.Rot[j];
                            ang[j] = vo > 0 ? Extract(d, cur + vo, frame, bd.RotScale[j]) + bas : bas;
                        }
                        AngleQuat(ang[0], ang[1], ang[2], tq);
                        Store(q, bone, tq, delta, bd);
                        cur += 6;
                    }
                    // позиция
                    if ((flags & 0x01) != 0)            // RAWPOS: Vector48
                    {
                        float x = Half(AnimFile.U16(d, cur)), y = Half(AnimFile.U16(d, cur + 2)), z = Half(AnimFile.U16(d, cur + 4));
                        SetPos(p, bone, x, y, z, delta, bd, false);
                    }
                    else if ((flags & 0x04) != 0)       // ANIMPOS: RLE
                    {
                        float[] v = new float[3];
                        for (int j = 0; j < 3; j++)
                        {
                            int vo = (short)AnimFile.U16(d, cur + j * 2);
                            v[j] = vo > 0 ? Extract(d, cur + vo, frame, bd.PosScale[j]) : 0f;
                        }
                        SetPos(p, bone, v[0], v[1], v[2], delta, bd, true);
                    }
                }
                if (next == 0) break;
                off += next;
            }
            // сдвиг кадра в секции (frame после ChainStart) учтён при чтении Extract
        }

        private static void Store(float[] q, int bone, float[] src, bool delta, AnimBone bd)
        {
            if (delta)
            {
                float[] r = new float[4];
                QMul(bd.Quat, src, r);
                for (int k = 0; k < 4; k++) q[bone * 4 + k] = r[k];
            }
            else for (int k = 0; k < 4; k++) q[bone * 4 + k] = src[k];
        }

        // RAWPOS - готовая позиция, ANIMPOS - смещение от позиции кости (если не delta)
        private static void SetPos(float[] p, int bone, float x, float y, float z, bool delta, AnimBone bd, bool animated)
        {
            bool addBase = delta || animated;
            p[bone * 3] = addBase ? bd.Pos[0] + x : x;
            p[bone * 3 + 1] = addBase ? bd.Pos[1] + y : y;
            p[bone * 3 + 2] = addBase ? bd.Pos[2] + z : z;
        }

        private static int ChainStart(AnimFile f, AnimDesc a, ref int frame)
        {
            byte[] d = f.D;
            if (a.SectionFrames != 0 && a.SectionIndex != 0)
            {
                int section;
                if (a.NumFrames > a.SectionFrames && frame == a.NumFrames - 1) { frame = 0; section = a.NumFrames / a.SectionFrames + 1; }
                else { section = frame / a.SectionFrames; frame -= section * a.SectionFrames; }
                int so = a.Base + a.SectionIndex + section * 8;
                if (so + 8 > d.Length) return -1;
                int block = AnimFile.I(d, so), idx = AnimFile.I(d, so + 4);
                if (block != 0) return -1;
                return a.Base + idx;
            }
            if (a.Block != 0) return -1;
            return a.Base + a.Index;
        }

        // ------------------------------------------------------------------
        // Расчёт позы секвенции
        // ------------------------------------------------------------------
        private static float ParamValue(AnimFile f, int idx, Dictionary<string, float> pnorm)
        {
            if (idx < 0 || idx >= f.Params.Length) return 0f;
            PoseParam pp = f.Params[idx];
            float n;
            if (!pnorm.TryGetValue(pp.Name, out n)) n = 0.5f;
            return pp.Start + n * (pp.End - pp.Start);
        }

        private static void Axis(float v, float start, float end, int group, out int i0, out float frac)
        {
            i0 = 0; frac = 0f;
            if (group <= 1) return;
            float range = end - start;
            float t = Math.Abs(range) < 1e-6f ? 0f : (v - start) / range;
            t = Math.Max(0f, Math.Min(1f, t));
            float s = t * (group - 1);
            i0 = Math.Min((int)Math.Floor(s), group - 2);
            frac = s - i0;
        }

        // world и skin - массивы 12*BoneCount; cycle 0..1; возвращает false, если данных нет
        public bool Evaluate(SeqRef sr, float cycle, Dictionary<string, float> pnorm, float[] world, float[] skin)
        {
            int nb = BoneCount;
            float[] aq = new float[nb * 4], ap = new float[nb * 3];
            if (!EvalPose(sr, cycle, pnorm, aq, ap)) return false;
            Pose(aq, ap, world, skin);
            return true;
        }

        // локальные позы секвенции в костях основной модели; aq/ap должны быть нулевыми
        private bool EvalPose(SeqRef sr, float cycle, Dictionary<string, float> pnorm, float[] aq, float[] ap)
        {
            int nb = BoneCount;
            AnimFile f = Files[sr.File];
            int[] map = Maps[sr.File];
            SeqDesc sd = sr.Desc;
            float wsum = 0f;
            bool any = false;

            List<int> idx = new List<int>(); List<float> wts = new List<float>();
            if (sd.Blends.Length <= 1) { idx.Add(0); wts.Add(1f); }
            else
            {
                int ix, iy; float fx, fy;
                Axis(ParamValue(f, sd.ParamX, pnorm), sd.StartX, sd.EndX, sd.GroupX, out ix, out fx);
                Axis(ParamValue(f, sd.ParamY, pnorm), sd.StartY, sd.EndY, sd.GroupY, out iy, out fy);
                for (int dy = 0; dy <= (sd.GroupY > 1 ? 1 : 0); dy++)
                    for (int dx = 0; dx <= (sd.GroupX > 1 ? 1 : 0); dx++)
                    {
                        float w = (dx == 0 ? 1f - fx : fx) * (dy == 0 ? 1f - fy : fy);
                        int bi = (iy + dy) * sd.GroupX + (ix + dx);
                        if (w > 1e-4f && bi < sd.Blends.Length) { idx.Add(bi); wts.Add(w); }
                    }
            }

            float[] q0 = new float[f.Bones.Length * 4], p0 = new float[f.Bones.Length * 3];
            float[] q1 = new float[f.Bones.Length * 4], p1 = new float[f.Bones.Length * 3];
            float[] dq = new float[nb * 4], dp = new float[nb * 3];
            for (int e = 0; e < idx.Count; e++)
            {
                int ai = sd.Blends[idx[e]];
                if (ai < 0 || ai >= f.Anims.Length) continue;
                AnimDesc ad = f.Anims[ai];
                if (ad.Block != 0 && ad.SectionFrames == 0) continue;
                float fr = Math.Max(0f, Math.Min(1f, cycle)) * (ad.NumFrames - 1);
                int f0 = (int)Math.Floor(fr), f1 = Math.Min(f0 + 1, ad.NumFrames - 1);
                float t = fr - f0;
                Decode(f, ad, f0, q0, p0);
                if (f1 != f0 && t > 1e-4f)
                {
                    Decode(f, ad, f1, q1, p1);
                    for (int b = 0; b < f.Bones.Length; b++)
                    {
                        float dot = q0[b * 4] * q1[b * 4] + q0[b * 4 + 1] * q1[b * 4 + 1] + q0[b * 4 + 2] * q1[b * 4 + 2] + q0[b * 4 + 3] * q1[b * 4 + 3];
                        float sgn = dot < 0 ? -1f : 1f;
                        float len = 0;
                        for (int k = 0; k < 4; k++) { q0[b * 4 + k] = q0[b * 4 + k] * (1 - t) + sgn * q1[b * 4 + k] * t; len += q0[b * 4 + k] * q0[b * 4 + k]; }
                        len = (float)Math.Sqrt(len); if (len > 1e-6f) for (int k = 0; k < 4; k++) q0[b * 4 + k] /= len;
                        for (int k = 0; k < 3; k++) p0[b * 3 + k] = p0[b * 3 + k] * (1 - t) + p1[b * 3 + k] * t;
                    }
                }
                // перенос на кости основной модели
                float[] tq = new float[nb * 4], tp = new float[nb * 3];
                for (int b = 0; b < nb; b++)
                {
                    for (int k = 0; k < 4; k++) tq[b * 4 + k] = Main.Bones[b].Quat[k];
                    for (int k = 0; k < 3; k++) tp[b * 3 + k] = Main.Bones[b].Pos[k];
                }
                for (int sb = 0; sb < f.Bones.Length; sb++)
                {
                    int db = map[sb];
                    if (db < 0) continue;
                    for (int k = 0; k < 4; k++) tq[db * 4 + k] = q0[sb * 4 + k];
                    for (int k = 0; k < 3; k++) tp[db * 3 + k] = p0[sb * 3 + k];
                }
                float w2 = wts[e];
                for (int b = 0; b < nb; b++)
                {
                    float dot = 0;
                    if (wsum > 0) dot = aq[b * 4] * tq[b * 4] + aq[b * 4 + 1] * tq[b * 4 + 1] + aq[b * 4 + 2] * tq[b * 4 + 2] + aq[b * 4 + 3] * tq[b * 4 + 3];
                    float sgn = dot < 0 ? -1f : 1f;
                    for (int k = 0; k < 4; k++) aq[b * 4 + k] += sgn * tq[b * 4 + k] * w2;
                    for (int k = 0; k < 3; k++) ap[b * 3 + k] += tp[b * 3 + k] * w2;
                }
                wsum += w2; any = true;
            }
            if (!any) return false;
            for (int b = 0; b < nb; b++)
            {
                float len = (float)Math.Sqrt(aq[b * 4] * aq[b * 4] + aq[b * 4 + 1] * aq[b * 4 + 1] + aq[b * 4 + 2] * aq[b * 4 + 2] + aq[b * 4 + 3] * aq[b * 4 + 3]);
                if (len > 1e-6f) for (int k = 0; k < 4; k++) aq[b * 4 + k] /= len;
                for (int k = 0; k < 3; k++) ap[b * 3 + k] /= wsum;
            }
            return true;
        }

        // ------------------------------------------------------------------
        // Слои поверх основной секвенции
        // ------------------------------------------------------------------
        private static void QSlerp(float[] a, int ao, float[] b, int bo, float t, float[] r)
        {
            float dot = a[ao] * b[bo] + a[ao + 1] * b[bo + 1] + a[ao + 2] * b[bo + 2] + a[ao + 3] * b[bo + 3];
            float sg = dot < 0 ? -1f : 1f; dot *= sg;
            float k0, k1;
            if (dot > 0.9995f) { k0 = 1f - t; k1 = t * sg; }
            else
            {
                float om = (float)Math.Acos(Math.Min(1f, dot)), so = (float)Math.Sin(om);
                k0 = (float)Math.Sin((1f - t) * om) / so; k1 = (float)Math.Sin(t * om) / so * sg;
            }
            float len = 0;
            for (int k = 0; k < 4; k++) { r[k] = a[ao + k] * k0 + b[bo + k] * k1; len += r[k] * r[k]; }
            len = (float)Math.Sqrt(len);
            if (len > 1e-6f) for (int k = 0; k < 4; k++) r[k] /= len;
        }

        // веса костей слоя (в костях основной модели)
        public float[] LayerWeights(LayerSpec l)
        {
            int nb = BoneCount;
            float[] w = new float[nb];
            if (l.Mask == 2 && l.Root >= 0 && l.Root < nb)
            {
                for (int b = 0; b < nb; b++)
                {
                    int c = b, guard = 0;
                    while (c >= 0 && guard++ < 256) { if (c == l.Root) { w[b] = 1f; break; } c = Main.Bones[c].Parent; }
                }
            }
            else if (l.Mask == 0 && l.Seq != null && l.Seq.Desc.WeightList != null)
            {
                float[] wl = l.Seq.Desc.WeightList; int[] map = Maps[l.Seq.File];
                for (int sb = 0; sb < wl.Length && sb < map.Length; sb++) if (map[sb] >= 0) w[map[sb]] = wl[sb];
            }
            else for (int b = 0; b < nb; b++) w[b] = 1f;
            return w;
        }

        // infl[b] - итоговое влияние слоёв на кость (0..1), diff[b] - кость в слоях реально отличается от основной позы
        public bool EvaluateLayers(SeqRef baseSeq, float cycle, List<LayerSpec> layers, Dictionary<string, float> pnorm, float[] world, float[] skin, float[] infl, bool[] diff)
        {
            int nb = BoneCount;
            float[] aq = new float[nb * 4], ap = new float[nb * 3];
            bool ok = baseSeq != null && !baseSeq.Missing && EvalPose(baseSeq, cycle, pnorm, aq, ap);
            if (!ok)
                for (int b = 0; b < nb; b++)
                {
                    for (int k = 0; k < 4; k++) aq[b * 4 + k] = Main.Bones[b].Quat[k];
                    for (int k = 0; k < 3; k++) ap[b * 3 + k] = Main.Bones[b].Pos[k];
                }
            for (int b = 0; b < nb; b++) { infl[b] = 0; diff[b] = false; }
            float[] tq = new float[4], dq = new float[4], sq = new float[4], rq = new float[4], lb = new float[4], cur = new float[4];
            float[] ident = { 0, 0, 0, 1 };
            bool any = false;
            foreach (LayerSpec l in layers)
            {
                if (l.Seq == null || l.Seq.Missing) continue;
                float lc = l.Sync ? cycle : l.Cycle;
                float[] lq = new float[nb * 4], lp = new float[nb * 3];
                if (!EvalPose(l.Seq, lc, pnorm, lq, lp)) continue;
                bool add = l.Mode == 2 || (l.Mode == 0 && l.Seq.Additive);
                float[] refq = null, refp = null;
                if (add)
                {
                    refq = new float[nb * 4]; refp = new float[nb * 3];
                    if (l.Mode == 2 && !l.Seq.Additive) EvalPose(l.Seq, 0f, pnorm, refq, refp);
                    else
                        for (int b = 0; b < nb; b++)
                        {
                            for (int k = 0; k < 4; k++) refq[b * 4 + k] = Main.Bones[b].Quat[k];
                            for (int k = 0; k < 3; k++) refp[b * 3 + k] = Main.Bones[b].Pos[k];
                        }
                }
                float[] bw = LayerWeights(l);
                for (int b = 0; b < nb; b++)
                {
                    float s = l.Weight * bw[b];
                    if (s <= 0.0001f) continue;
                    any = true;
                    if (s > infl[b]) infl[b] = s;
                    float pd = 0, dot;
                    for (int k = 0; k < 3; k++) { float dd = lp[b * 3 + k] - (add ? refp[b * 3 + k] : ap[b * 3 + k]); pd += dd * dd; }
                    Array.Copy(lq, b * 4, lb, 0, 4);
                    if (add)
                    {
                        Array.Copy(refq, b * 4, tq, 0, 4); tq[0] = -tq[0]; tq[1] = -tq[1]; tq[2] = -tq[2];
                        QMul(tq, lb, dq);
                        dot = Math.Abs(dq[3]);
                    }
                    else dot = Math.Abs(aq[b * 4] * lb[0] + aq[b * 4 + 1] * lb[1] + aq[b * 4 + 2] * lb[2] + aq[b * 4 + 3] * lb[3]);
                    if (dot < 0.99995f || pd > 0.0025f) diff[b] = true;
                    float st = Math.Min(1f, s);
                    if (!add)
                    {
                        QSlerp(aq, b * 4, lq, b * 4, st, rq);
                        for (int k = 0; k < 4; k++) aq[b * 4 + k] = rq[k];
                        for (int k = 0; k < 3; k++) ap[b * 3 + k] += (lp[b * 3 + k] - ap[b * 3 + k]) * st;
                    }
                    else
                    {
                        QSlerp(ident, 0, dq, 0, st, sq);
                        Array.Copy(aq, b * 4, cur, 0, 4);
                        QMul(cur, sq, rq);
                        for (int k = 0; k < 4; k++) aq[b * 4 + k] = rq[k];
                        for (int k = 0; k < 3; k++) ap[b * 3 + k] += (lp[b * 3 + k] - refp[b * 3 + k]) * s;
                    }
                }
            }
            Pose(aq, ap, world, skin);
            return ok || any;
        }

        public void BindPose(float[] world, float[] skin)
        {
            int nb = BoneCount;
            float[] q = new float[nb * 4], p = new float[nb * 3];
            for (int b = 0; b < nb; b++)
            {
                for (int k = 0; k < 4; k++) q[b * 4 + k] = Main.Bones[b].Quat[k];
                for (int k = 0; k < 3; k++) p[b * 3 + k] = Main.Bones[b].Pos[k];
            }
            Pose(q, p, world, skin);
        }

        // локальные позы -> мировые матрицы костей и матрицы скиннинга
        private void Pose(float[] q, float[] p, float[] world, float[] skin)
        {
            int nb = BoneCount;
            float[] local = new float[12];
            for (int b = 0; b < nb; b++)
            {
                for (int k = 0; k < 12; k++) local[k] = 0;
                QuatToMat(new float[] { q[b * 4], q[b * 4 + 1], q[b * 4 + 2], q[b * 4 + 3] }, local, 0);
                local[3] = p[b * 3]; local[7] = p[b * 3 + 1]; local[11] = p[b * 3 + 2];
                int par = Main.Bones[b].Parent;
                if (par < 0 || par >= b) Array.Copy(local, 0, world, b * 12, 12);
                else Mul(world, par * 12, local, 0, world, b * 12);
            }
            float[] tmp = new float[12];
            for (int b = 0; b < nb; b++)
            {
                Mul(world, b * 12, Main.Bones[b].PoseToBone, 0, tmp, 0);
                Array.Copy(tmp, 0, skin, b * 12, 12);
            }
        }

        public static void SkinVertices(MdlData m, float[] skin, float[] outPos, float[] outNrm)
        {
            int n = m.VertexCount;
            for (int i = 0; i < n; i++)
            {
                float px = m.Pos[i * 3], py = m.Pos[i * 3 + 1], pz = m.Pos[i * 3 + 2];
                float nx = m.Nrm[i * 3], ny = m.Nrm[i * 3 + 1], nz = m.Nrm[i * 3 + 2];
                float ox = 0, oy = 0, oz = 0, qx = 0, qy = 0, qz = 0;
                int cnt = m.NumBones[i];
                if (cnt <= 0) cnt = 1;
                for (int k = 0; k < cnt && k < 3; k++)
                {
                    float w = k == 0 && m.NumBones[i] == 0 ? 1f : m.Weights[i * 3 + k];
                    if (w <= 0f) continue;
                    int bo = m.BoneIdx[i * 3 + k] * 12;
                    if (bo + 12 > skin.Length) continue;
                    ox += w * (skin[bo] * px + skin[bo + 1] * py + skin[bo + 2] * pz + skin[bo + 3]);
                    oy += w * (skin[bo + 4] * px + skin[bo + 5] * py + skin[bo + 6] * pz + skin[bo + 7]);
                    oz += w * (skin[bo + 8] * px + skin[bo + 9] * py + skin[bo + 10] * pz + skin[bo + 11]);
                    qx += w * (skin[bo] * nx + skin[bo + 1] * ny + skin[bo + 2] * nz);
                    qy += w * (skin[bo + 4] * nx + skin[bo + 5] * ny + skin[bo + 6] * nz);
                    qz += w * (skin[bo + 8] * nx + skin[bo + 9] * ny + skin[bo + 10] * nz);
                }
                outPos[i * 3] = ox; outPos[i * 3 + 1] = oy; outPos[i * 3 + 2] = oz;
                outNrm[i * 3] = qx; outNrm[i * 3 + 1] = qy; outNrm[i * 3 + 2] = qz;
            }
        }
    }
}
