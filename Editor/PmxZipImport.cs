using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PmxImport.Editor
{
    public static class PmxZipImport
    {
        const long MaxExpandedBytes = 2L * 1024 * 1024 * 1024;
        const long MaxFileBytes = 512L * 1024 * 1024;
        const int MaxEntries = 10000;
        static readonly HashSet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            ".pmx", ".png", ".jpg", ".jpeg", ".bmp", ".tga", ".dds", ".sph", ".spa", ".tif", ".tiff", ".gif", ".webp",
            ".txt", ".md", ".pdf", ".csv", ".vmd", ".vpd", ".wav", ".mp3", ".ogg"
        };
        public sealed class Result
        {
            public string folder;
            public string[] models;
            public int skippedFiles;
        }

        [MenuItem("Tools/PMX/ZIPからモデルを読み込む")]
        public static void ChooseZip()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            { EditorUtility.DisplayDialog("PMX ZIP", "Playモードを停止してから読み込んでください。", "OK"); return; }
            string path = EditorUtility.OpenFilePanel("PMXモデルのZIPを選択", "", "zip");
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                var result = Import(path);
                if (result == null) EditorUtility.DisplayDialog("PMX ZIP", "このZIPにはPMXファイルがありません。", "OK");
                else SelectResult(result);
            }
            catch (Exception e) { Debug.LogException(e); EditorUtility.DisplayDialog("PMX ZIP", e.Message, "OK"); }
        }

        [MenuItem("Assets/PMX/選択したZIPを読み込む", true)]
        static bool CanImportSelected() => AssetDatabase.GetAssetPath(Selection.activeObject).EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        [MenuItem("Assets/PMX/選択したZIPを読み込む")]
        static void ImportSelected()
        {
            try { var result = Import(AssetDatabase.GetAssetPath(Selection.activeObject)); if (result != null) SelectResult(result); }
            catch (Exception e) { Debug.LogException(e); }
        }

        public static void SelectResult(Result result)
        {
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(result.models.Length == 1 ? result.models[0] : result.folder);
            EditorGUIUtility.PingObject(Selection.activeObject);
            Debug.Log($"PMX ZIP: {result.models.Length}モデルを読み込みました: {result.folder}" +
                (result.skippedFiles > 0 ? $" (対象外のファイル {result.skippedFiles}件は展開しませんでした)" : ""));
        }

        public static string Fingerprint(string path)
        {
            using var input = File.OpenRead(path);
            using var hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(input)).Replace("-", "");
        }

        public static string ValidateEntryName(string name)
        {
            name = name.Replace('\\', '/');
            if (string.IsNullOrEmpty(name) || name.StartsWith("/") || name.Contains(':') || name.Length > 220)
                throw new InvalidDataException("ZIP内のパスが不正です: " + name);
            var parts = name.TrimEnd('/').Split('/');
            foreach (var part in parts)
            {
                if (part == "." || part == ".." || part.Length == 0 || part.EndsWith(".") || part.EndsWith(" ") ||
                    part.Any(c => c < 32 || "<>\"|?*".Contains(c)))
                    throw new InvalidDataException("ZIP内のパスが不正です: " + name);
                string stem = part.Split('.')[0].ToUpperInvariant();
                if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
                    "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(stem))
                    throw new InvalidDataException("ZIP内に使用できない名前があります: " + name);
            }
            return string.Join("/", parts);
        }

        static string[] ReadEntryNames(Stream stream)
        {
            // UnityのMonoではZIPのUTF-8フラグの扱いに差があるため、中央ディレクトリから名前を読む。
            if (stream.Length < 22) throw new InvalidDataException("ZIPファイルが途中で切れています。");
            int length = (int)Math.Min(stream.Length, 65557);
            var tail = new byte[length]; stream.Position = stream.Length - length;
            using var reader = new BinaryReader(stream, Encoding.UTF8, true);
            if (reader.Read(tail, 0, length) != length) throw new InvalidDataException("ZIP末尾を読み込めません。");
            int end = -1;
            for (int i = length - 22; i >= 0; i--)
                if (BitConverter.ToUInt32(tail, i) == 0x06054b50 && i + 22 + BitConverter.ToUInt16(tail, i + 20) == length) { end = i; break; }
            if (end < 0) throw new InvalidDataException("ZIPの中央ディレクトリがありません。");
            int count = BitConverter.ToUInt16(tail, end + 10);
            uint offset = BitConverter.ToUInt32(tail, end + 16);
            if (count == 65535 || offset == uint.MaxValue) throw new InvalidDataException("ZIP64形式は未対応です。通常のZIP形式で圧縮してください。");
            if (BitConverter.ToUInt16(tail, end + 4) != 0 || BitConverter.ToUInt16(tail, end + 6) != 0)
                throw new InvalidDataException("分割ZIPは読み込めません。");
            if (count > MaxEntries || offset >= stream.Length) throw new InvalidDataException("ZIP内のファイル数または構造が不正です。");
            stream.Position = offset;
            var names = new string[count];
            var utf8 = new UTF8Encoding(false, true);
            for (int i = 0; i < count; i++)
            {
                var header = reader.ReadBytes(46);
                if (header.Length != 46 || BitConverter.ToUInt32(header, 0) != 0x02014b50) throw new InvalidDataException("ZIP内のヘッダーが不正です。");
                int flags = BitConverter.ToUInt16(header, 8), nameLength = BitConverter.ToUInt16(header, 28);
                int extraLength = BitConverter.ToUInt16(header, 30), commentLength = BitConverter.ToUInt16(header, 32);
                var raw = reader.ReadBytes(nameLength);
                if (raw.Length != nameLength || stream.Position + extraLength + commentLength > stream.Length) throw new InvalidDataException("ZIP内の名前が途中で切れています。");
                if ((flags & 0x800) != 0) names[i] = utf8.GetString(raw);
                else
                {
                    try { names[i] = utf8.GetString(raw); }
                    catch (DecoderFallbackException) { names[i] = Encoding.GetEncoding(932).GetString(raw); }
                }
                stream.Position += extraLength + commentLength;
            }
            stream.Position = 0;
            return names;
        }

        public static Result Import(string zipPath)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Playモードを停止してから読み込んでください。");
            string project = Path.GetDirectoryName(Application.dataPath);
            zipPath = Path.GetFullPath(zipPath);
            using var stream = File.OpenRead(zipPath);
            var entryNames = ReadEntryNames(stream);
            // UTF-8フラグ付きはUTF-8、従来の日本語ZIPはShift-JISでデコードする。
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read, false, Encoding.GetEncoding(932));
            if (zip.Entries.Count > MaxEntries) throw new InvalidDataException("ZIP内のファイル数が上限10000件を超えています。");
            if (entryNames.Length != zip.Entries.Count) throw new InvalidDataException("ZIP内のファイル数が一致しません。");
            if (!entryNames.Any(n => n.EndsWith(".pmx", StringComparison.OrdinalIgnoreCase))) return null;
            var files = new List<(ZipArchiveEntry entry, string name)>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long expanded = 0; int skipped = 0;
            for (int index = 0; index < zip.Entries.Count; index++)
            {
                var entry = zip.Entries[index]; string originalName = entryNames[index];
                string name = ValidateEntryName(originalName);
                if (name.StartsWith("__MACOSX/", StringComparison.OrdinalIgnoreCase) || name.Split('/').Any(p => p.StartsWith("."))) { skipped++; continue; }
                // Unixのsymlink属性を拒否する。
                if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new InvalidDataException("ZIP内のリンクは展開できません: " + name);
                if (originalName.EndsWith("/") || originalName.EndsWith("\\")) continue;
                if (!Extensions.Contains(Path.GetExtension(name))) { skipped++; continue; }
                if (!names.Add(name)) throw new InvalidDataException("ZIP内に同名のファイルがあります: " + name);
                if (entry.Length < 0 || entry.Length > MaxFileBytes) throw new InvalidDataException("ファイルが上限512MBを超えています: " + name);
                expanded = checked(expanded + entry.Length);
                if (expanded > MaxExpandedBytes) throw new InvalidDataException("展開サイズが上限2GBを超えています。");
                files.Add((entry, name));
            }
            if (!files.Any(f => Path.GetExtension(f.name).Equals(".pmx", StringComparison.OrdinalIgnoreCase))) return null;
            foreach (var file in files)
                if (names.Any(n => n.StartsWith(file.name + "/", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("ZIP内のファイルとフォルダーが重複しています: " + file.name);
            string stage = Path.Combine(project, "Library", "PmxZipStage", Guid.NewGuid().ToString("N"));
            string destination = null;
            Directory.CreateDirectory(stage);
            try
            {
                byte[] buffer = new byte[65536];
                foreach (var file in files)
                {
                    string output = Path.GetFullPath(Path.Combine(stage, file.name));
                    if (!output.StartsWith(stage + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("ZIP内のパスが展開先の外を指しています。");
                    Directory.CreateDirectory(Path.GetDirectoryName(output));
                    using var input = file.entry.Open();
                    using var target = new FileStream(output, FileMode.CreateNew, FileAccess.Write);
                    long written = 0; int count;
                    while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        written += count;
                        if (written > file.entry.Length || written > MaxFileBytes) throw new InvalidDataException("ZIP内のファイル長が不正です: " + file.name);
                        target.Write(buffer, 0, count);
                    }
                    if (written != file.entry.Length) throw new InvalidDataException("ZIP内のファイルが途中で切れています: " + file.name);
                }
                const string parent = "Assets/MmdZipImports";
                if (!AssetDatabase.IsValidFolder(parent)) AssetDatabase.CreateFolder("Assets", "MmdZipImports");
                string title = Path.GetFileNameWithoutExtension(zipPath);
                foreach (char c in Path.GetInvalidFileNameChars()) title = title.Replace(c, '_');
                title = title.Trim().TrimEnd('.');
                if (string.IsNullOrEmpty(title)) title = "Model";
                if (title.Length > 48) title = title.Substring(0, 48);
                string folder = AssetDatabase.GenerateUniqueAssetPath(parent + "/" + title);
                destination = Path.GetFullPath(Path.Combine(project, folder));
                if (!destination.StartsWith(Path.GetFullPath(Path.Combine(project, parent)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || Directory.Exists(destination))
                    throw new IOException("展開先フォルダーを作成できません。");
                Directory.Move(stage, destination);
                // 先にテクスチャを読み込み、次にPMXをインポートする。
                foreach (var file in files.Where(f => !Path.GetExtension(f.name).Equals(".pmx", StringComparison.OrdinalIgnoreCase)))
                    AssetDatabase.ImportAsset(folder + "/" + file.name, ImportAssetOptions.ForceSynchronousImport);
                var models = files.Where(f => Path.GetExtension(f.name).Equals(".pmx", StringComparison.OrdinalIgnoreCase)).Select(f => folder + "/" + f.name).ToArray();
                foreach (string model in models) AssetDatabase.ImportAsset(model, ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                foreach (string model in models)
                    if (AssetDatabase.LoadAssetAtPath<GameObject>(model) == null)
                        throw new InvalidDataException("PMXの読み込みに失敗しました: " + model + "。Consoleで詳細を確認してください。展開したファイルは残っています。");
                return new Result { folder = folder, models = models, skippedFiles = skipped };
            }
            finally
            {
                if (Directory.Exists(stage)) Directory.Delete(stage, true);
                // Assetsへの移動後は失敗しても削除せず、修復・再インポートできる状態に残す。
            }
        }
    }

    public sealed class PmxZipPostprocessor : AssetPostprocessor
    {
        static readonly HashSet<string> Pending = new HashSet<string>();
        static bool scheduled;
        [Serializable] sealed class Item { public string id, hash, folder; }
        [Serializable] sealed class History { public List<Item> items = new List<Item>(); }

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (var path in imported.Concat(moved))
                if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) Pending.Add(path);
            if (Pending.Count == 0 || scheduled) return;
            scheduled = true; EditorApplication.delayCall += Process;
        }

        static void Process()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
            { EditorApplication.delayCall += Process; return; }
            var pending = Pending.ToArray(); Pending.Clear(); scheduled = false;
            string historyPath = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Library", "PmxZipHistory.json");
            History history;
            try { history = File.Exists(historyPath) ? JsonUtility.FromJson<History>(File.ReadAllText(historyPath)) : new History(); }
            catch { history = new History(); }
            if (history == null || history.items == null) history = new History();
            foreach (string path in pending)
            {
                if (!File.Exists(path)) continue;
                try
                {
                    string id = AssetDatabase.AssetPathToGUID(path), hash = PmxZipImport.Fingerprint(path);
                    var old = history.items.FirstOrDefault(i => i.id == id);
                    if (old != null && old.hash == hash && (string.IsNullOrEmpty(old.folder) || AssetDatabase.IsValidFolder(old.folder))) continue;
                    var result = PmxZipImport.Import(path);
                    history.items.RemoveAll(i => i.id == id);
                    history.items.Add(new Item { id = id, hash = hash, folder = result?.folder });
                    if (result != null) PmxZipImport.SelectResult(result);
                }
                catch (Exception e) { Debug.LogError("PMX ZIP: " + path + "\n" + e.Message); }
            }
            File.WriteAllText(historyPath, JsonUtility.ToJson(history, true));
        }
    }
}
