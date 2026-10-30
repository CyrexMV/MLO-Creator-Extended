// Collision for a prop whose model carries none (a kitchen, a counter, shelves, a bed: their collision is in the files of the game's interior they come from),
// made from the model's own shape: boxes in the model's frame. A small prop (up to 1.5 m across) gets its box. A bigger one is looked at from above in
// cells of 25 cm: in each cell the heights where the model has faces are put into groups (a gap of more than 60 cm parts them), and each group becomes a
// box: one that does not reach higher than 1.3 m over the model's bottom stands on the bottom (a counter, a table, a bed: solid down to the floor), a higher
// one hangs where it is (a wall cupboard, a shelf, a wall); a flat group alone (the floor or the ceiling of a model of a whole room) is left out. Cells
// with the same boxes are joined into bigger boxes. C# 5.
using System;
using System.Collections.Generic;
using System.IO;
using CodeWalker.GameFiles;
using SharpDX;

namespace MloCreator
{
    public static partial class Vanilla
    {
        static Dictionary<string, List<double[]>> propColCache = new Dictionary<string, List<double[]>>();

        // ---------------- an archetype's flags (from the map types file that defines it): 0x04000000 with 0x00020000 is a door the game moves (it swings) ----------------
        static Dictionary<string, Dictionary<uint, uint>> ytypFlags = new Dictionary<string, Dictionary<uint, uint>>();
        public static bool ArchetypeFlags(string name, out uint flags)
        {
            flags = 0; uint h = JenkHash.GenHash(name.ToLowerInvariant()); string yt = YtypOf(h); if (yt == null) return false;
            Dictionary<uint, uint> fl;
            lock (ytypFlags) ytypFlags.TryGetValue(yt, out fl);
            if (fl == null)
            {
                fl = new Dictionary<uint, uint>();
                try { RpfFileEntry fe; byte[] b = Bytes(yt, out fe); YtypFile y = new YtypFile(); y.Load(b, fe); if (y.AllArchetypes != null) foreach (Archetype a in y.AllArchetypes) if (a != null) fl[a.Hash.Hash] = a._BaseArchetypeDef.flags; } catch (Exception) { }
                lock (ytypFlags) ytypFlags[yt] = fl;
            }
            return fl.TryGetValue(h, out flags);
        }
        public static bool IsWorkingDoor(string name) { uint f; return ArchetypeFlags(name, out f) && (f & 0x04000000) != 0 && (f & 0x00020000) != 0; }

        // ---------------- which game build a prop needs: the DLC its model comes from (FiveM's sv_enforceGameBuild) ----------------
        static readonly string[][] DlcBuilds = {
            new[] { "2060", "mpvinewood", "mpheist3", "mpsum" }, new[] { "2189", "mpheist4" }, new[] { "2372", "mptuner" }, new[] { "2545", "mpsecurity" },
            new[] { "2699", "mpsum2" }, new[] { "2802", "mpchristmas3" }, new[] { "2944", "mp2023_01" }, new[] { "3095", "mp2023_02" }, new[] { "3258", "mp2024_01" },
            new[] { "3407", "mp2024_02" }, new[] { "3570", "mp2025_01" }, new[] { "3751", "mp2025_02" }, new[] { "3889", "mp2026_01" } };          // (FiveM's builds, docs.fivem.net: sv_enforceGameBuild)
        // the DLC pack a model comes from (its archive's path: ...\dlcpacks\<pack>\...), or "" for the base game; build: the game build it needs (1604: any)
        public static string DlcOf(string name, out int build)
        {
            build = 1604; string k = name.ToLowerInvariant(); RpfFile o = null;
            if (!own.TryGetValue(k + ".ydr", out o) && !own.TryGetValue(k + ".yft", out o)) return "";
            string p = (o.Path ?? "").ToLowerInvariant().Replace('/', '\\'); int i = p.IndexOf("dlcpacks\\"); if (i < 0) return "";
            string pack = p.Substring(i + 9); int j = pack.IndexOf('\\'); if (j > 0) pack = pack.Substring(0, j);
            foreach (string[] r in DlcBuilds) for (int x = 1; x < r.Length; x++) if (pack == r[x] || pack.StartsWith(r[x] + "_")) { build = int.Parse(r[0]); return pack; }
            // (a later DLC than the ones listed: mpYYYY_NN after 2025_01 needs a newer build than 3570)
            System.Text.RegularExpressions.Match mm = System.Text.RegularExpressions.Regex.Match(pack, "^(mp|patch)(20\\d\\d)_(\\d\\d)");
            if (mm.Success) { int y = int.Parse(mm.Groups[2].Value), n = int.Parse(mm.Groups[3].Value); if (y * 10 + n > 20261) build = 9999; else if (y * 10 + n >= 20231) { foreach (string[] r in DlcBuilds) if (r[1] == "mp" + y + "_" + mm.Groups[3].Value) build = int.Parse(r[0]); } }
            return pack;
        }
        // the game build the server forces (sv_enforceGameBuild in a server.cfg up to four folders above the resources), 0: not found
        public static int ServerBuild(string outDir)
        {
            try
            {
                for (string d = outDir; !string.IsNullOrEmpty(d) && d.Length > 3; d = Path.GetDirectoryName(d))
                {
                    string f = Path.Combine(d, "server.cfg"); if (!File.Exists(f)) continue;
                    foreach (string l in File.ReadAllLines(f)) { System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(l, "^\\s*(set\\s+)?sv_enforceGameBuild\\s+\"?(\\d+)"); if (m.Success) return int.Parse(m.Groups[2].Value); }
                    return 1604;          // (FiveM's own build when the server sets none)
                }
            }
            catch (Exception) { }
            return 0;
        }

