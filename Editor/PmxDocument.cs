// SPDX-License-Identifier: MIT
// Format reader written from the PMX field layout; no MMD Tools source is included.
using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace PmxImport.Editor
{
    internal sealed class PmxDocument
    {
        internal struct Vertex { public Vector3 position, normal; public Vector2 uv; public Vector4[] additional; public BoneWeight skin; public float edge; }
        internal struct Surface { public string name; public Color diffuse, specular, ambient, outline; public float shininess, edge; public byte flags, sphereMode; public int texture, sphere, toon, indices; public bool sharedToon; }
        internal struct Bone { public string name; public Vector3 position; public int parent; }
        internal struct Offset { public int index; public Vector3 delta; public float influence; }
        internal struct Morph { public string name; public byte panel, kind; public Offset[] offsets; }
        internal struct Body { public string name; public int bone; public byte group, shape, mode; public ushort allowedGroups; public Vector3 size, position, radians; public float mass, movementDamping, rotationDamping, bounce, friction; }
        internal struct Connection { public string name; public byte kind; public int first, second; public Vector3 position, radians, translationLow, translationHigh, rotationLow, rotationHigh, translationSpring, rotationSpring; }
        public string name;
        public Vertex[] vertices;
        public int[] indices;
        public string[] textures;
        public Surface[] surfaces;
        public Bone[] bones;
        public Morph[] morphs;
        public Body[] bodies;
        public Connection[] connections;

        public static PmxDocument Load(string path)
        {
            using (var file = File.OpenRead(path))
            using (var input = new BinaryReader(file)) return new Decoder(input).Decode();
        }

        sealed class Decoder
        {
            readonly BinaryReader input;
            byte[] globals;
            Encoding encoding;
            public Decoder(BinaryReader input) { this.input = input; }
            void Need(long bytes) { if (bytes < 0 || bytes > input.BaseStream.Length - input.BaseStream.Position) throw new InvalidDataException("PMX data is truncated."); }
            byte[] Bytes(int length) { Need(length); return input.ReadBytes(length); }
            int Count(int minimumBytes = 1)
            {
                Need(4); int value = input.ReadInt32();
                if (value < 0 || value > 10_000_000 || (long)value * minimumBytes > input.BaseStream.Length - input.BaseStream.Position)
                    throw new InvalidDataException("Invalid PMX element count.");
                return value;
            }
            string Text() { int size = Count(); return encoding.GetString(Bytes(size)); }
            string Name() { string local = Text(), english = Text(); return string.IsNullOrWhiteSpace(local) ? english : local; }
            float F() { Need(4); float v = input.ReadSingle(); if (float.IsNaN(v) || float.IsInfinity(v)) throw new InvalidDataException("Non-finite PMX value."); return v; }
            Vector2 V2() => new Vector2(F(), F());
            Vector3 V3() => new Vector3(F(), F(), F());
            Vector4 V4() => new Vector4(F(), F(), F(), F());
            Color RGB() { var v = V3(); return new Color(v.x, v.y, v.z, 1); }
            Color RGBA() { var v = V4(); return new Color(v.x, v.y, v.z, v.w); }
            void Skip(int size) { Need(size); input.BaseStream.Position += size; }
            int Index(int slot)
            {
                Need(globals[slot]);
                switch (globals[slot])
                {
                    case 1: return slot == 2 ? input.ReadByte() : input.ReadSByte();
                    case 2: return slot == 2 ? input.ReadUInt16() : input.ReadInt16();
                    default: return input.ReadInt32();
                }
            }
            public PmxDocument Decode()
            {
                if (Encoding.ASCII.GetString(Bytes(4)) != "PMX ") throw new InvalidDataException("Expected PMX signature.");
                float version = F(); if (Math.Abs(version - 2.0f) > .001f && Math.Abs(version - 2.1f) > .001f) throw new InvalidDataException("Only PMX 2.0 / 2.1 are supported.");
                globals = Bytes(input.ReadByte());
                if (globals.Length < 8 || globals[0] > 1 || globals[1] > 4) throw new InvalidDataException("Invalid PMX header.");
                for (int i = 2; i < 8; i++) if (globals[i] != 1 && globals[i] != 2 && globals[i] != 4) throw new InvalidDataException("Invalid PMX index width.");
                encoding = globals[0] == 0 ? new UnicodeEncoding(false, false, true) : new UTF8Encoding(false, true);
                var result = new PmxDocument { name = Name() }; Text(); Text();
                result.vertices = new Vertex[Count(38)];
                for (int i = 0; i < result.vertices.Length; i++)
                {
                    var v = new Vertex { position = V3(), normal = V3(), uv = V2(), additional = new Vector4[globals[1]] };
                    for (int n = 0; n < v.additional.Length; n++) v.additional[n] = V4();
                    byte kind = input.ReadByte(); var skin = new BoneWeight();
                    switch (kind)
                    {
                        case 0: skin.boneIndex0 = Index(5); skin.weight0 = 1; break;
                        case 1:
                        case 3:
                            skin.boneIndex0 = Index(5); skin.boneIndex1 = Index(5); skin.weight0 = F(); skin.weight1 = 1 - skin.weight0;
                            if (kind == 3) Skip(36); break;
                        case 2:
                        case 4:
                            skin.boneIndex0 = Index(5); skin.boneIndex1 = Index(5); skin.boneIndex2 = Index(5); skin.boneIndex3 = Index(5);
                            skin.weight0 = F(); skin.weight1 = F(); skin.weight2 = F(); skin.weight3 = F(); break;
                        default: throw new InvalidDataException("Unknown PMX skinning type.");
                    }
                    v.skin = skin; v.edge = F(); result.vertices[i] = v;
                }
                result.indices = new int[Count(globals[2])];
                if (result.indices.Length % 3 != 0) throw new InvalidDataException("PMX face indices must form triangles.");
                for (int i = 0; i < result.indices.Length; i++) { int index = Index(2); if (index < 0 || index >= result.vertices.Length) throw new InvalidDataException("Invalid PMX vertex index."); result.indices[i] = index; }
                result.textures = new string[Count(4)]; for (int i = 0; i < result.textures.Length; i++) result.textures[i] = Text();
                result.surfaces = new Surface[Count(80)];
                for (int i = 0; i < result.surfaces.Length; i++)
                {
                    var m = new Surface { name = Name(), diffuse = RGBA(), specular = RGB(), shininess = F(), ambient = RGB(), flags = input.ReadByte(), outline = RGBA(), edge = F(), texture = Index(3), sphere = Index(3), sphereMode = input.ReadByte() };
                    byte toon = input.ReadByte(); if (toon > 1) throw new InvalidDataException("Invalid PMX toon reference.");
                    m.sharedToon = toon == 1; m.toon = m.sharedToon ? input.ReadByte() : Index(3); Text(); m.indices = Count(0); result.surfaces[i] = m;
                }
                result.bones = new Bone[Count(22)];
                for (int i = 0; i < result.bones.Length; i++)
                {
                    result.bones[i] = new Bone { name = Name(), position = V3(), parent = Index(5) }; Skip(4); ushort flags = input.ReadUInt16();
                    if ((flags & 1) != 0) Index(5); else Skip(12);
                    if ((flags & 0x300) != 0) { Index(5); Skip(4); }
                    if ((flags & 0x400) != 0) Skip(12);
                    if ((flags & 0x800) != 0) Skip(24);
                    if ((flags & 0x2000) != 0) Skip(4);
                    if ((flags & 0x20) != 0) { Index(5); Skip(8); int links = Count(globals[5] + 1); for (int k = 0; k < links; k++) { Index(5); if (input.ReadByte() != 0) Skip(24); } }
                }
                result.morphs = new Morph[Count(14)];
                for (int i = 0; i < result.morphs.Length; i++)
                {
                    var m = new Morph { name = Name(), panel = input.ReadByte(), kind = input.ReadByte() }; int offsets = Count();
                    m.offsets = m.kind <= 1 ? new Offset[offsets] : Array.Empty<Offset>();
                    for (int n = 0; n < offsets; n++)
                        switch (m.kind)
                        {
                            case 0: m.offsets[n] = new Offset { index = Index(6), influence = F() }; break;
                            case 1: m.offsets[n] = new Offset { index = Index(2), delta = V3() }; break;
                            case 2: Index(5); Skip(28); break;
                            case 3: case 4: case 5: case 6: case 7: Index(2); Skip(16); break;
                            case 8: Index(4); Skip(113); break;
                            case 9: Index(6); Skip(4); break;
                            case 10: Index(7); Skip(25); break;
                            default: throw new InvalidDataException("Unknown PMX morph type.");
                        }
                    result.morphs[i] = m;
                }
                int displays = Count(13);
                for (int i = 0; i < displays; i++) { Name(); Skip(1); int items = Count(); for (int n = 0; n < items; n++) { byte kind = input.ReadByte(); if (kind > 1) throw new InvalidDataException("Invalid display item."); Index(kind == 0 ? 5 : 6); } }
                result.bodies = new Body[Count(65)];
                for (int i = 0; i < result.bodies.Length; i++) result.bodies[i] = new Body { name = Name(), bone = Index(5), group = input.ReadByte(), allowedGroups = input.ReadUInt16(), shape = input.ReadByte(), size = V3(), position = V3(), radians = V3(), mass = F(), movementDamping = F(), rotationDamping = F(), bounce = F(), friction = F(), mode = input.ReadByte() };
                result.connections = new Connection[Count(106)];
                for (int i = 0; i < result.connections.Length; i++) result.connections[i] = new Connection { name = Name(), kind = input.ReadByte(), first = Index(7), second = Index(7), position = V3(), radians = V3(), translationLow = V3(), translationHigh = V3(), rotationLow = V3(), rotationHigh = V3(), translationSpring = V3(), rotationSpring = V3() };
                // Soft bodies are not simulated by this importer. The preceding records are complete.
                return result;
            }
        }
    }
}
