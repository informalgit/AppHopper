using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

// Runs against an explicitly selected build, without Main, hooks, input injection,
// foreground changes, or registry writes. Native GUI smoke is separate.
static class RegressionTests
{
    static Type program;
    const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
    static int failures;
    static object Call(string name, params object[] args)
    {
        try { return program.GetMethod(name, PrivateStatic).Invoke(null, args); }
        catch (TargetInvocationException e) { throw e.InnerException; }
    }
    static object Get(string name) { return program.GetField(name, PrivateStatic).GetValue(null); }
    static void Set(string name, object value) { program.GetField(name, PrivateStatic).SetValue(null, value); }
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Test(string name, Action body)
    {
        try { body(); Console.WriteLine("PASS " + name); }
        catch (Exception e) { failures++; Console.WriteLine("FAIL " + name + ": " + e.Message); }
    }
    static string Capture(Action body)
    {
        var bytes = new MemoryStream();
        var writer = new StreamWriter(bytes, new UTF8Encoding(false));
        Set("_log", writer);
        FieldInfo count = program.GetField("_logBytes", PrivateStatic);
        if (count != null) count.SetValue(null, (long)0);
        try { body(); writer.Flush(); return Encoding.UTF8.GetString(bytes.ToArray()); }
        finally { Set("_log", null); writer.Dispose(); }
    }
    static void Hook(uint vk, uint flags, IntPtr tag, bool swallowed)
    {
        Type native = program.Assembly.GetType("AppHopper.NativeMethods");
        Type structure = native.GetNestedType("KBDLLHOOKSTRUCT");
        object data = Activator.CreateInstance(structure);
        structure.GetField("vkCode").SetValue(data, vk);
        structure.GetField("flags").SetValue(data, flags);
        structure.GetField("dwExtraInfo").SetValue(data, tag);
        IntPtr memory = Marshal.AllocHGlobal(Marshal.SizeOf(structure));
        try
        {
            Marshal.StructureToPtr(data, memory, false);
            IntPtr result = (IntPtr)Call("KbHookProc", 0, new IntPtr((flags & 0x80) != 0 ? 0x101 : 0x100), memory);
            Check((result == new IntPtr(1)) == swallowed, "unexpected hook disposition for vk=" + vk);
        }
        finally { Marshal.FreeHGlobal(memory); }
    }
    static FileSecurity Acl(string owner, string writer)
    {
        var acl = new FileSecurity();
        acl.SetOwner(new SecurityIdentifier(owner));
        acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier("S-1-5-32-544"), FileSystemRights.FullControl, AccessControlType.Allow));
        if (writer != null) acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(writer), FileSystemRights.Write, AccessControlType.Allow));
        return acl;
    }
    static void ApplyKeys(Array inputs, int count, bool[] held)
    {
        for (int i = 0; i < count; i++)
        {
            object input = inputs.GetValue(i);
            object union = input.GetType().GetField("data").GetValue(input);
            object keyboard = union.GetType().GetField("keyboard").GetValue(union);
            int vk = (ushort)keyboard.GetType().GetField("wVk").GetValue(keyboard);
            uint flags = (uint)keyboard.GetType().GetField("dwFlags").GetValue(keyboard);
            held[vk] = (flags & 2) == 0;
        }
    }
    static object StartupValue()
    {
        using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
            return key == null ? null : key.GetValue("AppHopper");
    }
    static int Main(string[] args)
    {
        if (args.Length != 1) { Console.Error.WriteLine("Usage: RegressionTests.exe <AppHopper build>"); return 2; }
        program = Assembly.LoadFile(Path.GetFullPath(args[0])).GetType("AppHopper.Program");
        Test("existing layout, crop, paging and ordering checks", delegate { Check((bool)Call("RunSelfTests"), "self-test failed"); });
        Test("cloak filtering keeps transient desktop-switch windows eligible", delegate
        {
            Check((string)Call("CloakIneligibilityReason", 1, true) == "cloaked", "app cloak accepted");
            Check((string)Call("CloakIneligibilityReason", 2, false) == "other-desktop", "other-desktop cloak accepted");
            Check(Call("CloakIneligibilityReason", 2, true) == null, "current-desktop shell cloak rejected");
        });
        Test("default sensitive-field redaction", delegate
        {
            Set("_logVerbose", false);
            string output = Capture(delegate { Call("Log", "title=" + Call("LogText", "private document") + " exe=" + Call("LogText", "private.exe")); });
            Check(!output.Contains("private document") && !output.Contains("private.exe") && output.Contains("<redacted>"), "private field leaked");
        });
        Test("verbose diagnostics escape record separators", delegate
        {
            Set("_logVerbose", true);
            string output = Capture(delegate { Call("Log", "title=" + Call("LogText", "secret\r\ninjected")); });
            Check(output.Contains("secret\\r\\ninjected"), "verbose title not escaped");
            Check(output.Split(new string[] { Environment.NewLine }, StringSplitOptions.None).Length == 2, "title forged another log record");
            Set("_logVerbose", false);
        });
        Test("8 MiB exact boundary stops further logging", delegate
        {
            const int cap = 8 * 1024 * 1024;
            int overhead = Encoding.UTF8.GetByteCount(DateTime.Now.ToString("HH:mm:ss.fff ") + Environment.NewLine);
            string output = Capture(delegate
            {
                Call("Log", new string('x', cap - overhead));
                Call("Log", "overflow");
            });
            Check(Encoding.UTF8.GetByteCount(output) == cap && !output.Contains("overflow"), "byte cap violated at exact boundary");
        });
        Test("multibyte records respect byte cap", delegate
        {
            const int cap = 8 * 1024 * 1024;
            int overhead = Encoding.UTF8.GetByteCount(DateTime.Now.ToString("HH:mm:ss.fff ") + Environment.NewLine);
            string output = Capture(delegate
            {
                Call("Log", new string('x', cap - overhead * 2 - 2));
                Call("Log", new string('\u6d4b', 9));
                Call("Log", "ok");
            });
            Check(Encoding.UTF8.GetByteCount(output) <= cap && !output.Contains("\u6d4b") && output.Contains("ok"), "UTF-8 overflow or later fitting record lost");
        });
        Test("failed log stream stops further writes without duplicating records", delegate
        {
            string path = Path.GetTempFileName();
            StreamWriter writer = null;
            try
            {
                writer = new StreamWriter(path, false, new UTF8Encoding(false));
                Set("_log", writer); Set("_logBytes", (long)0);
                Call("Log", "retained-record");
                writer.BaseStream.Dispose();
                string before = File.ReadAllText(path);
                Call("Log", "failed-record");
                Check(Get("_log") == null, "failed writer still active");
                Call("Log", "later-record");
                Check(File.ReadAllText(path) == before && before.Contains("retained-record"),
                    "failure duplicated or changed the last persisted record");
            }
            finally
            {
                Set("_log", null);
                if (writer != null) { try { writer.Dispose(); } catch (ObjectDisposedException) { } }
                File.Delete(path);
            }
        });
        Test("startup rejects sibling prefixes and traversal escapes", delegate
        {
            Check((bool)Call("IsUnderDirectory", @"C:\Program Files\AppHopper\app.exe", @"C:\Program Files"), "valid descendant rejected");
            Check(!(bool)Call("IsUnderDirectory", @"C:\Program Files Evil\app.exe", @"C:\Program Files"), "prefix bypass");
            Check(!(bool)Call("IsUnderDirectory", @"C:\Program Files\..\Users\app.exe", @"C:\Program Files"), "traversal bypass");
            Check(!(bool)Call("IsProtectedStartupPath", args[0]), "unprotected test build accepted");
        });
        Test("startup ACL permits only trusted mutation and ownership", delegate
        {
            Check((bool)Call("HasProtectedAcl", Acl("S-1-5-32-544", null)), "admin ACL rejected");
            Check(!(bool)Call("HasProtectedAcl", Acl("S-1-5-32-544", "S-1-5-32-545")), "Users write accepted");
            Check(!(bool)Call("HasProtectedAcl", Acl("S-1-5-32-544", "S-1-1-0")), "Everyone write accepted");
            Check(!(bool)Call("HasProtectedAcl", Acl("S-1-5-32-545", null)), "untrusted owner accepted");
        });
        Test("generic write and generic all cannot bypass startup ACL", delegate
        {
            foreach (string rights in new string[] { "GW", "GA" })
            {
                var security = new FileSecurity();
                security.SetSecurityDescriptorSddlForm("O:BAG:BAD:(A;;FA;;;BA)(A;;" + rights + ";;;BU)");
                Check(!(bool)Call("HasProtectedAcl", security), "untrusted " + rights + " accepted");
            }
        });
        Test("rejected autostart leaves Run registration untouched", delegate
        {
            Check(!(bool)Call("IsProtectedStartupPath", Assembly.GetExecutingAssembly().Location),
                "refusing registry test from a protected runner location");
            object before = StartupValue();
            Check(!(bool)Call("SetStartup", true), "unprotected test runner registered");
            Check(object.Equals(before, StartupValue()), "rejected startup changed registry");
        });
        Test("full and partial native replay preserve physical modifiers", delegate
        {
            foreach (bool addAlt in new bool[] { false, true })
            foreach (bool addShift in new bool[] { false, true })
            {
                Array inputs = (Array)Call("NativeTabInputs", addAlt, addShift);
                for (int sent = 0; sent <= inputs.Length; sent++)
                {
                    var held = new bool[256];
                    held[0x12] = !addAlt;
                    held[0x10] = !addShift;
                    ApplyKeys(inputs, sent, held);
                    Array recovery = (Array)Call("ReplayReleases", inputs, (uint)sent);
                    if (recovery != null) ApplyKeys(recovery, recovery.Length, held);
                    Check(!held[9] && held[0x12] == !addAlt && held[0x10] == !addShift,
                        "replay prefix " + sent + " left synthetic key held or released physical modifier");
                }
            }
        });
        Test("failed message delivery does not consume Alt-Tab", delegate
        {
            Set("_msg", null); Set("_session", false); Set("_enabled", true); Set("_committing", false); Set("_tabHookDown", false);
            Hook(9, 0x20, IntPtr.Zero, false);
            Hook(9, 0x80, IntPtr.Zero, false);
        });
        Test("matching Tab release consumed after disable", delegate
        {
            Set("_tabHookDown", true); Set("_enabled", false);
            Hook(9, 0x80, IntPtr.Zero, true);
            Check(!(bool)Get("_tabHookDown"), "Tab latch not cleared");
            Hook(9, 0x80, IntPtr.Zero, false);
            Set("_enabled", true);
        });
        Test("forwarded Tab auto-repeat must also forward its release", delegate
        {
            Set("_tabHookDown", true); Set("_enabled", false);
            try
            {
                Hook(9, 0, IntPtr.Zero, false);
                Hook(9, 0x80, IntPtr.Zero, false);
                Check(!(bool)Get("_tabHookDown"), "mixed-disposition Tab latch not cleared");
            }
            finally { Set("_tabHookDown", false); Set("_enabled", true); }
        });
        Test("physical generic, left and right Alt releases always pass", delegate
        {
            Set("_session", true); Set("_msg", null);
            foreach (uint vk in new uint[] { 0x12, 0xA4, 0xA5 }) Hook(vk, 0x80, IntPtr.Zero, false);
            Set("_session", false);
        });
        Test("self-replayed input bypasses hook and preserves physical latch", delegate
        {
            Set("_tabHookDown", true);
            Hook(9, 0x80, new IntPtr(0x41504852), false);
            Check((bool)Get("_tabHookDown"), "replay cleared physical Tab latch");
            Set("_tabHookDown", false);
        });
        Test("input during commit remains native", delegate
        {
            Set("_committing", true);
            Hook(9, 0x20, IntPtr.Zero, false);
            Set("_committing", false);
        });
        Test("invalid activation rejected without input", delegate { Check(!(bool)Call("ForceForeground", IntPtr.Zero), "invalid HWND accepted"); });
        Console.WriteLine("failures=" + failures);
        return failures == 0 ? 0 : 1;
    }
}
