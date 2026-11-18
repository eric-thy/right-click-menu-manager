using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace Cmm
{
    internal static class ExplorerCommandProbe
    {
        private static readonly Guid IidExplorerCommand = new Guid("A08CE4D0-FA25-44AB-B57C-C7B1C323E0B9");
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, string> Cache =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> Failed =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static int _batchDeadline;

        [ComImport]
        [Guid("A08CE4D0-FA25-44AB-B57C-C7B1C323E0B9")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IExplorerCommand
        {
            [PreserveSig] int GetTitle(IntPtr psiItemArray, out IntPtr ppszName);
            [PreserveSig] int GetIcon(IntPtr psiItemArray, out IntPtr ppszIcon);
            [PreserveSig] int GetToolTip(IntPtr psiItemArray, out IntPtr ppszInfotip);
            [PreserveSig] int GetCanonicalName(out Guid pguidCommandName);
            [PreserveSig] int GetState(IntPtr psiItemArray, bool fOkToBeSlow, out uint pCmdState);
            [PreserveSig] int Invoke(IntPtr psiItemArray, IntPtr pbc);
            [PreserveSig] int GetFlags(out uint pFlags);
            [PreserveSig] int EnumSubCommands(out IntPtr ppEnum);
        }

        private class ProbeResult
        {
            public string Title;
            public string Error;
        }

        [DllImport("ole32.dll")]
        private static extern int CoInitializeEx(IntPtr pvReserved, uint coInit);

        [DllImport("ole32.dll")]
        private static extern void CoUninitialize();

        [DllImport("ole32.dll")]
        private static extern int CoCreateInstance(ref Guid rclsid, IntPtr pUnkOuter, uint dwClsContext, ref Guid riid, out IntPtr ppv);

        public static void BeginBatch(int milliseconds)
        {
            _batchDeadline = Environment.TickCount + Math.Max(100, milliseconds);
        }

        public static string ProbeTitle(string clsid)
        {
            if (string.IsNullOrWhiteSpace(clsid)) return null;
            Guid clsidValue;
            if (!Guid.TryParse(clsid, out clsidValue)) return null;
            string key = clsidValue.ToString("B").ToUpperInvariant();
            lock (Sync)
            {
                string cached;
                if (Cache.TryGetValue(key, out cached)) return cached;
                if (Failed.Contains(key)) return null;
            }
            int remaining = _batchDeadline - Environment.TickCount;
            if (_batchDeadline != 0 && remaining <= 0) return null;

            var result = new ProbeResult();
            Thread thread = new Thread(delegate()
            {
                try
                {
                    result.Title = ProbeOnSta(clsidValue);
                }
                catch (Exception ex)
                {
                    result.Error = ex.Message;
                }
            });
            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Name = "CmmExplorerCommandProbe";
            thread.Start();
            int wait = _batchDeadline == 0 ? 1800 : Math.Min(800, remaining);
            if (!thread.Join(wait))
            {
                lock (Sync) { Failed.Add(key); }
                return null;
            }

            lock (Sync)
            {
                if (!string.IsNullOrEmpty(result.Title))
                    Cache[key] = result.Title;
                else
                    Failed.Add(key);
            }
            return result.Title;
        }

        private static string ProbeOnSta(Guid clsid)
        {
            int init = CoInitializeEx(IntPtr.Zero, 0x2); // COINIT_APARTMENTTHREADED
            bool initialized = init >= 0;
            IntPtr pointer = IntPtr.Zero;
            object comObject = null;
            try
            {
                Guid iid = IidExplorerCommand;
                int hr = CoCreateInstance(ref clsid, IntPtr.Zero, 0x17, ref iid, out pointer);
                if (hr != 0 || pointer == IntPtr.Zero) return null;
                comObject = Marshal.GetObjectForIUnknown(pointer);
                IExplorerCommand command = comObject as IExplorerCommand;
                if (command == null) return null;
                IntPtr titlePointer = IntPtr.Zero;
                try
                {
                    int titleHr = command.GetTitle(IntPtr.Zero, out titlePointer);
                    if (titleHr != 0 || titlePointer == IntPtr.Zero) return null;
                    string title = Marshal.PtrToStringUni(titlePointer);
                    if (string.IsNullOrWhiteSpace(title)) return null;
                    return title.Trim();
                }
                finally
                {
                    if (titlePointer != IntPtr.Zero) Marshal.FreeCoTaskMem(titlePointer);
                }
            }
            finally
            {
                if (comObject != null && Marshal.IsComObject(comObject))
                {
                    try { Marshal.ReleaseComObject(comObject); }
                    catch { }
                }
                if (pointer != IntPtr.Zero)
                {
                    try { Marshal.Release(pointer); }
                    catch { }
                }
                if (initialized) CoUninitialize();
            }
        }
    }
}
