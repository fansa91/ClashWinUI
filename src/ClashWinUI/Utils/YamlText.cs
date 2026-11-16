using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ClashDotNetFramework.Utils
{
    /// <summary>
    /// 订阅配置的行级编辑器。
    ///
    /// 为什么不直接反序列化成对象再整份重写：订阅里常见的注释、YAML 锚点（&amp;xxx / &lt;&lt;: *xxx）、
    /// 多行字符串、rule-provider 写法都会在整份重写时丢失或被改写，用户更新订阅后还会覆盖回原样。
    /// 因此这里一律按“行”定位、按“块”替换，只动目标键，其余内容原封不动。
    ///
    /// 同时保证：
    ///   - 保留 UTF-8 BOM 与 CRLF/LF 换行风格；
    ///   - 内容没有变化时不写回文件（幂等，可被频繁调用）；
    ///   - 单次调用只读写一次文件，多个键的修改合并成一次写入。
    /// </summary>
    internal static class YamlText
    {
        // 顶层键：行首无缩进、非注释，形如 "tun:" / "dns:"
        private static readonly Regex TopLevelKeyRegex =
            new Regex(@"^([A-Za-z0-9_.-]+)\s*:", RegexOptions.Compiled);

        // 标量行：key + 值 + 可能的行尾注释，替换时保留注释
        private static readonly Regex ScalarLineRegex =
            new Regex(@"^([A-Za-z0-9_.-]+\s*:\s*)([^#\r\n]*?)(\s*#.*)?$", RegexOptions.Compiled);

        private static readonly Regex EnableLineRegex =
            new Regex(@"^(\s*)enable\s*:.*$", RegexOptions.Compiled);

        private static readonly Regex EnableValueRegex =
            new Regex(@"^(\s*enable\s*:\s*)(true|false)(.*)$",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // 块内键值行: "  'a.com': 1.2.3.4"
        private static readonly Regex MapEntryRegex =
            new Regex(@"^[ \t]+'?([^':]+)'?\s*:\s*(.+?)\s*$", RegexOptions.Compiled);

        // 块内列表项: "  - value"
        private static readonly Regex ListItemRegex =
            new Regex(@"^[ \t]+-[ \t]+(.+?)\s*$", RegexOptions.Compiled);

        public static string Bool(bool value) => value ? "true" : "false";

        /// <summary>
        /// 读出文件、交给 edit 修改、必要时写回。edit 里可以连续调用下面的方法修改多个键。
        /// </summary>
        public static void Update(string path, Action<List<string>> edit)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return;

            try
            {
                byte[] raw = File.ReadAllBytes(path);
                bool hasBom = raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF;
                int offset = hasBom ? 3 : 0;
                string text = Encoding.UTF8.GetString(raw, offset, raw.Length - offset);
                string newline = text.Contains("\r\n") ? "\r\n" : "\n";

                var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
                edit(lines);

                string output = string.Join(newline, lines);
                if (string.Equals(output, text, StringComparison.Ordinal))
                    return;

                File.WriteAllText(path, output, new UTF8Encoding(hasBom));
            }
            catch (Exception e)
            {
                Logging.Error("写入配置失败：\n" + e.Message);
            }
        }

        /// <summary>取行首无缩进的 YAML 键；注释、缩进行、列表项都不算顶层键。</summary>
        public static string GetTopLevelKey(string line)
        {
            if (string.IsNullOrEmpty(line))
                return null;
            char c = line[0];
            if (c == '#' || c == ' ' || c == '\t' || c == '\r' || c == '\n')
                return null;
            Match match = TopLevelKeyRegex.Match(line);
            return match.Success ? match.Groups[1].Value : null;
        }

        public static int FindTopLevelKey(List<string> lines, string key)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                if (string.Equals(GetTopLevelKey(lines[i]), key, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        /// <summary>块范围结束于下一个顶层键或 YAML 文档分隔符。</summary>
        public static int FindBlockEnd(List<string> lines, int start)
        {
            for (int i = start + 1; i < lines.Count; i++)
            {
                if (GetTopLevelKey(lines[i]) != null || lines[i].Trim() == "---")
                    return i;
            }
            return lines.Count;
        }

        /// <summary>
        /// 设置顶层标量键。键不存在时追加；存在时只替换值，保留行尾注释。
        /// </summary>
        public static void SetScalar(List<string> lines, string key, string value)
        {
            int index = FindTopLevelKey(lines, key);
            if (index < 0)
            {
                AppendBlock(lines, new[] { key + ": " + value });
                return;
            }

            Match match = ScalarLineRegex.Match(lines[index]);
            if (!match.Success)
            {
                lines[index] = key + ": " + value;
                return;
            }

            string comment = match.Groups[3].Success ? match.Groups[3].Value : string.Empty;
            lines[index] = match.Groups[1].Value + value + comment;
        }

        /// <summary>
        /// 设置某个块内的子键（如 profile.store-selected）。块不存在时不做任何事。
        /// 子键不存在时按块内已有缩进插入到块首，块内其他字段保持原样。
        /// </summary>
        public static void SetNestedScalar(List<string> lines, string parentKey, string childKey, string value)
        {
            int start = FindTopLevelKey(lines, parentKey);
            if (start < 0)
                return;

            int end = FindBlockEnd(lines, start);
            var childRegex = new Regex(@"^([ \t]+)" + Regex.Escape(childKey) + @"\s*:(.*)$", RegexOptions.Compiled);
            string indent = null;

            for (int i = start + 1; i < end; i++)
            {
                string line = lines[i];
                Match child = childRegex.Match(line);
                if (child.Success)
                {
                    string rest = child.Groups[2].Value;
                    // 分离行尾注释，例如 "  enable: true # 说明"
                    int hash = rest.IndexOf('#');
                    string comment = hash >= 0 ? rest.Substring(hash).TrimEnd() : string.Empty;
                    string prefix = child.Groups[1].Value + childKey + ": " + value;
                    lines[i] = comment.Length > 0 ? prefix + " " + comment : prefix;
                    return;
                }

                if (indent == null && line.Trim().Length > 0 && !line.TrimStart().StartsWith("#"))
                    indent = new Regex(@"^[ \t]+").Match(line).Value;
            }

            if (string.IsNullOrEmpty(indent))
                indent = "  ";
            lines.Insert(start + 1, indent + childKey + ": " + value);
        }

        /// <summary>
        /// 只改块内的 enable 行，保留原有缩进、行尾注释和块内其他字段。
        /// 块内没有 enable 行时在键后插入一行；块不存在时不做任何事。
        /// </summary>
        public static void SetEnable(List<string> lines, string key, bool value)
        {
            int start = FindTopLevelKey(lines, key);
            if (start < 0)
                return;

            int end = FindBlockEnd(lines, start);
            string literal = value ? "true" : "false";

            for (int i = start + 1; i < end; i++)
            {
                Match lineMatch = EnableLineRegex.Match(lines[i]);
                if (!lineMatch.Success)
                    continue;

                Match valueMatch = EnableValueRegex.Match(lines[i]);
                lines[i] = valueMatch.Success
                    ? valueMatch.Groups[1].Value + literal + valueMatch.Groups[3].Value
                    : lineMatch.Groups[1].Value + "enable: " + literal;
                return;
            }

            lines.Insert(start + 1, "  enable: " + literal);
        }

        /// <summary>整块替换：存在则替换该块，不存在则追加。</summary>
        public static void ReplaceOrAppendBlock(List<string> lines, string key, IEnumerable<string> block)
        {
            int start = FindTopLevelKey(lines, key);
            var materialized = block.ToList();
            if (start < 0)
            {
                AppendBlock(lines, materialized);
                return;
            }

            int end = FindBlockEnd(lines, start);
            // 块范围在文件末尾会包含尾部的空行；替换时保留它们，
            // 否则每替换一次文件就少一个结尾换行，块之间的空行也会被吃掉（破坏幂等）。
            int removeEnd = end;
            while (removeEnd > start + 1 && lines[removeEnd - 1].Trim().Length == 0)
                removeEnd--;

            lines.RemoveRange(start, removeEnd - start);
            lines.InsertRange(start, materialized);
        }

        /// <summary>在文件末尾追加一个块，并保留结尾换行。</summary>
        public static void AppendBlock(List<string> lines, IEnumerable<string> block)
        {
            if (lines.Count > 0 && lines[lines.Count - 1].Trim().Length == 0)
            {
                lines.InsertRange(lines.Count - 1, block);
            }
            else
            {
                lines.AddRange(block);
                lines.Add(string.Empty);
            }
        }

        /// <summary>删除顶层块（不存在则什么都不做），顺带合并删除产生的连续空行。</summary>
        public static void RemoveBlock(List<string> lines, string key)
        {
            int start = FindTopLevelKey(lines, key);
            if (start < 0)
                return;

            int end = FindBlockEnd(lines, start);
            lines.RemoveRange(start, end - start);

            if (start > 0 && start < lines.Count
                && lines[start].Trim().Length == 0 && lines[start - 1].Trim().Length == 0)
            {
                lines.RemoveAt(start);
            }
        }

        /// <summary>
        /// 字符串列表块（如 authentication、tunnels、nameserver）。
        /// 列表为空时删除该键；值统一加双引号，避免冒号、逗号被 YAML 误解析。
        /// </summary>
        public static void SetListBlock(List<string> lines, string key, IReadOnlyList<string> items)
        {
            if (items == null || items.Count == 0)
            {
                RemoveBlock(lines, key);
                return;
            }
            ReplaceOrAppendBlock(lines, key, BuildListLines(key, items, string.Empty));
        }

        /// <summary>键值映射块（如 hosts）。值为空时整块删除。</summary>
        public static void SetMapBlock(List<string> lines, string key, IReadOnlyList<KeyValuePair<string, string>> entries)
        {
            if (entries == null || entries.Count == 0)
            {
                RemoveBlock(lines, key);
                return;
            }

            var block = new List<string> { key + ":" };
            foreach (var entry in entries)
                block.Add("  " + SingleQuote(entry.Key) + ": " + entry.Value);
            ReplaceOrAppendBlock(lines, key, block);
        }

        /// <summary>
        /// 设置某个块内的字符串列表键（如 dns.nameserver）。
        /// 父块不存在时不做任何事（dns 段由 TunHelper 在需要时创建，这里不凭空造段）。
        /// </summary>
        public static void SetNestedList(List<string> lines, string parentKey, string childKey, IReadOnlyList<string> items)
        {
            int start = FindTopLevelKey(lines, parentKey);
            if (start < 0)
                return;

            int end = FindBlockEnd(lines, start);
            int childStart = -1;
            string indent = null;

            for (int i = start + 1; i < end; i++)
            {
                Match match = MatchNestedKey(lines[i], childKey);
                if (!match.Success)
                    continue;
                childStart = i;
                indent = match.Groups[1].Value;
                break;
            }

            bool hasItems = items != null && items.Count > 0;

            if (childStart < 0)
            {
                if (!hasItems)
                    return;
                string parentIndent = DetectChildIndent(lines, start, end) ?? "  ";
                int insertAt = FindNestedInsertIndex(lines, start, end);
                lines.InsertRange(insertAt, BuildListLines(childKey, items, parentIndent));
                return;
            }

            int childEnd = FindNestedBlockEnd(lines, childStart, indent.Length);
            lines.RemoveRange(childStart, childEnd - childStart);
            if (hasItems)
                lines.InsertRange(childStart, BuildListLines(childKey, items, indent));
        }

        /// <summary>设置某个块内的键值映射（如 dns.nameserver-policy）。</summary>
        public static void SetNestedMap(List<string> lines, string parentKey, string childKey,
            IReadOnlyList<KeyValuePair<string, string>> entries)
        {
            int start = FindTopLevelKey(lines, parentKey);
            if (start < 0)
                return;

            bool hasEntries = entries != null && entries.Count > 0;
            int end = FindBlockEnd(lines, start);
            int childStart = -1;
            string indent = null;

            for (int i = start + 1; i < end; i++)
            {
                Match match = MatchNestedKey(lines[i], childKey);
                if (!match.Success)
                    continue;
                childStart = i;
                indent = match.Groups[1].Value;
                break;
            }

            if (childStart < 0)
            {
                if (!hasEntries)
                    return;
                string parentIndent = DetectChildIndent(lines, start, end) ?? "  ";
                int insertAt = FindNestedInsertIndex(lines, start, end);
                lines.InsertRange(insertAt, BuildMapLines(childKey, entries, parentIndent));
                return;
            }

            int childEnd = FindNestedBlockEnd(lines, childStart, indent.Length);
            lines.RemoveRange(childStart, childEnd - childStart);
            if (hasEntries)
                lines.InsertRange(childStart, BuildMapLines(childKey, entries, indent));
        }

        /// <summary>
        /// 取出某个块内的子键原文（含其列表项/子块），用于覆盖前备份；不存在时返回 null。
        /// </summary>
        public static List<string> ExtractNestedBlock(List<string> lines, string parentKey, string childKey)
        {
            int start = FindTopLevelKey(lines, parentKey);
            if (start < 0)
                return null;

            int end = FindBlockEnd(lines, start);
            for (int i = start + 1; i < end; i++)
            {
                Match match = MatchNestedKey(lines[i], childKey);
                if (!match.Success)
                    continue;

                int childEnd = FindNestedBlockEnd(lines, i, match.Groups[1].Value.Length);
                var block = new List<string>();
                for (int j = i; j < childEnd; j++)
                    block.Add(lines[j]);

                while (block.Count > 1 && block[block.Count - 1].Trim().Length == 0)
                    block.RemoveAt(block.Count - 1);
                return block;
            }
            return null;
        }

        /// <summary>把备份的原文放回原位置；子键不存在时追加到父块末尾。</summary>
        public static void ReplaceNestedRaw(List<string> lines, string parentKey, string childKey, IReadOnlyList<string> block)
        {
            if (block == null || block.Count == 0)
                return;

            int start = FindTopLevelKey(lines, parentKey);
            if (start < 0)
            {
                AppendBlock(lines, block);
                return;
            }

            int end = FindBlockEnd(lines, start);
            for (int i = start + 1; i < end; i++)
            {
                Match match = MatchNestedKey(lines[i], childKey);
                if (!match.Success)
                    continue;

                int childEnd = FindNestedBlockEnd(lines, i, match.Groups[1].Value.Length);
                lines.RemoveRange(i, childEnd - i);
                lines.InsertRange(i, block);
                return;
            }

            int insertAt = FindNestedInsertIndex(lines, start, end);
            lines.InsertRange(insertAt, block);
        }

        /// <summary>删除某个块内的子键（含其列表项/子块），不存在则什么都不做。</summary>
        public static void RemoveNestedKey(List<string> lines, string parentKey, string childKey)
        {
            int start = FindTopLevelKey(lines, parentKey);
            if (start < 0)
                return;

            int end = FindBlockEnd(lines, start);
            for (int i = start + 1; i < end; i++)
            {
                Match match = MatchNestedKey(lines[i], childKey);
                if (!match.Success)
                    continue;
                int childEnd = FindNestedBlockEnd(lines, i, match.Groups[1].Value.Length);
                lines.RemoveRange(i, childEnd - i);
                return;
            }
        }

        /// <summary>
        /// 读出某个顶层块内的键值行（用于保留订阅自带、且不由客户端托管的内容）。
        /// </summary>
        public static List<KeyValuePair<string, string>> ReadMapEntries(List<string> lines, string key)
        {
            var result = new List<KeyValuePair<string, string>>();
            int start = FindTopLevelKey(lines, key);
            if (start < 0)
                return result;

            int end = FindBlockEnd(lines, start);
            for (int i = start + 1; i < end; i++)
            {
                Match match = MapEntryRegex.Match(lines[i]);
                if (match.Success)
                    result.Add(new KeyValuePair<string, string>(
                        match.Groups[1].Value.Trim(), match.Groups[2].Value.Trim()));
            }
            return result;
        }

        /// <summary>读出某个顶层块内的列表项（形如 "- xxx"）。</summary>
        public static List<string> ReadListItems(List<string> lines, string key)
        {
            var result = new List<string>();
            int start = FindTopLevelKey(lines, key);
            if (start < 0)
                return result;

            int end = FindBlockEnd(lines, start);
            for (int i = start + 1; i < end; i++)
            {
                Match match = ListItemRegex.Match(lines[i]);
                if (match.Success)
                    result.Add(Unquote(match.Groups[1].Value.Trim()));
            }
            return result;
        }

        /// <summary>去掉包裹字符串的单/双引号，用于比较托管项。</summary>
        public static string Unquote(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length < 2)
                return value;

            char first = value[0];
            char last = value[value.Length - 1];
            if (first == last && (first == '\'' || first == '"'))
            {
                string inner = value.Substring(1, value.Length - 2);
                return first == '\'' ? inner.Replace("''", "'") : inner.Replace("\\\"", "\"");
            }
            return value;
        }

        private static List<string> BuildMapLines(string key, IReadOnlyList<KeyValuePair<string, string>> entries, string indent)
        {
            var block = new List<string> { indent + key + ":" };
            foreach (var entry in entries)
                block.Add(indent + "  " + SingleQuote(entry.Key) + ": " + entry.Value);
            return block;
        }

        /// <summary>
        /// 把用户自定义的 YAML 片段合并进配置：按顶层键逐个替换同名块，不存在则追加。
        /// 片段内的缩进与注释原样保留；片段为空时不做任何修改。
        /// </summary>
        public static void ApplyFragment(List<string> lines, string fragment)
        {
            foreach (var block in SplitTopLevelBlocks(fragment))
                ReplaceOrAppendBlock(lines, block.Key, block.Lines);
        }

        internal sealed class FragmentBlock
        {
            public string Key;
            public List<string> Lines;
        }

        /// <summary>
        /// 把一段 YAML 文本按顶层键切成若干块。块之前的孤立行与注释会被丢弃，
        /// 块内的缩进行、列表项、注释全部保留（注释行不以顶层键形式匹配，自然落进当前块）。
        /// </summary>
        internal static List<FragmentBlock> SplitTopLevelBlocks(string fragment)
        {
            var result = new List<FragmentBlock>();
            if (string.IsNullOrWhiteSpace(fragment))
                return result;

            FragmentBlock current = null;
            foreach (string raw in fragment.Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.TrimEnd();
                string key = GetTopLevelKey(line);
                if (key != null)
                {
                    if (current != null)
                        result.Add(current);
                    current = new FragmentBlock { Key = key, Lines = new List<string> { line } };
                    continue;
                }

                if (current == null)
                    continue;
                current.Lines.Add(line);
            }

            if (current != null)
                result.Add(current);

            foreach (var block in result)
            {
                while (block.Lines.Count > 1 && block.Lines[block.Lines.Count - 1].Trim().Length == 0)
                    block.Lines.RemoveAt(block.Lines.Count - 1);
            }
            return result;
        }

        /// <summary>双引号包裹，转义反斜杠与引号。</summary>
        public static string Quote(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        /// <summary>单引号包裹，内部单引号按 YAML 规则写成两个单引号。</summary>
        public static string SingleQuote(string value)
        {
            return "'" + (value ?? string.Empty).Replace("'", "''") + "'";
        }

        private static List<string> BuildListLines(string key, IReadOnlyList<string> items, string indent)
        {
            var block = new List<string> { indent + key + ":" };
            foreach (string item in items)
                block.Add(indent + "  - " + Quote(item));
            return block;
        }

        private static Match MatchNestedKey(string line, string childKey)
        {
            if (line.Length == 0 || line[0] != ' ' && line[0] != '\t')
                return Match.Empty;
            return Regex.Match(line, @"^([ \t]+)" + Regex.Escape(childKey) + @"\s*:");
        }

        /// <summary>嵌套块结束位置：第一个缩进不深于起始键的非空行。</summary>
        private static int FindNestedBlockEnd(List<string> lines, int start, int indentLength)
        {
            for (int i = start + 1; i < lines.Count; i++)
            {
                string line = lines[i];
                if (line.Trim().Length == 0)
                    continue;

                int indent = line.Length - line.TrimStart(' ', '\t').Length;
                if (indent <= indentLength)
                    return i;
            }
            return lines.Count;
        }

        /// <summary>取父块内第一个子键的缩进，用于新插入子块时对齐。</summary>
        private static string DetectChildIndent(List<string> lines, int start, int end)
        {
            for (int i = start + 1; i < end; i++)
            {
                string line = lines[i];
                if (line.Trim().Length == 0 || line.TrimStart().StartsWith("#"))
                    continue;

                int indent = line.Length - line.TrimStart(' ', '\t').Length;
                if (indent > 0)
                    return line.Substring(0, indent);
            }
            return null;
        }

        /// <summary>
        /// 嵌套块的插入位置：父块最后一个非空行之后。
        /// 直接插在 end 处会把新子键排到父块尾部空行之后，再次替换时位置又会变化，破坏幂等。
        /// </summary>
        private static int FindNestedInsertIndex(List<string> lines, int start, int end)
        {
            int index = end;
            while (index > start + 1 && lines[index - 1].Trim().Length == 0)
                index--;
            return index;
        }
    }
}
