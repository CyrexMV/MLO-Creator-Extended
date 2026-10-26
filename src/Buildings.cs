// The game's buildings by name: an index of every interior (MLO) the game's map files place, with its rooms, objects, portals and entity sets (built once, then
// cached on disk), and a list of well-known places (police stations, hospitals, city halls...) under the words people use for them, in English and German.
// A search gives the interiors at a place, or, where the game has none, the models of the building's outside. C# 5.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Security;
using System.Xml;
using CodeWalker.GameFiles;
using SharpDX;

namespace MloCreator
{
    public class MloEntry
    {
        public uint Hash; public string Name, Ytyp, Ymaps; public float X, Y, Z, Heading; public int Rooms, Entities, Portals;
        public string[] RoomNames = new string[0], Sets = new string[0];
        public string Hay;              // what a search by name looks through (lower case)
    }
    public class Place
    {
        public string Label, Words; public float X, Y, R;
        public Place(string label, string words, float x, float y, float r) { Label = label; Words = words; X = x; Y = y; R = r; }
    }

    public static class Buildings
    {
        static List<MloEntry> idx; static string idxStamp;
        static readonly object gate = new object();
        static readonly CultureInfo IC = CultureInfo.InvariantCulture;

        // Where the well-known buildings stand (the middle of the building, world x / y) and how far round it to look. The words are matched by their start, so
        // "polic" finds every police station; a word only one entry has (lspd, mrpd, pillbox) finds that one.
        public static readonly Place[] Places = new Place[] {
            // police
            new Place("Mission Row Police Station", "lspd mrpd mission row police polizei pd station wache revier", 441, -982, 80),
            new Place("Vespucci Police Station", "vespucci vpd police polizei station wache revier", -1093, -810, 80),
            new Place("La Mesa Police Station", "la mesa police polizei station wache revier", 826, -1290, 70),
            new Place("Davis Sheriff's Station", "davis sheriff lssd police polizei station wache revier", 360, -1584, 70),
            new Place("Rockford Hills Police Station", "rockford hills police polizei station wache revier", -561, -131, 60),
            new Place("Vinewood Police Station", "vinewood police polizei station wache revier", 638, 1, 70),
            new Place("Sandy Shores Sheriff's Office", "sandy shores sheriff bcso police polizei station wache revier", 1853, 3686, 60),
            new Place("Paleto Bay Sheriff's Office", "paleto bay sheriff bcso police polizei station wache revier", -448, 6012, 60),
            new Place("Bolingbroke Penitentiary", "bolingbroke penitentiary prison jail gefaengnis knast", 1690, 2590, 220),
            // hospitals
            new Place("Pillbox Hill Medical Center", "pillbox hill medical center hospital krankenhaus klinik ems ambulance", 330, -590, 90),
            new Place("Central Los Santos Medical Center", "central los santos medical center hospital krankenhaus klinik ems", 340, -1396, 70),
            new Place("Mount Zonah Medical Center", "mount zonah medical center hospital krankenhaus klinik ems", -449, -340, 70),
            new Place("St. Fiacre Hospital", "st saint fiacre hospital krankenhaus klinik ems", 1152, -1527, 50),
            new Place("Sandy Shores Medical Center", "sandy shores medical center hospital krankenhaus klinik ems", 1839, 3672, 50),
            new Place("Paleto Bay Care Center", "paleto bay care center medical hospital krankenhaus klinik ems", -247, 6331, 50),
            new Place("Eclipse Medical Tower", "eclipse medical tower hospital krankenhaus klinik", -676, 310, 60),
            new Place("Coroner's Office (morgue)", "coroner morgue leichenhalle pathologie", 240, -1379, 50),
            // fire stations
            new Place("El Burro Heights Fire Station", "el burro heights fire station feuerwehr feuerwache lsfd fd", 1193, -1473, 50),
            new Place("Davis Fire Station", "davis fire station feuerwehr feuerwache lsfd fd", 205, -1650, 50),
            new Place("Rockford Hills Fire Station", "rockford hills fire station feuerwehr feuerwache lsfd fd", -635, -121, 50),
            new Place("Sandy Shores Fire Station", "sandy shores fire station feuerwehr feuerwache bcfd fd", 1691, 3585, 50),
            new Place("Paleto Bay Fire Station", "paleto bay fire station feuerwehr feuerwache bcfd fd", -379, 6118, 50),
            // government
            new Place("Los Santos City Hall", "los santos city hall government gov rathaus regierung courthouse court gericht doj", 236, -410, 80),
            new Place("Rockford Hills City Hall", "rockford hills city hall government gov rathaus regierung", -544, -204, 80),
            new Place("FIB Building", "fib federal investigation bureau government gov regierung", 136, -749, 90),
            new Place("IAA Building", "iaa government gov regierung", 117, -620, 60),
            new Place("Fort Zancudo", "fort zancudo military army militaer armee base", -2360, 3250, 400),
            new Place("Humane Labs", "humane labs research lab labor", 3540, 3700, 150),
            // banks
            new Place("Pacific Standard Bank", "pacific standard bank vault tresor", 235, 216, 70),
            new Place("Fleeca Bank (Legion Square)", "fleeca bank legion square", 149, -1040, 40),
            new Place("Fleeca Bank (Burton)", "fleeca bank burton hawick", 314, -278, 40),
            new Place("Fleeca Bank (Rockford Hills)", "fleeca bank rockford hills", -351, -49, 40),
            new Place("Fleeca Bank (Del Perro)", "fleeca bank del perro", -1212, -330, 40),
            new Place("Fleeca Bank (Great Ocean Highway)", "fleeca bank great ocean highway chumash", -2962, 482, 40),
            new Place("Fleeca Bank (Route 68)", "fleeca bank route 68 harmony", 1175, 2706, 40),
            new Place("Blaine County Savings Bank", "blaine county savings bank paleto", -111, 6467, 40),
            new Place("Maze Bank Tower", "maze bank tower", -75, -818, 80),
            // businesses and story places
            new Place("Vanilla Unicorn", "vanilla unicorn strip club stripclub", 127, -1300, 50),
            new Place("Tequi-la-la", "tequilala tequi la la bar club", -565, 276, 40),
            new Place("Bahama Mamas West", "bahama mamas west club", -1388, -618, 50),
            new Place("Lifeinvader", "lifeinvader life invader office", -1083, -246, 50),
            new Place("Weazel News", "weazel news tv studio", -598, -929, 60),
            new Place("Premium Deluxe Motorsport (Simeon)", "pdm premium deluxe motorsport simeon car dealer dealership autohaus", -47, -1097, 50),
            new Place("Los Santos Customs (Burton)", "lsc los santos customs burton tuning werkstatt mechanic", -365, -131, 40),
            new Place("Benny's Original Motor Works", "bennys benny original motor works tuning werkstatt mechanic", -205, -1312, 50),
            new Place("Ammu-Nation (Pillbox Hill)", "ammunation ammu nation gun store shop waffenladen", 22, -1107, 30),
            new Place("Lost MC Clubhouse", "lost mc clubhouse biker", 982, -103, 50),
            new Place("Diamond Casino", "diamond casino resort", 924, 46, 120),
            new Place("Michael's House", "michael de santa house haus villa", -812, 179, 50),
            new Place("Franklin's House", "franklin house haus vinewood hills", 7, 536, 50),
            new Place("Franklin's Aunt's House", "franklin aunt denise house haus forum", -14, -1440, 40),
            new Place("Trevor's Trailer", "trevor trailer wohnwagen sandy", 1973, 3815, 40),
            new Place("Lester's House", "lester house haus el burro", 1273, -1719, 40),
            new Place("Floyd's Apartment", "floyd apartment wohnung vespucci", -1150, -1520, 40),
            new Place("Los Santos International Airport", "lsia airport flughafen international", -1037, -2737, 220),
            new Place("Galileo Observatory", "galileo observatory sternwarte", -425, 1123, 60),
            new Place("Cluckin' Bell Factory", "cluckin bell factory chicken fabrik paleto", -70, 6250, 120)
        };

