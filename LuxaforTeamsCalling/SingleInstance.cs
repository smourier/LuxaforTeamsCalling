using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace LuxaforTeamsCalling
{
    public sealed class SingleInstance
    {
        private const int HWND_BROADCAST = 0xFFFF;

        [DllImport("user32")]
        private static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam);

        [DllImport("user32", CharSet = CharSet.Unicode)]
        private static extern int RegisterWindowMessage(string message);

        [DllImport("user32")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        public SingleInstance(string uniqueName)
        {
            if (uniqueName == null)
                throw new ArgumentNullException(nameof(uniqueName));

            Mutex = new Mutex(true, uniqueName);
            Message = RegisterWindowMessage("WM_" + uniqueName);
        }

        public Mutex Mutex { get; }
        public int Message { get; }

        // NOTE: if this always return false, close & restart Visual Studio
        // this is probably due to the vshost.exe thing
        public void RunFirstInstance(Action action) => RunFirstInstance(action, IntPtr.Zero, IntPtr.Zero);
        public void RunFirstInstance(Action action, IntPtr wParam) => RunFirstInstance(action, wParam, IntPtr.Zero);
        public void RunFirstInstance(Action action, IntPtr wParam, IntPtr lParam)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            if (WaitForMutext(wParam, lParam))
            {
                try
                {
                    action();
                }
                finally
                {
                    ReleaseMutex();
                }
            }
        }

        public static void ActivateWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
                return;

            FormUtilities.ActivateWindow(FormUtilities.GetModalWindow(hwnd));
        }

#pragma warning disable IDE0060 // Remove unused parameter
        public void OnWndProc(IntPtr hwnd, int m, IntPtr wParam, IntPtr lParam, bool restorePlacement, bool activate)
#pragma warning restore IDE0060 // Remove unused parameter
        {
            if (m == Message)
            {
                if (restorePlacement)
                {
                    var placement = WindowPlacement.GetPlacement(hwnd, false);
                    if (placement.IsValid && placement.IsMinimized)
                    {
                        const int SW_SHOWNORMAL = 1;
                        placement.ShowCmd = SW_SHOWNORMAL;
                        placement.SetPlacement(hwnd);
                    }
                }

                if (activate)
                {
                    SetForegroundWindow(hwnd);
                    FormUtilities.ActivateWindow(FormUtilities.GetModalWindow(hwnd));
                }
            }
        }

        public void OnWndProc(Form form, Message m, bool activate)
        {
            if (form == null)
                throw new ArgumentNullException(nameof(form));

            if (m.Msg == Message)
            {
                if (activate)
                {
                    if (form.WindowState == FormWindowState.Minimized)
                    {
                        form.WindowState = FormWindowState.Normal;
                    }

                    form.Activate();
                    FormUtilities.ActivateWindow(FormUtilities.GetModalWindow(form.Handle));
                }
            }
        }

        public void ReleaseMutex() => Mutex.ReleaseMutex();
        public bool WaitForMutext(IntPtr wParam, IntPtr lParam) => WaitForMutext(false, wParam, lParam);
        public bool WaitForMutext(bool force, IntPtr wParam, IntPtr lParam)
        {
            var b = PrivateWaitForMutext(force);
            if (!b)
            {
                PostMessage((IntPtr)HWND_BROADCAST, Message, wParam, lParam);
            }
            return b;
        }

        private bool PrivateWaitForMutext(bool force)
        {
            if (force)
                return true;

            try
            {
                return Mutex.WaitOne(TimeSpan.Zero, true);
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch (AbandonedMutexException)
            {
                return true;
            }
#pragma warning restore CA1031 // Do not catch general exception types
        }
    }

    // NOTE: don't add any field or public get/set property, as this must exactly map to Windows' WINDOWPLACEMENT structure
    [StructLayout(LayoutKind.Sequential)]
#pragma warning disable CA1815 // Override equals and operator equals on value types
    public struct WindowPlacement
