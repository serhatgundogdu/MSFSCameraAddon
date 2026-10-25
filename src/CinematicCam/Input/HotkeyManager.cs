using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace CinematicCam.Input;

/// <summary>
/// Sistem genelinde kısayol tuşları (RegisterHotKey). MSFS penceresi odaktayken de çalışır.
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x1, ModCtrl = 0x2, ModShift = 0x4, ModWin = 0x8;

    private readonly HwndSource _source;
    private readonly Dictionary<int, string> _actions = new();
    private int _nextId = 0x5000;

    public event Action<string>? Pressed;

    public HotkeyManager(IntPtr hwnd)
    {
        _source = HwndSource.FromHwnd(hwnd) ?? throw new InvalidOperationException("Pencere tanıtıcısı yok.");
        _source.AddHook(WndProc);
    }

    /// <summary>Tüm kısayolları kaydeder; kaydedilemeyenleri (başka uygulama kullanıyorsa) döndürür.</summary>
    public List<string> RegisterAll(IReadOnlyDictionary<string, string> bindings)
    {
        UnregisterAll();
        var failed = new List<string>();
        foreach (var (action, text) in bindings)
        {
            if (string.IsNullOrWhiteSpace(text)) continue;
            if (!TryParse(text, out uint mods, out uint vk) || !RegisterHotKey(_source.Handle, _nextId, mods, vk))
            {
                failed.Add($"{action} ({text})");
                continue;
            }
            _actions[_nextId++] = action;
        }
        return failed;
    }

    public void UnregisterAll()
    {
        foreach (var id in _actions.Keys) UnregisterHotKey(_source.Handle, id);
        _actions.Clear();
    }

    public static bool TryParse(string text, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;
        Key? key = null;
        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl": case "control": modifiers |= ModCtrl; break;
                case "alt": modifiers |= ModAlt; break;
                case "shift": modifiers |= ModShift; break;
                case "win": modifiers |= ModWin; break;
                default:
                    if (!Enum.TryParse<Key>(part, true, out var k)) return false;
                    key = k;
                    break;
            }
        }
        if (key == null) return false;
        virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key.Value);
        return virtualKey != 0;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            Pressed?.Invoke(action);
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.RemoveHook(WndProc);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