        // ---------------- the index of the game's interiors ----------------
        static string Clean(string s) { return (s ?? "").Replace('\t', ' ').Replace(';', ',').Replace('\r', ' ').Replace('\n', ' '); }

        static void EnsureIndex()
        {
            lock (gate)
            {
                string stamp = Vanilla.Stamp();
                if (idx != null && idxStamp == stamp) return;
                string f = Path.Combine(Vanilla.CacheDir, "mlo_index.txt");
                List<MloEntry> list = new List<MloEntry>();
                if (File.Exists(f))
                {
                    string[] lines = File.ReadAllLines(f, Encoding.UTF8);
                    if (lines.Length > 0 && lines[0] == stamp)
                    {
                        for (int i = 1; i < lines.Length; i++) { MloEntry e = ParseLine(lines[i]); if (e != null) list.Add(e); }
                        idx = list; idxStamp = stamp; return;
                    }
                }
                // every interior the map files place (the same one at the same spot in several files is listed once, with every file)
                Dictionary<string, MloEntry> byKey = new Dictionary<string, MloEntry>();
                List<string> maps = Vanilla.Names(".ymap"); int n = 0;
                foreach (string mn in maps)
                {
                    n++; if (n % 300 == 0) Vanilla.Status = "Finding the game's interiors, first time only: map file " + n + " / " + maps.Count;
                    if (mn.EndsWith("_lod.ymap") || mn.IndexOf("occl", StringComparison.Ordinal) >= 0) continue;
                    try
                    {
                        RpfFile owner; RpfFileEntry fe = Vanilla.GetEntry(mn, out owner); if (fe == null) continue;
                        YmapFile y = new YmapFile(); y.Load(owner.ExtractFile(fe), fe);
                        if (y.MloEntities == null) continue;
                        foreach (YmapEntityDef en in y.MloEntities)
                        {
                            uint h = en.CEntityDef.archetypeName.Hash;
                            string key = h + "|" + Math.Round(en.Position.X, 1) + "|" + Math.Round(en.Position.Y, 1) + "|" + Math.Round(en.Position.Z, 1);
                            MloEntry e; if (byKey.TryGetValue(key, out e)) { e.Ymaps += "," + mn; continue; }
                            Quaternion q = en.Orientation;
                            e = new MloEntry(); e.Hash = h; e.Ymaps = mn; e.X = en.Position.X; e.Y = en.Position.Y; e.Z = en.Position.Z;
                            e.Heading = (float)(2 * Math.Atan2(q.Z, q.W) * 180 / Math.PI);
                            byKey[key] = e;
                        }
                    }
                    catch (Exception) { }
                }
                // what each interior holds, read from the map types file that defines it
                Dictionary<string, List<MloEntry>> byYtyp = new Dictionary<string, List<MloEntry>>();
                foreach (MloEntry e in byKey.Values)
                {
                    e.Ytyp = Vanilla.YtypOf(e.Hash) ?? "";
                    List<MloEntry> l; if (!byYtyp.TryGetValue(e.Ytyp, out l)) byYtyp[e.Ytyp] = l = new List<MloEntry>(); l.Add(e);
                }
                n = 0;
                foreach (KeyValuePair<string, List<MloEntry>> kv in byYtyp)
                {
                    n++; if (n % 20 == 0) Vanilla.Status = "Reading the game's interiors, first time only: " + n + " / " + byYtyp.Count;
                    Dictionary<uint, MloArchetype> arch = new Dictionary<uint, MloArchetype>();
                    if (kv.Key.Length > 0)
                    {
                        try
                        {
                            RpfFile owner; RpfFileEntry fe = Vanilla.GetEntry(kv.Key, out owner);
                            if (fe != null)
                            {
                                YtypFile yt = new YtypFile(); yt.Load(owner.ExtractFile(fe), fe);
                                if (yt.AllArchetypes != null) foreach (Archetype a in yt.AllArchetypes) { MloArchetype m = a as MloArchetype; if (m != null) arch[m.Hash.Hash] = m; }
                            }
                        }
                        catch (Exception) { }
                    }
                    foreach (MloEntry e in kv.Value)
                    {
                        MloArchetype m; arch.TryGetValue(e.Hash, out m);
                        e.Name = m != null ? m.Name : null;
                        if (string.IsNullOrEmpty(e.Name) || Regex.IsMatch(e.Name, "^(0x)?[0-9A-Fa-f]{8}$|^[0-9]+$")) { string s = JenkIndex.TryGetString(e.Hash); e.Name = string.IsNullOrEmpty(s) ? "0x" + e.Hash.ToString("X8") : s; }
                        if (m == null) continue;
                        List<string> rooms = new List<string>(), sets = new List<string>();
                        if (m.rooms != null) foreach (MCMloRoomDef r in m.rooms) { string rn = r.RoomName; if (!string.IsNullOrEmpty(rn) && rn.ToLowerInvariant() != "limbo") rooms.Add(Clean(rn)); }
                        if (m.entitySets != null) foreach (MCMloEntitySet s in m.entitySets) { string sn = s.Name; if (!string.IsNullOrEmpty(sn)) sets.Add(Clean(sn)); }
                        e.Rooms = rooms.Count; e.RoomNames = rooms.ToArray(); e.Sets = sets.ToArray();
                        e.Entities = m.entities != null ? m.entities.Length : 0; e.Portals = m.portals != null ? m.portals.Length : 0;
                    }
                }
                list.AddRange(byKey.Values);
                StringBuilder sb = new StringBuilder(); sb.AppendLine(stamp);
                foreach (MloEntry e in list)
                {
                    sb.AppendLine(string.Join("\t", new string[] { e.Hash.ToString(IC), Clean(e.Name), e.Ytyp, e.Ymaps, e.X.ToString("0.###", IC), e.Y.ToString("0.###", IC), e.Z.ToString("0.###", IC), e.Heading.ToString("0.###", IC),
                        e.Rooms.ToString(IC), e.Entities.ToString(IC), e.Portals.ToString(IC), string.Join(";", e.RoomNames), string.Join(";", e.Sets) }));
                    SetHay(e);
                }
                try { File.WriteAllText(f, sb.ToString(), new UTF8Encoding(false)); } catch (Exception) { }
                idx = list; idxStamp = stamp; Vanilla.Status = "";
            }
        }

