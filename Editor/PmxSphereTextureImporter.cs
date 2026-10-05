// SPDX-License-Identifier: MIT
using System;
using System.IO;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace PmxImport.Editor
{
    [ScriptedImporter(2, new[] { "sph", "spa" })]
    public class PmxSphereTextureImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext context)
        {
            byte[] bytes = File.ReadAllBytes(context.assetPath);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = Path.GetFileNameWithoutExtension(context.assetPath), wrapMode = TextureWrapMode.Clamp };
            try
            {
                if (!ImageConversion.LoadImage(texture, bytes, false)) LoadBitmap(texture, bytes);
                texture.Apply(true, false); context.AddObjectToAsset("texture", texture); context.SetMainObject(texture);
            }
            catch (Exception error) { UnityEngine.Object.DestroyImmediate(texture); context.LogImportError("Sphere texture: " + error.Message); }
        }
        static void LoadBitmap(Texture2D texture, byte[] bytes)
        {
            using (var input = new BinaryReader(new MemoryStream(bytes)))
            {
                if (input.ReadUInt16() != 0x4D42) throw new InvalidDataException("Expected PNG, JPEG or uncompressed BMP sphere texture.");
                input.ReadUInt32(); input.ReadUInt32(); uint start = input.ReadUInt32();
                uint header = input.ReadUInt32(); if (header < 40) throw new InvalidDataException("Unsupported BMP header.");
                int width = input.ReadInt32(), signedHeight = input.ReadInt32(); ushort planes = input.ReadUInt16(), bits = input.ReadUInt16(); uint compression = input.ReadUInt32();
                if (width <= 0 || width > 16384 || signedHeight == 0 || signedHeight == int.MinValue || Math.Abs(signedHeight) > 16384 || planes != 1 || (bits != 8 && bits != 24 && bits != 32) || compression != 0) throw new InvalidDataException("Unsupported BMP dimensions or pixel format.");
                int height = Math.Abs(signedHeight), stride = ((width * bits + 31) / 32) * 4;
                if (start + (long)height * stride > bytes.Length || (long)width * height > 64_000_000) throw new InvalidDataException("BMP pixel data is truncated or too large.");
                Color32[] palette = null;
                if (bits == 8)
                {
                    input.BaseStream.Position = 46; uint used = input.ReadUInt32(); int count = used == 0 ? 256 : checked((int)used);
                    long paletteStart = 14L + header;
                    if (count > 256 || paletteStart + count * 4L > start || start > bytes.Length) throw new InvalidDataException("Invalid BMP palette.");
                    palette = new Color32[count]; input.BaseStream.Position = paletteStart;
                    for (int i = 0; i < count; i++) { byte b = input.ReadByte(), g = input.ReadByte(), r = input.ReadByte(); input.ReadByte(); palette[i] = new Color32(r, g, b, 255); }
                }
                else if (start < 14L + header) throw new InvalidDataException("Invalid BMP pixel offset.");
                var pixels = new Color32[width * height];
                for (int row = 0; row < height; row++)
                {
                    int y = signedHeight > 0 ? row : height - row - 1;
                    int offset = checked((int)start + row * stride);
                    for (int x = 0; x < width; x++)
                    {
                        if (palette != null) { int index = bytes[offset++]; if (index >= palette.Length) throw new InvalidDataException("Invalid BMP palette index."); pixels[y * width + x] = palette[index]; }
                        else { byte b = bytes[offset++], g = bytes[offset++], r = bytes[offset++]; byte a = bits == 32 ? bytes[offset++] : (byte)255; pixels[y * width + x] = new Color32(r, g, b, a); }
                    }
                }
                texture.Reinitialize(width, height, TextureFormat.RGBA32, true); texture.SetPixels32(pixels);
            }
        }
    }
}
