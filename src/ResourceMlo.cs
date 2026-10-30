// An MLO made elsewhere, opened again (Buildings of the game, "Open an MLO resource"): a FiveM resource folder whose stream folder holds the interior's map
// types file (.ytyp), the map file that places it (.ymap), its models, textures and collision. Its files come before the game's own of the same name
// (Vanilla.UseResource), so it opens and is changed like an interior of the game. When a project opens one of its interiors, its map files are copied
// (the snapshot): every export of the project starts from them, whatever an earlier export wrote over them. The export writes the changes back into the
// resource, after zipping the whole folder next to it (the backup). C# 5.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using CodeWalker.GameFiles;
using SharpDX;

namespace MloCreator
{
    public static partial class Buildings
    {
        public static void ForgetModels() { lock (gameDocs) gameDocs.Clear(); }

        // the resource's own folder: the one picked, or the one above it when its stream folder (or a folder in it) was picked
        public static string ResourceRoot(string dir)
        {
            string d = Path.GetFullPath(dir).TrimEnd('\\');
            for (string p = d; p != null; p = Path.GetDirectoryName(p))
                if (File.Exists(Path.Combine(p, "fxmanifest.lua")) || File.Exists(Path.Combine(p, "__resource.lua"))) return p;
            return d;
        }

        // The interiors of a resource folder: each MLO archetype of its .ytyp files, with where its .ymap files place it (or, for one that replaces an interior
        // of the game, where the game does).
        public static Dictionary<string, object> OpenResource(string dir, string gta, string cacheDir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) throw new Exception("The folder " + dir + " is not there.");
            dir = ResourceRoot(dir);
            if (Directory.GetFiles(dir, "*.fxap", SearchOption.AllDirectories).Length > 0)
                throw new Exception("This resource is encrypted by FiveM's asset escrow (it has a .fxap file): only the server can read its files, no editor can open them. Only resources with readable files can be opened (your own, free ones, or ones bought without escrow).");
            Vanilla.Init(gta, cacheDir); Vanilla.UseResource(dir, null);
            List<string> warns = new List<string>(Vanilla.LooseWarnings);
            if (!File.Exists(Path.Combine(dir, "fxmanifest.lua")) && !File.Exists(Path.Combine(dir, "__resource.lua"))) warns.Add("There is no fxmanifest.lua in " + dir + ": is this the resource's folder?");
            List<string> ytyps = Vanilla.LooseNames(".ytyp"), ymaps = Vanilla.LooseNames(".ymap");
            if (ytyps.Count == 0) throw new Exception("There is no .ytyp file in " + dir + ": an MLO's interior is defined in one (a resource with only .ymap files places the game's models and has no interior of its own).");

