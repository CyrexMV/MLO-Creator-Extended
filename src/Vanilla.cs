// Reads the game's own files (through CodeWalker.Core) to grab a vanilla house: finds the map pieces around a door spot,
// extracts their triangles and collision, then HouseFit works out frame, floor level, interior outline and windows. C# 5.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using CodeWalker.GameFiles;
using SharpDX;

namespace MloCreator
{
    public class EntityHit
    {
        public string Ymap, Name, Lod; public uint Hash; public Vector3 Pos, Scale; public Quaternion Rot;
        public RpfFileEntry Ydr; public RpfFile YdrOwner; public Vector3 BMin, BMax; public bool Covers; public double Dist;
    }
    // a prop the game's map files place (plants and the like): where it stands, where the middle of it is, how wide and tall it is, which map files place it
    public class PropHit
    {
        public Quaternion Rot = Quaternion.Identity; public Vector3 Scale = new Vector3(1, 1, 1);          // how the map places it (turn, size)
        public string Name, Lod, Why, Kind; public uint Hash; public Vector3 Pos, Centre; public float R, H; public bool Sized; public List<string> Files = new List<string>();      // Why: "name", or what shows the model is a plant (its archive, shader or textures)
        public int Tris = -1, TrisAll;                 // triangles of the prop that lie inside the rooms, and all of them (-1: not counted)
    }
    public class RefTri { public double[] P = new double[9]; public int Group; }
    public class RefGroup { public string Entity, Texture; public float[] Color = new float[] { 0.62f, 0.60f, 0.56f }; }
    public class ColPoly
    {
        public int Kind;                // 0 triangle, 1 box
        public double[] P;              // triangle: 9 numbers; box: 12 numbers (4 corners as stored)
        public string File; public int Room; public string Material; public bool Hi;
    }

    public static partial class Vanilla
    {
        public static volatile string Status = "";
        static RpfManager rpf; static string gta, cache;
        static Dictionary<string, RpfFileEntry> ent = new Dictionary<string, RpfFileEntry>();
        static Dictionary<string, RpfFile> own = new Dictionary<string, RpfFile>();
        static readonly object gate = new object();
        public static bool Ready { get { return rpf != null; } }
        public static string GtaPath { get { return gta; } }
        public static string CacheDir { get { return cache; } }
        // first line of every index file in the cache: the index is built again when the game (its folder, its exe or its archives) changed
        public static string Stamp() { return gta + "|" + File.GetLastWriteTimeUtc(Path.Combine(gta, "GTA5.exe")).Ticks + "|" + ent.Count; }

        public static void Init(string gtaPath, string cacheDir)
        {
            lock (gate)
            {
                if (rpf != null && gtaPath == gta) return;
                if (!Directory.Exists(gtaPath) || !File.Exists(Path.Combine(gtaPath, "GTA5.exe"))) throw new Exception("GTA V was not found" + (string.IsNullOrEmpty(gtaPath) ? "" : " in \"" + gtaPath + "\"") + ". Put its folder in settings.json next to the program as \"gtaPath\" (or set the GTA5_PATH environment variable).");
                Status = "Reading the game archives (about 10 seconds)...";
                GTA5Keys.LoadFromPath(gtaPath, "");
                RpfManager m = new RpfManager();
                m.Init(gtaPath, delegate (string s) { }, delegate (string s) { }, false, true);
                ent = new Dictionary<string, RpfFileEntry>(); own = new Dictionary<string, RpfFile>();
                foreach (RpfFile rf in m.AllRpfs)
                {
                    if (rf.AllEntries == null) continue;
                    foreach (RpfEntry e in rf.AllEntries)
                    {
                        RpfFileEntry fe = e as RpfFileEntry; if (fe == null) continue;
                        string k = fe.Name.ToLowerInvariant(); ent[k] = fe; own[k] = rf;          // later archives (updates, dlc) win
                    }
                }
                rpf = m; gta = gtaPath; cache = cacheDir; Directory.CreateDirectory(cacheDir);
                ymapIdx = null; bboxCache.Clear(); texColor.Clear(); ytdDone.Clear(); ytdByHash = null; parentTxd = null; archYtyp = null;
                Status = "";
            }
        }

        public static RpfFileEntry GetEntry(string lowerName, out RpfFile owner) { owner = null; RpfFileEntry e; if (!ent.TryGetValue(lowerName, out e)) return null; owner = own[lowerName]; return e; }
        public static List<string> Names(string suffix) { List<string> l = new List<string>(); foreach (string k in ent.Keys) if (k.EndsWith(suffix)) l.Add(k); return l; }

        // a file as CodeWalker reads it (without its header, not compressed): the opened resource's first (see UseResource), then the game's
        public static byte[] Bytes(string name, out RpfFileEntry entry)
        {
            string k = name.ToLowerInvariant(); entry = null; string lp; if (looseRead.TryGetValue(k, out lp) || libRead.TryGetValue(k, out lp)) return LooseData(lp, k, out entry);
            RpfFileEntry e; if (!ent.TryGetValue(k, out e)) return null;
            entry = e; return own[k].ExtractFile(e);
        }
        public static bool Has(string name) { string k = name.ToLowerInvariant(); return looseRead.ContainsKey(k) || libRead.ContainsKey(k) || ent.ContainsKey(k); }
        static long SizeOf(string k) { string lp; if (looseRead.TryGetValue(k, out lp) || libRead.TryGetValue(k, out lp)) return new FileInfo(lp).Length; RpfFileEntry e; return ent.TryGetValue(k, out e) ? e.GetFileSize() : 0; }

        // ---------------- the files of a FiveM resource opened in the editor (an MLO made elsewhere): they come before the game's own of the same name ----------------
        // looseRead: lower-case name -> the file to read (the copy taken when the project was opened, for the files an export writes over; else the resource's
        // own). looseHome: where the file is in the resource. The files an earlier export of the same project made are left out (it makes them again).
        static Dictionary<string, string> looseRead = new Dictionary<string, string>(), looseHome = new Dictionary<string, string>();
        static Dictionary<uint, string> looseArch = new Dictionary<uint, string>(), looseYtd = new Dictionary<uint, string>();      // archetype -> its .ytyp; texture dictionary -> its .ytd
        static Dictionary<uint, uint> looseTxd = new Dictionary<uint, uint>();                                                      // archetype -> its texture dictionary
        static string looseKey;
        public static readonly string[] LooseKinds = { ".ydr", ".yft", ".ytd", ".ytyp", ".ymap", ".ybn" };
        public static string ResourceDir { get; private set; }
        public static List<string> LooseWarnings = new List<string>();

