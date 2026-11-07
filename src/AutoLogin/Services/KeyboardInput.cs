using System;
using System.Runtime.InteropServices;
using System.Threading;
using AutoLogin.Interop;

namespace AutoLogin.Services
{
    /// <summary>
    /// Unicode 键盘输入（不受输入法影响），以及常用按键发送。
    /// </summary>
    internal static class KeyboardInput
    {
        public static bool SendText(string text, int delayMs, ILogService log)
        {
            if (string.IsNullOrEmpty(text)) return true;

            bool ok = true;
            Thread.Sleep(150);

            foreach (char c in text)
            {
                NativeMethods.INPUT down = new NativeMethods.INPUT();
                down.type = NativeMethods.INPUT_KEYBOARD;
                down.ki.wScan = c;
                down.ki.dwFlags = NativeMethods.KEYEVENTF_UNICODE;

                NativeMethods.INPUT up = new NativeMethods.INPUT();
                up.type = NativeMethods.INPUT_KEYBOARD;
                up.ki.wScan = c;
                up.ki.dwFlags = NativeMethods.KEYEVENTF_UNICODE | NativeMethods.KEYEVENTF_KEYUP;

                NativeMethods.INPUT[] inputs = new NativeMethods.INPUT[2];
                inputs[0] = down;
                inputs[1] = up;

                uint sent = NativeMethods.SendInput(
                    2,
                    inputs,
                    Marshal.SizeOf(typeof(NativeMethods.INPUT)));

                if (sent != 2)
                {
                    log.Warning("SendInput 发送字符失败(chr=" + (int)c + "),Win32Error=" + Marshal.GetLastWin32Error());
                    ok = false;
                }

                Thread.Sleep(Math.Max(1, delayMs));
            }

            return ok;
        }

        public static void SendKey(ushort vk)
        {
            NativeMethods.INPUT down = new NativeMethods.INPUT();
            down.type = NativeMethods.INPUT_KEYBOARD;
            down.ki.wVk = vk;

            NativeMethods.INPUT up = new NativeMethods.INPUT();
            up.type = NativeMethods.INPUT_KEYBOARD;
            up.ki.wVk = vk;
            up.ki.dwFlags = NativeMethods.KEYEVENTF_KEYUP;

            NativeMethods.INPUT[] inputs = new NativeMethods.INPUT[2];
            inputs[0] = down;
            inputs[1] = up;

            NativeMethods.SendInput(2, inputs, Marshal.SizeOf(typeof(NativeMethods.INPUT)));
        }
    }
}
