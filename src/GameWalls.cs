// New walls in a game interior (Buildings of the game, gamemlo.js): one model per room with the walls drawn in it, the textures they use, and their collision
// built into the model as boxes (one per piece of wall: beside, over and under the openings). The model is a fixed object of the interior, defined like the
// game's own furniture with built-in collision (a wardrobe: archetype flags 0x20000000, a physics dictionary of its own name), so it needs no collision file
// of the interior. C# 5.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using CodeWalker.GameFiles;

namespace MloCreator
{
    public static partial class Exporter
    {
        public class WallModel { public string Name, ArchXml; public int Room, Tris, Boxes; }

        // groups: [{ room, mats: [{ tex, glass, pos, nrm, uv, idx (base64) }], boxes: [{ c: [x, y, z], h: [hx, hy, hz], yaw, mat }] }], textures: [{ name, size, rgba }]
        // P: the prefix of the files' names (the models are P + "walls" + room, the textures P + "tex")
        public static List<WallModel> GameWalls(List<object> groups, List<object> textures, string P, string stream, string work, List<string> files, List<string> log)
        {
            List<WallModel> res = new List<WallModel>(); if (groups.Count == 0) return res;
            string txd = P + "tex"; bool needGlass = false;
            List<TexData> texs = new List<TexData>();
            foreach (object to in textures)
            {
                Dictionary<string, object> td = J.O(to);
                TexData t = new TexData(); t.Name = Safe(J.S(J.G(td, "name"), "tex"), "tex", 24); t.Size = J.I(J.G(td, "size"), 256);
                byte[] rgba = Convert.FromBase64String(J.S(J.G(td, "rgba"), ""));
                if (rgba.Length != t.Size * t.Size * 4) throw new Exception("Texture " + t.Name + " has the wrong pixel data size.");
                t.Argb = new int[t.Size * t.Size]; for (int i = 0; i < t.Argb.Length; i++) t.Argb[i] = Px(rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2], 255);
                texs.Add(t);
            }
            List<GroupData> gs = new List<GroupData>(); List<List<double[]>> boxes = new List<List<double[]>>();
            foreach (object go in groups)
            {
                Dictionary<string, object> gd = J.O(go); GroupData g = new GroupData(); g.Room = J.I(J.G(gd, "room"), 0); g.Name = "walls" + g.Room; g.Lod = 100;
                foreach (object mo in J.A(J.G(gd, "mats")))
                {
                    Dictionary<string, object> md = J.O(mo); MatData m = new MatData();
                    m.Tex = Safe(J.S(J.G(md, "tex"), "plaster_white"), "plaster_white", 24); m.Glass = J.B(J.G(md, "glass"), false);
                    m.Pos = Floats(J.S(J.G(md, "pos"), "")); m.Nrm = Floats(J.S(J.G(md, "nrm"), "")); m.Uv = Floats(J.S(J.G(md, "uv"), "")); m.Idx = Ints(J.S(J.G(md, "idx"), ""));
                    if (m.Idx.Length == 0 || m.Pos.Length == 0) continue;
                    if (m.Pos.Length / 3 > 65000) throw new Exception("The new walls of one room have " + (m.Pos.Length / 3) + " vertices with one texture; the game's limit is 65535.");
                    if (m.Glass) needGlass = true; g.Mats.Add(m);
                }
                List<double[]> bl = new List<double[]>();
                foreach (object bo in J.A(J.G(gd, "boxes")))
                {
                    Dictionary<string, object> bd = J.O(bo); List<object> c = J.A(J.G(bd, "c")), h = J.A(J.G(bd, "h"));
                    bl.Add(new double[] { J.D(c[0], 0), J.D(c[1], 0), J.D(c[2], 0), J.D(h[0], 0.05), J.D(h[1], 0.05), J.D(h[2], 0.05), J.D(J.G(bd, "yaw"), 0), J.I(J.G(bd, "mat"), 1) });
                }
                if (g.Mats.Count == 0) continue;
                gs.Add(g); boxes.Add(bl);
            }
            if (gs.Count == 0) return res;
            if (needGlass) { texs.Add(Solid("glass", 64, Px(186, 206, 214, 70), true, "DIFFUSE")); texs.Add(Solid("glass_n", 16, Px(128, 128, 255, 255), false, "NORMAL")); texs.Add(Solid("glass_s", 16, Px(255, 255, 255, 255), false, "SPECULAR")); }
            string ddsDir = Path.Combine(work, "dds"); Directory.CreateDirectory(ddsDir);
            foreach (TexData t in texs) Dds.Write(Path.Combine(ddsDir, P + t.Name + ".dds"), t.Argb, t.Size, t.Alpha);
            YtdFile ytd = XmlYtd.GetYtd(TxdXml(texs, P), ddsDir);
            Save(Path.Combine(stream, txd + ".ytd"), ytd.Save(), files);

            for (int i = 0; i < gs.Count; i++)
            {
                GroupData g = gs[i]; string name = P + g.Name;
                string xml = DrawableXml(name, g, P); if (boxes[i].Count > 0) xml = xml.Replace("</Drawable>", WallBoundsXml(boxes[i]) + "</Drawable>");
                YdrFile ydr = XmlYdr.GetYdr(xml, work);
                Save(Path.Combine(stream, name + ".ydr"), ydr.Save(), files);
                V3 mn, mx; Bounds(g, out mn, out mx);
                WallModel w = new WallModel(); w.Name = name; w.Room = g.Room; w.Boxes = boxes[i].Count;
                foreach (MatData m in g.Mats) w.Tris += m.Idx.Length / 3;
                w.ArchXml = DrawableArch(name, txd, 100, mn, mx).Replace("<flags value=\"0\" />", "<flags value=\"536870912\" />").Replace("<physicsDictionary />", "<physicsDictionary>" + name + "</physicsDictionary>");
                res.Add(w);
                log.Add(name + ": " + w.Tris + " triangles, " + w.Boxes + " collision boxes");
            }
            return res;
        }

