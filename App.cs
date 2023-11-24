using System.Text;
using System.Text.RegularExpressions;

namespace GIIDX
{
    static class App
    {
        const string PATH = "C:\\Program Files (x86)\\Windows Kits\\10\\Include\\10.0.22621.0";

        static Regex MainPattern = new Regex("uuid\\([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\\)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        const int MainPatternValueLength = 42; // Example: uuid(AEC22FB8-76F3-4639-9BE0-28EB43A67A2E)

        static Regex ValuePattern = new Regex("[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        const int ValuePatternValueLength = 36; // Example: AEC22FB8-76F3-4639-9BE0-28EB43A67A2E

        static bool IsSpace(string content, int start)
        {
            return content[start] == ' ' || content[start] == '\t' || content[start] == '\r' || content[start] == '\n';
        }

        static bool IsAlphaNumeric(string content, int start)
        {
            var c = content[start];

            return (c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
        }

        static bool IsUnderScode(string content, int start)
        {
            return content[start] == '_';
        }

        static int SkipSpaces(string content, int start, int end)
        {
            var current = start;

            for (; current < end; current++) { if (!IsSpace(content, current)) { break;} }

            return current;
        }

        static bool IsComment(string content, int start, int end)
        {
            if (start != end && (start + 1) != end && content[start] == '/' && content[start + 1] == '*') { return true; }

            if (start != end && (start + 1) != end && content[start] == '/' && content[start + 1] == '/') { return true; }

            return false;
        }

        static bool IsAnonymous(string token) { return token == "{"; }

        static bool AcquireAnonymousTypeName(string content, int start, int end, out int indx, out string token)
        {
            while (AcquireToken(content, start, end, out indx, out token))
            {
                if (token == "}")
                {
                    return AcquireToken(content, indx + token.Length, end, out indx, out token);
                }
                else if (token.StartsWith("}"))
                {
                    indx = indx + 1;

                    token = token.Substring(1);

                    return true;
                }

                start = indx + token.Length;
            }

            indx = -1;
            token = string.Empty;

            return false;
        }

        static bool AcquireToken(string content, int start, int end, out int indx, out string token)
        {
            // Skip leading spaces
            var current = SkipSpaces(content, start, end);

            if (current == end) { indx = -1; token = string.Empty; return false; }

            while (IsComment(content, current, end))
            {
                // Skip /* ... */ comments

                while (current != end && (current + 1) != end)
                {
                    if (content[current] == '/' && content[current + 1] == '*')
                    {
                        // Skip all the way to after */
                        for (; current < end && (current + 1) < end; current++)
                        {
                            if (content[current] == '*' && content[current + 1] == '/') { current = current + 2; break; }
                        }
                    }
                    else { break; }
                }

                if (current == end) { indx = -1; token = string.Empty; return false; }

                current = SkipSpaces(content, current, end);

                // Skip // ... comments

                while (current != end && (current + 1) != end)
                {
                    if (content[current] == '/' && content[current + 1] == '/')
                    {
                        // Skip all the way to end of line
                        for (; current < end && (current + 1) < end; current++)
                        {
                            if (content[current] == '\r' || content[current + 1] == '\n') { current = current + 1; break; }
                        }
                    }
                    else { break; }
                }

                current = SkipSpaces(content, current, end);

                if (current == end) { indx = -1; token = string.Empty; return false; }
            }

            current = SkipSpaces(content, current, end);

            if (current == end) { indx = -1; token = string.Empty; return false; }

            indx = current;
            var sb = new StringBuilder(end - current);

            for (var x = current; x < end; x++)
            {
                if (IsSpace(content, x)) { break; }

                if (sb.Length == 0 && content[x] == '\"') { sb.Append(content[x]); break; }

                if (sb.Length != 0 && !(IsAlphaNumeric(content, x) || IsUnderScode(content, x))) { break; }

                sb.Append(content[x]);
            }

            token = sb.ToString();

            // Check for anonymous entities.
            if (IsAnonymous(token))
            {
                // Example:
                // typedef [uuid(8fb5f0ce-dfdd-4f0a-85b9-8988d8dd8ff2)] enum
                // { 
                //    TF_LBI_CLK_RIGHT       = 1, 
                //    TF_LBI_CLK_LEFT        = 2, 
                // } TfLBIClick;

                if (!AcquireAnonymousTypeName(content, current + token.Length, end, out indx, out token)) { return false; }
            }

            return true;
        }

        static bool SkipString(string content, int start, int end, out int indx)
        {
            char prev = ' ';

            for (var x = start; x < end; x++)
            {
                if (content[x] == '\"' && prev != '\\')
                {
                    indx = x + 1;

                    return true;
                }

                prev = content[x];
            }

            indx = -1;

            return false;
        }

        static void ProcessContent(string content, string uid, int start, int end)
        {
            int indx = -1;
            string token = string.Empty;

            while (AcquireToken(content, start, end, out indx, out token))
            {
                switch (token)
                {
                    case "\"":
                        {
                            // Skip strings ...

                            int idx = -1;

                            if (SkipString(content, indx + token.Length, end, out idx)) { start = idx; break; }

                            return;
                        }
                    case "class":
                    case "coclass":
                    case "enum":
                    case "interface":
                    case "struct":
                        {
                            var idx = -1;
                            var name = string.Empty;

                            if (AcquireToken(content, indx + token.Length, end, out idx, out name))
                            {
                                Console.WriteLine("{0} {1}", uid, name);

                                return;
                            }

                            break;
                        }
                    case "library":
                    default: { start = indx + token.Length; break; }
                }
            }
        }

        static void ProcessFile(string file)
        {
            var content = File.ReadAllText(file);

            var items = MainPattern.Matches(content).Where(m => m.Success)
                .Select(m => new KeyValuePair<int, string>(m.Index, m.Value)).ToArray();

            for (var x = 0; x < items.Length; x++)
            {
                ProcessContent(content, ValuePattern.Match(items[x].Value).Value.ToUpper(),
                    items[x].Key, (x + 1) == items.Length ? content.Length : items[x + 1].Key);
            }
        }

        static void Main()
        {
            var files = Directory.GetFiles(PATH, "*.idl", SearchOption.AllDirectories);

            for (var x = 0; x < files.Length; x++) { ProcessFile(files[x]); }
        }
    }
}