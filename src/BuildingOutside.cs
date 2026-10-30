// The building around a game interior (Buildings of the game): the objects the game's map files place there (the building's own models, its parts, the props
// round it), read so they can be shown, moved, turned, copied and deleted with the interior's, and written back into the map files that place them. A map
// file is the game's, read as CodeWalker's XML; only the entries of the changed objects are touched. C# 5.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;
using CodeWalker.GameFiles;
using SharpDX;

namespace MloCreator
{
    public static partial class Buildings
    {
        // The map's objects whose middle is in the interior's box (in its own frame: mn, mx) and `margin` metres round it, full detail only (the low detail
        // copies the game shows from afar are left alone), each once with every map file that places it (a DLC's copy of a map file places them again).
        // Each one in the interior's frame, like the interior's objects: { n, p, q (real turn), s, level, flags, lod, ext: [{ y: map file, i: its index among the
        // file's entities }] }.
        public static List<object> Outside(Dictionary<string, object> world, double[] mn, double[] mx, double margin, string gta, string cacheDir, List<string> log)
        {
            Vanilla.Init(gta, cacheDir);
            double wx = J.D(J.G(world, "x"), 0), wy = J.D(J.G(world, "y"), 0), wz = J.D(J.G(world, "z"), 0), h = J.D(J.G(world, "heading"), 0) * Math.PI / 180, c = Math.Cos(h), s = Math.Sin(h);
            // the box in the world
            double x0 = 1e9, y0 = 1e9, x1 = -1e9, y1 = -1e9;
            foreach (double lx in new double[] { mn[0] - margin, mx[0] + margin }) foreach (double ly in new double[] { mn[1] - margin, mx[1] + margin })
                { double px = wx + lx * c - ly * s, py = wy + lx * s + ly * c; x0 = Math.Min(x0, px); y0 = Math.Min(y0, py); x1 = Math.Max(x1, px); y1 = Math.Max(y1, py); }
            Quaternion qinv = Quaternion.RotationAxis(Vector3.UnitZ, (float)-h);
            Dictionary<string, Dictionary<string, object>> byKey = new Dictionary<string, Dictionary<string, object>>(); List<object> res = new List<object>();
            // (the building's big models stand where their block's middle is, often well away: one that stands up to 80 m away counts when its box reaches in)
            const double FAR = 80;
            List<string> maps = Vanilla.YmapsAt(x0 - FAR, y0 - FAR, x1 + FAR, y1 + FAR); int n = 0;
            foreach (string mn0 in maps)
            {
                n++; Vanilla.Status = "Reading the building's map files: " + n + " / " + maps.Count;
                try
                {
                    RpfFileEntry fe; byte[] yd = Vanilla.Bytes(mn0, out fe); if (yd == null) continue;
                    YmapFile y = new YmapFile(); y.Load(yd, fe);
                    if (y.CEntityDefs == null) continue;
                    for (int i = 0; i < y.CEntityDefs.Length; i++)
                    {
                        CEntityDef d = y.CEntityDefs[i]; string lod = d.lodLevel.ToString(); if (lod != "LODTYPES_DEPTH_HD" && lod != "LODTYPES_DEPTH_ORPHANHD") continue;
                        Vector3 p = d.position; if (p.X < x0 - FAR || p.X > x1 + FAR || p.Y < y0 - FAR || p.Y > y1 + FAR) continue;
                        string name = JenkIndex.TryGetString(d.archetypeName.Hash); if (string.IsNullOrEmpty(name) || !Vanilla.HasModel(name)) continue;
                        bool near = p.X >= x0 && p.X <= x1 && p.Y >= y0 && p.Y <= y1 && p.Z >= wz + mn[2] - margin - 5 && p.Z <= wz + mx[2] + margin + 5;
                        if (!near)
                        {
                            // a big model (over 100 KB) whose box, as a ball round where it stands, reaches into the box
                            long size; Vector3[] bb = Vanilla.ModelBox(name, out size); if (bb == null || size < 100000) continue;
                            double sc = Math.Max(d.scaleXY, d.scaleZ); if (sc <= 0) sc = 1;
                            Vector3 lo = bb[0], hi = bb[1]; double r = sc * Math.Max(Math.Max(Math.Abs(lo.X), Math.Abs(hi.X)), Math.Max(Math.Max(Math.Abs(lo.Y), Math.Abs(hi.Y)), Math.Max(Math.Abs(lo.Z), Math.Abs(hi.Z)))) * 1.42;
                            double cx = Math.Max(x0, Math.Min(x1, p.X)), cy = Math.Max(y0, Math.Min(y1, p.Y)); if ((cx - p.X) * (cx - p.X) + (cy - p.Y) * (cy - p.Y) > r * r) continue;
                            if (p.Z + r < wz + mn[2] - margin || p.Z - r > wz + mx[2] + margin) continue;
                        }
                        name = name.ToLowerInvariant();
                        string key = d.archetypeName.Hash + "|" + Math.Round(p.X, 2) + "|" + Math.Round(p.Y, 2) + "|" + Math.Round(p.Z, 2);
                        Dictionary<string, object> e; List<object> files;
                        if (byKey.TryGetValue(key, out e)) { files = (List<object>)e["ext"]; files.Add(new Dictionary<string, object> { { "y", mn0 }, { "i", i } }); continue; }
                        // where it stands in the interior's frame; a map entity's turn is stored as the inverse of the real one
                        double dx = p.X - wx, dy = p.Y - wy;
                        Quaternion real = new Quaternion(-d.rotation.X, -d.rotation.Y, -d.rotation.Z, d.rotation.W), loc = qinv * real; loc.Normalize();
                        e = new Dictionary<string, object>();
                        e["n"] = name; e["p"] = new double[] { Math.Round(dx * c + dy * s, 4), Math.Round(-dx * s + dy * c, 4), Math.Round(p.Z - wz, 4) };
                        e["q"] = new double[] { Math.Round(loc.X, 6), Math.Round(loc.Y, 6), Math.Round(loc.Z, 6), Math.Round(loc.W, 6) };
                        e["s"] = new double[] { d.scaleXY, d.scaleZ }; e["level"] = lod.Replace("LODTYPES_DEPTH_", ""); e["flags"] = d.flags; e["lod"] = d.lodDist;
                        e["room"] = -2; e["set"] = -1; e["o"] = -1; e["ext"] = new List<object> { new Dictionary<string, object> { { "y", mn0 }, { "i", i } } };
                        byKey[key] = e; res.Add(e);
                    }
                }
                catch (Exception ex) { log.Add(mn0 + ": " + ex.Message); }
            }
            Vanilla.Status = "";
            log.Add(res.Count + " objects of the map in " + maps.Count + " map files round the interior");
            return res;
        }