        // The lights built into a prop's model (a lamp's), in its frame: p position, c colour, k brightness, f range (falloff, m), e softness (falloff exponent),
        // t "Point" / "Spot" / "Capsule", d a spot's direction, cone its outer angle (degrees). A model that breaks or moves (.yft) has them in its fragment.
        public static List<object> LightsOf(string name)
        {
            List<object> res = new List<object>(); string k = name.ToLowerInvariant();
            try
            {
                LightAttributes[] ls = null; RpfFileEntry fe; byte[] b;
                if (Has(k + ".ydr")) { b = Bytes(k + ".ydr", out fe); YdrFile y = new YdrFile(); y.Load(b, fe); Drawable d = y.Drawable; if (d != null && d.LightAttributes != null) ls = d.LightAttributes.data_items; }
                else if (Has(k + ".yft")) { b = Bytes(k + ".yft", out fe); YftFile y = new YftFile(); y.Load(b, fe); if (y.Fragment != null && y.Fragment.LightAttributes != null) ls = y.Fragment.LightAttributes.data_items; }
                if (ls == null) return res;
                foreach (LightAttributes l in ls)
                {
                    if (l == null) continue; Dictionary<string, object> o = new Dictionary<string, object>();
                    o["p"] = new double[] { l.Position.X, l.Position.Y, l.Position.Z }; o["c"] = new int[] { l.ColorR, l.ColorG, l.ColorB }; o["k"] = l.Intensity; o["f"] = l.Falloff; o["e"] = l.FalloffExponent;
                    o["t"] = l.Type.ToString(); o["d"] = new double[] { l.Direction.X, l.Direction.Y, l.Direction.Z }; o["cone"] = l.ConeOuterAngle; res.Add(o);
                }
            }
            catch (Exception) { }
            return res;
        }

        // [cx, cy, cz, hx, hy, hz] in the model's frame (not turned); null: the model is not there
        public static List<double[]> PropCollisionBoxes(string name)
        {
            string k = name.ToLowerInvariant(); List<double[]> res;
            lock (propColCache) if (propColCache.TryGetValue(k, out res)) return res;
            res = MakeColBoxes(k);
            lock (propColCache) propColCache[k] = res;
            return res;
        }