#pragma warning restore CA1815 // Override equals and operator equals on value types
    {
        public int Length { get; set; }
        public int Flags { get; set; }
        public int ShowCmd { get; set; }
        public int MinPositionX { get; set; }
        public int MinPositionY { get; set; }
        public int MaxPositionX { get; set; }
        public int MaxPositionY { get; set; }
        public int NormalPositionLeft { get; set; }
        public int NormalPositionTop { get; set; }
        public int NormalPositionRight { get; set; }
        public int NormalPositionBottom { get; set; }

        [DllImport("user32", SetLastError = true)]
        private static extern bool SetWindowPlacement(IntPtr hWnd, ref WindowPlacement lpwndpl);

        [DllImport("user32", SetLastError = true)]
        private static extern bool GetWindowPlacement(IntPtr hWnd, ref WindowPlacement lpwndpl);

        private const int SW_SHOWMINIMIZED = 2;

        public bool IsMinimized => ShowCmd == SW_SHOWMINIMIZED;
        public bool IsValid => Length == Marshal.SizeOf(typeof(WindowPlacement));

        public void SetPlacement(IntPtr windowHandle) => SetWindowPlacement(windowHandle, ref this);
        public static WindowPlacement GetPlacement(IntPtr windowHandle, bool throwOnError)
        {
            var placement = new WindowPlacement();
            if (windowHandle == IntPtr.Zero)
                return placement;

            placement.Length = Marshal.SizeOf(typeof(WindowPlacement));
            if (!GetWindowPlacement(windowHandle, ref placement))
            {
                if (throwOnError)
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                return new WindowPlacement();
            }
            return placement;
        }
    }

    public static class FormUtilities
    {
        [DllImport("user32")]
        private static extern IntPtr GetWindow(IntPtr hWnd, int uCmd);

        [DllImport("user32", SetLastError = true)]
        private static extern IntPtr SetActiveWindow(IntPtr hWnd);

        [DllImport("user32")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("kernel32")]
        public static extern int GetCurrentThreadId();

        [DllImport("user32")]
        private static extern bool EnumThreadWindows(int dwThreadId, EnumChildrenCallback lpEnumFunc, IntPtr lParam);

        private delegate bool EnumChildrenCallback(IntPtr hwnd, IntPtr lParam);

        private class ModalWindowUtil
        {
            private const int GW_OWNER = 4;
            private int _maxOwnershipLevel;
            private IntPtr _maxOwnershipHandle;

            private bool EnumChildren(IntPtr hwnd, IntPtr lParam)
            {
                var level = 1;
                if (IsWindowVisible(hwnd) && IsOwned(lParam, hwnd, ref level))
                {
                    if (level > _maxOwnershipLevel)
                    {
                        _maxOwnershipHandle = hwnd;
                        _maxOwnershipLevel = level;
                    }
                }
                return true;
            }

            private static bool IsOwned(IntPtr owner, IntPtr hwnd, ref int level)
            {
                var handle = GetWindow(hwnd, GW_OWNER);
                if (handle == IntPtr.Zero)
                    return false;

                if (handle == owner)
                    return true;

                level++;
                return IsOwned(owner, handle, ref level);
            }

            public static void ActivateWindow(IntPtr hwnd)
            {
                if (hwnd != IntPtr.Zero)
                {
                    SetActiveWindow(hwnd);
                }
            }

            public static IntPtr GetModalWindow(IntPtr owner)
            {
                var util = new ModalWindowUtil();
                EnumThreadWindows(GetCurrentThreadId(), util.EnumChildren, owner);
                return util._maxOwnershipHandle; // may be IntPtr.Zero
            }
        }

        public static void ActivateWindow(IntPtr hwnd) => ModalWindowUtil.ActivateWindow(hwnd);
        public static IntPtr GetModalWindow(IntPtr owner) => ModalWindowUtil.GetModalWindow(owner);
    }
}
