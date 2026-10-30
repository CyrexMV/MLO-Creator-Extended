// Changing the game's own models in a game interior project (Buildings of the game): openings cut through a model (a door, a window, a passage), its faces
// or whole parts deleted, the texture of a part changed. A changed model is exported under a name of its own (the resource's prefix and its name), defined
// in a map types file of the resource and put in place of the original only for the objects of the project: the same model elsewhere in the world stays as
// it was. The openings also cut the collision: the interior's own files (in its frame) and the map's files round it (in the world). C# 5.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using CodeWalker.GameFiles;

namespace MloCreator
{
    public static partial class Buildings
    {
        // a game model as an editable document, read once (for the preview; the export reads it afresh, since saving changes the document)
        static Dictionary<string, ModelDoc> gameDocs = new Dictionary<string, ModelDoc>();
        static ModelDoc GameDoc(string name, string cacheDir, bool fresh)
        {
            string n = name.ToLowerInvariant(); ModelDoc md;
            if (!fresh) lock (gameDocs) if (gameDocs.TryGetValue(n, out md)) return md;
            byte[] ydr = Vanilla.ResourceFile(n + ".ydr");
            if (ydr == null) throw new Exception(Vanilla.ResourceFile(n + ".yft") != null ? n + " is a model that breaks or moves (.yft): it cannot be changed here, only moved, turned or deleted." : "The game has no model " + n + ".ydr.");
            string dir = Path.Combine(cacheDir, "gamedocs", n + (fresh ? "_x" : "")); if (Directory.Exists(dir)) { try { Directory.Delete(dir, true); } catch { } }
            md = ModelDoc.Load(ydr, dir, n);
            if (!fresh) lock (gameDocs) gameDocs[n] = md;
            return md;
        }
        static List<CutBox> CutsOf(Dictionary<string, object> mod) { return ImportHost.BoxesFrom(J.G(mod, "cuts"), null); }
        // the faces to take out: the ones deleted one by one and every face of the parts removed (geometry numbers)
        static HashSet<long> DeletesOf(Dictionary<string, object> mod, ModelDoc md)
        {
            HashSet<long> del = ImportHost.DeletesFrom(J.G(mod, "deletes"));
            foreach (object po in J.A(J.G(mod, "parts"))) { int g = J.I(po, -1); if (g < 0 || g >= md.Geoms.Count) continue; int nt = md.Geoms[g].Idx.Length / 3; for (int t = 0; t < nt; t++) del.Add(ModelCut.Key(g, t)); }
            return del;
        }

        // the preview: the model's geometries after the cuts and deletes (only the ones that changed), like the import's cut preview
        public static Dictionary<string, object> ModelEdit(string name, Dictionary<string, object> mod, string gta, string cacheDir)
        {
            Vanilla.Init(gta, cacheDir);
            ModelDoc md = GameDoc(name, cacheDir, false); List<GeomOut> outs;
            lock (md) outs = ModelCut.Apply(md, CutsOf(mod), DeletesOf(mod, md));
            List<object> geoms = new List<object>();
            for (int i = 0; i < outs.Count; i++)
            {
                GeomOut o = outs[i]; if (o.Src.Lod != 0 || !o.Changed) continue;
                Dictionary<string, object> gj = ImportHost.GeomJsonOf(o.Src, i, o.V, o.Idx); if (o.TriSrc != null) gj["src"] = ImportHost.B64Of(o.TriSrc); geoms.Add(gj);
            }
            Dictionary<string, object> r = new Dictionary<string, object>(); r["ok"] = true; r["geoms"] = geoms; return r;
        }

        public class ModelOut { public string Old, New, Arch; public int Cuts, Deleted, Retextured, Lights; }