        static List<double[]> MakeColBoxes(string k)
        {
            string f; long sz; DrawableBase d;
            try { d = OpenDrawable(k, out f, out sz); } catch (Exception) { return null; }
            if (d == null || d.DrawableModels == null) return null;
            DrawableModel[] ms = d.DrawableModels.High ?? d.DrawableModels.Med ?? d.DrawableModels.Low; if (ms == null) return null;
            // the model's triangles (the position is the first thing of every vertex)
            List<float[]> tris = new List<float[]>(); Vector3 mn = new Vector3(1e9f), mx = new Vector3(-1e9f);
            foreach (DrawableModel m in ms)
            {
                if (m == null || m.Geometries == null) continue;
                foreach (DrawableGeometry g in m.Geometries)
                {
                    if (g == null || g.VertexData == null || g.VertexData.VertexBytes == null || g.IndexBuffer == null || g.IndexBuffer.Indices == null) continue;
                    byte[] vb = g.VertexData.VertexBytes; int st = g.VertexData.VertexStride, nv = g.VertexData.VertexCount; ushort[] ix = g.IndexBuffer.Indices;
                    if (st < 12) continue;
                    for (int t = 0; t + 2 < ix.Length; t += 3)
                    {
                        float[] tr = new float[9]; bool ok = true;
                        for (int c = 0; c < 3; c++)
                        {
                            int v = ix[t + c]; if (v >= nv || v * st + 12 > vb.Length) { ok = false; break; }
                            for (int a = 0; a < 3; a++) tr[c * 3 + a] = BitConverter.ToSingle(vb, v * st + a * 4);
                        }
                        if (!ok) continue; tris.Add(tr);
                        for (int c = 0; c < 3; c++) { mn = Vector3.Min(mn, new Vector3(tr[c * 3], tr[c * 3 + 1], tr[c * 3 + 2])); mx = Vector3.Max(mx, new Vector3(tr[c * 3], tr[c * 3 + 1], tr[c * 3 + 2])); }
                    }
                }
            }
            List<double[]> res = new List<double[]>(); if (tris.Count == 0) return res;
            double W = mx.X - mn.X, D = mx.Y - mn.Y, bot = mn.Z;
            if (W <= 1.5 && D <= 1.5)
            {
                // a small prop: its box, 2 cm smaller on the sides, 2 cm over its bottom
                double hx = W / 2 - 0.02, hy = D / 2 - 0.02, z0 = mn.Z + 0.02, z1 = mx.Z;
                if (hx > 0.01 && hy > 0.01 && z1 - z0 > 0.03) res.Add(new double[] { (mn.X + mx.X) / 2, (mn.Y + mx.Y) / 2, (z0 + z1) / 2, hx, hy, (z1 - z0) / 2 });
                return res;
            }
            for (double cell = 0.25; ; cell *= 1.5)
            {
                res = Cells(tris, mn, mx, cell, bot);
                if (res.Count <= 256 || cell > 2) return res;
            }
        }