            // where the resource's map files place interiors
            Dictionary<uint, List<object>> placed = new Dictionary<uint, List<object>>();
            foreach (string y in ymaps)
            {
                try
                {
                    RpfFileEntry fe; byte[] d = Vanilla.Bytes(y, out fe); YmapFile ym = new YmapFile(); ym.Load(d, fe); if (ym.MloEntities == null) continue;
                    foreach (YmapEntityDef en in ym.MloEntities)
                    {
                        uint h = en.CEntityDef.archetypeName.Hash; Quaternion q = en.Orientation; List<object> l;
                        if (!placed.TryGetValue(h, out l)) placed[h] = l = new List<object>();
                        Dictionary<string, object> p = new Dictionary<string, object>(); p["x"] = en.Position.X; p["y"] = en.Position.Y; p["z"] = en.Position.Z; p["heading"] = 2 * Math.Atan2(q.Z, q.W) * 180 / Math.PI; p["ymap"] = y; l.Add(p);
                    }
                }
                catch (Exception ex) { warns.Add(y + ": " + ex.Message); }
            }
            List<object> list = new List<object>();
            foreach (string yt in ytyps)
            {
                if (Vanilla.SameAsGame(yt)) continue;            // (the game's own file, copied along for props of its interiors)
                YtypFile f;
                try { RpfFileEntry fe; byte[] d = Vanilla.Bytes(yt, out fe); f = new YtypFile(); f.Load(d, fe); }
                catch (Exception ex) { warns.Add(yt + ": " + ex.Message); continue; }
                if (f.AllArchetypes == null) continue;
                foreach (Archetype a in f.AllArchetypes)
                {
                    MloArchetype m = a as MloArchetype; if (m == null) continue; uint h = m.Hash.Hash;
                    string nm = JenkIndex.TryGetString(h); bool known = !string.IsNullOrEmpty(nm); if (!known) nm = "0x" + h.ToString("X8");
                    int ents = m.entities != null ? m.entities.Length : 0; if (m.entitySets != null) foreach (MCMloEntitySet es in m.entitySets) if (es.Entities != null) ents += es.Entities.Length;
                    Dictionary<string, object> r = new Dictionary<string, object>();
                    r["kind"] = "interior"; r["name"] = nm; r["named"] = known; r["hash"] = h; r["ytyp"] = yt; r["rooms"] = m.rooms != null ? m.rooms.Length : 0; r["portals"] = m.portals != null ? m.portals.Length : 0; r["entities"] = ents;
                    List<string> rn = new List<string>(); if (m.rooms != null) foreach (MCMloRoomDef rd in m.rooms) rn.Add(rd.RoomName ?? ""); r["roomNames"] = rn;
                    List<object> at; MloEntry ge = null;
                    if (!placed.TryGetValue(h, out at) || at.Count == 0)
                    {
                        // an interior of the game the resource changes: the game places it
                        try { EnsureIndex(); foreach (MloEntry e in idx) if (e.Hash == h) { ge = e; break; } } catch (Exception) { }
                        at = new List<object>();
                        if (ge != null) { Dictionary<string, object> p = new Dictionary<string, object>(); p["x"] = ge.X; p["y"] = ge.Y; p["z"] = ge.Z; p["heading"] = ge.Heading; p["ymap"] = ""; at.Add(p); r["game"] = true; }
                    }
                    if (at.Count == 0) { warns.Add(nm + " (" + yt + ") is not placed by any map file of the resource or the game: it opens at 0, 0, 0."); Dictionary<string, object> p0 = new Dictionary<string, object>(); p0["x"] = 0.0; p0["y"] = 0.0; p0["z"] = 0.0; p0["heading"] = 0.0; p0["ymap"] = ""; at.Add(p0); }
                    Dictionary<string, object> first = (Dictionary<string, object>)at[0];
                    r["x"] = first["x"]; r["y"] = first["y"]; r["z"] = first["z"]; r["heading"] = first["heading"];
                    List<string> ym0 = new List<string>(); foreach (object o in at) { string y = (string)((Dictionary<string, object>)o)["ymap"]; if (y.Length > 0 && !ym0.Contains(y)) ym0.Add(y); }
                    r["ymaps"] = ym0; r["placements"] = at.Count;
                    if (at.Count > 1) warns.Add(nm + " is placed " + at.Count + " times; it opens at the first place (" + ym0[0] + ").");
                    list.Add(r);
                }
            }
            if (list.Count == 0) throw new Exception("The .ytyp files of " + dir + " define no interior (MLO), only props.");
            Dictionary<string, object> res = new Dictionary<string, object>();
            res["ok"] = true; res["dir"] = dir; res["resource"] = Path.GetFileName(dir); res["interiors"] = list; res["warnings"] = warns;
            res["models"] = Vanilla.LooseNames(".ydr").Count + Vanilla.LooseNames(".yft").Count;
            return res;
        }

