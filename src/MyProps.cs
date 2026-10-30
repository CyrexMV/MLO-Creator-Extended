// The user's own props (the props folder next to the program: .ydr models with their .ytd texture dictionaries). They are searched and shown like the game's
// props (Vanilla.UseLibrary); an export that places one copies its model and texture dictionary into stream/ and defines it (CBaseArchetypeDef): a model
// with collision built in like the game's fixed furniture (flags 0x20000000, a physics dictionary of its own name), else a plain one. C# 5.
using System;
using System.Collections.Generic;
using System.IO;

namespace MloCreator
{
    public static partial class Exporter
    {
        // the archetype of one of the user's props, after its files are copied into stream/ (files gets their paths there); null: not one of theirs, or unreadable
        public static string MyPropArch(string name, string stream, List<string> files, List<string> log, List<string> warns)
        {
            if (!Vanilla.IsLibrary(name)) return null;
            string n = name.ToLowerInvariant(), ydr = Vanilla.LibraryPath(n + ".ydr");
            SharpDX.Vector3[] box = Vanilla.PropBox(n);
            if (box == null) { warns.Add("Your prop " + n + " could not be read; it was left out."); return null; }
            File.Copy(ydr, Path.Combine(stream, n + ".ydr"), true); files.Add("stream/" + n + ".ydr");
            string ytd = Vanilla.LibraryYtdFor(n), txd = "";
            if (ytd != null) { string dst = Path.Combine(stream, ytd); if (!File.Exists(dst)) { File.Copy(Vanilla.LibraryPath(ytd), dst, true); files.Add("stream/" + ytd); } txd = Path.GetFileNameWithoutExtension(ytd); }
            bool col = Vanilla.HasOwnCollision(n);
            if (Vanilla.GameHasArchetype(CodeWalker.GameFiles.JenkHash.GenHash(n))) warns.Add("Your prop " + n + " has the name of a prop of the game: the game's one is replaced everywhere while the resource runs. Rename the file to keep both.");
            string a = DrawableArch(n, txd, 150, new V3(box[0].X, box[0].Y, box[0].Z), new V3(box[1].X, box[1].Y, box[1].Z));
            if (col) a = a.Replace("<flags value=\"0\" />", "<flags value=\"536870912\" />").Replace("<physicsDictionary />", "<physicsDictionary>" + n + "</physicsDictionary>");
            log.Add("your prop " + n + ": " + n + ".ydr" + (txd.Length > 0 ? " with " + txd + ".ytd" : " (textures in the model)") + (col ? ", collision in the model" : ", no collision"));
            return a;
        }
        // a map types file with the user's props of a game interior's export (their archetypes)
        public static byte[] MyPropsYtyp(string ytypName, List<string> archs)
        {
            System.Text.StringBuilder s = new System.Text.StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<CMapTypes>\n <extensions />\n <archetypes>\n");
            foreach (string a in archs) s.Append(a).Append('\n');
            s.Append(" </archetypes>\n <name>" + ytypName + "</name>\n <dependencies />\n <compositeEntityTypes itemType=\"CCompositeEntityType\" />\n</CMapTypes>\n");
            System.Xml.XmlDocument d = new System.Xml.XmlDocument(); d.LoadXml(s.ToString()); return CodeWalker.GameFiles.XmlMeta.GetRSCData(d);
        }

        // copies model files into the props folder (a .ydr and its .ytd of the same name next to it); returns the props added
        public static List<string> AddMyProps(string dir, string[] paths)
        {
            List<string> added = new List<string>(); if (paths == null) return added; Directory.CreateDirectory(dir);
            foreach (string p in paths)
            {
                string x = Path.GetExtension(p).ToLowerInvariant(); if (x != ".ydr" && x != ".ytd") continue;
                string dst = Path.Combine(dir, Path.GetFileName(p).ToLowerInvariant()); File.Copy(p, dst, true);
                if (x == ".ydr") added.Add(Path.GetFileNameWithoutExtension(dst));
                // (a model's texture dictionary of the same name, next to it, comes along)
                string ytd = Path.ChangeExtension(p, ".ytd"); if (x == ".ydr" && File.Exists(ytd)) File.Copy(ytd, Path.Combine(dir, Path.GetFileName(ytd).ToLowerInvariant()), true);
            }
            return added;
        }
    }
}
