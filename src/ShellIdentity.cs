using System;
using System.Runtime.InteropServices;
using System.Text;

namespace ZaloManagerPoc
{
    public static class ShellIdentity
    {
        [ComImport, Guid("00021401-0000-0000-C000-000000000046")] private class ShellLink { }
        [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int size, IntPtr data, uint flags);
            void GetIDList(out IntPtr list);
            void SetIDList(IntPtr list);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int size);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string text);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int size);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string path);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int size);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string text);
            void GetHotkey(out ushort key);
            void SetHotkey(ushort key);
            void GetShowCmd(out int command);
            void SetShowCmd(int command);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int size, out int index);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
            void Resolve(IntPtr window, uint flags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
        }
        public static void SaveShortcut(string path, Profile profile, string icon)
        {
            var link = (IShellLinkW)new ShellLink();
            try
            {
                link.SetPath(profile.Executable); link.SetWorkingDirectory(System.IO.Path.GetDirectoryName(profile.Executable));
                link.SetDescription(profile.Name); link.SetIconLocation(icon, 0);
                ((System.Runtime.InteropServices.ComTypes.IPersistFile)link).Save(path, true);
            }
            finally { Marshal.ReleaseComObject(link); }
            Shortcut(path, profile);
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct PropertyKey { public Guid Format; public uint Id; }
        [StructLayout(LayoutKind.Explicit, Size = 24)]
        private struct PropVariant { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public IntPtr Text; }
        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore
        {
            void GetCount(out uint count);
            void GetAt(uint index, out PropertyKey key);
            void GetValue(ref PropertyKey key, out PropVariant value);
            void SetValue(ref PropertyKey key, ref PropVariant value);
            void Commit();
        }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHGetPropertyStoreFromParsingName(string path, IntPtr bind, uint flags, ref Guid iid, out IPropertyStore store);
        [DllImport("shell32.dll", PreserveSig = false)]
        private static extern void SHGetPropertyStoreForWindow(IntPtr window, ref Guid iid, out IPropertyStore store);
        [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern void SHChangeNotify(uint change, uint flags, string path, IntPtr extra);
        public static void NotifyShortcut(string path) { SHChangeNotify(0x2000, 0x0005, path, IntPtr.Zero); }
        public static bool IsProfileShortcut(string path, Profile profile)
        {
            Guid iid = typeof(IPropertyStore).GUID; IPropertyStore store;
            SHGetPropertyStoreFromParsingName(path, IntPtr.Zero, 0, ref iid, out store);
            try
            {
                var key = new PropertyKey { Format = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), Id = 5 };
                PropVariant value; store.GetValue(ref key, out value);
                try { return value.Type == 31 && Marshal.PtrToStringUni(value.Text) == AppId(profile); }
                finally { PropVariantClear(ref value); }
            }
            finally { Marshal.ReleaseComObject(store); }
        }
        private static void Write(IPropertyStore store, uint id, string text)
        {
            var key = new PropertyKey { Format = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), Id = id };
            var value = new PropVariant { Type = 31, Text = Marshal.StringToCoTaskMemUni(text) };
            try { store.SetValue(ref key, ref value); }
            finally { Marshal.FreeCoTaskMem(value.Text); }
        }
        private static string AppId(Profile profile) { return profile.IsOriginal ? "com.vng.zalo" : "zmgr.p" + profile.Id; }
        public static void Shortcut(string path, Profile profile)
        {
            Guid iid = typeof(IPropertyStore).GUID; IPropertyStore store;
            SHGetPropertyStoreFromParsingName(path, IntPtr.Zero, 2, ref iid, out store);
            try { Write(store, 5, AppId(profile)); store.Commit(); }
            finally { Marshal.ReleaseComObject(store); }
        }
        public static void Window(IntPtr window, Profile profile, string iconPath)
        {
            Guid iid = typeof(IPropertyStore).GUID; IPropertyStore store;
            SHGetPropertyStoreForWindow(window, ref iid, out store);
            try
            {
                Write(store, 2, "\"" + profile.Executable + "\"");
                Write(store, 3, iconPath + ",0"); Write(store, 4, profile.Name); Write(store, 5, AppId(profile)); store.Commit();
            }
            finally { Marshal.ReleaseComObject(store); }
        }
    }
}