        // Writes every changed model into stream/ under its new name and returns them (old name, new name, the archetype's entry for the map types file).
        // mods: { model: { cuts, deletes, parts, tex: { shader index: { id, tile } } } }; textures: [{ name, size, rgba }] of the program's textures used.
        public static List<ModelOut> ExportModels(Dictionary<string, object> mods, List<object> textures, string P, string stream, string cacheDir, List<string> log, List<string> warns)
        {
            List<ModelOut> res = new List<ModelOut>(); if (mods == null) return res;
            Dictionary<string, Dictionary<string, object>> texByName = new Dictionary<string, Dictionary<string, object>>();
            foreach (object to in textures) { Dictionary<string, object> t = J.O(to); if (t != null) texByName[J.S(J.G(t, "name"), "")] = t; }
            foreach (KeyValuePair<string, object> kv in mods)
            {
                Dictionary<string, object> mod = J.O(kv.Value); if (mod == null) continue;
                string name = kv.Key.ToLowerInvariant(); Dictionary<string, object> tex = J.O(J.G(mod, "tex"));
                Dictionary<string, object> lights = J.O(J.G(mod, "lights"));
                if (J.A(J.G(mod, "cuts")).Count == 0 && J.A(J.G(mod, "deletes")).Count == 0 && J.A(J.G(mod, "parts")).Count == 0 && (tex == null || tex.Count == 0) && (lights == null || lights.Count == 0)) continue;
                try
                {
                    ModelDoc md = GameDoc(name, cacheDir, true);
                    HashSet<long> del = DeletesOf(mod, md); List<CutBox> cuts = CutsOf(mod);
                    List<GeomOut> outs = ModelCut.Apply(md, cuts, del);
                    ModelOut mo = new ModelOut(); mo.Old = name; mo.New = (P + name).Length <= 60 ? P + name : P + name.Substring(0, 60 - P.Length); mo.Cuts = cuts.Count; mo.Deleted = del.Count;
                    // a part with a texture of the program: its shader takes the texture (put into the model), and its faces get texture coordinates in metres
                    if (tex != null && tex.Count > 0)
                    {
                        XmlNodeList shaders = md.Doc.SelectNodes("/Drawable/ShaderGroup/Shaders/Item"); XmlNode sg = md.Doc.SelectSingleNode("/Drawable/ShaderGroup");
                        XmlNode td = sg.SelectSingleNode("TextureDictionary"); if (td == null) { td = md.Doc.CreateElement("TextureDictionary"); sg.InsertBefore(td, sg.FirstChild); }
                        HashSet<string> added = new HashSet<string>();
                        foreach (KeyValuePair<string, object> tk in tex)
                        {
                            int si; if (!int.TryParse(tk.Key, out si) || si < 0 || si >= shaders.Count) continue;
                            Dictionary<string, object> to = J.O(tk.Value); string id = J.S(J.G(to, "id"), ""); double tile = Math.Max(0.1, J.D(J.G(to, "tile"), 1)); Dictionary<string, object> t;
                            if (!texByName.TryGetValue(id, out t)) { warns.Add(name + ": the texture " + id + " was not sent."); continue; }
                            string tn = P + Regex.Replace(id.ToLowerInvariant(), "[^a-z0-9_]", "_");
                            XmlNode df = shaders[si].SelectSingleNode("Parameters/Item[@name='DiffuseSampler']/Name"); if (df == null) { warns.Add(name + ": part " + si + " has no picture to change."); continue; }
                            df.InnerText = tn;
                            if (added.Add(tn))
                            {
                                int size = J.I(J.G(t, "size"), 256); byte[] rgba = Convert.FromBase64String(J.S(J.G(t, "rgba"), "")); int[] argb = new int[size * size];
                                for (int i = 0; i < argb.Length; i++) argb[i] = (255 << 24) | (rgba[i * 4] << 16) | (rgba[i * 4 + 1] << 8) | rgba[i * 4 + 2];
                                Dds.Write(Path.Combine(md.TexDir, tn + ".dds"), argb, size, false);
                                XmlDocumentFragment f = md.Doc.CreateDocumentFragment();
                                f.InnerXml = "<Item><Name>" + tn + "</Name><Unk32 value=\"128\" /><Usage>DIFFUSE</Usage><UsageFlags>UNK24</UsageFlags><ExtraFlags value=\"0\" /><Width value=\"" + size + "\" /><Height value=\"" + size + "\" /><MipLevels value=\"" + Dds.MipCount(size) + "\" /><Format>D3DFMT_DXT1</Format><FileName>" + tn + ".dds</FileName></Item>";
                                td.AppendChild(f.FirstChild);
                            }
                            foreach (GeomOut o in outs) if (o.Src.Shader == si) { Retexture(o, tile); mo.Retextured++; }
                        }
                    }
                    // its lights changed: { light number: { on, c: [r, g, b], k brightness, f range, e softness } } (one switched off shines with 0)
                    if (lights != null && lights.Count > 0)
                    {
                        XmlNodeList li = md.Doc.SelectNodes("/Drawable/Lights/Item");
                        foreach (KeyValuePair<string, object> lk in lights)
                        {
                            int idx; Dictionary<string, object> L = J.O(lk.Value); if (!int.TryParse(lk.Key, out idx) || idx < 0 || idx >= li.Count || L == null) continue;
                            XmlNode it = li[idx]; List<object> c = J.A(J.G(L, "c"));
                            XmlElement ce = it.SelectSingleNode("Colour") as XmlElement; if (ce != null && c.Count >= 3) { ce.SetAttribute("r", J.I(c[0], 255).ToString(IC)); ce.SetAttribute("g", J.I(c[1], 255).ToString(IC)); ce.SetAttribute("b", J.I(c[2], 255).ToString(IC)); }
                            if (J.G(L, "k") != null || !J.B(J.G(L, "on"), true)) SetVal(it, "Intensity", F(J.B(J.G(L, "on"), true) ? Math.Max(0, J.D(J.G(L, "k"), 1)) : 0));
                            if (J.G(L, "f") != null) SetVal(it, "Falloff", F(Math.Max(0.05, J.D(J.G(L, "f"), 1))));
                            if (J.G(L, "e") != null) SetVal(it, "FalloffExponent", F(Math.Max(0.1, J.D(J.G(L, "e"), 8))));
                            mo.Lights++;
                        }
                    }
                    byte[] ydr = md.Save(outs);
                    File.WriteAllBytes(Path.Combine(stream, mo.New + ".ydr"), ydr);
                    mo.Arch = ArchetypeOf(name, mo.New);
                    if (mo.Arch == null) { warns.Add(name + ": its archetype was not found in the game's map types files, so it was left as it was."); File.Delete(Path.Combine(stream, mo.New + ".ydr")); continue; }
                    res.Add(mo); log.Add(mo.New + ".ydr: " + name + " with " + mo.Cuts + " opening(s), " + mo.Deleted + " face(s) taken out" + (mo.Retextured > 0 ? ", " + mo.Retextured + " part(s) with a new texture" : "") + (mo.Lights > 0 ? ", " + mo.Lights + " light(s) changed" : ""));
                }
                catch (Exception ex) { warns.Add(name + ": " + ex.Message); }
            }
            return res;
        }
        // A copy of a game model at another size (a door of the game fitted to an opening): its faces, its built-in collision and its box scaled by sx, sy, sz
        // about its origin, written as stream/<newName>.ydr; its archetype is the original's (its flags: a door stays a door), renamed and scaled.
        public static ModelOut ScaledModel(string name, double sx, double sy, double sz, string newName, string stream, string cacheDir, List<string> log, List<string> warns)
        {
            try
            {
                ModelDoc md = GameDoc(name, cacheDir, true);
                List<GeomOut> outs = ModelCut.Apply(md, new List<CutBox>(), new HashSet<long>());
                foreach (GeomOut o in outs)
                {
                    GeomData g = o.Src; if (o.V == null || !o.Changed) { o.V = (float[])g.V.Clone(); o.Idx = (int[])g.Idx.Clone(); o.Changed = true; }
                    int S = g.Stride;
                    for (int i = 0; i + S <= o.V.Length; i += S)
                    {
                        o.V[i + g.PosOff] *= (float)sx; o.V[i + g.PosOff + 1] *= (float)sy; o.V[i + g.PosOff + 2] *= (float)sz;
                        if (g.NrmOff >= 0) { double nx = o.V[i + g.NrmOff] / sx, ny = o.V[i + g.NrmOff + 1] / sy, nz = o.V[i + g.NrmOff + 2] / sz, l = Math.Sqrt(nx * nx + ny * ny + nz * nz); if (l > 1e-9) { o.V[i + g.NrmOff] = (float)(nx / l); o.V[i + g.NrmOff + 1] = (float)(ny / l); o.V[i + g.NrmOff + 2] = (float)(nz / l); } }
                    }
                }
                ScaleXml(md.Doc.DocumentElement, sx, sy, sz);
                File.WriteAllBytes(Path.Combine(stream, newName + ".ydr"), md.Save(outs));
                ModelOut mo = new ModelOut(); mo.Old = name; mo.New = newName; mo.Arch = ArchetypeOf(name, newName);
                if (mo.Arch == null) { warns.Add(name + ": its archetype was not found in the game's map types files, so it keeps its own size."); File.Delete(Path.Combine(stream, newName + ".ydr")); return null; }
                XmlDocument ad = new XmlDocument(); ad.LoadXml(mo.Arch); ScaleXml(ad.DocumentElement, sx, sy, sz);
                XmlNode pd = ad.DocumentElement.SelectSingleNode("physicsDictionary"); if (pd != null) pd.InnerText = newName;          // (its collision is the copy's own)
                mo.Arch = ad.DocumentElement.OuterXml;
                log.Add(newName + ".ydr: " + name + " at " + Math.Round(sx * 100) + " x " + Math.Round(sy * 100) + " x " + Math.Round(sz * 100) + " % (width, depth, height), its collision too");
                return mo;
            }
            catch (Exception ex) { warns.Add(name + " could not be resized (" + ex.Message + "); it keeps its own size."); return null; }
        }
        // every place and size in a model's or archetype's XML scaled: boxes, centres, spheres, the collision's vertices and transforms, the lights' places
        static readonly HashSet<string> ScaledVec = new HashSet<string> { "BoundingBoxMin", "BoundingBoxMax", "BoundingSphereCenter", "BoxMin", "BoxMax", "BoxCenter", "SphereCenter", "GeometryCenter", "bbMin", "bbMax", "bsCentre" };
        static void ScaleXml(XmlNode n, double sx, double sy, double sz)
        {
            double sm = Math.Max(sx, Math.Max(sy, sz));
            foreach (XmlNode c in n.SelectNodes("descendant-or-self::*"))
            {
                XmlElement e = c as XmlElement; if (e == null) continue;
                bool vec = ScaledVec.Contains(e.Name) || (e.Name == "Position" && e.ParentNode != null && e.ParentNode.ParentNode != null && e.ParentNode.ParentNode.Name == "Lights");
                if (vec && e.HasAttribute("x")) { Func<string, double, string> m = (a, f) => { double v; return double.TryParse(e.GetAttribute(a), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? F(v * f) : e.GetAttribute(a); }; e.SetAttribute("x", m("x", sx)); e.SetAttribute("y", m("y", sy)); e.SetAttribute("z", m("z", sz)); continue; }
                if ((e.Name == "BoundingSphereRadius" || e.Name == "SphereRadius" || e.Name == "bsRadius") && e.HasAttribute("value")) { double v; if (double.TryParse(e.GetAttribute("value"), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) e.SetAttribute("value", F(v * sm)); continue; }
                if (e.Name == "Vertices" && !e.HasChildNodes == false && e.ChildNodes.Count == 1 && e.FirstChild.NodeType == XmlNodeType.Text)
                {
                    // "x, y, z" per line
                    StringBuilder sb = new StringBuilder();
                    foreach (string line in e.InnerText.Split('\n'))
                    {
                        string[] p = line.Split(','); double a, b, d;
                        if (p.Length == 3 && double.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out a) && double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out b) && double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                            sb.Append(F(a * sx)).Append(", ").Append(F(b * sy)).Append(", ").Append(F(d * sz)).Append('\n');
                        else sb.Append(line).Append('\n');
                    }
                    e.InnerText = sb.ToString(); continue;
                }
                if (e.Name == "CompositeTransform" && e.ChildNodes.Count == 1 && e.FirstChild.NodeType == XmlNodeType.Text)
                {
                    // a 4 x 4 matrix, the last row its place
                    List<string> rows = new List<string>(); foreach (string line in e.InnerText.Split('\n')) if (line.Trim().Length > 0) rows.Add(line.Trim());
                    if (rows.Count == 4) { string[] t = rows[3].Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries); double a, b, d; if (t.Length >= 3 && double.TryParse(t[0], NumberStyles.Float, CultureInfo.InvariantCulture, out a) && double.TryParse(t[1], NumberStyles.Float, CultureInfo.InvariantCulture, out b) && double.TryParse(t[2], NumberStyles.Float, CultureInfo.InvariantCulture, out d)) { rows[3] = F(a * sx) + " " + F(b * sy) + " " + F(d * sz) + (t.Length > 3 ? " " + t[3] : ""); e.InnerText = "\n" + string.Join("\n", rows.ToArray()) + "\n"; } }
                }
            }
        }
        // texture coordinates from the faces' place: across the face's main direction, in metres over the texture's size (like the program's own walls)
        static void Retexture(GeomOut o, double tile)
        {
            GeomData g = o.Src; if (g.UvOff < 0 || g.PosOff < 0) return;
            if (o.V == null || !o.Changed) { o.V = (float[])g.V.Clone(); o.Idx = (int[])g.Idx.Clone(); o.Changed = true; }
            int S = g.Stride, nv = o.V.Length / S;
            for (int v = 0; v < nv; v++)
            {
                double x = o.V[v * S + g.PosOff], y = o.V[v * S + g.PosOff + 1], z = o.V[v * S + g.PosOff + 2], nx = 0, ny = 0, nz = 1;
                if (g.NrmOff >= 0) { nx = Math.Abs(o.V[v * S + g.NrmOff]); ny = Math.Abs(o.V[v * S + g.NrmOff + 1]); nz = Math.Abs(o.V[v * S + g.NrmOff + 2]); }
                double u, w; if (nx >= ny && nx >= nz) { u = y; w = -z; } else if (ny >= nx && ny >= nz) { u = x; w = -z; } else { u = x; w = y; }
                o.V[v * S + g.UvOff] = (float)(u / tile); o.V[v * S + g.UvOff + 1] = (float)(w / tile);
            }
        }
        // the original's entry in its map types file, renamed (a drawable of its own: not in a dictionary)
        static string ArchetypeOf(string name, string newName)
        {
            string yt = Vanilla.YtypOf(JenkHash.GenHash(name)); if (yt == null) return null;
            RpfFileEntry fe; byte[] yd = Vanilla.Bytes(yt, out fe); if (yd == null) return null;
            YtypFile y = new YtypFile(); y.Load(yd, fe); string fn; XmlDocument doc = new XmlDocument(); doc.LoadXml(MetaXml.GetXml(y, out fn));
            foreach (XmlNode it in doc.SelectNodes("/CMapTypes/archetypes/Item"))
            {
                XmlNode nn = it.SelectSingleNode("name"); if (nn == null || nn.InnerText.Trim().ToLowerInvariant() != name) continue;
                nn.InnerText = newName; XmlNode an = it.SelectSingleNode("assetName"); if (an != null) an.InnerText = newName;
                XmlNode at = it.SelectSingleNode("assetType"); if (at != null) at.InnerText = "ASSET_TYPE_DRAWABLE";
                XmlNode dd = it.SelectSingleNode("drawableDictionary"); if (dd != null) dd.InnerText = "";
                return it.OuterXml;
            }
            return null;
        }
        // the map types file of the changed models
        public static byte[] ModelsYtyp(string P, List<ModelOut> models)
        {
            StringBuilder s = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<CMapTypes>\n <extensions />\n <archetypes>\n");
            foreach (ModelOut m in models) s.Append(m.Arch).Append('\n');
            // (an opened resource that has this file from an earlier session: its other models stay in it)
            HashSet<string> mine = new HashSet<string>(); foreach (ModelOut m in models) mine.Add(m.New.ToLowerInvariant());
            if (Vanilla.IsLoose(P + "models.ytyp"))
                try
                {
                    RpfFileEntry fe; byte[] od0 = Vanilla.Bytes(P + "models.ytyp", out fe); YtypFile y = new YtypFile(); y.Load(od0, fe); string fn; XmlDocument od = new XmlDocument(); od.LoadXml(MetaXml.GetXml(y, out fn));
                    foreach (XmlNode it in od.SelectNodes("/CMapTypes/archetypes/Item")) { XmlNode nn = it.SelectSingleNode("name"); if (nn != null && !mine.Contains(nn.InnerText.Trim().ToLowerInvariant())) s.Append(it.OuterXml).Append('\n'); }
                }
                catch (Exception) { }
            s.Append(" </archetypes>\n <name>" + P + "models</name>\n <dependencies />\n <compositeEntityTypes itemType=\"CCompositeEntityType\" />\n</CMapTypes>\n");
            XmlDocument d = new XmlDocument(); d.LoadXml(s.ToString()); return XmlMeta.GetRSCData(d);
        }