        static List<double[]> Cells(List<float[]> tris, Vector3 mn, Vector3 mx, double cell, double bot)
        {
            int nx = Math.Max(1, (int)Math.Ceiling((mx.X - mn.X) / cell)), ny = Math.Max(1, (int)Math.Ceiling((mx.Y - mn.Y) / cell));
            List<float>[,] zs = new List<float>[nx, ny];
            Action<double, double, float> add = (x, y, z) =>
            {
                int i = (int)((x - mn.X) / cell), j = (int)((y - mn.Y) / cell); if (i < 0 || j < 0 || i >= nx || j >= ny) return;
                if (zs[i, j] == null) zs[i, j] = new List<float>(); zs[i, j].Add(z);
            };
            foreach (float[] t in tris)
            {
                double ax = t[0], ay = t[1], bx = t[3], by = t[4], cx = t[6], cy = t[7];
                double area = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
                float tz0 = Math.Min(t[2], Math.Min(t[5], t[8])), tz1 = Math.Max(t[2], Math.Max(t[5], t[8]));
                if (Math.Abs(area) < 1e-4)
                {
                    // standing up (a wall, the front of a counter): along its edges, at every 25 cm of its height
                    for (int e = 0; e < 3; e++)
                    {
                        double px = t[e * 3], py = t[e * 3 + 1], qx = t[(e + 1) % 3 * 3], qy = t[(e + 1) % 3 * 3 + 1], len = Math.Sqrt((qx - px) * (qx - px) + (qy - py) * (qy - py));
                        int n = Math.Max(1, (int)(len / (cell / 2)));
                        for (int s = 0; s <= n; s++) for (float z = tz0; z <= tz1 + 1e-4f; z += 0.25f) { add(px + (qx - px) * s / n, py + (qy - py) * s / n, Math.Min(z, tz1)); if (tz1 - tz0 < 1e-4f) break; }
                        for (int s = 0; s <= n; s++) add(px + (qx - px) * s / n, py + (qy - py) * s / n, tz1);
                    }
                    continue;
                }
                // lying (a top, a shelf, a sloped face): its height at the middle of every cell under it, and at its corners
                for (int c = 0; c < 3; c++) add(t[c * 3], t[c * 3 + 1], t[c * 3 + 2]);
                int i0 = Math.Max(0, (int)((Math.Min(ax, Math.Min(bx, cx)) - mn.X) / cell)), i1 = Math.Min(nx - 1, (int)((Math.Max(ax, Math.Max(bx, cx)) - mn.X) / cell));
                int j0 = Math.Max(0, (int)((Math.Min(ay, Math.Min(by, cy)) - mn.Y) / cell)), j1 = Math.Min(ny - 1, (int)((Math.Max(ay, Math.Max(by, cy)) - mn.Y) / cell));
                for (int i = i0; i <= i1; i++) for (int j = j0; j <= j1; j++)
                    {
                        double px = mn.X + (i + 0.5) * cell, py = mn.Y + (j + 0.5) * cell;
                        double w1 = ((bx - px) * (cy - py) - (by - py) * (cx - px)) / area, w2 = ((cx - px) * (ay - py) - (cy - py) * (ax - px)) / area, w3 = 1 - w1 - w2;
                        if (w1 < -0.01 || w2 < -0.01 || w3 < -0.01) continue;
                        add(px, py, (float)(w1 * t[2] + w2 * t[5] + w3 * t[8]));
                    }
            }
            // every cell's groups of heights, as boxes: [z0, z1] in 5 cm steps
            Dictionary<long, bool[,]> byRange = new Dictionary<long, bool[,]>();
            for (int i = 0; i < nx; i++) for (int j = 0; j < ny; j++)
                {
                    List<float> l = zs[i, j]; if (l == null) continue; l.Sort();
                    int a = 0;
                    for (int b = 1; b <= l.Count; b++)
                    {
                        if (b < l.Count && l[b] - l[b - 1] <= 0.6f) continue;
                        double lo = l[a], hi = l[b - 1]; a = b;
                        double z0, z1 = hi;
                        if (hi - bot <= 1.3) { if (hi - bot < 0.08) continue; z0 = bot + 0.02; }          // (low: solid down to the bottom; the floor alone is left out)
                        else { if (hi - lo < 0.05) continue; z0 = lo; }                                     // (high: where it is; a ceiling alone is left out)
                        long key = ((long)Math.Round(z0 / 0.05) << 20) | (long)Math.Round(z1 / 0.05 + 1000);
                        bool[,] gr; if (!byRange.TryGetValue(key, out gr)) byRange[key] = gr = new bool[nx, ny]; gr[i, j] = true;
                    }
                }
            List<double[]> res = new List<double[]>();
            foreach (KeyValuePair<long, bool[,]> kv in byRange)
            {
                double z0 = (kv.Key >> 20) * 0.05, z1 = ((kv.Key & 0xFFFFF) - 1000) * 0.05; bool[,] g = kv.Value;
                for (int j = 0; j < ny; j++) for (int i = 0; i < nx; i++)
                    {
                        if (!g[i, j]) continue;
                        int i1 = i; while (i1 + 1 < nx && g[i1 + 1, j]) i1++;
                        int j1 = j; for (; j1 + 1 < ny; j1++) { bool all = true; for (int x = i; x <= i1; x++) if (!g[x, j1 + 1]) { all = false; break; } if (!all) break; }
                        for (int x = i; x <= i1; x++) for (int y = j; y <= j1; y++) g[x, y] = false;
                        double x0 = mn.X + i * cell, x1 = Math.Min(mx.X, mn.X + (i1 + 1) * cell), y0 = mn.Y + j * cell, y1 = Math.Min(mx.Y, mn.Y + (j1 + 1) * cell);
                        res.Add(new double[] { (x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2, (x1 - x0) / 2 - 0.01, (y1 - y0) / 2 - 0.01, Math.Max(0.02, (z1 - z0) / 2) });
                    }
            }
            return res;
        }
    }
}
