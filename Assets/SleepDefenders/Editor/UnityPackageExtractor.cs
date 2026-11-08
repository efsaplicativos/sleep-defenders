using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

namespace SleepDefenders.EditorTools
{
    /// <summary>
    /// Extrai um .unitypackage (tar.gz com pastas por GUID: asset, asset.meta, pathname) direto
    /// para o projeto, de forma síncrona. Usado para instalar o "TMP Essential Resources" dentro
    /// da mesma chamada do construtor (AssetDatabase.ImportPackage só termina no frame seguinte).
    /// Arquivos que já existem não são sobrescritos.
    /// </summary>
    public static class UnityPackageExtractor
    {
        public static int Extract(string packagePath)
        {
            var entries = new Dictionary<string, Dictionary<string, byte[]>>();
            using (FileStream file = File.OpenRead(packagePath))
            using (var gzip = new GZipStream(file, CompressionMode.Decompress))
            {
                var header = new byte[512];
                while (ReadExactly(gzip, header, 512))
                {
                    if (IsZeroBlock(header)) break;
                    string name = ReadString(header, 0, 100);
                    string prefix = ReadString(header, 345, 155);
                    if (!string.IsNullOrEmpty(prefix)) name = prefix + "/" + name;
                    string sizeField = ReadString(header, 124, 12).Trim();
                    long size = sizeField.Length > 0 ? Convert.ToInt64(sizeField, 8) : 0;
                    char type = (char)header[156];

                    byte[] data = new byte[size];
                    if (size > 0 && !ReadExactly(gzip, data, (int)size)) break;
                    int pad = (int)((512 - size % 512) % 512);
                    if (pad > 0) ReadExactly(gzip, new byte[pad], pad);

                    if (type != '0' && type != '\0') continue; // só arquivos comuns
                    name = name.Replace('\\', '/');
                    if (name.StartsWith("./")) name = name.Substring(2);
                    int slash = name.IndexOf('/');
                    if (slash <= 0) continue;
                    string guid = name.Substring(0, slash);
                    string part = name.Substring(slash + 1);
                    if (!entries.TryGetValue(guid, out var parts)) entries[guid] = parts = new Dictionary<string, byte[]>();
                    parts[part] = data;
                }
            }

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            int written = 0;
            foreach (var kv in entries)
            {
                if (!kv.Value.TryGetValue("pathname", out byte[] pathBytes)) continue;
                string assetPath = Encoding.UTF8.GetString(pathBytes).Split('\n')[0].Trim();
                if (!assetPath.StartsWith("Assets/")) continue;
                string full = Path.Combine(projectRoot, assetPath);

                if (kv.Value.TryGetValue("asset", out byte[] asset))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(full));
                    if (!File.Exists(full)) { File.WriteAllBytes(full, asset); written++; }
                }
                else Directory.CreateDirectory(full); // entrada de pasta

                if (kv.Value.TryGetValue("asset.meta", out byte[] meta) && !File.Exists(full + ".meta"))
                    File.WriteAllBytes(full + ".meta", meta);
            }
            Debug.Log($"[SleepDefenders] {Path.GetFileName(packagePath)}: {written} arquivos extraídos.");
            return written;
        }

        private static bool ReadExactly(Stream s, byte[] buffer, int count)
        {
            int read = 0;
            while (read < count)
            {
                int n = s.Read(buffer, read, count - read);
                if (n <= 0) return false;
                read += n;
            }
            return true;
        }

        private static bool IsZeroBlock(byte[] block)
        {
            foreach (byte b in block) if (b != 0) return false;
            return true;
        }

        private static string ReadString(byte[] buffer, int offset, int length)
        {
            int end = offset;
            while (end < offset + length && buffer[end] != 0) end++;
            return Encoding.ASCII.GetString(buffer, offset, end - offset);
        }
    }
}