        static MloEntry ParseLine(string line)
        {
            string[] p = line.Split('\t'); if (p.Length < 13) return null;
            try
            {
                MloEntry e = new MloEntry(); e.Hash = uint.Parse(p[0], IC); e.Name = p[1]; e.Ytyp = p[2]; e.Ymaps = p[3];
                e.X = float.Parse(p[4], IC); e.Y = float.Parse(p[5], IC); e.Z = float.Parse(p[6], IC); e.Heading = float.Parse(p[7], IC);
                e.Rooms = int.Parse(p[8], IC); e.Entities = int.Parse(p[9], IC); e.Portals = int.Parse(p[10], IC);
                e.RoomNames = p[11].Length > 0 ? p[11].Split(';') : new string[0]; e.Sets = p[12].Length > 0 ? p[12].Split(';') : new string[0];
                SetHay(e); return e;
            }
            catch (Exception) { return null; }
        }
        static void SetHay(MloEntry e) { e.Hay = (e.Name + " " + e.Ytyp + " " + string.Join(" ", e.RoomNames) + " " + string.Join(" ", e.Sets)).ToLowerInvariant().Replace('_', ' '); }

        // ---------------- searching ----------------
        // lower case, umlauts written out, everything that is not a letter or digit a space
        static string Norm(string s)
        {
            s = (s ?? "").ToLowerInvariant().Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue").Replace("ß", "ss").Replace("'", "").Replace("-", "");
            return Regex.Replace(s, "[^a-z0-9.]+", " ").Trim();
        }
        static bool PlaceMatches(Place p, string[] words)
        {
            string[] pw = Norm(p.Label + " " + p.Words).Split(' ');
            foreach (string w in words)
            {
                bool hit = false;
                foreach (string x in pw) if (x == w || (w.Length >= 3 && x.StartsWith(w, StringComparison.Ordinal))) { hit = true; break; }
                if (!hit) return false;
            }
            return true;
        }

        // what kind of interior it is besides a building's rooms: "tunnel" (metro, road tunnels, underpasses), "hidden" (far under the map: the game's scripted
        // interiors, entered by a teleport), or "" for a building's own interior
        static readonly Regex TunnelName = new Regex("metro|tunnel|_tun|^tun|subway|undpass|sewer", RegexOptions.IgnoreCase);
        static string Tag(MloEntry e) { if (TunnelName.IsMatch(e.Name ?? "")) return "tunnel"; if (e.Z < -30) return "hidden"; return ""; }