        // the walls' collision inside their model: a composite of boxes, each turned about the vertical (yaw) and moved to its middle
        // a collision file (in the world) of boxes: [cx, cy, cz, hx, hy, hz, yaw, material] each
        public static byte[] BoxesYbn(List<double[]> bl)
        {
            XmlDocument d = new XmlDocument(); d.LoadXml("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<BoundsFile>\n" + WallBoundsXml(bl) + "</BoundsFile>\n");
            return XmlYbn.GetYbn(d).Save();
        }
        static string WallBoundsXml(List<double[]> bl)
        {
            V3 mn = new V3(1e9, 1e9, 1e9), mx = new V3(-1e9, -1e9, -1e9); double vol = 0;
            foreach (double[] b in bl)
            {
                double cs = Math.Cos(b[6]), sn = Math.Sin(b[6]), ex = Math.Abs(b[3] * cs) + Math.Abs(b[4] * sn), ey = Math.Abs(b[3] * sn) + Math.Abs(b[4] * cs);
                mn = new V3(Math.Min(mn.X, b[0] - ex), Math.Min(mn.Y, b[1] - ey), Math.Min(mn.Z, b[2] - b[5])); mx = new V3(Math.Max(mx.X, b[0] + ex), Math.Max(mx.Y, b[1] + ey), Math.Max(mx.Z, b[2] + b[5]));
                vol += 8 * b[3] * b[4] * b[5];
            }
            V3 ctr = (mn + mx) * 0.5; double crad = (mx - mn).Len() * 0.5;
            StringBuilder s = new StringBuilder();
            s.AppendLine(" <Bounds type=\"Composite\">");
            s.AppendLine("  " + Vec("BoxMin", mn)); s.AppendLine("  " + Vec("BoxMax", mx)); s.AppendLine("  " + Vec("BoxCenter", ctr)); s.AppendLine("  " + Vec("SphereCenter", ctr)); s.AppendLine("  <SphereRadius value=\"" + F(crad) + "\" />");
            s.AppendLine("  <Margin value=\"0\" />\n  <Volume value=\"" + F(vol) + "\" />\n  <Inertia x=\"1\" y=\"1\" z=\"1\" />\n  <MaterialIndex value=\"0\" />\n  <MaterialColourIndex value=\"0\" />\n  <ProceduralID value=\"0\" />\n  <RoomID value=\"0\" />\n  <PedDensity value=\"0\" />\n  <UnkFlags value=\"0\" />\n  <PolyFlags value=\"0\" />\n  <UnkType value=\"1\" />");
            s.AppendLine("  <Children>");
            foreach (double[] b in bl)
            {
                double hx = b[3], hy = b[4], hz = b[5], cs = Math.Cos(b[6]), sn = Math.Sin(b[6]), rad = Math.Sqrt(hx * hx + hy * hy + hz * hz), margin = Math.Min(0.04, 0.25 * Math.Min(hx, Math.Min(hy, hz)));
                string inertia = "x=\"" + F((hy * hy + hz * hz) / 3) + "\" y=\"" + F((hx * hx + hz * hz) / 3) + "\" z=\"" + F((hx * hx + hy * hy) / 3) + "\"";
                s.AppendLine("   <Item type=\"Box\">");
                s.AppendLine("    " + Vec("BoxMin", new V3(-hx, -hy, -hz))); s.AppendLine("    " + Vec("BoxMax", new V3(hx, hy, hz))); s.AppendLine("    " + Vec("BoxCenter", new V3(0, 0, 0))); s.AppendLine("    " + Vec("SphereCenter", new V3(0, 0, 0))); s.AppendLine("    <SphereRadius value=\"" + F(rad) + "\" />");
                s.AppendLine("    <Margin value=\"" + F(margin) + "\" />\n    <Volume value=\"" + F(8 * hx * hy * hz) + "\" />\n    <Inertia " + inertia + " />\n    <MaterialIndex value=\"" + (int)b[7] + "\" />\n    <MaterialColourIndex value=\"0\" />\n    <ProceduralID value=\"0\" />\n    <RoomID value=\"0\" />\n    <PedDensity value=\"0\" />\n    <UnkFlags value=\"0\" />\n    <PolyFlags value=\"0\" />\n    <UnkType value=\"1\" />");
                s.AppendLine("    <CompositeTransform>\n     " + F(cs) + " " + F(sn) + " 0 0\n     " + F(-sn) + " " + F(cs) + " 0 0\n     0 0 1 0\n     " + F(b[0]) + " " + F(b[1]) + " " + F(b[2]) + " 1\n    </CompositeTransform>");
                s.AppendLine("    <CompositeFlags1>MAP_WEAPON, MAP_DYNAMIC, MAP_ANIMAL, MAP_COVER, MAP_VEHICLE</CompositeFlags1>");
                s.AppendLine("    <CompositeFlags2>VEHICLE_NOT_BVH, VEHICLE_BVH, PED, RAGDOLL, ANIMAL, ANIMAL_RAGDOLL, OBJECT, PLANT, PROJECTILE, EXPLOSION, FORKLIFT_FORKS, TEST_WEAPON, TEST_CAMERA, TEST_AI, TEST_SCRIPT, TEST_VEHICLE_WHEEL, GLASS</CompositeFlags2>");
                s.AppendLine("   </Item>");
            }
            s.AppendLine("  </Children>\n </Bounds>");
            return s.ToString();
        }
    }
}
