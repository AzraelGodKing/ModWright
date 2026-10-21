using System.Runtime.InteropServices;

namespace SteamCmdHelper;

/// <summary>
/// The list on the taskbar button's right-click menu: "Play RimWorld" and "Play &lt;instance&gt;", each starting this program
/// with --play so the game starts without opening the window. Windows' ICustomDestinationList, used directly so no
/// extra library is needed; anything going wrong just leaves the list as it was.
/// </summary>
public static class JumpList
{
    public static void Set(IReadOnlyList<(string Title, string Args, string Tip)> tasks)
    {
        if (!OperatingSystem.IsWindows() || Environment.ProcessPath is not { } exe) return;
        try
        {
            var list = (ICustomDestinationList)new DestinationList();
            list.BeginList(out _, typeof(IObjectArray).GUID, out _);
            var col = (IObjectCollection)new EnumerableObjectCollection();
            foreach (var (title, args, tip) in tasks)
            {
                var link = (IShellLinkW)new ShellLink();
                link.SetPath(exe);
                link.SetArguments(args);
                link.SetDescription(tip);
                link.SetIconLocation(exe, 0);
                var props = (IPropertyStore)link;
                var key = new PropertyKey(new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9"), 2); // PKEY_Title
                var value = new PropVariant(title);
                props.SetValue(ref key, ref value);
                props.Commit();
                value.Clear();
                col.AddObject(link);
            }
            if (tasks.Count > 0) list.AddUserTasks((IObjectArray)col);
            list.CommitList();
        }
        catch { /* no jump lists here (older shell, policy): nothing to show */ }
    }

    [ComImport, Guid("77F10CF0-3DB5-4966-B520-B7C54FD35ED6")] class DestinationList { }
    [ComImport, Guid("2D3468C1-36A7-43B6-AC24-D3F02FD9607A")] class EnumerableObjectCollection { }
    [ComImport, Guid("00021401-0000-0000-C000-000000000046")] class ShellLink { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("6332DEBF-87B5-4670-90C0-5E57B408A49E")]
    interface ICustomDestinationList
    {
        void SetAppID([MarshalAs(UnmanagedType.LPWStr)] string pszAppID);
        void BeginList(out uint pcMaxSlots, [In] ref Guid riid, [Out, MarshalAs(UnmanagedType.Interface)] out object ppv);
        void AppendCategory([MarshalAs(UnmanagedType.LPWStr)] string pszCategory, [MarshalAs(UnmanagedType.Interface)] IObjectArray poa);
        void AppendKnownCategory(int category);
        void AddUserTasks([MarshalAs(UnmanagedType.Interface)] IObjectArray poa);
        void CommitList();
        void GetRemovedDestinations([In] ref Guid riid, [Out, MarshalAs(UnmanagedType.Interface)] out object ppv);
        void DeleteList([MarshalAs(UnmanagedType.LPWStr)] string pszAppID);
        void AbortList();
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9")]
    interface IObjectArray
    {
        void GetCount(out uint pcObjects);
        void GetAt(uint uiIndex, [In] ref Guid riid, [Out, MarshalAs(UnmanagedType.Interface)] out object ppv);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("5632B1A4-E38A-400A-928A-D4CD63230295")]
    interface IObjectCollection
    {
        void GetCount(out uint pcObjects);
        void GetAt(uint uiIndex, [In] ref Guid riid, [Out, MarshalAs(UnmanagedType.Interface)] out object ppv);
        void AddObject([MarshalAs(UnmanagedType.Interface)] object pvObject);
        void AddFromArray([MarshalAs(UnmanagedType.Interface)] IObjectArray poaSource);
        void RemoveObject(uint uiIndex);
        void Clear();
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    interface IPropertyStore
    {
        void GetCount(out uint cProps);
        void GetAt(uint iProp, out PropertyKey pkey);
        void GetValue([In] ref PropertyKey key, out PropVariant pv);
        void SetValue([In] ref PropertyKey key, [In] ref PropVariant pv);
        void Commit();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    struct PropertyKey
    {
        public Guid FormatId;
        public uint PropertyId;
        public PropertyKey(Guid f, uint p) { FormatId = f; PropertyId = p; }
    }

    [StructLayout(LayoutKind.Explicit)]
    struct PropVariant
    {
        [FieldOffset(0)] ushort _vt;
        [FieldOffset(8)] IntPtr _ptr;

        public PropVariant(string value)
        {
            _vt = 31; // VT_LPWSTR
            _ptr = Marshal.StringToCoTaskMemUni(value);
        }

        public void Clear()
        {
            if (_ptr != IntPtr.Zero) Marshal.FreeCoTaskMem(_ptr);
            _ptr = IntPtr.Zero;
            _vt = 0;
        }
    }
}