        // the openings cut through changed models, where they are in the interior's frame: each one for every object of the project that shows the model
        public static List<CutBox> OpeningBoxes(List<object> ents, Dictionary<string, object> mods)
        {
            List<CutBox> res = new List<CutBox>(); if (mods == null) return res;
            foreach (object o in ents)
            {
                Dictionary<string, object> e = J.O(o); if (e == null || J.B(J.G(e, "del"), false)) continue;
                Dictionary<string, object> mod = J.O(J.G(mods, J.S(J.G(e, "n"), ""))); if (mod == null) continue;
                List<object> p = J.A(J.G(e, "p")), q = J.A(J.G(e, "q")), sc = J.A(J.G(e, "s"));
                SharpDX.Quaternion qq = new SharpDX.Quaternion((float)J.D(q[0], 0), (float)J.D(q[1], 0), (float)J.D(q[2], 0), (float)J.D(q[3], 1));
                double sxy = sc.Count > 0 ? J.D(sc[0], 1) : 1, sz = sc.Count > 1 ? J.D(sc[1], 1) : 1;
                SharpDX.Vector3 xa = SharpDX.Vector3.Transform(SharpDX.Vector3.UnitX, qq); double hd = Math.Atan2(xa.Y, xa.X);
                foreach (CutBox b in CutsOf(mod))
                {
                    SharpDX.Vector3 c = SharpDX.Vector3.Transform(new SharpDX.Vector3((float)(b.Cx * sxy), (float)(b.Cy * sxy), (float)(b.Cz * sz)), qq);
                    CutBox w = new CutBox(); w.Id = b.Id; w.Type = b.Type; w.Cx = J.D(p[0], 0) + c.X; w.Cy = J.D(p[1], 0) + c.Y; w.Cz = J.D(p[2], 0) + c.Z; w.Yaw = b.Yaw + hd; w.Hu = b.Hu * sxy; w.Hn = b.Hn * sxy; w.Hz = b.Hz * sz; w.Prepare(); res.Add(w);
                }
            }
            return res;
        }
        // Where an object stood whose collision is not its own (it is in the collision files of the interior or the map): its model's box there, 2 cm smaller on
        // the sides and 3 cm higher at the bottom so the floor and the walls it stands at stay. null: the model has its own collision (it goes with the object),
        // it is not known, or it is bigger than 15 m (a piece of the building: cutting that much would leave holes). pos, the real turn and size: where it stood.
        public static CutBox RemovalBox(string name, double px, double py, double pz, double qx, double qy, double qz, double qw, double sxy, double sz)
        {
            if (string.IsNullOrEmpty(name) || name.StartsWith("hash_") || Vanilla.HasOwnCollision(name)) return null;
            long size; SharpDX.Vector3[] bb = Vanilla.ModelBox(name, out size); if (bb == null) return null;
            double hx = (bb[1].X - bb[0].X) / 2 * sxy, hy = (bb[1].Y - bb[0].Y) / 2 * sxy, hz = (bb[1].Z - bb[0].Z) / 2 * sz;
            if (hx > 7.5 || hy > 7.5 || hz > 7.5) return null;
            SharpDX.Quaternion q = new SharpDX.Quaternion((float)qx, (float)qy, (float)qz, (float)qw); q.Normalize();
            SharpDX.Vector3 c = SharpDX.Vector3.Transform(new SharpDX.Vector3((float)((bb[0].X + bb[1].X) / 2 * sxy), (float)((bb[0].Y + bb[1].Y) / 2 * sxy), (float)((bb[0].Z + bb[1].Z) / 2 * sz)), q), xa = SharpDX.Vector3.Transform(SharpDX.Vector3.UnitX, q);
            CutBox b = new CutBox(); b.Id = "removed:" + name; b.Type = "removed";
            b.Cx = px + c.X; b.Cy = py + c.Y; b.Cz = pz + c.Z + 0.015; b.Yaw = Math.Atan2(xa.Y, xa.X); b.Hu = Math.Max(0.01, hx - 0.02); b.Hn = Math.Max(0.01, hy - 0.02); b.Hz = Math.Max(0.01, hz - 0.015);
            b.Prepare(); return b;
        }
        // the place of an entry of a map or interior file: position, real turn (stored as the inverse), size
        public static double[] PlaceOf(XmlNode n)
        {
            XmlElement p = n.SelectSingleNode("position") as XmlElement, r = n.SelectSingleNode("rotation") as XmlElement, s1 = n.SelectSingleNode("scaleXY") as XmlElement, s2 = n.SelectSingleNode("scaleZ") as XmlElement;
            Func<XmlElement, string, double> at = (e, a) => { double v; return e != null && double.TryParse(e.GetAttribute(a), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0; };
            return new double[] { at(p, "x"), at(p, "y"), at(p, "z"), -at(r, "x"), -at(r, "y"), -at(r, "z"), r == null ? 1 : at(r, "w"), s1 == null ? 1 : at(s1, "value"), s2 == null ? 1 : at(s2, "value") };
        }
        public static CutBox RemovalBoxAt(XmlNode n)
        {
            XmlNode an = n.SelectSingleNode("archetypeName"); double[] v = PlaceOf(n);
            return an == null ? null : RemovalBox(an.InnerText.Trim().ToLowerInvariant(), v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7] == 0 ? 1 : v[7], v[8] == 0 ? 1 : v[8]);
        }

