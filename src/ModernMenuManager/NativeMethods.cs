using System;
using System.Runtime.InteropServices;
using System.Text;

namespace ModernMenuManager
{
    static class NativeMethods
    {
        public const uint CLSCTX_LOCAL_SERVER = 0x4;
        public const uint COINIT_MULTITHREADED = 0x0;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetStagedPackagePathByFullName(string packageFullName, ref uint pathLength, StringBuilder path);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetPackagePathByFullName(string packageFullName, ref uint pathLength, StringBuilder path);

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        public static extern int SHLoadIndirectString(string pszSource, StringBuilder pszOutBuf, int cchOutBuf, IntPtr ppvReserved);

        [DllImport("ole32.dll")]
        public static extern int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);

        [DllImport("ole32.dll")]
        public static extern int CoCreateInstance(ref Guid rclsid, IntPtr pUnkOuter, uint dwClsContext, ref Guid riid,
                                                  [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
    }

    [ComImport, Guid("a08ce4d0-fa25-44ab-b57c-c7b1c323e0b9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IExplorerCommand
    {
        [PreserveSig] int GetTitle(IntPtr psiItemArray, out IntPtr ppszName);
        [PreserveSig] int GetIcon(IntPtr psiItemArray, out IntPtr ppszIcon);
        [PreserveSig] int GetToolTip(IntPtr psiItemArray, out IntPtr ppszInfotip);
        [PreserveSig] int GetCanonicalName(out Guid pguidCommandName);
        [PreserveSig] int GetState(IntPtr psiItemArray, [MarshalAs(UnmanagedType.Bool)] bool fOkToBeSlow, out uint pCmdState);
        [PreserveSig] int Invoke(IntPtr psiItemArray, IntPtr pbc);
        [PreserveSig] int GetFlags(out uint pFlags);
        [PreserveSig] int EnumSubCommands(out IntPtr ppEnum);
    }
}
