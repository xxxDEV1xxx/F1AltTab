using System.Diagnostics;
using System.Runtime.InteropServices;

class Program
{
    // Hook constants
    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL    = 14;

    private const int WM_KEYDOWN     = 0x0100;
    private const int WM_SYSKEYDOWN  = 0x0104;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_XBUTTONDOWN = 0x020B;

    // Virtual keys
    private const int VK_F1      = 0x70;
    private const int VK_F2      = 0x71;
    private const int VK_F3      = 0x72;
    private const int VK_F4      = 0x73;
    private const int VK_F5      = 0x74;
    private const int VK_F6      = 0x75;
    private const int VK_RETURN  = 0x0D;
    private const int VK_MENU    = 0x12;   // Left Alt
    private const int VK_TAB     = 0x09;
    private const int VK_CONTROL = 0x11;
    private const int VK_C       = 0x43;
    private const int VK_V       = 0x56;
    private const int VK_X       = 0x58;
    private const int VK_DELETE  = 0x2E;

    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const int  LLKHF_INJECTED  = 0x00000010;

    private static LowLevelKeyboardProc _keyboardProc = KeyboardHookCallback;
    private static LowLevelMouseProc   _mouseProc    = MouseHookCallback;

    private static IntPtr _keyboardHookID = IntPtr.Zero;
    private static IntPtr _mouseHookID    = IntPtr.Zero;

    // State: is Alt currently being held by our F1 logic?
    private static bool _altHeldByF1 = false;

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public int vkCode;
        public int scanCode;
        public int flags;
        public int time;
        public IntPtr dwExtraInfo;
    }

    [STAThread]
    static void Main()
    {
        // Hide console window
        var handle = GetConsoleWindow();
        if (handle != IntPtr.Zero)
            ShowWindow(handle, 0);

        _keyboardHookID = SetKeyboardHook(_keyboardProc);
        _mouseHookID    = SetMouseHook(_mouseProc);

        Application.Run();   // keep process alive

        UnhookWindowsHookEx(_keyboardHookID);
        UnhookWindowsHookEx(_mouseHookID);
    }

    private static IntPtr SetKeyboardHook(LowLevelKeyboardProc proc)
    {
        using var curProcess = Process.GetCurrentProcess();
        using var curModule  = curProcess.MainModule!;
        return SetWindowsHookEx(WH_KEYBOARD_LL, proc,
            GetModuleHandle(curModule.ModuleName), 0);
    }

    private static IntPtr SetMouseHook(LowLevelMouseProc proc)
    {
        using var curProcess = Process.GetCurrentProcess();
        using var curModule  = curProcess.MainModule!;
        return SetWindowsHookEx(WH_MOUSE_LL, proc,
            GetModuleHandle(curModule.ModuleName), 0);
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    // -------------------- Keyboard --------------------
    private static IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
        {
            var kbd = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            int vk = kbd.vkCode;
            bool isInjected = (kbd.flags & LLKHF_INJECTED) != 0;

            // ---- F1 special handling (only real F1 presses) ----
            if (vk == VK_F1 && !isInjected)
            {
                if (!_altHeldByF1)
                {
                    // First press → hold Alt + send Tab
                    keybd_event((byte)VK_MENU, 0, 0, UIntPtr.Zero);               // Alt down
                    keybd_event((byte)VK_TAB,  0, 0, UIntPtr.Zero);               // Tab down
                    keybd_event((byte)VK_TAB,  0, KEYEVENTF_KEYUP, UIntPtr.Zero); // Tab up
                    _altHeldByF1 = true;
                }
                else
                {
                    // Later presses → just Tab (Alt stays down)
                    keybd_event((byte)VK_TAB, 0, 0, UIntPtr.Zero);
                    keybd_event((byte)VK_TAB, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                }

                return (IntPtr)1; // swallow the real F1
            }

            // ---- Any *real* non-F1 key while we are holding Alt → release ----
            if (_altHeldByF1 && !isInjected && vk != VK_F1)
            {
                ReleaseAlt();
            }

            // ---- Normal remaps (F2–F6) – only when not in F1 mode and not injected ----
            if (!_altHeldByF1 && !isInjected)
            {
                switch (vk)
                {
                    case VK_F2: // Enter
                        keybd_event((byte)VK_RETURN, 0, 0, UIntPtr.Zero);
                        keybd_event((byte)VK_RETURN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                        return (IntPtr)1;

                    case VK_F3: // Copy
                        SendCombo(VK_CONTROL, VK_C);
                        return (IntPtr)1;

                    case VK_F4: // Paste
                        SendCombo(VK_CONTROL, VK_V);
                        return (IntPtr)1;

                    case VK_F5: // Cut
                        SendCombo(VK_CONTROL, VK_X);
                        return (IntPtr)1;

                    case VK_F6: // Delete
                        keybd_event((byte)VK_DELETE, 0, 0, UIntPtr.Zero);
                        keybd_event((byte)VK_DELETE, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                        return (IntPtr)1;
                }
            }
        }

        return CallNextHookEx(_keyboardHookID, nCode, wParam, lParam);
    }

    // -------------------- Mouse --------------------
    private static IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            if (msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN ||
                msg == WM_MBUTTONDOWN || msg == WM_XBUTTONDOWN)
            {
                if (_altHeldByF1)
                {
                    ReleaseAlt();
                }
            }
        }

        return CallNextHookEx(_mouseHookID, nCode, wParam, lParam);
    }

    // -------------------- Helpers --------------------
    private static void ReleaseAlt()
    {
        keybd_event((byte)VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        _altHeldByF1 = false;
    }

    private static void SendCombo(int modifier, int key)
    {
        keybd_event((byte)modifier, 0, 0, UIntPtr.Zero);
        keybd_event((byte)key,      0, 0, UIntPtr.Zero);
        keybd_event((byte)key,      0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event((byte)modifier, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    // -------------------- P/Invoke --------------------
    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int idHook,
        LowLevelKeyboardProc lpfn,
        IntPtr hMod,
        uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int idHook,
        LowLevelMouseProc lpfn,
        IntPtr hMod,
        uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(
        IntPtr hhk,
        int nCode,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    private static extern void keybd_event(
        byte bVk,
        byte bScan,
        uint dwFlags,
        UIntPtr dwExtraInfo);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(
        IntPtr hWnd,
        int nCmdShow);
}