        // Writes the map files of the changed outside objects into stream/: moved or turned ones get their new place (back in the world), deleted ones are
        // taken out, copies are added after the file's last entity. ents: the editor's objects (those with ext). Returns the files written.
        // rename: changed models (the game's name -> the new one): the objects that show one are pointed at the new one, even when not moved
        // removedWorld gets where removed or moved objects stood whose collision is in the map's files (in the world), each once
        public static List<string> ExportOutside(List<object> ents, Dictionary<string, object> world, string stream, List<string> log, List<string> warns, Dictionary<string, string> rename, List<CutBox> removedWorld)
        {
            HashSet<string> removedSeen = new HashSet<string>();
            double wx = J.D(J.G(world, "x"), 0), wy = J.D(J.G(world, "y"), 0), wz = J.D(J.G(world, "z"), 0), h = J.D(J.G(world, "heading"), 0) * Math.PI / 180, c = Math.Cos(h), s = Math.Sin(h);
            Quaternion qw = Quaternion.RotationAxis(Vector3.UnitZ, (float)h);
            // what to do in each map file: index -> new place (or null: delete); copies to add (their source index, new place)
            Dictionary<double[], string> nameOf = new Dictionary<double[], string>();          // a place -> the changed model the object shows there
            Dictionary<string, Dictionary<int, double[]>> edits = new Dictionary<string, Dictionary<int, double[]>>(); Dictionary<string, List<KeyValuePair<int, double[]>>> adds = new Dictionary<string, List<KeyValuePair<int, double[]>>>();
            foreach (object o in ents)
            {
                Dictionary<string, object> e = J.O(o); if (e == null) continue; List<object> ext = J.A(J.G(e, "ext")); if (ext.Count == 0) continue;
                bool del = J.B(J.G(e, "del"), false), dup = J.B(J.G(e, "dup"), false), ch = J.B(J.G(e, "ch"), false) || (rename != null && rename.ContainsKey(J.S(J.G(e, "n"), "").ToLowerInvariant())); if (!del && !ch && !dup) continue;
                string newName = null; if (rename != null) rename.TryGetValue(J.S(J.G(e, "n"), "").ToLowerInvariant(), out newName);
                double[] place = null;
                if (!del)
                {
                    List<object> p = J.A(J.G(e, "p")), q = J.A(J.G(e, "q")), sc = J.A(J.G(e, "s")); double lx = J.D(p[0], 0), ly = J.D(p[1], 0);
                    Quaternion real = qw * new Quaternion((float)J.D(q[0], 0), (float)J.D(q[1], 0), (float)J.D(q[2], 0), (float)J.D(q[3], 1)); real.Normalize();
                    place = new double[] { wx + lx * c - ly * s, wy + lx * s + ly * c, wz + J.D(p[2], 0), -real.X, -real.Y, -real.Z, real.W, sc.Count > 0 ? J.D(sc[0], 1) : 1, sc.Count > 1 ? J.D(sc[1], 1) : 1 };      // (stored as the inverse)
                    if (newName != null) nameOf[place] = newName;
                }
                foreach (object fo in ext)
                {
                    Dictionary<string, object> f = J.O(fo); string y = J.S(J.G(f, "y"), ""); int i = J.I(J.G(f, "i"), -1); if (y.Length == 0 || i < 0) continue;
                    if (dup) { if (del) continue; List<KeyValuePair<int, double[]>> l; if (!adds.TryGetValue(y, out l)) adds[y] = l = new List<KeyValuePair<int, double[]>>(); l.Add(new KeyValuePair<int, double[]>(i, place)); continue; }
                    Dictionary<int, double[]> d; if (!edits.TryGetValue(y, out d)) edits[y] = d = new Dictionary<int, double[]>(); d[i] = place;
                }
            }
            List<string> files = new List<string>(); HashSet<string> names = new HashSet<string>(edits.Keys); foreach (string k in adds.Keys) names.Add(k);
            foreach (string y in names)
            {
                RpfFileEntry fe; byte[] yd = Vanilla.Bytes(y, out fe); if (yd == null) { warns.Add("The game has no map file " + y + "; its changes were left out."); continue; }
                YmapFile ym = new YmapFile(); ym.Load(yd, fe);
                string fn; XmlDocument doc = new XmlDocument(); doc.LoadXml(MetaXml.GetXml(ym, out fn));
                XmlNode entsEl = doc.SelectSingleNode("/CMapData/entities"); if (entsEl == null) continue;
                List<XmlNode> items = new List<XmlNode>(); foreach (XmlNode it in entsEl.SelectNodes("Item")) { XmlAttribute ta = it.Attributes == null ? null : it.Attributes["type"]; if (ta != null && ta.Value == "CEntityDef") items.Add(it); }
                int before = items.Count, moved = 0, removed = 0, copied = 0; List<XmlNode> drop = new List<XmlNode>();
                Dictionary<int, double[]> d0; if (edits.TryGetValue(y, out d0))
                    foreach (KeyValuePair<int, double[]> kv in d0)
                    {
                        if (kv.Key >= items.Count) continue;
                        // where it stood, when it is taken away or moved (or turned): its collision, if it is in the map's files, is cut away there
                        double[] was = PlaceOf(items[kv.Key]), v = kv.Value;
                        bool gone = v == null || Math.Abs(was[0] - v[0]) + Math.Abs(was[1] - v[1]) + Math.Abs(was[2] - v[2]) > 0.05 || Math.Abs(Math.Abs(was[3] * -v[3] + was[4] * -v[4] + was[5] * -v[5] + was[6] * v[6]) - 1) > 0.0005;
                        string rk = Math.Round(was[0], 2) + "|" + Math.Round(was[1], 2) + "|" + Math.Round(was[2], 2);
                        if (gone && removedWorld != null && removedSeen.Add(rk)) { CutBox rb = RemovalBoxAt(items[kv.Key]); if (rb != null) removedWorld.Add(rb); }
                        if (kv.Value == null) { drop.Add(items[kv.Key]); removed++; } else { Place(items[kv.Key], kv.Value); Rename(items[kv.Key], nameOf, kv.Value); moved++; }
                    }
                List<KeyValuePair<int, double[]>> a0; if (adds.TryGetValue(y, out a0))
                    foreach (KeyValuePair<int, double[]> kv in a0)
                    {
                        if (kv.Key >= items.Count || kv.Value == null) continue;
                        XmlNode cp = items[kv.Key].CloneNode(true); Place(cp, kv.Value); Rename(cp, nameOf, kv.Value); SetVal(cp, "guid", "0"); entsEl.AppendChild(cp); copied++;
                    }
                // (an object taken out of a file that the game's lower detail copies point into would shift them: only full detail files are read, which have none)
                foreach (XmlNode dn in drop) entsEl.RemoveChild(dn);
                byte[] data = XmlMeta.GetRSCData(doc); if (data == null) { warns.Add(y + " could not be written."); continue; }
                YmapFile chk = new YmapFile(); RpfFile.LoadResourceFile(chk, data, 2);
                int got = chk.CEntityDefs != null ? chk.CEntityDefs.Length : 0;
                if (got != before - removed + copied) { warns.Add(y + ": the written file has " + got + " objects instead of " + (before - removed + copied) + "; it was left out."); continue; }
                files.Add(Write(stream, y, data));
                log.Add(y + ": " + moved + " moved or turned, " + removed + " deleted, " + copied + " copied (the building round the interior)");
            }
            return files;
        }
        // the --outside-probe of Program: the objects 10 m round a point, and for the first ones the turn CodeWalker gives them next to the one read here
        public static List<string> OutsideProbe(double[] v, string gta, string cacheDir)
        {
            List<string> log = new List<string>();
            List<object> found = Outside(new Dictionary<string, object> { { "x", v[0] }, { "y", v[1] }, { "z", v[2] }, { "heading", v[3] } }, new double[] { -10, -10, -2 }, new double[] { 10, 10, 8 }, 6, gta, cacheDir, log);
            Quaternion qw = Quaternion.RotationAxis(Vector3.UnitZ, (float)(v[3] * Math.PI / 180));
            for (int k = 0; k < Math.Min(6, found.Count); k++)
            {
                Dictionary<string, object> e = (Dictionary<string, object>)found[k]; Dictionary<string, object> f0 = (Dictionary<string, object>)((List<object>)e["ext"])[0]; int idx = (int)f0["i"];
                RpfFile ow; RpfFileEntry fe = Vanilla.GetEntry((string)f0["y"], out ow); YmapFile y = new YmapFile(); y.Load(ow.ExtractFile(fe), fe);
                YmapEntityDef en = null; foreach (YmapEntityDef a in y.AllEntities) if (a.CEntityDef.position == y.CEntityDefs[idx].position && a.CEntityDef.archetypeName.Hash == y.CEntityDefs[idx].archetypeName.Hash) en = a;
                double[] q = (double[])e["q"]; Quaternion back = qw * new Quaternion((float)q[0], (float)q[1], (float)q[2], (float)q[3]);
                log.Add(e["n"] + " (" + ((List<object>)e["ext"]).Count + " files) local " + string.Join(", ", Array.ConvertAll((double[])e["p"], d => d.ToString("0.##"))) + " | stored " + y.CEntityDefs[idx].rotation + " | real from here " + back + " | CodeWalker " + (en == null ? "?" : en.Orientation.ToString()));
            }
            return log;
        }
        static void Rename(XmlNode n, Dictionary<double[], string> nameOf, double[] place) { string nm; if (!nameOf.TryGetValue(place, out nm)) return; XmlNode an = n.SelectSingleNode("archetypeName"); if (an != null) an.InnerText = nm; }
        static void Place(XmlNode n, double[] v)
        {
            SetXyz(n, "position", v[0], v[1], v[2]);
            XmlElement r = n.SelectSingleNode("rotation") as XmlElement; if (r != null) { r.SetAttribute("x", F(v[3])); r.SetAttribute("y", F(v[4])); r.SetAttribute("z", F(v[5])); r.SetAttribute("w", F(v[6])); }
            SetVal(n, "scaleXY", F(v[7])); SetVal(n, "scaleZ", F(v[8]));
        }
    }
}
