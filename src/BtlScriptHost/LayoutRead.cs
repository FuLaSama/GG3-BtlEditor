using System.Globalization;
using BtlCore.Front;

namespace BtlCore.Scripting
{
    /// <summary>按页面 bind 读一个输入框。不创建节点。</summary>
    public static class LayoutRead
    {
        public static object Value(BtlFrontDocument doc, string sectionBind, string fieldBind, int? index, int? cell, string widget, int? shift, int? width)
        {
            string path = Compose(sectionBind, fieldBind, index, cell);
            if (path == null) return null;
            bool check = widget == "check";
            object raw = NsPath.Look(doc, path, check);
            if (raw == null || raw is bool || width == null) return raw;
            if (!TryInt(raw, out long n)) return null;
            int bits = width.Value;
            long mask = bits >= 63 ? long.MaxValue : (1L << bits) - 1;
            return (n >> (shift ?? 0)) & mask;
        }

        public static string Compose(string sectionBind, string fieldBind, int? index, int? cell)
        {
            if (string.IsNullOrEmpty(fieldBind)) return null;
            if (fieldBind.IndexOf('{') >= 0)
            {
                if (fieldBind.Contains("{index}") && index == null) return null;
                if (fieldBind.Contains("{cell}") && cell == null) return null;
                string path = fieldBind;
                if (index != null) path = path.Replace("{index}", index.Value.ToString(CultureInfo.InvariantCulture));
                if (cell != null) path = path.Replace("{cell}", cell.Value.ToString(CultureInfo.InvariantCulture));
                return path;
            }
            if (string.IsNullOrEmpty(sectionBind) && NsPath.IsNs(fieldBind)) return fieldBind;
            if (!NsPath.IsNs(sectionBind) || !NsPath.IsNs(fieldBind) || index == null) return null;
            return sectionBind + "[" + index.Value.ToString(CultureInfo.InvariantCulture) + "]" + fieldBind;
        }

        static bool TryInt(object raw, out long n)
        {
            n = 0;
            try
            {
                n = Convert.ToInt64(raw, CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception ex) when (ex is FormatException || ex is InvalidCastException || ex is OverflowException)
            {
                return false;
            }
        }
    }
}
