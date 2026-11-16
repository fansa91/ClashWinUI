using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace ClashDotNetFramework.Utils
{
    /// <summary>
    /// 为 WinUI 3 窗口设置最小尺寸。
    /// AppWindow 没有提供 MinSize，拖拽缩放只能通过拦截 WM_GETMINMAXINFO 实现；
    /// 否则窗口可以被拖到极窄，设置页的标签列会被压缩到不可见（只剩一堆无说明的开关）。
    /// </summary>
    public static class WindowSizing
    {
        private const int WM_GETMINMAXINFO = 0x0024;
        private const int BASE_DPI = 96;

        private delegate IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData);

        [DllImport("comctl32.dll", SetLastError = true)]
        private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, IntPtr uIdSubclass, IntPtr dwRefData);

        [DllImport("comctl32.dll")]
        private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hWnd);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT Reserved;
            public POINT MaxSize;
            public POINT MaxPosition;
            public POINT MinTrackSize;
            public POINT MaxTrackSize;
        }

        // 委托与阈值必须由静态字段持有：委托被 GC 回收后回调会导致进程崩溃
        private static SubclassProc _proc;
        private static int _minWidth;
        private static int _minHeight;
        private static bool _applied;

        /// <summary>
        /// 以设备无关像素指定最小窗口尺寸（会按当前 DPI 换算为物理像素）。
        /// </summary>
        public static void ApplyMinimumSize(Window window, int minWidth, int minHeight)
        {
            if (_applied || window == null)
                return;

            try
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                if (hwnd == IntPtr.Zero)
                    return;

                _minWidth = minWidth;
                _minHeight = minHeight;
                _proc = OnSubclass;
                _applied = SetWindowSubclass(hwnd, _proc, (IntPtr)1, IntPtr.Zero);
            }
            catch
            {
                // 拿不到窗口句柄时忽略：只是失去最小尺寸约束，不影响其他功能
            }
        }

        private static IntPtr OnSubclass(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData)
        {
            if (uMsg == WM_GETMINMAXINFO && lParam != IntPtr.Zero)
            {
                try
                {
                    double scale = 1.0;
                    var dpi = GetDpiForWindow(hWnd);
                    if (dpi > 0)
                        scale = (double)dpi / BASE_DPI;

                    var info = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                    info.MinTrackSize.X = (int)Math.Round(_minWidth * scale);
                    info.MinTrackSize.Y = (int)Math.Round(_minHeight * scale);
                    Marshal.StructureToPtr(info, lParam, false);
                }
                catch
                {
                }
            }

            return DefSubclassProc(hWnd, uMsg, wParam, lParam);
        }
    }
}
