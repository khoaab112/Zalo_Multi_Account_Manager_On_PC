using System;
using System.IO;
using Microsoft.Win32;

namespace ZaloManagerPoc
{
    internal static class OriginalWindowsStartup
    {
        internal const string Argument = "--launch-original-at-logon";
        private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string BackupPath = @"Software\ZaloManagerPoC\OriginalStartup";
        private const string RunValue = "Zalo";
        private const string NativeCommand = "NativeCommand";
        private const string ManagedCommand = "ManagedCommand";

        private static string Command { get { return "\"" + typeof(Program).Assembly.Location + "\" " + Argument; } }

        internal static bool IsEnabled()
        {
            using (var run = Registry.CurrentUser.OpenSubKey(RunPath))
                return run != null && SameCommand(ReadCommand(run, RunValue), Command);
        }

        internal static void SetEnabled(Profile original, bool enabled)
        {
            using (var run = Registry.CurrentUser.OpenSubKey(RunPath, true))
            {
                if (run == null)
                    throw new IOException("Hãy bật khởi động cùng Windows trong Zalo gốc trước.");
                using (var backup = enabled ? Registry.CurrentUser.CreateSubKey(BackupPath) :
                    Registry.CurrentUser.OpenSubKey(BackupPath))
                    UpdateRegistration(run, backup, original.Source, enabled);
            }
        }

        internal static void UpdateRegistration(RegistryKey run, RegistryKey backup, string originalDirectory, bool enabled)
        {
            string current = ReadCommand(run, RunValue);
            string priorManaged = ReadCommand(backup, ManagedCommand);
            bool owned = SameCommand(current, Command) || SameCommand(current, priorManaged);
            if (!enabled)
            {
                if (!owned) return;
                string native = ReadCommand(backup, NativeCommand);
                if (String.IsNullOrEmpty(native))
                    throw new IOException("Thiếu bản sao lệnh khởi động gốc của Zalo.");
                run.SetValue(RunValue, native, backup.GetValueKind(NativeCommand));
                return;
            }

            if (!owned)
            {
                if (!IsNativeCommand(current, originalDirectory))
                    throw new IOException("Cần có mục tự khởi động trỏ thẳng tới Zalo.exe trong bản cài Zalo gốc.");
                backup.SetValue(NativeCommand, current, run.GetValueKind(RunValue));
            }
            else if (String.IsNullOrEmpty(ReadCommand(backup, NativeCommand)))
                throw new IOException("Thiếu bản sao lệnh khởi động gốc của Zalo.");
            else if (SameCommand(current, Command)) return;

            // Save the native registration before replacing it. Keep the same Run value name.
            backup.SetValue(ManagedCommand, Command, RegistryValueKind.String);
            backup.Flush();
            run.SetValue(RunValue, Command, RegistryValueKind.String);
        }

        private static string ReadCommand(RegistryKey key, string name)
        {
            return key == null ? null : key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        }

        private static bool SameCommand(string first, string second)
        {
            return !String.IsNullOrEmpty(first) && String.Equals(first, second, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsNativeCommand(string command, string originalDirectory)
        {
            if (String.IsNullOrWhiteSpace(command) || String.IsNullOrWhiteSpace(originalDirectory)) return false;
            string path = Environment.ExpandEnvironmentVariables(command.Trim());
            if (path.Length > 1 && path[0] == '"' && path[path.Length - 1] == '"')
                path = path.Substring(1, path.Length - 2);
            string direct = Path.Combine(originalDirectory, "Zalo.exe");
            if (SameCommand(path, direct)) return true;
            string parent = Path.GetDirectoryName(originalDirectory);
            return !String.IsNullOrEmpty(parent) &&
                Path.GetFileName(originalDirectory).StartsWith("Zalo-", StringComparison.OrdinalIgnoreCase) &&
                SameCommand(path, Path.Combine(parent, "Zalo.exe"));
        }
    }
}