        // the files of a resource folder (null: none, only the game's); snap: the copy of its map files taken when the project was opened (Buildings.Snapshot)
        public static void UseResource(string dir, string snap)
        {
            lock (gate)
            {
                if (string.IsNullOrEmpty(dir))
                {
                    if (looseKey == null) return;
                    looseRead = new Dictionary<string, string>(); looseHome = new Dictionary<string, string>(); looseArch = new Dictionary<uint, string>(); looseYtd = new Dictionary<uint, string>(); looseTxd = new Dictionary<uint, uint>();
                    looseKey = null; ResourceDir = null; LooseWarnings = new List<string>(); ForgetModels(); return;
                }
                dir = Path.GetFullPath(dir); if (!Directory.Exists(dir)) throw new Exception("The resource folder " + dir + " is not there any more.");
                if (!string.IsNullOrEmpty(snap) && !Directory.Exists(snap)) throw new Exception("The copy of the resource taken when this project was opened is gone (" + snap + "). Open the resource again (Buildings of the game, Open an MLO resource).");
                List<string> files = new List<string>(); foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories)) if (Array.IndexOf(LooseKinds, Path.GetExtension(f).ToLowerInvariant()) >= 0) files.Add(f);
                files.Sort(StringComparer.OrdinalIgnoreCase);
                HashSet<string> made = Buildings.MadeBy(snap, dir);
                StringBuilder key = new StringBuilder(dir + "|" + snap);
                foreach (string f in files) key.Append('|').Append(f).Append(':').Append(File.GetLastWriteTimeUtc(f).Ticks);
                if (key.ToString() == looseKey) return;
                Dictionary<string, string> read = new Dictionary<string, string>(), home = new Dictionary<string, string>(); List<string> warns = new List<string>();
                foreach (string f in files)
                {
                    string k = Path.GetFileName(f).ToLowerInvariant(); if (made.Contains(k)) continue;
                    if (home.ContainsKey(k)) { warns.Add(k + " is in the resource twice (" + home[k].Substring(dir.Length) + " and " + f.Substring(dir.Length) + "): the first one is used."); continue; }
                    home[k] = f; read[k] = f;
                }
                if (!string.IsNullOrEmpty(snap)) foreach (string f in Directory.GetFiles(snap)) { string k = Path.GetFileName(f).ToLowerInvariant(); if (Array.IndexOf(LooseKinds, Path.GetExtension(k)) >= 0) read[k] = f; }
                // their names (the game knows its models by the hash of the name): a file's own name, and the one of an interior's collision file after hi@
                foreach (string k in read.Keys) { string stem = Path.GetFileNameWithoutExtension(k); JenkIndex.Ensure(stem); if (stem.StartsWith("hi@")) JenkIndex.Ensure(stem.Substring(3)); }
                Dictionary<uint, string> arch = new Dictionary<uint, string>(), ytd = new Dictionary<uint, string>(); Dictionary<uint, uint> txd = new Dictionary<uint, uint>();
                foreach (string k in read.Keys) if (k.EndsWith(".ytd")) ytd[JenkHash.GenHash(k.Substring(0, k.Length - 4))] = k;
                foreach (KeyValuePair<string, string> kv in read)
                {
                    if (!kv.Key.EndsWith(".ytyp")) continue;
                    try
                    {
                        RpfFileEntry fe; byte[] d = LooseData(kv.Value, kv.Key, out fe); YtypFile y = new YtypFile(); y.Load(d, fe); if (y.AllArchetypes == null) continue;
                        foreach (Archetype a in y.AllArchetypes) { if (a == null) continue; arch[a.Hash.Hash] = kv.Key; if (a.TextureDict.Hash != 0) txd[a.Hash.Hash] = a.TextureDict.Hash; }
                    }
                    catch (Exception ex) { warns.Add(kv.Key + ": " + ex.Message); }
                }
                looseRead = read; looseHome = home; looseArch = arch; looseYtd = ytd; looseTxd = txd; looseKey = key.ToString(); ResourceDir = dir; LooseWarnings = warns;
                ForgetModels();
            }
        }
        // the models read before came from other files
        static void ForgetModels() { lock (bboxCache) bboxCache.Clear(); lock (ownCol) ownCol.Clear(); lock (propColCache) propColCache.Clear(); Buildings.ForgetModels(); ImportHost.ForgetModels(); }
        static byte[] LooseData(string path, string k, out RpfFileEntry entry)
        {
            byte[] d = File.ReadAllBytes(path); long len = d.Length;
            if (len < 16 || BitConverter.ToUInt32(d, 0) != 0x37435352) throw new Exception(k + " cannot be read: it is not a game file as CodeWalker writes it (encrypted by FiveM's asset escrow, or broken).");          // (RSC7)
            RpfResourceFileEntry e = RpfFile.CreateResourceFileEntry(ref d, 0);
            d = ResourceBuilder.Decompress(d); e.Name = k; e.NameLower = k; e.FileSize = (uint)len; e.Path = path;
            entry = e; return d;
        }
        // the opened resource's files: their names (with the ending), where one of them is in the resource (null: not there), is it there
        public static List<string> LooseNames(string suffix) { List<string> l = new List<string>(); foreach (string k in looseRead.Keys) if (k.EndsWith(suffix)) l.Add(k); l.Sort(StringComparer.Ordinal); return l; }
        public static string LooseHome(string name) { string p; return looseHome.TryGetValue(name.ToLowerInvariant(), out p) ? p : null; }
        public static bool IsLoose(string name) { return looseRead.ContainsKey(name.ToLowerInvariant()); }
        // is the opened resource's file of this name the game's own, unchanged (a map types file an export copied along)
        public static bool SameAsGame(string name)
        {
            string k = name.ToLowerInvariant(), lp; RpfFileEntry ge; if (!looseRead.TryGetValue(k, out lp) || !ent.TryGetValue(k, out ge)) return false;
            try { RpfFileEntry le; byte[] a = LooseData(lp, k, out le), b = own[k].ExtractFile(ge); if (a == null || b == null || a.Length != b.Length) return false; for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true; }
            catch (Exception) { return false; }
        }

        // ---------------- the user's own props (the props folder next to the program): their models and texture dictionaries, after an opened resource's, before the game's ----------------
        static Dictionary<string, string> libRead = new Dictionary<string, string>();
        static string libKey;
        public static string LibraryDir { get; private set; }
        public static void UseLibrary(string dir)
        {
            lock (gate)
            {
                List<string> files = new List<string>(); if (Directory.Exists(dir)) foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories)) { string x = Path.GetExtension(f).ToLowerInvariant(); if (x == ".ydr" || x == ".ytd") files.Add(f); }
                files.Sort(StringComparer.OrdinalIgnoreCase);
                StringBuilder key = new StringBuilder(dir); foreach (string f in files) key.Append('|').Append(f).Append(':').Append(File.GetLastWriteTimeUtc(f).Ticks);
                LibraryDir = dir; if (key.ToString() == libKey) return;
                Dictionary<string, string> read = new Dictionary<string, string>();
                foreach (string f in files) { string k = Path.GetFileName(f).ToLowerInvariant(); if (!read.ContainsKey(k)) { read[k] = f; JenkIndex.Ensure(Path.GetFileNameWithoutExtension(k)); } }
                bool had = libKey != null; libRead = read; libKey = key.ToString(); libTex.Clear();
                if (had) ForgetModels();
            }
        }
        // is it one of the user's props (a model name, without .ydr); the names of the props (or of their texture dictionaries), with the ending
        public static bool IsLibrary(string name) { return !string.IsNullOrEmpty(name) && libRead.ContainsKey(name.ToLowerInvariant() + ".ydr"); }
        public static List<string> LibraryNames(string suffix) { List<string> l = new List<string>(); foreach (string k in libRead.Keys) if (k.EndsWith(suffix)) l.Add(k); l.Sort(StringComparer.Ordinal); return l; }
        public static string LibraryPath(string file) { string p; return libRead.TryGetValue(file.ToLowerInvariant(), out p) ? p : null; }
        // the textures a model's shaders use (lower case)
        public static HashSet<string> TexturesOf(string model)
        {
            HashSet<string> r = new HashSet<string>(); string f; long sz; DrawableBase d = OpenDrawable(model, out f, out sz); if (d == null || d.ShaderGroup == null || d.ShaderGroup.Shaders == null) return r;
            foreach (ShaderFX s in d.ShaderGroup.Shaders.data_items) { if (s == null || s.ParametersList == null) continue; foreach (ShaderParameter p in s.ParametersList.Parameters) { TextureBase t = p.Data as TextureBase; if (t != null && !string.IsNullOrEmpty(t.Name)) r.Add(t.Name.ToLowerInvariant()); } }
            return r;
        }
        // the user's texture dictionary a prop of theirs needs: the one of its own name, else the first that holds one of its textures (null: its textures are in the model)
        static Dictionary<string, HashSet<string>> libTex = new Dictionary<string, HashSet<string>>();
        public static string LibraryYtdFor(string model)
        {
            string own = model.ToLowerInvariant() + ".ytd"; if (libRead.ContainsKey(own)) return own;
            HashSet<string> want = TexturesOf(model); if (want.Count == 0) return null;
            foreach (string y in LibraryNames(".ytd"))
            {
                HashSet<string> names;
                lock (libTex) libTex.TryGetValue(y, out names);
                if (names == null)
                {
                    names = new HashSet<string>();
                    try { RpfFileEntry fe; byte[] b = Bytes(y, out fe); YtdFile t = new YtdFile(); t.Load(b, fe); if (t.TextureDict != null && t.TextureDict.Textures != null) foreach (Texture x in t.TextureDict.Textures.data_items) names.Add((x.Name ?? "").ToLowerInvariant()); } catch (Exception) { }
                    lock (libTex) libTex[y] = names;
                }
                foreach (string w in want) if (names.Contains(w)) return y;
            }
            return null;
        }

        // ---------------- ymap extents index (built once, then cached on disk) ----------------
        class YmapInfo { public string Name; public float X0, Y0, X1, Y1; }
        static List<YmapInfo> ymapIdx;

        static void EnsureYmapIndex()
        {
            if (ymapIdx != null) return;
            string f = Path.Combine(cache, "ymap_index.txt");
            string stamp = gta + "|" + File.GetLastWriteTimeUtc(Path.Combine(gta, "GTA5.exe")).Ticks + "|" + ent.Count;
            List<YmapInfo> list = new List<YmapInfo>();
            if (File.Exists(f))
            {
                string[] lines = File.ReadAllLines(f);
                if (lines.Length > 1 && lines[0] == stamp)
                {
                    for (int i = 1; i < lines.Length; i++)
                    {
                        string[] p = lines[i].Split('|'); if (p.Length < 5) continue;
                        YmapInfo yi = new YmapInfo(); yi.Name = p[0];
                        yi.X0 = float.Parse(p[1], CultureInfo.InvariantCulture); yi.Y0 = float.Parse(p[2], CultureInfo.InvariantCulture); yi.X1 = float.Parse(p[3], CultureInfo.InvariantCulture); yi.Y1 = float.Parse(p[4], CultureInfo.InvariantCulture);
                        list.Add(yi);
                    }
                    ymapIdx = list; return;
                }
            }
            int n = 0, total = 0; foreach (string k in ent.Keys) if (k.EndsWith(".ymap")) total++;
            StringBuilder sb = new StringBuilder(); sb.AppendLine(stamp);
            foreach (KeyValuePair<string, RpfFileEntry> kv in ent)
            {
                if (!kv.Key.EndsWith(".ymap")) continue;
                n++; if (n % 500 == 0) Status = "Indexing the game's map files, first run only: " + n + " / " + total;
                try
                {
                    byte[] data = own[kv.Key].ExtractFile(kv.Value); if (data == null) continue;
                    YmapFile y = new YmapFile(); y.Load(data, kv.Value);
                    Vector3 a = y.CMapData.entitiesExtentsMin, b = y.CMapData.entitiesExtentsMax;
                    if (float.IsNaN(a.X) || float.IsInfinity(a.X) || (a.X == 0 && a.Y == 0 && b.X == 0 && b.Y == 0)) continue;
                    if (y.AllEntities == null || y.AllEntities.Length == 0) continue;
                    YmapInfo yi = new YmapInfo(); yi.Name = kv.Key; yi.X0 = a.X; yi.Y0 = a.Y; yi.X1 = b.X; yi.Y1 = b.Y; list.Add(yi);
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0}|{1}|{2}|{3}|{4}", kv.Key, a.X, a.Y, b.X, b.Y));
                }
                catch (Exception) { }
            }
            File.WriteAllText(f, sb.ToString()); ymapIdx = list; Status = "";
        }

        // the map files whose entities reach into a box of the world (x / y), the lod and occlusion files left out
        public static List<string> YmapsAt(double x0, double y0, double x1, double y1)
        {
            EnsureYmapIndex(); List<string> l = new List<string>();
            foreach (YmapInfo yi in ymapIdx)
            {
                if (yi.X1 < x0 || yi.X0 > x1 || yi.Y1 < y0 || yi.Y0 > y1) continue;
                string n = yi.Name; if (n.IndexOf("_lod", StringComparison.Ordinal) >= 0 || n.IndexOf("occl", StringComparison.Ordinal) >= 0 || n.IndexOf("lodlights", StringComparison.Ordinal) >= 0) continue;
                l.Add(n);
            }
            return l;
        }
        public static bool HasModel(string name) { return HasDrawable(name); }
        // Does a model carry its own collision (built into it, like a chair or a door)? One that does not (a counter, a shelf, a piece of the building) has its
        // collision in the collision files of the interior or the map round it.
        static Dictionary<string, bool> ownCol = new Dictionary<string, bool>();
        public static bool HasOwnCollision(string name)
        {
            string k = name.ToLowerInvariant(); bool b; lock (ownCol) if (ownCol.TryGetValue(k, out b)) return b;
            // (a model that breaks or moves, .yft, has its collision in its physics: its own too)
            try { string f; long sz; DrawableBase d = OpenDrawable(k, out f, out sz); Drawable dr = d as Drawable; b = d != null && (f.EndsWith(".yft") || (dr != null && dr.Bound != null)); } catch (Exception) { b = false; }
            lock (ownCol) ownCol[k] = b; return b;
        }
        // a model's box ([min, max] in its own frame) and the size of its file (bytes), or null
        public static Vector3[] ModelBox(string name, out long size)
        {
            size = 0; string k = name.ToLowerInvariant();
            if (Has(k + ".ydr")) size = SizeOf(k + ".ydr"); else if (Has(k + ".yft")) size = SizeOf(k + ".yft");
            return PropBox(name);
        }

        // ---------------- finding the map pieces around a point ----------------
        static Dictionary<string, Vector3[]> bboxCache = new Dictionary<string, Vector3[]>();

        // A prop's drawable: a plain model (.ydr) or, for a fence, a bin, a table, a fragment (.yft: the drawable of the fragment). null when the game has neither by this name.
        static DrawableBase OpenDrawable(string name, out string file, out long size)
        {
            string k = name.ToLowerInvariant(); RpfFileEntry e; file = null; size = 0;
            if (Has(k + ".ydr")) { file = k + ".ydr"; size = SizeOf(file); YdrFile y = new YdrFile(); y.Load(Bytes(file, out e), e); return y.Drawable; }
            if (Has(k + ".yft")) { file = k + ".yft"; size = SizeOf(file); YftFile f = new YftFile(); f.Load(Bytes(file, out e), e); return f.Fragment != null ? f.Fragment.Drawable : null; }
            return null;
        }
        static bool HasDrawable(string name) { string k = name.ToLowerInvariant(); return Has(k + ".ydr") || Has(k + ".yft"); }

        static Vector3[] DrawableBox(string name, out RpfFileEntry entry)
        {
            entry = null; string k = name.ToLowerInvariant() + ".ydr"; RpfFileEntry e; if (!ent.TryGetValue(k, out e)) return null;
            entry = e; Vector3[] bb;
            if (bboxCache.TryGetValue(k, out bb)) return bb;
            try
            {
                YdrFile y = new YdrFile(); y.Load(own[k].ExtractFile(e), e);
                if (y.Drawable == null) { bboxCache[k] = null; return null; }
                bb = new Vector3[] { y.Drawable.BoundingBoxMin, y.Drawable.BoundingBoxMax };
            }
            catch (Exception) { bb = null; }
            bboxCache[k] = bb; return bb;
        }
        // the same for any prop, fragments too (a prop's bounding box)
        public static Vector3[] PropBox(string name)
        {
            string ck = "prop|" + name.ToLowerInvariant(); Vector3[] bb; if (bboxCache.TryGetValue(ck, out bb)) return bb;
            try { string f; long sz; DrawableBase d = OpenDrawable(name, out f, out sz); bb = d == null ? null : new Vector3[] { d.BoundingBoxMin, d.BoundingBoxMax }; } catch (Exception) { bb = null; }
            bboxCache[ck] = bb; return bb;
        }

        public static List<EntityHit> FindEntities(double px, double py, double radius)
        {
            EnsureYmapIndex();
            List<EntityHit> hits = new List<EntityHit>(); HashSet<string> seen = new HashSet<string>();
            List<string> maps = new List<string>();
            foreach (YmapInfo yi in ymapIdx) if (px >= yi.X0 - 2 && px <= yi.X1 + 2 && py >= yi.Y0 - 2 && py <= yi.Y1 + 2) maps.Add(yi.Name);
            Status = "Looking through " + maps.Count + " map files near the door...";
            foreach (string mn in maps)
            {
                try
                {
                    YmapFile y = new YmapFile(); y.Load(own[mn].ExtractFile(ent[mn]), ent[mn]);
                    if (y.AllEntities == null) continue;
                    foreach (YmapEntityDef en in y.AllEntities)
                    {
                        double dx = en.Position.X - px, dy = en.Position.Y - py; if (dx * dx + dy * dy > radius * radius) continue;
                        string lod = en.CEntityDef.lodLevel.ToString(); if (lod != "LODTYPES_DEPTH_HD" && lod != "LODTYPES_DEPTH_ORPHANHD") continue;
                        uint h = en.CEntityDef.archetypeName.Hash; string name = JenkIndex.TryGetString(h); if (string.IsNullOrEmpty(name)) continue;
                        string key = h + "|" + Math.Round(en.Position.X, 2) + "|" + Math.Round(en.Position.Y, 2) + "|" + Math.Round(en.Position.Z, 2);
                        if (!seen.Add(key)) continue;
                        RpfFileEntry ye; string yk = name.ToLowerInvariant() + ".ydr"; if (!ent.TryGetValue(yk, out ye)) continue;
                        if (ye.GetFileSize() < 40000) continue;                                     // small props are skipped
                        EntityHit eh = new EntityHit(); eh.Ymap = mn; eh.Name = name; eh.Hash = h; eh.Pos = en.Position; eh.Rot = en.Orientation; eh.Scale = en.Scale; eh.Lod = lod; eh.Ydr = ye; eh.YdrOwner = own[yk]; eh.Dist = Math.Sqrt(dx * dx + dy * dy);
                        RpfFileEntry dummy; Vector3[] bb = DrawableBox(name, out dummy); if (bb == null) continue;
                        eh.BMin = bb[0]; eh.BMax = bb[1];
                        // is the point inside the entity's oriented box?
                        Quaternion inv = en.Orientation; inv.Invert();
                        Vector3 loc = Vector3.Transform(new Vector3((float)(px - en.Position.X), (float)(py - en.Position.Y), 0), inv);
                        float sx = en.Scale.X == 0 ? 1 : en.Scale.X; loc.X /= sx; loc.Y /= sx;
                        eh.Covers = loc.X >= bb[0].X - 1 && loc.X <= bb[1].X + 1 && loc.Y >= bb[0].Y - 1 && loc.Y <= bb[1].Y + 1;
                        hits.Add(eh);
                    }
                }
                catch (Exception) { }
            }
            Status = "";
            return hits;
        }

        // The props the map files place inside a box (world x/y), whose name nameOk accepts. The game keeps the same prop in several map files (the streaming one and its
        // "hei_" and "lr_" copies), so a prop standing at the same spot is listed once, with every file that places it. Only full detail props count (HD and orphan HD).
        // the middles of the triangles of a prop's most detailed model, in its own coordinates (x y z, x y z...)
        static Dictionary<string, float[]> centroidCache = new Dictionary<string, float[]>();
        static float[] Centroids(string name)
        {
            string k = name.ToLowerInvariant(); float[] res; if (centroidCache.TryGetValue(k, out res)) return res;
            List<float> list = new List<float>();
            try
            {
                string fl; long sz; DrawableBase d = OpenDrawable(name, out fl, out sz);
                if (d != null && d.DrawableModels != null && d.DrawableModels.High != null)
                    foreach (DrawableModel m in d.DrawableModels.High)
                    {
                        if (m.Geometries == null) continue;
                        foreach (DrawableGeometry g in m.Geometries)
                        {
                            VertexData vd = g.VertexData; if (vd == null || g.IndexBuffer == null || g.IndexBuffer.Indices == null) continue;
                            int posOff = vd.Info.GetComponentOffset(0), stride = g.VertexStride; byte[] vb = vd.VertexBytes; if (posOff < 0 || stride <= 0) continue;
                            int nv = vb.Length / stride; ushort[] idx = g.IndexBuffer.Indices;
                            for (int t = 0; t + 2 < idx.Length; t += 3)
                            {
                                int a = idx[t], b = idx[t + 1], c = idx[t + 2]; if (a == b || b == c || a == c || a >= nv || b >= nv || c >= nv) continue;
                                float mx = 0, my = 0, mz = 0; int[] ids = new int[] { a, b, c };
                                for (int j = 0; j < 3; j++) { int o = ids[j] * stride + posOff; mx += BitConverter.ToSingle(vb, o); my += BitConverter.ToSingle(vb, o + 4); mz += BitConverter.ToSingle(vb, o + 8); }
                                list.Add(mx / 3); list.Add(my / 3); list.Add(mz / 3);
                            }
                        }
                    }
            }
            catch (Exception) { }
            res = list.ToArray(); centroidCache[k] = res; return res;
        }

        // Is this prop a plant, whatever its name says? Names lie ("prop_am_box_wood_01" is a 3 m bush): the archive that holds the model (v_bush, v_ext_veg...), the shader of its parts
        // (the game's foliage shaders are "trees_*") and the names of their textures (branches, leaves, grass, weeds) tell. null: no; else why.
        static Dictionary<string, string> plantWhy = new Dictionary<string, string>();
        static readonly System.Text.RegularExpressions.Regex PlantArchive = new System.Text.RegularExpressions.Regex("bush|veg|tree|plant|foliage|grass|flower|hedge|shrub|palm", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        static readonly System.Text.RegularExpressions.Regex PlantTexture = new System.Text.RegularExpressions.Regex("branch|leaf|leaves|foliage|grass|weed|bush|palm|fern|hedge|flower|shrub|ivy|cactus", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        public static string PlantWhy(string name)
        {
            string k = name.ToLowerInvariant(), res; if (plantWhy.TryGetValue(k, out res)) return res;
            res = null; RpfFileEntry e; string kf = ent.ContainsKey(k + ".ydr") ? k + ".ydr" : (ent.ContainsKey(k + ".yft") ? k + ".yft" : null);
            if (kf != null && ent.TryGetValue(kf, out e))
            {
                if (PlantArchive.IsMatch(own[kf].Name)) res = "archive " + own[kf].Name;
                else if (e.GetFileSize() < 600000)
                {
                    try
                    {
                        string fl; long sz; DrawableBase dr = OpenDrawable(name, out fl, out sz);
                        if (dr != null && dr.DrawableModels != null && dr.DrawableModels.High != null)
                            foreach (DrawableModel m in dr.DrawableModels.High)
                            {
                                if (m.Geometries == null) continue;
                                foreach (DrawableGeometry g in m.Geometries)
                                {
                                    string sh = ""; try { sh = g.Shader.Name.ToString(); } catch (Exception) { }
                                    if (sh.StartsWith("trees", StringComparison.OrdinalIgnoreCase)) { res = "foliage shader"; break; }
                                    try { ShaderParametersBlock pl = g.Shader.ParametersList; for (int q = 0; q < pl.Parameters.Length; q++) { TextureBase tb = pl.Parameters[q].Data as TextureBase; if (tb != null && PlantTexture.IsMatch(tb.Name)) { res = "foliage texture"; break; } } } catch (Exception) { }
                                    if (res != null) break;
                                }
                                if (res != null) break;
                            }
                    }
                    catch (Exception) { }
                }
            }
            plantWhy[k] = res; return res;
        }

        // A prop's most detailed model as loose triangles (3 vertices each, flat normals) for a picture of it, at most maxTris of them (every n-th triangle when it has more). Cached.
        static Dictionary<string, float[][]> meshCache = new Dictionary<string, float[][]>();
        public static float[][] PropMesh(string name, int maxTris)
        {
            string k = name.ToLowerInvariant(); float[][] res; if (meshCache.TryGetValue(k, out res)) return res;
            List<float> pos = new List<float>(), nrm = new List<float>(); res = null;
            if (HasDrawable(name))
            {
                try
                {
                    string fl; long sz; DrawableBase dr = OpenDrawable(name, out fl, out sz); List<float[]> tris = new List<float[]>();
                    if (dr != null && dr.DrawableModels != null && dr.DrawableModels.High != null)
                        foreach (DrawableModel m in dr.DrawableModels.High)
                        {
                            if (m.Geometries == null) continue;
                            foreach (DrawableGeometry g in m.Geometries)
                            {
                                VertexData vd = g.VertexData; if (vd == null || g.IndexBuffer == null || g.IndexBuffer.Indices == null) continue;
                                int posOff = vd.Info.GetComponentOffset(0), stride = g.VertexStride; byte[] vb = vd.VertexBytes; if (posOff < 0 || stride <= 0) continue;
                                int nv = vb.Length / stride; ushort[] idx = g.IndexBuffer.Indices;
                                for (int t = 0; t + 2 < idx.Length; t += 3)
                                {
                                    int a = idx[t], b = idx[t + 1], c = idx[t + 2]; if (a == b || b == c || a == c || a >= nv || b >= nv || c >= nv) continue;
                                    float[] tr = new float[9]; int[] ids = new int[] { a, b, c };
                                    for (int j = 0; j < 3; j++) { int o = ids[j] * stride + posOff; tr[j * 3] = BitConverter.ToSingle(vb, o); tr[j * 3 + 1] = BitConverter.ToSingle(vb, o + 4); tr[j * 3 + 2] = BitConverter.ToSingle(vb, o + 8); }
                                    tris.Add(tr);
                                }
                            }
                        }
                    int step = Math.Max(1, (int)Math.Ceiling(tris.Count / (double)Math.Max(1, maxTris)));
                    for (int i = 0; i < tris.Count; i += step)
                    {
                        float[] tr = tris[i]; float ux = tr[3] - tr[0], uy = tr[4] - tr[1], uz = tr[5] - tr[2], vx = tr[6] - tr[0], vy = tr[7] - tr[1], vz = tr[8] - tr[2];
                        float nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx, l = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz); if (l < 1e-9f) { nx = 0; ny = 0; nz = 1; l = 1; }
                        for (int j = 0; j < 3; j++) { pos.Add(tr[j * 3]); pos.Add(tr[j * 3 + 1]); pos.Add(tr[j * 3 + 2]); nrm.Add(nx / l); nrm.Add(ny / l); nrm.Add(nz / l); }
                    }
                    res = new float[][] { pos.ToArray(), nrm.ToArray(), new float[] { tris.Count } };
                }
                catch (Exception) { res = null; }
            }
            meshCache[k] = res; return res;
        }

        // vols: the rooms as boxes in the world, each { centre x, centre y, bottom z, top z, half width, half depth, cos yaw, sin yaw }; the triangles of a prop whose middle is inside one are counted
        // others: the props that are not plants too (bins, boxes, fences, tables...), except the ones skip says are the building's own models; Kind says which is which
        public static List<PropHit> PropsInBox(double x0, double y0, double x1, double y1, Func<string, bool> nameOk, List<double[]> vols, bool others, Func<string, bool> skip)
        {
            EnsureYmapIndex();
            List<PropHit> hits = new List<PropHit>(); Dictionary<string, PropHit> seen = new Dictionary<string, PropHit>();
            List<string> maps = new List<string>();
            foreach (YmapInfo yi in ymapIdx) if (x1 >= yi.X0 - 2 && x0 <= yi.X1 + 2 && y1 >= yi.Y0 - 2 && y0 <= yi.Y1 + 2) maps.Add(yi.Name);
            Status = "Looking through " + maps.Count + " map files around the building...";
            foreach (string mn in maps)
            {
                try
                {
                    YmapFile y = new YmapFile(); y.Load(own[mn].ExtractFile(ent[mn]), ent[mn]);
                    if (y.AllEntities == null) continue;
                    foreach (YmapEntityDef en in y.AllEntities)
                    {
                        if (en.Position.X < x0 || en.Position.X > x1 || en.Position.Y < y0 || en.Position.Y > y1) continue;
                        string lod = en.CEntityDef.lodLevel.ToString(); if (lod != "LODTYPES_DEPTH_HD" && lod != "LODTYPES_DEPTH_ORPHANHD") continue;
                        uint h = en.CEntityDef.archetypeName.Hash; string name = JenkIndex.TryGetString(h); if (string.IsNullOrEmpty(name)) continue;
                        string why = nameOk(name) ? "name" : PlantWhy(name);                                         // (a plant by its name, or by what the model is made of)
                        if (why == null) { if (!others || (skip != null && skip(name)) || !HasDrawable(name)) continue; }          // (not a plant: a prop only when asked for, and only a real model of its own)
                        string key = h + "|" + Math.Round(en.Position.X, 2) + "|" + Math.Round(en.Position.Y, 2) + "|" + Math.Round(en.Position.Z, 2);
                        PropHit ph; if (seen.TryGetValue(key, out ph)) { if (!ph.Files.Contains(mn)) ph.Files.Add(mn); continue; }
                        ph = new PropHit(); ph.Name = name; ph.Why = why; ph.Kind = why == null ? "prop" : "plant"; ph.Hash = h; ph.Rot = en.Orientation; ph.Scale = en.Scale; ph.Pos = en.Position; ph.Lod = lod.Replace("LODTYPES_DEPTH_", ""); ph.Files.Add(mn); ph.Centre = en.Position; ph.R = 0.5f; ph.H = 1f;
                        Vector3[] bb = PropBox(name);
                        if (bb != null)
                        {
                            float sx = en.Scale.X == 0 ? 1 : en.Scale.X, sz = en.Scale.Z == 0 ? 1 : en.Scale.Z;
                            Vector3 mid = new Vector3((bb[0].X + bb[1].X) / 2 * sx, (bb[0].Y + bb[1].Y) / 2 * sx, (bb[0].Z + bb[1].Z) / 2 * sz);
                            ph.Centre = en.Position + Vector3.Transform(mid, en.Orientation);
                            ph.R = Math.Max((bb[1].X - bb[0].X), (bb[1].Y - bb[0].Y)) / 2 * sx; ph.H = (bb[1].Z - bb[0].Z) * sz; ph.Sized = true;
                        }
                        if (why != "name" && ph.R > 6f) { seen[key] = ph; continue; }              // (a model wider than 12 m with leaf textures is ground or a whole block's detail file, not a plant)
                        if (why == null && ph.R > 8f) { seen[key] = ph; continue; }                // (nor is a prop that wide: a road piece, a whole building)
                        if (vols != null && vols.Count > 0)
                        {
                            // only a prop whose box can reach a room is looked at triangle by triangle
                            float reach = Math.Max(ph.R, ph.H) * 1.5f + 1f; bool near = false;
                            foreach (double[] v in vols) { double ddx = ph.Centre.X - v[0], ddy = ph.Centre.Y - v[1]; if (Math.Sqrt(ddx * ddx + ddy * ddy) <= reach + Math.Sqrt(v[4] * v[4] + v[5] * v[5])) { near = true; break; } }
                            float[] cs = near ? Centroids(name) : null; ph.Tris = 0;
                            if (cs != null && cs.Length > 0)
                            {
                                float sx2 = en.Scale.X == 0 ? 1 : en.Scale.X, sz2 = en.Scale.Z == 0 ? 1 : en.Scale.Z; ph.TrisAll = cs.Length / 3;
                                for (int ci = 0; ci + 2 < cs.Length; ci += 3)
                                {
                                    Vector3 w = Vector3.Transform(new Vector3(cs[ci] * sx2, cs[ci + 1] * sx2, cs[ci + 2] * sz2), en.Orientation) + en.Position;
                                    foreach (double[] v in vols)
                                    {
                                        if (w.Z < v[2] || w.Z > v[3]) continue; double dx = w.X - v[0], dy = w.Y - v[1];
                                        if (Math.Abs(dx * v[6] + dy * v[7]) <= v[4] && Math.Abs(-dx * v[7] + dy * v[6]) <= v[5]) { ph.Tris++; break; }
                                    }
                                }
                            }
                            else if (near) ph.Tris = -1;                                          // its model could not be read
                        }
                        seen[key] = ph; hits.Add(ph);
                    }
                }
                catch (Exception) { }
            }
            Status = "";
            return hits;
        }

        // ---------------- textures: average colour of a texture, looked up in the archive that holds the drawable ----------------
        static Dictionary<string, float[]> texColor = new Dictionary<string, float[]>();
        static HashSet<string> ytdDone = new HashSet<string>();

        static float[] AvgColor(Texture t)
        {
            try
            {
                byte[] px = CodeWalker.Utils.DDSIO.GetPixels(t, 0); if (px == null) return null;
                int w = t.Width, h = t.Height; long r = 0, g = 0, b = 0; int n = 0; int step = Math.Max(1, (w * h) / 4096);
                for (int i = 0; i + 3 < px.Length; i += 4 * step) { b += px[i]; g += px[i + 1]; r += px[i + 2]; n++; }
                if (n == 0) return null; return new float[] { r / (float)n / 255f, g / (float)n / 255f, b / (float)n / 255f };
            }
            catch (Exception) { return null; }
        }

        static void LoadTexturesFor(RpfFile owner, HashSet<string> wanted)
        {
            // texture dictionaries living in the same archive as the drawable
            foreach (RpfEntry e in owner.AllEntries)
            {
                RpfFileEntry fe = e as RpfFileEntry; if (fe == null || !fe.Name.EndsWith(".ytd", StringComparison.OrdinalIgnoreCase)) continue;
                string key = owner.Path + "|" + fe.Name; if (ytdDone.Contains(key)) continue;
                bool anyMissing = false; foreach (string w in wanted) if (!texColor.ContainsKey(w)) { anyMissing = true; break; }
                if (!anyMissing) return;
                ytdDone.Add(key);
                try
                {
                    YtdFile ytd = new YtdFile(); ytd.Load(owner.ExtractFile(fe), fe);
                    if (ytd.TextureDict == null || ytd.TextureDict.Textures == null) continue;
                    foreach (Texture t in ytd.TextureDict.Textures.data_items)
                    {
                        string tn = t.Name.ToLowerInvariant(); if (!wanted.Contains(tn) || texColor.ContainsKey(tn)) continue;
                        float[] c = AvgColor(t); if (c != null) texColor[tn] = c;
                    }
                }
                catch (Exception) { }
            }
        }

        // ---------------- which map types file (.ytyp) defines an archetype (built once from every .ytyp of the game, then cached on disk) ----------------
        static Dictionary<uint, string> archYtyp;
        static void EnsureArchetypeIndex()
        {
            if (archYtyp != null) return;
            string f = Path.Combine(cache, "archetype_index.txt"), stamp = gta + "|" + File.GetLastWriteTimeUtc(Path.Combine(gta, "GTA5.exe")).Ticks + "|" + ent.Count;
            Dictionary<uint, string> idx = new Dictionary<uint, string>();
            if (File.Exists(f))
            {
                string[] lines = File.ReadAllLines(f);
                if (lines.Length > 1 && lines[0] == stamp)
                {
                    for (int i = 1; i < lines.Length; i++) { string[] p = lines[i].Split('|'); uint h; if (p.Length == 2 && uint.TryParse(p[0], out h)) idx[h] = p[1]; }
                    archYtyp = idx; return;
                }
            }
            int n = 0, total = 0; foreach (string k in ent.Keys) if (k.EndsWith(".ytyp")) total++;
            StringBuilder sb = new StringBuilder(); sb.AppendLine(stamp);
            foreach (KeyValuePair<string, RpfFileEntry> kv in ent)
            {
                if (!kv.Key.EndsWith(".ytyp")) continue;
                n++; if (n % 100 == 0) Status = "Indexing the game's map types files, first time only: " + n + " / " + total;
                try
                {
                    YtypFile y = new YtypFile(); y.Load(own[kv.Key].ExtractFile(kv.Value), kv.Value); if (y.AllArchetypes == null) continue;
                    foreach (Archetype a in y.AllArchetypes) { if (a == null) continue; idx[a.Hash.Hash] = kv.Key; sb.AppendLine(a.Hash.Hash + "|" + kv.Key); }
                }
                catch (Exception) { }
            }
            try { File.WriteAllText(f, sb.ToString()); } catch (Exception) { }
            archYtyp = idx; Status = "";
        }
        // the map types file that defines an archetype, or null
        public static bool GameHasArchetype(uint hash) { EnsureArchetypeIndex(); return archYtyp.ContainsKey(hash); }          // (the game's own, not an opened resource's)
        public static string YtypOf(uint hash) { string yt; if (looseArch.TryGetValue(hash, out yt)) return yt; EnsureArchetypeIndex(); return archYtyp.TryGetValue(hash, out yt) ? yt : null; }
        // The map types files a set of props needs that the game does not always have loaded: the ones of its interiors (an apartment's furniture, the props of
        // the game's own interiors). Returns ytyp name -> the props of the list it defines.
        public static Dictionary<string, List<string>> InteriorYtypsFor(IEnumerable<string> names)
        {
            EnsureArchetypeIndex(); Dictionary<string, List<string>> res = new Dictionary<string, List<string>>();
            foreach (string n in names)
            {
                string yt; if (!archYtyp.TryGetValue(JenkHash.GenHash(n.ToLowerInvariant()), out yt)) continue;
                RpfFile o; if (GetEntry(yt, out o) == null || o.Path.IndexOf("\\interiors\\", StringComparison.OrdinalIgnoreCase) < 0) continue;          // (the ordinary props are always there)
                List<string> l; if (!res.TryGetValue(yt, out l)) res[yt] = l = new List<string>(); if (!l.Contains(n)) l.Add(n);
            }
            return res;
        }
        // a file of the game as it is on disk (resource header, compressed), or null
        public static byte[] ResourceFile(string name)
        {
            string lp; if (looseRead.TryGetValue(name.ToLowerInvariant(), out lp) || libRead.TryGetValue(name.ToLowerInvariant(), out lp)) { byte[] b = File.ReadAllBytes(lp); return b.Length >= 16 && BitConverter.ToUInt32(b, 0) == 0x37435352 ? b : null; }
            RpfFile o; RpfFileEntry fe = GetEntry(name.ToLowerInvariant(), out o); RpfResourceFileEntry rre = fe as RpfResourceFileEntry; if (rre == null) return null;
            return ResourceBuilder.AddResourceHeader(rre, ResourceBuilder.Compress(o.ExtractFile(fe)));
        }

        // ---------------- textures of a model, looked up in the game: its archetype's texture dictionary, the dictionaries in the archive that holds the model, and their parents (gtxd) ----------------
        static Dictionary<uint, string> ytdByHash;          // texture dictionary hash -> .ytd name
        static Dictionary<uint, uint> parentTxd;            // texture dictionary hash -> its parent's hash

        static void EnsureTxdIndex()
        {
            if (ytdByHash != null) return;
            Status = "Reading the game's texture dictionaries...";
            Dictionary<uint, string> byHash = new Dictionary<uint, string>();
            foreach (string n in ent.Keys) if (n.EndsWith(".ytd")) byHash[JenkHash.GenHash(n.Substring(0, n.Length - 4))] = n;
            Dictionary<uint, uint> par = new Dictionary<uint, uint>();
            foreach (RpfFile rf in rpf.AllRpfs)
            {
                if (rf.AllEntries == null) continue;
                foreach (RpfEntry e in rf.AllEntries)
                {
                    RpfFileEntry fe = e as RpfFileEntry; if (fe == null) continue; string n = fe.Name.ToLowerInvariant();
                    if (!n.EndsWith("gtxd.ymt") && !n.EndsWith("gtxd.meta")) continue;
                    try
                    {
                        byte[] data = rf.ExtractFile(fe); if (data == null) continue;
                        Dictionary<string, string> rel = null;
                        try { GtxdFile g = new GtxdFile(); g.Load(data, fe); rel = g.TxdRelationships; } catch (Exception) { }
                        if (rel == null || rel.Count == 0) { try { YmtFile y = new YmtFile(); y.Load(data, fe); rel = y.CMapParentTxds; } catch (Exception) { } }
                        if (rel == null) continue;
                        foreach (KeyValuePair<string, string> kv in rel) if (!string.IsNullOrEmpty(kv.Key) && !string.IsNullOrEmpty(kv.Value)) par[JenkHash.GenHash(kv.Key.ToLowerInvariant())] = JenkHash.GenHash(kv.Value.ToLowerInvariant());
                    }
                    catch (Exception) { }
                }
            }
            ytdByHash = byHash; parentTxd = par;
        }

        // the texture dictionary the model's archetype names: the map types file next to the model first, then every one whose name starts like the model's (dt1_03_...: dt1_03*.ytyp). 0: none found.
        static uint ArchetypeTxd(string model, RpfFile owner)
        {
            uint h = JenkHash.GenHash(model); string[] parts = model.Split('_'); string prefix = parts.Length >= 2 ? parts[0] + "_" + parts[1] : model;
            List<KeyValuePair<RpfFile, RpfFileEntry>> cands = new List<KeyValuePair<RpfFile, RpfFileEntry>>(); HashSet<string> seen = new HashSet<string>();
            if (owner != null && owner.AllEntries != null) foreach (RpfEntry e in owner.AllEntries) { RpfFileEntry fe = e as RpfFileEntry; if (fe != null && fe.Name.EndsWith(".ytyp", StringComparison.OrdinalIgnoreCase) && seen.Add(fe.Name.ToLowerInvariant())) cands.Add(new KeyValuePair<RpfFile, RpfFileEntry>(owner, fe)); }
            foreach (string n in Names(".ytyp")) { if (!n.StartsWith(prefix, StringComparison.Ordinal) || !seen.Add(n)) continue; RpfFile o; RpfFileEntry fe = GetEntry(n, out o); if (fe != null) cands.Add(new KeyValuePair<RpfFile, RpfFileEntry>(o, fe)); }
            foreach (KeyValuePair<RpfFile, RpfFileEntry> c in cands)
            {
                try
                {
                    YtypFile y = new YtypFile(); y.Load(c.Key.ExtractFile(c.Value), c.Value); if (y.AllArchetypes == null) continue;
                    foreach (Archetype a in y.AllArchetypes) if (a != null && a.Hash.Hash == h && a.TextureDict.Hash != 0) return a.TextureDict.Hash;
                }
                catch (Exception) { }
            }
            return 0;
        }

        // Finds the textures named in `wanted` (lower case) for the model `model` (its name without .ydr). Returns name -> texture for the ones found; `searched` gets the dictionaries read.
        public static Dictionary<string, Texture> FindTextures(string model, HashSet<string> wanted, List<string> searched)
        {
            Dictionary<string, Texture> found = new Dictionary<string, Texture>(); if (wanted.Count == 0) return found;
            model = model.ToLowerInvariant(); EnsureTxdIndex();
            RpfFile owner; RpfFileEntry ydr = GetEntry(model + ".ydr", out owner);
            Status = "Looking up the textures of " + model + "...";
            // the dictionaries to read, in order: the archetype's, one named like the model, the others in the model's archive; each followed by its parents
            List<uint> order = new List<uint>(); HashSet<uint> queued = new HashSet<uint>();
            Action<uint> add = null; add = delegate (uint th) { int depth = 0; while (th != 0 && depth++ < 16 && queued.Add(th)) { order.Add(th); uint p; if (!parentTxd.TryGetValue(th, out p)) break; th = p; } };
            uint lt; if (looseTxd.TryGetValue(JenkHash.GenHash(model), out lt)) add(lt);          // (a model of the opened resource: its archetype names its dictionary)
            add(ArchetypeTxd(model, ydr != null ? owner : null));
            add(JenkHash.GenHash(model));
            if (ydr != null && owner.AllEntries != null) foreach (RpfEntry e in owner.AllEntries) { RpfFileEntry fe = e as RpfFileEntry; if (fe != null && fe.Name.EndsWith(".ytd", StringComparison.OrdinalIgnoreCase)) add(JenkHash.GenHash(Path.GetFileNameWithoutExtension(fe.Name).ToLowerInvariant())); }
            foreach (uint th in order)
            {
                if (found.Count >= wanted.Count) break;
                string name; if (!looseYtd.TryGetValue(th, out name) && !ytdByHash.TryGetValue(th, out name)) continue;
                RpfFileEntry fe; byte[] data; try { data = Bytes(name, out fe); } catch (Exception) { continue; } if (data == null) continue;
                try
                {
                    YtdFile ytd = new YtdFile(); ytd.Load(data, fe); searched.Add(name);
                    if (ytd.TextureDict == null || ytd.TextureDict.Textures == null) continue;
                    foreach (Texture t in ytd.TextureDict.Textures.data_items)
                    {
                        string tn = (t.Name ?? "").ToLowerInvariant(); if (wanted.Contains(tn) && !found.ContainsKey(tn)) found[tn] = t;
                    }
                }
                catch (Exception) { }
            }
            // the opened resource's other dictionaries (an MLO's models often share one)
            // (and the user's: the dictionary of the prop's own name first)
            List<string> extra = new List<string>(); string ownYtd = model + ".ytd"; if (libRead.ContainsKey(ownYtd)) extra.Add(ownYtd);
            foreach (string y in LibraryNames(".ytd")) if (y != ownYtd) extra.Add(y);
            extra.AddRange(LooseNames(".ytd"));
            foreach (string name in extra)
            {
                if (found.Count >= wanted.Count) break; if (searched.Contains(name)) continue;
                try
                {
                    RpfFileEntry fe; byte[] data = Bytes(name, out fe); YtdFile ytd = new YtdFile(); ytd.Load(data, fe); searched.Add(name);
                    if (ytd.TextureDict == null || ytd.TextureDict.Textures == null) continue;
                    foreach (Texture t in ytd.TextureDict.Textures.data_items) { string tn = (t.Name ?? "").ToLowerInvariant(); if (wanted.Contains(tn) && !found.ContainsKey(tn)) found[tn] = t; }
                }
                catch (Exception) { }
            }
            Status = "";
            return found;
        }

        // ---------------- reference geometry ----------------
        static float Half(byte[] b, int o)
        {
            int h = BitConverter.ToUInt16(b, o), s = (h >> 15) & 1, e = (h >> 10) & 31, m = h & 1023; double v;
            if (e == 0) v = Math.Pow(2, -14) * (m / 1024.0); else if (e == 31) v = 0; else v = Math.Pow(2, e - 15) * (1 + m / 1024.0);
            return (float)(s == 1 ? -v : v);
        }

        public static void ExtractGeometry(List<EntityHit> hits, double[] win, List<RefGroup> groups, List<RefTri> tris)
        {
            // win = minX, maxX, minY, maxY, minZ, maxZ in world coordinates
            uint diffHash = JenkHash.GenHash("diffusesampler");
            Dictionary<string, int> groupIdx = new Dictionary<string, int>();
            Dictionary<string, HashSet<string>> wantByOwner = new Dictionary<string, HashSet<string>>();
            Dictionary<string, RpfFile> ownerByPath = new Dictionary<string, RpfFile>();
            foreach (EntityHit eh in hits)
            {
                // world box test
                Vector3[] cs = new Vector3[8]; int ci = 0;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = new Vector3((i & 1) == 0 ? eh.BMin.X : eh.BMax.X, (i & 2) == 0 ? eh.BMin.Y : eh.BMax.Y, (i & 4) == 0 ? eh.BMin.Z : eh.BMax.Z);
                    cs[ci++] = Vector3.Transform(c * (eh.Scale.X == 0 ? 1 : eh.Scale.X), eh.Rot) + eh.Pos;
                }
                double x0 = 1e9, x1 = -1e9, y0 = 1e9, y1 = -1e9; foreach (Vector3 c in cs) { x0 = Math.Min(x0, c.X); x1 = Math.Max(x1, c.X); y0 = Math.Min(y0, c.Y); y1 = Math.Max(y1, c.Y); }
                if (x1 < win[0] || x0 > win[1] || y1 < win[2] || y0 > win[3]) continue;
                YdrFile ydr = new YdrFile();
                try { ydr.Load(eh.YdrOwner.ExtractFile(eh.Ydr), eh.Ydr); } catch (Exception) { continue; }
                if (ydr.Drawable == null || ydr.Drawable.DrawableModels == null || ydr.Drawable.DrawableModels.High == null) continue;
                foreach (DrawableModel m in ydr.Drawable.DrawableModels.High)
                {
                    if (m.Geometries == null) continue;
                    foreach (DrawableGeometry g in m.Geometries)
                    {
                        VertexData vd = g.VertexData; if (vd == null || g.IndexBuffer == null || g.IndexBuffer.Indices == null) continue;
                        string tex = "";
                        try
                        {
                            ShaderParametersBlock pl = g.Shader.ParametersList; string first = "";
                            for (int k = 0; k < pl.Parameters.Length; k++)
                            {
                                TextureBase tb = pl.Parameters[k].Data as TextureBase; if (tb == null) continue;
                                if (first == "") first = tb.Name; if ((uint)pl.Hashes[k] == diffHash) tex = tb.Name;
                            }
                            if (tex == "") tex = first;
                        }
                        catch (Exception) { }
                        int posOff = vd.Info.GetComponentOffset(0); int stride = g.VertexStride; byte[] vb = vd.VertexBytes; if (posOff < 0 || stride <= 0) continue;
                        int nv = vb.Length / stride; double sc = eh.Scale.X == 0 ? 1 : eh.Scale.X;
                        double[] wx = new double[nv], wy = new double[nv], wz = new double[nv]; bool[] inside = new bool[nv];
                        for (int i = 0; i < nv; i++)
                        {
                            int o = i * stride + posOff;
                            Vector3 w = Vector3.Transform(new Vector3(BitConverter.ToSingle(vb, o) * (float)sc, BitConverter.ToSingle(vb, o + 4) * (float)sc, BitConverter.ToSingle(vb, o + 8) * (float)sc), eh.Rot) + eh.Pos;
                            wx[i] = w.X; wy[i] = w.Y; wz[i] = w.Z;
                            inside[i] = w.X >= win[0] && w.X <= win[1] && w.Y >= win[2] && w.Y <= win[3] && w.Z >= win[4] && w.Z <= win[5];
                        }
                        ushort[] idx = g.IndexBuffer.Indices; string gk = eh.Name + "|" + tex; int gi;
                        for (int t = 0; t + 2 < idx.Length; t += 3)
                        {
                            int a = idx[t], b = idx[t + 1], c = idx[t + 2];
                            if (a == b || b == c || a == c || a >= nv || b >= nv || c >= nv) continue;
                            if (!(inside[a] && inside[b] && inside[c])) continue;
                            if (!groupIdx.TryGetValue(gk, out gi))
                            {
                                gi = groups.Count; groupIdx[gk] = gi; RefGroup rg = new RefGroup(); rg.Entity = eh.Name; rg.Texture = tex; groups.Add(rg);
                                if (tex != "")
                                {
                                    string ok = eh.YdrOwner.Path; ownerByPath[ok] = eh.YdrOwner; if (!wantByOwner.ContainsKey(ok)) wantByOwner[ok] = new HashSet<string>(); wantByOwner[ok].Add(tex.ToLowerInvariant());
                                }
                            }
                            RefTri rt = new RefTri(); rt.Group = gi; int[] ids = new int[] { a, b, c };
                            for (int j = 0; j < 3; j++) { rt.P[j * 3] = wx[ids[j]]; rt.P[j * 3 + 1] = wy[ids[j]]; rt.P[j * 3 + 2] = wz[ids[j]]; }
                            tris.Add(rt);
                        }
                    }
                }
            }
            Status = "Colouring the reference from the game's textures...";
            foreach (KeyValuePair<string, HashSet<string>> kv in wantByOwner) LoadTexturesFor(ownerByPath[kv.Key], kv.Value);
            foreach (RefGroup g in groups) { float[] c; if (!string.IsNullOrEmpty(g.Texture) && texColor.TryGetValue(g.Texture.ToLowerInvariant(), out c)) g.Color = c; }
            Status = "";
        }

        // ---------------- collision ----------------
        public static void ExtractCollision(List<EntityHit> hits, double[] win, List<ColPoly> polys, List<string> filesUsed)
        {
            HashSet<string> owners = new HashSet<string>(); List<RpfFile> ownerList = new List<RpfFile>();
            foreach (EntityHit eh in hits) { if (!eh.Covers) continue; if (owners.Add(eh.YdrOwner.Path)) ownerList.Add(eh.YdrOwner); }
            foreach (RpfFile rf in ownerList)
            {
                foreach (RpfEntry e in rf.AllEntries)
                {
                    RpfFileEntry fe = e as RpfFileEntry; if (fe == null || !fe.Name.EndsWith(".ybn", StringComparison.OrdinalIgnoreCase)) continue;
                    // the entry that is actually active for this name
                    RpfFileEntry active; if (!ent.TryGetValue(fe.Name.ToLowerInvariant(), out active)) continue;
                    RpfFile ao = own[fe.Name.ToLowerInvariant()];
                    Bounds root; YbnFile y = new YbnFile();
                    try { y.Load(ao.ExtractFile(active), active); root = y.Bounds; } catch (Exception) { continue; }
                    if (root == null) continue;
                    if (root.BoxMax.X < win[0] || root.BoxMin.X > win[1] || root.BoxMax.Y < win[2] || root.BoxMin.Y > win[3]) continue;
                    int before = polys.Count;
                    WalkBounds(root, win, polys, fe.Name.ToLowerInvariant());
                    if (polys.Count > before) filesUsed.Add(fe.Name + " (" + (polys.Count - before) + " polygons)");
                }
            }
        }

        static void WalkBounds(Bounds b, double[] win, List<ColPoly> polys, string file)
        {
            if (b == null) return;
            BoundComposite comp = b as BoundComposite;
            if (comp != null) { if (comp.Children != null) foreach (Bounds c in comp.Children.data_items) WalkBounds(c, win, polys, file); return; }
            BoundGeometry geo = b as BoundGeometry; if (geo == null || geo.Polygons == null) return;
            if (b.BoxMax.X < win[0] || b.BoxMin.X > win[1] || b.BoxMax.Y < win[2] || b.BoxMin.Y > win[3]) return;
            foreach (BoundPolygon p in geo.Polygons)
            {
                BoundPolygonTriangle tp = p as BoundPolygonTriangle; BoundPolygonBox bp = p as BoundPolygonBox;
                if (tp == null && bp == null) continue;
                Vector3[] vp = p.VertexPositions; if (vp == null) continue;
                bool inside = false;
                foreach (Vector3 v in vp) if (v.X >= win[0] && v.X <= win[1] && v.Y >= win[2] && v.Y <= win[3] && v.Z >= win[4] && v.Z <= win[5]) { inside = true; break; }
                if (!inside) continue;
                ColPoly cp = new ColPoly(); cp.File = file; cp.Hi = file.StartsWith("hi@"); cp.Kind = tp != null ? 0 : 1;
                int n = tp != null ? 3 : 4; cp.P = new double[n * 3];
                for (int i = 0; i < n; i++) { cp.P[i * 3] = vp[i].X; cp.P[i * 3 + 1] = vp[i].Y; cp.P[i * 3 + 2] = vp[i].Z; }
                try { cp.Material = p.Material.Type.ToString(); cp.Room = p.Material.RoomId; } catch (Exception) { cp.Material = ""; }
                polys.Add(cp);
            }
        }
    }
}
