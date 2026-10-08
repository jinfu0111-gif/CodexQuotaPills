using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace CodexUsageOverlay
{
    // Reads static declarations only. Never extracts, executes or modifies app code.
    internal static class CodexDesktopContract
    {
        internal static bool Matches(string declarations, string implementation)
        {
            foreach (string pair in new[] { "thread-stream-state-changed:11", "thread-stream-following-changed:1",
                "thread-owner-discovery:1", "thread-follower-start-turn:2", "thread-archived:2", "thread-queued-followups-changed:2" })
            {
                string[] parts = pair.Split(':');
                // A different declaration anywhere in the bundle is incompatible.
                MatchCollection values = Regex.Matches(declarations, "[\"'`]" + Regex.Escape(parts[0]) + "[\"'`]\\s*:\\s*(\\d+)");
                if (values.Count == 0) return false;
                foreach (Match value in values) if (value.Groups[1].Value != parts[1]) return false;
            }
            return implementation.Contains("case`thread-follower-start-turn`:") &&
                implementation.Contains(".startTurn(") && implementation.Contains(".params.turnStart") &&
                implementation.Contains("inheritThreadSettings:") && implementation.Contains("supportsUntrustedAppInput:!0");
        }

        internal static void ValidateArchive(string path)
        {
            using (FileStream file = File.OpenRead(path))
            using (BinaryReader reader = new BinaryReader(file, Encoding.UTF8))
            {
                if (file.Length < 16 || reader.ReadUInt32() != 4) throw new IOException("Codex 资源格式未适配");
                uint headerSize = reader.ReadUInt32();
                uint pickleSize = reader.ReadUInt32();
                uint jsonSize = reader.ReadUInt32();
                if (headerSize < 8 || headerSize > 16 * 1024 * 1024 || pickleSize > headerSize ||
                    jsonSize > headerSize - 8 || jsonSize < 2 || 8L + headerSize > file.Length)
                    throw new IOException("Codex 资源头无效");
                var json = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024, RecursionLimit = 100 };
                object root = json.DeserializeObject(Encoding.UTF8.GetString(ReadExactly(reader, (int)jsonSize)));
                object build = Child(Child(Child(root, ".vite"), "build"), null);
                IDictionary<string, object> files = ResumeJson.Map(ResumeJson.Get(build, "files"));
                if (files == null) throw new IOException("Codex 资源布局未适配");
                string declarations = null, implementation = null;
                int count = 0;
                foreach (KeyValuePair<string, object> entry in files)
                {
                    if (!entry.Key.EndsWith(".js", StringComparison.Ordinal) ||
                        (!entry.Key.StartsWith("src-", StringComparison.Ordinal) && !entry.Key.StartsWith("bootstrap-", StringComparison.Ordinal))) continue;
                    if (++count > 16) throw new IOException("Codex 合约资源数量异常");
                    if (ResumeJson.Get(entry.Value, "unpacked") != null) throw new IOException("Codex 合约资源未打包");
                    long offset, size;
                    if (!Int64.TryParse(Convert.ToString(ResumeJson.Get(entry.Value, "offset"), CultureInfo.InvariantCulture), out offset) ||
                        !Int64.TryParse(Convert.ToString(ResumeJson.Get(entry.Value, "size"), CultureInfo.InvariantCulture), out size) ||
                        offset < 0 || size < 1 || size > 32 * 1024 * 1024 || offset > file.Length - 8L - headerSize - size)
                        throw new IOException("Codex 合约资源位置无效");
                    file.Position = 8L + headerSize + offset;
                    string source = Encoding.UTF8.GetString(ReadExactly(reader, (int)size));
                    if (entry.Key.StartsWith("src-", StringComparison.Ordinal)) declarations = (declarations ?? "") + source;
                    else implementation = (implementation ?? "") + source;
                }
                if (declarations == null || implementation == null || !Matches(declarations, implementation))
                    throw new IOException("Codex IPC 合约已改变，需适配后续跑（最近验证 " + CodexDesktopResumeClient.VerifiedPackageVersion + "）");
            }
        }
        private static object Child(object node, string name)
        { return name == null ? node : ResumeJson.Get(ResumeJson.Get(node, "files"), name); }
        private static byte[] ReadExactly(BinaryReader reader, int size)
        {
            byte[] bytes = reader.ReadBytes(size);
            if (bytes.Length != size) throw new IOException("Codex 资源读取不完整");
            return bytes;
        }
    }
}
