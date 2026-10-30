// The bridge to the live editor in FiveM (fivem\mlo_creator_live next to the program): the resource is copied into the output folder (the server's resources),
// a project goes into its data folder as a session (every object with its place in the world, and where the game has it), and the changes made in the game
// come back from there. C# 5.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using CodeWalker.GameFiles;
using SharpDX;

namespace MloCreator
{
    public static class LiveBridge
    {
        public const string Res = "mlo_creator_live";
        static JavaScriptSerializer Json() { JavaScriptSerializer j = new JavaScriptSerializer(); j.MaxJsonLength = int.MaxValue; return j; }
        public static string SessionName(string project) { string n = Regex.Replace((project ?? "").ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_'); return n.Length == 0 ? "project" : (n.Length > 40 ? n.Substring(0, 40) : n); }
        static string DataDir(string outRoot) { return Path.Combine(outRoot, Res, "data"); }

        // the resource into the output folder: its code is replaced, its data (the sessions) stays
        public static Dictionary<string, object> Install(string programRoot, string outRoot)
        {
            string src = Path.Combine(programRoot, "fivem", Res), dst = Path.Combine(outRoot, Res);
            if (!Directory.Exists(src)) throw new Exception("The live editor's files are missing next to the program (fivem\\" + Res + ").");
            int n = 0; Copy(src, dst, ref n);
            Directory.CreateDirectory(Path.Combine(dst, "data"));
            Dictionary<string, object> r = new Dictionary<string, object>(); r["ok"] = true; r["dir"] = dst; r["files"] = n; return r;
        }
        static void Copy(string from, string to, ref int n)
        {
            Directory.CreateDirectory(to);
            foreach (string f in Directory.GetFiles(from))
            {
                if (Path.GetFileName(Path.GetDirectoryName(f)).Equals("data", StringComparison.OrdinalIgnoreCase) && f.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
                File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true); n++;
            }
            foreach (string d in Directory.GetDirectories(from)) Copy(d, Path.Combine(to, Path.GetFileName(d)), ref n);
        }

        // A project as a session. Each object: id (the project's lid), n, x y z qx qy qz qw (where it is, in the world, its real turn), del, game (the game has it:
        // ox oy oz oqx oqy oqz oqw is where, so the live editor can hide it there). A session the game changed and MLO Creator has not taken is not overwritten unless force.
        public static Dictionary<string, object> Send(Dictionary<string, object> game, string project, string outRoot, bool force, string gta, string cacheDir)
        {
            Vanilla.Init(gta, cacheDir);
            string dir = DataDir(outRoot); if (!Directory.Exists(Path.Combine(outRoot, Res))) throw new Exception("The live editor is not in " + outRoot + " yet: press Install the live editor first.");
            Directory.CreateDirectory(dir);
            string name = SessionName(project), file = Path.Combine(dir, name + ".json");
            Dictionary<string, object> r = new Dictionary<string, object>(); r["name"] = name; r["file"] = file;
            if (!force && File.Exists(file))
            {
                Dictionary<string, object> old = Json().DeserializeObject(File.ReadAllText(file, Encoding.UTF8)) as Dictionary<string, object>;
                if (old != null && J.B(J.G(old, "dirty"), false)) { r["ok"] = false; r["dirty"] = true; r["error"] = "The game has changes in " + name + " that are not in the project yet: take them first (or send anyway to throw them away)."; return r; }
            }
            Dictionary<string, object> w = J.O(J.G(game, "world"));
            double wx = J.D(J.G(w, "x"), 0), wy = J.D(J.G(w, "y"), 0), wz = J.D(J.G(w, "z"), 0), h = J.D(J.G(w, "heading"), 0) * Math.PI / 180, c = Math.Cos(h), s = Math.Sin(h);
            Quaternion qh = Quaternion.RotationAxis(Vector3.UnitZ, (float)h);
            // where the game has its objects: the interior's own (in its frame) and the building's (in the world)
            List<object> orig = J.A(J.G(Buildings.Interior(J.S(J.G(game, "ytyp"), ""), (uint)J.D(J.G(game, "hash"), 0), gta, cacheDir), "entities"));
            Dictionary<string, YmapFile> ymaps = new Dictionary<string, YmapFile>();
            Func<double, double, double, double[]> toWorld = (x, y, z) => new double[] { Math.Round(wx + x * c - y * s, 4), Math.Round(wy + x * s + y * c, 4), Math.Round(wz + z, 4) };
            Func<List<object>, double[]> qWorld = (q) => { Quaternion v = qh * new Quaternion((float)J.D(q[0], 0), (float)J.D(q[1], 0), (float)J.D(q[2], 0), (float)J.D(q[3], 1)); v.Normalize(); return new double[] { Math.Round(v.X, 6), Math.Round(v.Y, 6), Math.Round(v.Z, 6), Math.Round(v.W, 6) }; };
            List<object> ents = new List<object>(); int skipped = 0;
            foreach (object o in J.A(J.G(game, "entities")))
            {
                Dictionary<string, object> e = J.O(o); if (e == null) continue; string id = J.S(J.G(e, "lid"), ""); if (id.Length == 0) { skipped++; continue; }
                List<object> p = J.A(J.G(e, "p")), q = J.A(J.G(e, "q")); if (p.Count < 3 || q.Count < 4) continue;
                bool dup = J.B(J.G(e, "dup"), false); int oi = J.I(J.G(e, "o"), -1); List<object> ext = J.A(J.G(e, "ext"));
                double[] pw = toWorld(J.D(p[0], 0), J.D(p[1], 0), J.D(p[2], 0)), qw = qWorld(q);
                Dictionary<string, object> le = new Dictionary<string, object>();
                le["id"] = id; le["n"] = J.S(J.G(e, "n"), "").ToLowerInvariant(); le["x"] = pw[0]; le["y"] = pw[1]; le["z"] = pw[2]; le["qx"] = qw[0]; le["qy"] = qw[1]; le["qz"] = qw[2]; le["qw"] = qw[3];
                le["del"] = J.B(J.G(e, "del"), false); le["game"] = false;
                if (!dup && ext.Count > 0)
                {
                    Dictionary<string, object> f = J.O(ext[0]); string y = J.S(J.G(f, "y"), ""); int i = J.I(J.G(f, "i"), -1); YmapFile ym;
                    if (!ymaps.TryGetValue(y, out ym)) { RpfFileEntry fe; byte[] yd = Vanilla.Bytes(y, out fe); ym = null; if (yd != null) { ym = new YmapFile(); ym.Load(yd, fe); } ymaps[y] = ym; }
                    if (ym != null && ym.CEntityDefs != null && i >= 0 && i < ym.CEntityDefs.Length)
                    {
                        CEntityDef d = ym.CEntityDefs[i]; le["game"] = true; le["ox"] = d.position.X; le["oy"] = d.position.Y; le["oz"] = d.position.Z;
                        le["oqx"] = -d.rotation.X; le["oqy"] = -d.rotation.Y; le["oqz"] = -d.rotation.Z; le["oqw"] = d.rotation.W;            // (stored as the inverse)
                    }
                }
                else if (!dup && ext.Count == 0 && oi >= 0 && oi < orig.Count)
                {
                    Dictionary<string, object> g0 = J.O(orig[oi]); double[] op = (double[])g0["p"], oq = (double[])g0["q"];
                    double[] ow2 = toWorld(op[0], op[1], op[2]), oqw = qWorld(new List<object> { oq[0], oq[1], oq[2], oq[3] });
                    le["game"] = true; le["ox"] = ow2[0]; le["oy"] = ow2[1]; le["oz"] = ow2[2]; le["oqx"] = oqw[0]; le["oqy"] = oqw[1]; le["oqz"] = oqw[2]; le["oqw"] = oqw[3];
                }
                ents.Add(le);
            }
            Dictionary<string, object> sess = new Dictionary<string, object>();
            sess["name"] = name; sess["project"] = project; sess["interior"] = J.S(J.G(game, "name"), ""); sess["world"] = new double[] { wx, wy, wz, J.D(J.G(w, "heading"), 0) };
            sess["sent"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"); sess["dirty"] = false; sess["entities"] = ents;
            File.WriteAllText(file, Json().Serialize(sess), new UTF8Encoding(false));
            r["ok"] = true; r["objects"] = ents.Count; r["skipped"] = skipped; return r;
        }

        // the session as the game left it: its objects (id, n, place in the world, del, game); it is marked as taken
        public static Dictionary<string, object> Take(string project, string outRoot)
        {
            string name = SessionName(project), file = Path.Combine(DataDir(outRoot), name + ".json");
            if (!File.Exists(file)) throw new Exception("There is no live project " + name + " in " + DataDir(outRoot) + ": send it first.");
            Dictionary<string, object> sess = Json().DeserializeObject(File.ReadAllText(file, Encoding.UTF8)) as Dictionary<string, object>;
            if (sess == null) throw new Exception(file + " cannot be read.");
            Dictionary<string, object> r = new Dictionary<string, object>(); r["ok"] = true; r["name"] = name; r["entities"] = J.G(sess, "entities"); r["dirty"] = J.B(J.G(sess, "dirty"), false);
            sess["dirty"] = false; File.WriteAllText(file, Json().Serialize(sess), new UTF8Encoding(false));
            return r;
        }
    }
}
