using System;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

static class ActivationTests
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateDesktop(string name, IntPtr device, IntPtr mode, uint flags, uint access, IntPtr security);
    [DllImport("user32.dll", SetLastError = true)] static extern bool SetThreadDesktop(IntPtr desktop);
    [DllImport("user32.dll")] static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
    const uint BlockMessage = 0x8020;

    sealed class Target : NativeWindow
    {
        internal readonly ManualResetEvent Blocked = new ManualResetEvent(false);
        internal readonly ManualResetEvent Release = new ManualResetEvent(false);
        internal int NullMessages;
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0) Interlocked.Increment(ref NullMessages);
            if (message.Msg == BlockMessage)
            {
                Blocked.Set();
                Release.WaitOne(5000); // Safety release if a broken synchronization call never times out.
            }
            if (message.Msg == 0x10) Application.ExitThread();
            base.WndProc(ref message);
        }
    }

    static void Check(bool condition, string failure)
    {
        if (!condition) throw new Exception(failure);
    }

    static int Main(string[] args)
    {
        IntPtr desktop = CreateDesktop("AppHopperActivationTests-" + Guid.NewGuid().ToString("N"),
            IntPtr.Zero, IntPtr.Zero, 0, 0x1FF, IntPtr.Zero);
        if (desktop == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        int result = 1;
        var caller = new Thread(delegate()
        {
            Target target = null;
            IntPtr hwnd = IntPtr.Zero;
            Exception targetError = null;
            var ready = new ManualResetEvent(false);
            Thread receiver = null;
            try
            {
                if (!SetThreadDesktop(desktop)) throw new Win32Exception(Marshal.GetLastWin32Error());
                MethodInfo wait = Assembly.LoadFrom(args[0]).GetType("AppHopper.Program").GetMethod(
                    "WaitForForegroundNotification", BindingFlags.Static | BindingFlags.NonPublic);
                receiver = new Thread(delegate()
                {
                    try
                    {
                        if (!SetThreadDesktop(desktop)) throw new Win32Exception(Marshal.GetLastWin32Error());
                        target = new Target();
                        target.CreateHandle(new CreateParams { Caption = "Activation barrier fixture" });
                        hwnd = target.Handle;
                        ready.Set();
                        Application.Run();
                    }
                    catch (Exception error) { targetError = error; ready.Set(); }
                    finally { if (target != null && target.Handle != IntPtr.Zero) target.DestroyHandle(); }
                });
                receiver.IsBackground = true;
                receiver.Start();
                Check(ready.WaitOne(5000), "target did not create its window");
                if (targetError != null) throw targetError;
                int before = target.NullMessages;
                Check((bool)wait.Invoke(null, new object[] { hwnd, (uint)1000 }), "responsive target was not synchronized");
                Check(target.NullMessages > before, "barrier returned before the target processed WM_NULL");
                Console.WriteLine("PASS activation notification synchronizes a real foreign thread");

                ShowWindow(hwnd, 0);
                ShowWindow(hwnd, 7 /*SW_SHOWMINNOACTIVE*/);
                Check(IsIconic(hwnd), "fixture did not minimize");
                MethodInfo force = wait.DeclaringType.GetMethod("ForceForeground", BindingFlags.Static | BindingFlags.NonPublic);
                Check(!(bool)force.Invoke(null, new object[] { hwnd }), "background caller activated a target");
                Check((bool)wait.Invoke(null, new object[] { hwnd, (uint)1000 }), "target did not drain restoration messages");
                Check(IsIconic(hwnd), "background caller restored a minimized target without owning foreground");
                Console.WriteLine("PASS unowned foreground does not restore or activate a minimized target");

                Check(PostMessage(hwnd, BlockMessage, IntPtr.Zero, IntPtr.Zero), "could not block the target queue");
                Check(target.Blocked.WaitOne(5000), "target did not enter its blocked handler");
                Check(!(bool)wait.Invoke(null, new object[] { hwnd, (uint)80 }), "blocked target did not time out");
                Console.WriteLine("PASS blocked activation notification returns failure within its native timeout");
                target.Release.Set();
                Check(PostMessage(hwnd, 0x10, IntPtr.Zero, IntPtr.Zero), "could not stop target loop");
                Check(receiver.Join(5000), "target thread did not exit");
                Check(!(bool)wait.Invoke(null, new object[] { hwnd, (uint)80 }), "destroyed target was treated as synchronized");
                Console.WriteLine("PASS destroyed activation target is rejected");
                result = 0;
            }
            catch (Exception error) { Console.WriteLine("FAIL activation synchronization: " + error); }
            finally
            {
                if (target != null) target.Release.Set();
                if (receiver != null && receiver.IsAlive)
                {
                    if (hwnd != IntPtr.Zero) PostMessage(hwnd, 0x10, IntPtr.Zero, IntPtr.Zero);
                    receiver.Join(5000);
                }
                ready.Dispose();
                if (target != null) { target.Blocked.Dispose(); target.Release.Dispose(); }
            }
        });
        caller.Start();
        caller.Join();
        CloseDesktop(desktop);
        return result;
    }
}