        // the area a map file belongs to (dt1_19 of dt1_19_critical_0 and of its DLC copy hei_dt1_19_critical_0): its collision files start the same way
        public static string MapPrefix(string ymap)
        {
            string n = (ymap ?? "").ToLowerInvariant(); if (n.StartsWith("hei_")) n = n.Substring(4); string[] parts = n.Split('_');
            return parts.Length >= 2 ? parts[0] + "_" + parts[1] : "";
        }

        // The openings' collision: the interior's own files (its name, hi@ its name: in the interior's frame) with the boxes in that frame, and the map's files
        // round it whose box the openings reach (in the world) with the boxes moved there. Each changed file goes into stream/ under its own name.
        // intOnly: boxes for the interior's files only (where its removed objects stood, in its frame); mapWorld: boxes for the map's files only (where the
        // building's removed objects stood, in the world)
        public static List<string> CutCollision(List<CutBox> boxes, List<CutBox> intOnly, List<CutBox> mapWorld, Dictionary<string, object> world, string mlo, string twin, IEnumerable<string> mapPrefixes, string stream, List<string> log, List<string> warns)
        {
            List<string> files = new List<string>(); if (boxes.Count == 0 && intOnly.Count == 0 && mapWorld.Count == 0) return files;
            double wx = J.D(J.G(world, "x"), 0), wy = J.D(J.G(world, "y"), 0), wz = J.D(J.G(world, "z"), 0), h = J.D(J.G(world, "heading"), 0) * Math.PI / 180, c = Math.Cos(h), s = Math.Sin(h);
            List<CutBox> wb = new List<CutBox>(); double x0 = 1e30, y0 = 1e30, x1 = -1e30, y1 = -1e30;
            foreach (CutBox b in boxes)
            {
                CutBox w = new CutBox(); w.Id = b.Id; w.Type = b.Type; w.Cx = wx + b.Cx * c - b.Cy * s; w.Cy = wy + b.Cx * s + b.Cy * c; w.Cz = wz + b.Cz; w.Yaw = b.Yaw + h; w.Hu = b.Hu; w.Hn = b.Hn; w.Hz = b.Hz; w.Prepare(); wb.Add(w);
                x0 = Math.Min(x0, w.Mn[0]); y0 = Math.Min(y0, w.Mn[1]); x1 = Math.Max(x1, w.Mx[0]); y1 = Math.Max(y1, w.Mx[1]);
            }
            foreach (CutBox w in mapWorld) { w.Prepare(); wb.Add(w); x0 = Math.Min(x0, w.Mn[0]); y0 = Math.Min(y0, w.Mn[1]); x1 = Math.Max(x1, w.Mx[0]); y1 = Math.Max(y1, w.Mx[1]); }
            List<CutBox> ib = new List<CutBox>(boxes); ib.AddRange(intOnly);
            List<KeyValuePair<string, List<CutBox>>> todo = new List<KeyValuePair<string, List<CutBox>>>();
            foreach (string m in new string[] { mlo, twin }) if (!string.IsNullOrEmpty(m)) foreach (string n in new string[] { m + ".ybn", "hi@" + m + ".ybn" }) if (ib.Count > 0) todo.Add(new KeyValuePair<string, List<CutBox>>(n.ToLowerInvariant(), ib));
            HashSet<string> seen = new HashSet<string>();
            // (an opened resource's own collision files that are not its interior's: they are in the world)
            if (wb.Count > 0) foreach (string n in Vanilla.LooseNames(".ybn"))
                {
                    if (todo.Exists(t => t.Key == n) || !seen.Add(n)) continue;
                    try
                    {
                        RpfFileEntry fe; byte[] yd = Vanilla.Bytes(n, out fe); YbnFile y = new YbnFile(); y.Load(yd, fe); if (y.Bounds == null) continue;
                        if (y.Bounds.BoxMax.X < x0 || y.Bounds.BoxMin.X > x1 || y.Bounds.BoxMax.Y < y0 || y.Bounds.BoxMin.Y > y1) continue;
                        todo.Add(new KeyValuePair<string, List<CutBox>>(n, wb));
                    }
                    catch (Exception ex) { warns.Add(n + ": " + ex.Message); }
                }
            if (wb.Count > 0) foreach (string pre in mapPrefixes)
                foreach (string n in Vanilla.Names(".ybn"))
                {
                    if (n.IndexOf(pre, StringComparison.Ordinal) < 0 || !seen.Add(n)) continue;
                    try
                    {
                        RpfFileEntry fe; byte[] yd = Vanilla.Bytes(n, out fe); YbnFile y = new YbnFile(); y.Load(yd, fe); if (y.Bounds == null) continue;
                        if (y.Bounds.BoxMax.X < x0 || y.Bounds.BoxMin.X > x1 || y.Bounds.BoxMax.Y < y0 || y.Bounds.BoxMin.Y > y1) continue;
                        todo.Add(new KeyValuePair<string, List<CutBox>>(n, wb));
                    }
                    catch (Exception) { }
                }
            foreach (KeyValuePair<string, List<CutBox>> kv in todo)
            {
                byte[] raw = Vanilla.ResourceFile(kv.Key); if (raw == null) continue;
                try
                {
                    ColDoc cd = ColDoc.Load(raw, kv.Key); CollisionStats st = new CollisionStats(); XmlDocument d = CollisionCut.Cut(cd, kv.Value, st); warns.AddRange(st.Warnings);
                    if (d == null || st.Removed == 0) continue;
                    files.Add(Write(stream, kv.Key, XmlYbn.GetYbn(d).Save()));
                    log.Add(kv.Key + ": " + st.Removed + " collision polygons cut away, " + st.Added + " put back round the openings and where removed objects stood");
                }
                catch (Exception ex) { warns.Add(kv.Key + ": " + ex.Message); }
            }
            return files;
        }
    }
}