        static Dictionary<string, object> MloJson(MloEntry e, string place, double dist)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            r["kind"] = "interior"; r["tag"] = Tag(e); r["place"] = place; r["name"] = e.Name; r["hash"] = e.Hash; r["ytyp"] = e.Ytyp; r["ymaps"] = e.Ymaps.Split(',');
            r["x"] = e.X; r["y"] = e.Y; r["z"] = e.Z; r["heading"] = e.Heading; r["rooms"] = e.Rooms; r["roomNames"] = e.RoomNames; r["entities"] = e.Entities; r["portals"] = e.Portals; r["sets"] = e.Sets;
            if (dist >= 0) r["dist"] = Math.Round(dist, 1);
            return r;
        }

        // The models of a building's outside at a place without an interior: the big models whose box covers the place's middle (the biggest file first: the
        // building, not its lights or decals), else the nearest ones.
        static readonly Regex NotBuilding = new Regex("emissive|light|_lod|decal|_dtl|detail|shadow|proxy|_ovly|overlay|glass|_rail|ladder|_veg|_weed|_grass|ground|grnd|terrain|land\\d|road|_rd|splat|glue|^prop_", RegexOptions.IgnoreCase);
        static List<object> OutsideModels(Place p)
        {
            List<EntityHit> hits = Vanilla.FindEntities(p.X, p.Y, Math.Min(p.R, 120));
            hits.Sort(delegate (EntityHit a, EntityHit b) { if (a.Covers != b.Covers) return a.Covers ? -1 : 1; if (a.Covers) return b.Ydr.GetFileSize().CompareTo(a.Ydr.GetFileSize()); return a.Dist.CompareTo(b.Dist); });
            List<object> res = new List<object>(); HashSet<string> seen = new HashSet<string>();
            foreach (EntityHit h in hits)
            {
                if (NotBuilding.IsMatch(h.Name) || !seen.Add(h.Name.ToLowerInvariant())) continue;
                Quaternion q = h.Rot; Dictionary<string, object> m = new Dictionary<string, object>();
                m["name"] = h.Name; m["ymap"] = h.Ymap; m["x"] = h.Pos.X; m["y"] = h.Pos.Y; m["z"] = h.Pos.Z; m["heading"] = 2 * Math.Atan2(q.Z, q.W) * 180 / Math.PI; m["dist"] = Math.Round(h.Dist, 1); m["covers"] = h.Covers;
                res.Add(m); if (res.Count >= 6) break;
            }
            return res;
        }

        // A search: a place's words ("lspd", "krankenhaus pillbox", "government"), an interior's name or room names ("police", "v_hospital"), or a point ("441 -982").
        // Returns a list of results: interiors ({ kind: "interior", place, name, rooms, ... }) and places where the game has none ({ kind: "exterior", place, models }).
        public static List<object> Search(string query, string gta, string cacheDir, List<string> log)
        {
            Vanilla.Init(gta, cacheDir);
            try { EnsureIndex(); } finally { Vanilla.Status = ""; }
            List<object> res = new List<object>(); HashSet<MloEntry> shown = new HashSet<MloEntry>();
            string q = Norm(query); if (q.Length == 0) return res;

            // a point: two or three numbers
            Match pm = Regex.Match(query ?? "", @"^\s*(-?\d+(?:\.\d+)?)\s*[,; ]\s*(-?\d+(?:\.\d+)?)(?:\s*[,; ]\s*(-?\d+(?:\.\d+)?))?\s*$");
            if (pm.Success)
            {
                double px = double.Parse(pm.Groups[1].Value, IC), py = double.Parse(pm.Groups[2].Value, IC);
                Place here = new Place("Near " + Math.Round(px) + ", " + Math.Round(py), "", (float)px, (float)py, 100);
                AddPlace(here, res, shown); log.Add(res.Count + " result(s) near the point"); return res;
            }

            string[] words = q.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int places = 0;
            foreach (Place p in Places) if (PlaceMatches(p, words)) { AddPlace(p, res, shown); places++; }
            // interiors whose name or rooms have every word
            List<KeyValuePair<double, MloEntry>> named = new List<KeyValuePair<double, MloEntry>>();
            foreach (MloEntry e in idx)
            {
                if (shown.Contains(e)) continue;
                bool all = true; int used = 0;
                foreach (string w in words) { if (w.Length < 3) continue; used++; if (e.Hay.IndexOf(w, StringComparison.Ordinal) < 0) { all = false; break; } }       // ("v" of v_hospital says nothing)
                if (all && used > 0) named.Add(new KeyValuePair<double, MloEntry>(e.Name.ToLowerInvariant().IndexOf(words[0], StringComparison.Ordinal) >= 0 ? 0 : 1, e));
            }
            named.Sort(delegate (KeyValuePair<double, MloEntry> a, KeyValuePair<double, MloEntry> b) { int c = a.Key.CompareTo(b.Key); return c != 0 ? c : string.CompareOrdinal(a.Value.Name, b.Value.Name); });
            foreach (KeyValuePair<double, MloEntry> kv in named) { if (res.Count >= 80) break; res.Add(MloJson(kv.Value, null, -1)); }
            log.Add(places + " place(s) and " + named.Count + " interior(s) by name match \"" + query + "\"; " + idx.Count + " interiors in the index");
            return res;
        }