        // the copy of the resource's map files (.ytyp, .ymap, .ybn) a project starts from; its made.txt lists the files the project's last export put into the resource
        public static string Snapshot(string dir, string cacheDir)
        {
            string snap = Path.Combine(cacheDir, "resource_snapshots", Regex.Replace(Path.GetFileName(dir).ToLowerInvariant(), "[^a-z0-9_\\-]", "_") + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"));
            Directory.CreateDirectory(snap); HashSet<string> seen = new HashSet<string>();
            List<string> files = new List<string>(Directory.GetFiles(dir, "*", SearchOption.AllDirectories)); files.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string f in files)
            {
                string k = Path.GetFileName(f).ToLowerInvariant(), ext = Path.GetExtension(k);
                if ((ext == ".ytyp" || ext == ".ymap" || ext == ".ybn") && seen.Add(k)) File.Copy(f, Path.Combine(snap, k));
            }
            File.WriteAllText(Path.Combine(snap, "resource.txt"), dir + "\r\n");
            return snap;
        }
        public static HashSet<string> MadeBy(string snap, string dir)
        {
            HashSet<string> r = new HashSet<string>(); if (string.IsNullOrEmpty(snap)) return r; string f = Path.Combine(snap, "made.txt"); if (!File.Exists(f)) return r;
            foreach (string l in File.ReadAllLines(f)) { string t = l.Trim(); if (t.Length > 0) r.Add(Path.GetFileName(t).ToLowerInvariant()); }
            return r;
        }

        // ---------------- exporting into the resource itself ----------------
        // Before anything is written: the whole folder zipped next to it, then the files the project's last export made are taken out (they are in the zip;
        // this export makes them again). Returns the zip and the resource's files as they are now (to find the ones this export adds).
        static string BackupResource(string dir, string snap, List<string> log, out HashSet<string> before)
        {
            string zip = Path.Combine(Path.GetDirectoryName(dir), Path.GetFileName(dir) + "_backup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".zip");
            if (File.Exists(zip)) File.Delete(zip);
            System.IO.Compression.ZipFile.CreateFromDirectory(dir, zip, System.IO.Compression.CompressionLevel.Fastest, false);
            int gone = 0;
            foreach (string k in MadeBy(snap, dir))
            {
                string p = Path.Combine(dir, "stream", k); if (File.Exists(p)) { File.Delete(p); gone++; }
            }
            if (gone > 0) log.Add(gone + " file(s) of the project's last export taken out of the resource (made again now)");
            before = new HashSet<string>(StringComparer.OrdinalIgnoreCase); foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories)) before.Add(f);
            return zip;
        }
        // after the export: the files it added (they are the project's, made again by its next export)
        static void RememberMade(string dir, string snap, HashSet<string> before)
        {
            List<string> made = new List<string>();
            foreach (string f in Directory.GetFiles(Path.Combine(dir, "stream"))) if (!before.Contains(f)) made.Add(Path.GetFileName(f));
            File.WriteAllLines(Path.Combine(snap, "made.txt"), made.ToArray());
        }
        // a file of the export: where the opened resource has it (in place, wherever it is under its folder), else into stream/. Returns its path in the resource.
        static string Write(string stream, string name, byte[] data)
        {
            string home = Vanilla.LooseHome(name), p = home ?? Path.Combine(stream, name);
            File.WriteAllBytes(p, data);
            string root = Path.GetDirectoryName(stream); return p.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? p.Substring(root.Length + 1).Replace('\\', '/') : p;
        }
        // the lines the resource's fxmanifest.lua needs for what the export added (only the missing ones, at its end)
        static bool AddToManifest(string dir, List<string> lines, List<string> log)
        {
            string f = Path.Combine(dir, "fxmanifest.lua"); if (!File.Exists(f)) f = Path.Combine(dir, "__resource.lua"); if (!File.Exists(f) || lines.Count == 0) return false;
            string txt = File.ReadAllText(f); List<string> add = new List<string>();
            foreach (string l in lines) { string core = Regex.Replace(l, "\\s+", ""); if (Regex.Replace(txt, "\\s+", "").IndexOf(core, StringComparison.OrdinalIgnoreCase) < 0) add.Add(l); }
            if (add.Count == 0) return false;
            File.WriteAllText(f, txt.TrimEnd() + "\n\n-- added by MLO Creator\n" + string.Join("\n", add.ToArray()) + "\n", new UTF8Encoding(false));
            log.Add(Path.GetFileName(f) + ": " + add.Count + " line(s) added (" + string.Join(", ", add.ToArray()) + ")");
            return true;
        }
    }
}
