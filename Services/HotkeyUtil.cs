using System.Windows.Input;

namespace MoyuCast.Services;

public static class HotkeyUtil
{
    public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8;

    private static readonly Dictionary<string, uint> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["`"] = 0xC0, ["~"] = 0xC0,
        ["-"] = 0xBD, ["="] = 0xBB,
        ["["] = 0xDB, ["]"] = 0xDD, ["\\"] = 0xDC,
        [";"] = 0xBA, ["'"] = 0xDE,
        [","] = 0xBC, ["."] = 0xBE, ["/"] = 0xBF,
        ["Space"] = 0x20, ["Enter"] = 0x0D, ["Tab"] = 0x09,
        ["Esc"] = 0x1B, ["Escape"] = 0x1B,
        ["Insert"] = 0x2D, ["Delete"] = 0x2E,
        ["Home"] = 0x24, ["End"] = 0x23,
        ["PageUp"] = 0x21, ["PageDown"] = 0x22,
        ["Left"] = 0x25, ["Up"] = 0x26, ["Right"] = 0x27, ["Down"] = 0x28,
    };

    public static bool TryParse(string text, out uint mods, out uint vk)
    {
        mods = 0; vk = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        foreach (var raw in text.Split('+'))
        {
            var p = raw.Trim();
            if (p.Length == 0) continue;

            if (p.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)) { mods |= MOD_CONTROL; continue; }
            if (p.Equals("Alt", StringComparison.OrdinalIgnoreCase)) { mods |= MOD_ALT; continue; }
            if (p.Equals("Shift", StringComparison.OrdinalIgnoreCase)) { mods |= MOD_SHIFT; continue; }
            if (p.Equals("Win", StringComparison.OrdinalIgnoreCase)) { mods |= MOD_WIN; continue; }

            if (Named.TryGetValue(p, out var nv)) { vk = nv; continue; }

            if (p.Length >= 2 && (p[0] == 'F' || p[0] == 'f') && int.TryParse(p.Substring(1), out var fn) && fn >= 1 && fn <= 24)
            { vk = (uint)(0x6F + fn); continue; }

            if (p.Length == 1)
            {
                char c = char.ToUpperInvariant(p[0]);
                vk = c;
                continue;
            }

            // ����ӳ�� WPF Key ����
            if (Enum.TryParse<Key>(p, true, out var key))
            {
                vk = (uint)KeyInterop.VirtualKeyFromKey(key);
                continue;
            }
            return false;
        }
        return mods != 0 && vk != 0;
    }

    public static string Describe(ModifierKeys mods, Key key, out uint outMods, out uint outVk)
    {
        outMods = 0;
        if ((mods & ModifierKeys.Control) != 0) outMods |= MOD_CONTROL;
        if ((mods & ModifierKeys.Alt) != 0) outMods |= MOD_ALT;
        if ((mods & ModifierKeys.Shift) != 0) outMods |= MOD_SHIFT;
        if ((mods & ModifierKeys.Windows) != 0) outMods |= MOD_WIN;

        outVk = (uint)KeyInterop.VirtualKeyFromKey(key);

        var parts = new List<string>();
        if ((outMods & MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((outMods & MOD_ALT) != 0) parts.Add("Alt");
        if ((outMods & MOD_SHIFT) != 0) parts.Add("Shift");
        if ((outMods & MOD_WIN) != 0) parts.Add("Win");
        parts.Add(KeyName(key, outVk));
        return string.Join("+", parts);
    }

    private static string KeyName(Key key, uint vk)
    {
        switch (key)
        {
            case Key.OemTilde: return "`";
            case Key.OemMinus: return "-";
            case Key.OemPlus: return "=";
            case Key.OemOpenBrackets: return "[";
            case Key.OemCloseBrackets: return "]";
            case Key.OemPipe: return "\\";
            case Key.OemSemicolon: return ";";
            case Key.OemQuotes: return "'";
            case Key.OemComma: return ",";
            case Key.OemPeriod: return ".";
            case Key.OemQuestion: return "/";
            case Key.Space: return "Space";
        }
        if (key >= Key.D0 && key <= Key.D9) return ((char)('0' + (key - Key.D0))).ToString();
        if (key >= Key.NumPad0 && key <= Key.NumPad9) return "Num" + (key - Key.NumPad0);
        return key.ToString();
    }
}