        // The interiors at a place, nearest first: the building's own ones, then tunnels and hidden ones. An interior that is nearer to another known place belongs
        // to that one (the sheriff next to Sandy Shores' medical center). Where the place has none of its own, its outside models come first.
        // ---------------- one interior, as the game defines it ----------------
        // Everything the editor needs to show an interior as its single parts, in the interior's own frame (the MLO's origin, not turned): its rooms (box, flags,
        // the objects in each), its portals (four corners, the rooms on each side), its objects (model name, position, turn, size, room, entity set) and its
        // entity sets. A room object's rotation is stored as the inverse of its real turn; the turn given here is the real one.
        public static Dictionary<string, object> Interior(string ytyp, uint hash, string gta, string cacheDir)
        {
            Vanilla.Init(gta, cacheDir);
            if (string.IsNullOrEmpty(ytyp)) ytyp = Vanilla.YtypOf(hash);
            RpfFile owner = null; RpfFileEntry fe = string.IsNullOrEmpty(ytyp) ? null : Vanilla.GetEntry(ytyp.ToLowerInvariant(), out owner);
            if (fe == null) throw new Exception("The game has no map types file " + (ytyp ?? "") + " for this interior.");
            YtypFile yt = new YtypFile(); yt.Load(owner.ExtractFile(fe), fe);
            MloArchetype m = null;
            if (yt.AllArchetypes != null) foreach (Archetype a in yt.AllArchetypes) { MloArchetype ma = a as MloArchetype; if (ma != null && ma.Hash.Hash == hash) m = ma; }
            if (m == null) throw new Exception(ytyp + " does not define this interior.");

            List<object> ents = new List<object>();
            int[] roomOf = new int[m.entities != null ? m.entities.Length : 0]; for (int i = 0; i < roomOf.Length; i++) roomOf[i] = -1;
            List<object> rooms = new List<object>();
            if (m.rooms != null)
                for (int r = 0; r < m.rooms.Length; r++)
                {
                    MCMloRoomDef rd = m.rooms[r]; uint[] att = rd.AttachedObjects ?? new uint[0];
                    foreach (uint ai in att) if (ai < roomOf.Length) roomOf[ai] = r;
                    Dictionary<string, object> rj = new Dictionary<string, object>();
                    rj["name"] = rd.RoomName ?? ("room " + r); rj["mn"] = V3(rd.BBMin); rj["mx"] = V3(rd.BBMax); rj["flags"] = rd._Data.flags; rj["floorId"] = rd._Data.floorId;
                    rj["portals"] = rd._Data.portalCount; rj["count"] = att.Length;
                    rooms.Add(rj);
                }
            if (m.entities != null) for (int i = 0; i < m.entities.Length; i++) ents.Add(EntJson(m.entities[i], roomOf[i], -1));
            List<object> sets = new List<object>();
            if (m.entitySets != null)
                for (int s = 0; s < m.entitySets.Length; s++)
                {
                    MCMloEntitySet es = m.entitySets[s]; List<object> idx = new List<object>(); uint[] loc = es.Locations ?? new uint[0];
                    if (es.Entities != null) for (int i = 0; i < es.Entities.Length; i++) { idx.Add(ents.Count); ents.Add(EntJson(es.Entities[i], i < loc.Length ? (int)loc[i] : -1, s)); }
                    Dictionary<string, object> sj = new Dictionary<string, object>(); sj["name"] = es.Name ?? ("set " + s); sj["entities"] = idx; sets.Add(sj);
                }
            List<object> portals = new List<object>();
            if (m.portals != null)
                foreach (MCMloPortalDef pd in m.portals)
                {
                    List<object> cs = new List<object>(); if (pd.Corners != null) foreach (Vector4 c in pd.Corners) cs.Add(new double[] { c.X, c.Y, c.Z });
                    Dictionary<string, object> pj = new Dictionary<string, object>();
                    pj["from"] = pd._Data.roomFrom; pj["to"] = pd._Data.roomTo; pj["flags"] = pd._Data.flags; pj["corners"] = cs; pj["objects"] = pd.AttachedObjects ?? new uint[0];
                    portals.Add(pj);
                }
            Dictionary<string, object> res = new Dictionary<string, object>();
            string nm = m.Name; if (string.IsNullOrEmpty(nm) || Regex.IsMatch(nm, "^(0x)?[0-9A-Fa-f]{8}$|^[0-9]+$")) { string s0 = JenkIndex.TryGetString(hash); nm = string.IsNullOrEmpty(s0) ? "0x" + hash.ToString("X8") : s0; }
            res["name"] = nm; res["hash"] = hash; res["ytyp"] = ytyp; res["rooms"] = rooms; res["portals"] = portals; res["entities"] = ents; res["sets"] = sets;
            res["bmin"] = V3(m.BBMin); res["bmax"] = V3(m.BBMax);
            return res;
        }
        // ---------------- writing a changed interior back: a resource that replaces the game's map types file ----------------
        // The game's .ytyp is read as CodeWalker's XML and only the interior's parts are changed: its objects (each one that came from the game keeps its own
        // entry, so lights and other extensions stay; new ones are written fresh), the objects of each room and portal, and the entity sets. Everything else in
        // the file (the other archetypes, the rooms' and portals' own settings) stays as it was. game: project.game of the editor; its objects carry o (their index in
        // the list Interior gave, -1 for new ones), del (deleted), room and set.
        public static Dictionary<string, object> Export(Dictionary<string, object> game, string resource, string outRoot, string gta, string cacheDir)
        {
            Vanilla.Init(gta, cacheDir);
            List<string> log = new List<string>(), files = new List<string>(), warns = new List<string>();
            string ytyp = J.S(J.G(game, "ytyp"), "").ToLowerInvariant(); uint hash = (uint)J.D(J.G(game, "hash"), 0); string mloName = J.S(J.G(game, "name"), "");
            RpfFile owner; RpfFileEntry fe = Vanilla.GetEntry(ytyp, out owner);
            if (fe == null) throw new Exception("The game has no map types file " + ytyp + ".");
            YtypFile yt = new YtypFile(); yt.Load(owner.ExtractFile(fe), fe);
            string fn; XmlDocument doc = new XmlDocument(); doc.LoadXml(MetaXml.GetXml(yt, out fn));

            XmlNode arch = null;
            foreach (XmlNode it in doc.SelectNodes("/CMapTypes/archetypes/Item"))
            {
                XmlAttribute ta = it.Attributes == null ? null : it.Attributes["type"]; if (ta == null || ta.Value != "CMloArchetypeDef") continue;
                XmlNode nn = it.SelectSingleNode("name"); string n = nn == null ? "" : nn.InnerText.Trim().ToLowerInvariant();
                uint h = n.StartsWith("hash_") ? uint.Parse(n.Substring(5), NumberStyles.HexNumber) : JenkHash.GenHash(n);
                if (h == hash) { arch = it; break; }
            }
            if (arch == null) throw new Exception(ytyp + " does not define " + mloName + ".");
            XmlNode entsEl = arch.SelectSingleNode("entities"), roomsEl = arch.SelectSingleNode("rooms"), portalsEl = arch.SelectSingleNode("portals"), setsEl = arch.SelectSingleNode("entitySets");

            // the objects as the game has them, in the order Interior listed them: the interior's own, then each entity set's
            List<XmlNode> orig = new List<XmlNode>(); List<XmlNode> setNodes = new List<XmlNode>();
            if (entsEl != null) foreach (XmlNode n in entsEl.SelectNodes("Item")) orig.Add(n);
            int baseCount = orig.Count;
            if (setsEl != null) foreach (XmlNode s in setsEl.SelectNodes("Item")) { setNodes.Add(s); XmlNode se = s.SelectSingleNode("entities"); if (se != null) foreach (XmlNode n in se.SelectNodes("Item")) orig.Add(n); }

            List<object> ents = J.A(J.G(game, "entities"));
            int nRooms = roomsEl == null ? 0 : roomsEl.SelectNodes("Item").Count;
            List<XmlNode> newBase = new List<XmlNode>(); List<int>[] roomObjs = new List<int>[nRooms]; for (int r = 0; r < nRooms; r++) roomObjs[r] = new List<int>();
            Dictionary<int, int> remap = new Dictionary<int, int>();            // a base object's old index -> its new one
            List<XmlNode>[] setEnts = new List<XmlNode>[setNodes.Count]; List<int>[] setLocs = new List<int>[setNodes.Count]; for (int s = 0; s < setNodes.Count; s++) { setEnts[s] = new List<XmlNode>(); setLocs[s] = new List<int>(); }
            HashSet<int> used = new HashSet<int>(); int moved = 0, added = 0, deleted = 0; List<string> addedNames = new List<string>();
            foreach (object o in ents)
            {
                Dictionary<string, object> e = J.O(o); if (e == null) continue;
                int oi = J.I(J.G(e, "o"), -1), room = J.I(J.G(e, "room"), -1), set = J.I(J.G(e, "set"), -1);
                if (J.B(J.G(e, "del"), false)) { if (oi >= 0) deleted++; continue; }
                XmlNode node;
                if (oi >= 0 && oi < orig.Count)
                {
                    node = orig[oi].CloneNode(true);
                    if (!used.Add(oi)) { SetVal(node, "guid", "0"); added++; }   // (a copy of an object)
                    else if (J.B(J.G(e, "ch"), false)) moved++;
                }
                else { node = NewEntity(doc, J.S(J.G(e, "n"), "")); added++; addedNames.Add(J.S(J.G(e, "n"), "")); }
                List<object> p = J.A(J.G(e, "p")), q = J.A(J.G(e, "q")), sc = J.A(J.G(e, "s"));
                SetXyz(node, "position", J.D(p[0], 0), J.D(p[1], 0), J.D(p[2], 0));
                XmlElement rot = node.SelectSingleNode("rotation") as XmlElement;                // stored as the inverse of the real turn
                rot.SetAttribute("x", F(-J.D(q[0], 0))); rot.SetAttribute("y", F(-J.D(q[1], 0))); rot.SetAttribute("z", F(-J.D(q[2], 0))); rot.SetAttribute("w", F(J.D(q[3], 1)));
                if (sc.Count >= 2) { SetVal(node, "scaleXY", F(J.D(sc[0], 1))); SetVal(node, "scaleZ", F(J.D(sc[1], 1))); }
                if (set >= 0 && set < setNodes.Count) { setEnts[set].Add(node); setLocs[set].Add(Math.Max(0, room)); continue; }
                int ni = newBase.Count; newBase.Add(node); if (oi >= 0 && oi < baseCount && !remap.ContainsKey(oi)) remap[oi] = ni;
                if (room >= 0 && room < nRooms) roomObjs[room].Add(ni);
            }
            // write them in
            if (entsEl != null) { Clear(entsEl); foreach (XmlNode n in newBase) entsEl.AppendChild(n); }
            int ri = 0;
            if (roomsEl != null) foreach (XmlNode r in roomsEl.SelectNodes("Item")) { SetList(r, "attachedObjects", roomObjs[ri]); ri++; }
            if (portalsEl != null)
                foreach (XmlNode pt in portalsEl.SelectNodes("Item"))
                {
                    XmlNode ao = pt.SelectSingleNode("attachedObjects"); if (ao == null) continue; List<int> keep = new List<int>();
                    foreach (string t in ao.InnerText.Split(new char[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) { int v; int nv; if (int.TryParse(t, out v) && remap.TryGetValue(v, out nv)) keep.Add(nv); }
                    SetList(pt, "attachedObjects", keep);
                }
            for (int s = 0; s < setNodes.Count; s++)
            {
                XmlNode se = setNodes[s].SelectSingleNode("entities"); if (se == null) continue;
                Clear(se); foreach (XmlNode n in setEnts[s]) se.AppendChild(n);
                SetList(setNodes[s], "locations", setLocs[s]);
            }

            byte[] data = XmlMeta.GetRSCData(doc);
            if (data == null) throw new Exception("CodeWalker could not build the map types file.");
            // read it back: the interior and its objects have to be there
            YtypFile chk = new YtypFile(); RpfFile.LoadResourceFile(chk, data, 2); int got = -1;
            if (chk.AllArchetypes != null) foreach (Archetype a in chk.AllArchetypes) { MloArchetype m = a as MloArchetype; if (m != null && m.Hash.Hash == hash) got = m.entities != null ? m.entities.Length : 0; }
            if (got != newBase.Count) throw new Exception("The written map types file has " + got + " objects in the interior instead of " + newBase.Count + "; nothing was exported.");

            string res = Regex.Replace((resource ?? "").ToLowerInvariant(), "[^a-z0-9_\\-]", "_").Trim('_'); if (res.Length == 0) res = "mlo_" + Regex.Replace(mloName.ToLowerInvariant(), "[^a-z0-9_]", "_") + "_edit";
            string dir = Path.Combine(outRoot, res), stream = Path.Combine(dir, "stream");
            if (Directory.Exists(stream)) foreach (string old in Directory.GetFiles(stream)) File.Delete(old);           // (a file left from an earlier export would be streamed too)
            Directory.CreateDirectory(stream);
            File.WriteAllBytes(Path.Combine(stream, ytyp), data); files.Add("stream/" + ytyp);
            log.Add(ytyp + ": " + newBase.Count + " objects in the interior (was " + baseCount + "), " + moved + " moved or turned, " + deleted + " deleted, " + added + " added");

            // props of the game's own interiors among the new ones: their map types files go along (the game only loads them with that interior)
            Dictionary<string, List<string>> ityps = Vanilla.InteriorYtypsFor(addedNames);
            ityps.Remove(ytyp);
            foreach (string yx in ityps.Keys) { byte[] b = Vanilla.ResourceFile(yx); if (b == null) { warns.Add(yx + " could not be read from the game."); continue; } File.WriteAllBytes(Path.Combine(stream, yx), b); files.Add("stream/" + yx); }

            string twin = J.S(J.G(game, "twin"), "");
            StringBuilder fx = new StringBuilder("fx_version 'cerulean'\ngame 'gta5'\nlua54 'yes'\nthis_is_a_map 'yes'\n\nauthor 'MLO Creator'\ndescription '" + (mloName + " edited").Replace("'", "") + "'\nversion '1.0.0'\n\n");
            fx.Append("-- stream/" + ytyp + " replaces the game's file of the same name: the interior " + mloName + " with your changes\n");
            if (ityps.Count > 0) { fx.Append("-- the game's own map types files of props added from other interiors\n"); foreach (string yx in ityps.Keys) fx.Append("data_file 'DLC_ITYP_REQUEST' 'stream/" + yx + "'\n"); }
            File.WriteAllText(Path.Combine(dir, "fxmanifest.lua"), fx.ToString(), new UTF8Encoding(false)); files.Add("fxmanifest.lua");

            Dictionary<string, object> w = J.O(J.G(game, "world"));
            StringBuilder rd = new StringBuilder("# " + res + "\n\nThe game's interior **" + mloName + "**" + (J.S(J.G(game, "place"), "") != "" ? " (" + J.S(J.G(game, "place"), "") + ")" : "") + " with changes made in MLO Creator.\n\n");
            rd.Append("- Objects in the interior: " + newBase.Count + " (the game has " + baseCount + ")\n- Moved or turned: " + moved + "\n- Deleted: " + deleted + "\n- Added: " + added + (added > 0 ? " (" + string.Join(", ", addedNames.ToArray()) + ")" : "") + "\n\n");
            rd.Append("## Install\n\nCopy the folder `" + res + "` into your server's `resources` and add `ensure " + res + "` to `server.cfg`. Restart the server (a map file is not reloaded while the server runs).\n\n");
            rd.Append("## Files\n\n- `stream/" + ytyp + "` replaces the game's file of that name. Only one resource can replace it: another MLO edit or interior mod that ships the same file conflicts with this one.\n");
            foreach (string yx in ityps.Keys) rd.Append("- `stream/" + yx + "`: the game's own file, unchanged, for " + string.Join(", ", ityps[yx].ToArray()) + "\n");
            if (w != null) rd.Append("\n## Test\n\nThe interior stands at " + F(J.D(J.G(w, "x"), 0)) + ", " + F(J.D(J.G(w, "y"), 0)) + ", " + F(J.D(J.G(w, "z"), 0)) + ". Teleport there and look at the changes; the game has to be restarted (or the client reconnected) to read the file again.\n");
            if (twin.Length > 0) rd.Append("\n**Note:** the game places a second interior at the same spot (`" + twin + "`). Only one of the two shows in the game, usually the DLC one; if your changes do not show, open that one in MLO Creator and make them there.\n");
            File.WriteAllText(Path.Combine(dir, "README.md"), rd.ToString(), new UTF8Encoding(false)); files.Add("README.md");
            if (twin.Length > 0) warns.Add("The game places a second interior at the same spot (" + twin + "). Only one of the two shows in the game, usually the DLC one.");

            Dictionary<string, object> r0 = new Dictionary<string, object>();
            r0["ok"] = true; r0["dir"] = dir; r0["resource"] = res; r0["files"] = files; r0["log"] = log; r0["warnings"] = warns; r0["objects"] = newBase.Count; r0["moved"] = moved; r0["deleted"] = deleted; r0["added"] = added;
            return r0;
        }
        static string F(double v) { return v.ToString("0.######", IC); }
        static void Clear(XmlNode n) { while (n.HasChildNodes) n.RemoveChild(n.FirstChild); }          // (the element's own attributes stay)
        static void SetVal(XmlNode n, string tag, string v) { XmlElement e = n.SelectSingleNode(tag) as XmlElement; if (e != null) e.SetAttribute("value", v); }
        static void SetXyz(XmlNode n, string tag, double x, double y, double z) { XmlElement e = n.SelectSingleNode(tag) as XmlElement; if (e == null) return; e.SetAttribute("x", F(x)); e.SetAttribute("y", F(y)); e.SetAttribute("z", F(z)); }
        static void SetList(XmlNode parent, string tag, List<int> v)
        {
            XmlNode e = parent.SelectSingleNode(tag); if (e == null) { e = parent.OwnerDocument.CreateElement(tag); parent.AppendChild(e); }
            List<string> s = new List<string>(); foreach (int i in v) s.Add(i.ToString(IC)); e.InnerText = string.Join(" ", s.ToArray());
        }
        // a new object of the interior (a prop of the game): the same fields the game's own have; its distance is the model's own (-1)
        static XmlNode NewEntity(XmlDocument doc, string arch)
        {
            XmlDocumentFragment f = doc.CreateDocumentFragment();
            f.InnerXml = "<Item type=\"CEntityDef\"><archetypeName>" + SecurityElement.Escape(arch) + "</archetypeName><flags value=\"1572864\" /><guid value=\"0\" /><position x=\"0\" y=\"0\" z=\"0\" /><rotation x=\"0\" y=\"0\" z=\"0\" w=\"1\" />" +
                "<scaleXY value=\"1\" /><scaleZ value=\"1\" /><parentIndex value=\"-1\" /><lodDist value=\"-1\" /><childLodDist value=\"0\" /><lodLevel>LODTYPES_DEPTH_ORPHANHD</lodLevel><numChildren value=\"0\" />" +
                "<priorityLevel>PRI_REQUIRED</priorityLevel><extensions /><ambientOcclusionMultiplier value=\"255\" /><artificialAmbientOcclusion value=\"255\" /><tintValue value=\"0\" /></Item>";
            return f.FirstChild;
        }

        static double[] V3(Vector3 v) { return new double[] { Math.Round(v.X, 4), Math.Round(v.Y, 4), Math.Round(v.Z, 4) }; }
        static Dictionary<string, object> EntJson(MCEntityDef me, int room, int set)
        {
            CEntityDef d = me._Data; Dictionary<string, object> e = new Dictionary<string, object>();
            string n = JenkIndex.TryGetString(d.archetypeName.Hash); if (string.IsNullOrEmpty(n)) n = "0x" + d.archetypeName.Hash.ToString("X8");
            e["n"] = n.ToLowerInvariant(); e["p"] = V3(d.position);
            e["q"] = new double[] { Math.Round(-d.rotation.X, 6), Math.Round(-d.rotation.Y, 6), Math.Round(-d.rotation.Z, 6), Math.Round(d.rotation.W, 6) };         // (the stored one is the inverse)
            e["s"] = new double[] { d.scaleXY, d.scaleZ }; e["room"] = room; e["set"] = set; e["flags"] = d.flags; e["lod"] = d.lodDist; e["level"] = d.lodLevel.ToString().Replace("LODTYPES_DEPTH_", "");
            return e;
        }

        static void AddPlace(Place p, List<object> res, HashSet<MloEntry> shown)
        {
            List<KeyValuePair<double, MloEntry>> own = new List<KeyValuePair<double, MloEntry>>(), other = new List<KeyValuePair<double, MloEntry>>();
            bool known = Array.IndexOf(Places, p) >= 0;          // (a typed point takes everything round it)
            foreach (MloEntry e in idx)
            {
                double d = Dist(e, p); if (d > p.R) continue;
                bool elsewhere = false; if (known) foreach (Place o in Places) if (o != p && Dist(e, o) < d && Dist(e, o) <= o.R) { elsewhere = true; break; }
                if (elsewhere) continue;
                (Tag(e).Length == 0 ? own : other).Add(new KeyValuePair<double, MloEntry>(d, e));
            }
            Comparison<KeyValuePair<double, MloEntry>> byDist = delegate (KeyValuePair<double, MloEntry> a, KeyValuePair<double, MloEntry> b) { return a.Key.CompareTo(b.Key); };
            own.Sort(byDist); other.Sort(byDist);
            if (own.Count == 0)
            {
                Dictionary<string, object> r = new Dictionary<string, object>();
                r["kind"] = "exterior"; r["place"] = p.Label; r["x"] = p.X; r["y"] = p.Y;
                try { r["models"] = OutsideModels(p); } catch (Exception) { r["models"] = new List<object>(); }
                res.Add(r);
            }
            int n = 0;
            foreach (KeyValuePair<double, MloEntry> kv in own) { if (n >= 12) break; if (shown.Add(kv.Value)) { res.Add(MloJson(kv.Value, p.Label, kv.Key)); n++; } }
            n = 0;
            foreach (KeyValuePair<double, MloEntry> kv in other) { if (n >= 6) break; if (shown.Add(kv.Value)) { res.Add(MloJson(kv.Value, p.Label, kv.Key)); n++; } }
        }
        static double Dist(MloEntry e, Place p) { double dx = e.X - p.X, dy = e.Y - p.Y; return Math.Sqrt(dx * dx + dy * dy); }
    }
}
