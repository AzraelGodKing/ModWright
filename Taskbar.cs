using System.Runtime.InteropServices;

namespace SteamCmdHelper;

/// <summary>Upload progress on the window's taskbar button, and a flash when a long run ends in the background.</summary>
public static class Taskbar
{
    public enum State { None = 0, Indeterminate = 1, Normal = 2, Error = 4, Paused = 8 }

    static ITaskbarList3? _list;
    static bool _failed;

    static ITaskbarList3? List()
    {
        if (_list != null || _failed) return _list;
        try
        {
            var l = (ITaskbarList3)new TaskbarInstance();
            l.HrInit();
            _list = l;
        }
        catch { _failed = true; } // no taskbar (Server Core, some remote sessions): progress just isn't shown there
        return _list;
    }

    /// <summary>Shows <paramref name="percent"/> on the button in the given state; <see cref="State.None"/> clears it.</summary>
    public static void Progress(Form f, int percent, State state = State.Normal)
    {
        if (!f.IsHandleCreated || List() is not { } l) return;
        try
        {
            l.SetProgressState(f.Handle, (int)state);
            if (state is State.Normal or State.Error or State.Paused) l.SetProgressValue(f.Handle, (ulong)Math.Clamp(percent, 0, 100), 100);
        }
        catch { /* cosmetic only */ }
    }

    /// <summary>Flashes the taskbar button until the window is brought to the front, unless it already is.</summary>
    public static void FlashIfInBackground(Form f)
    {
        if (!f.IsHandleCreated || Form.ActiveForm == f) return;
        var info = new FlashInfo
        {
            Size = (uint)Marshal.SizeOf<FlashInfo>(),
            Hwnd = f.Handle,
            Flags = 0x2 | 0xC, // FLASHW_TRAY | FLASHW_TIMERNOFG
            Count = uint.MaxValue,
        };
        try { FlashWindowEx(ref info); } catch { }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct FlashInfo
    {
        public uint Size;
        public IntPtr Hwnd;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [DllImport("user32.dll")]
    static extern bool FlashWindowEx(ref FlashInfo info);

    [ComImport, Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ITaskbarList3
    {
        // ITaskbarList
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);
        // ITaskbarList2
        void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
        // ITaskbarList3 (only the two used here; the rest of its methods come after them)
        void SetProgressValue(IntPtr hwnd, ulong completed, ulong total);
        void SetProgressState(IntPtr hwnd, int state);
    }

    [ComImport, Guid("56FDF344-FD6D-11d0-958A-006097C9A090"), ClassInterface(ClassInterfaceType.None)]
    class TaskbarInstance { }
}
