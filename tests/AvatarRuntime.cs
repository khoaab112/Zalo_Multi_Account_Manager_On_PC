using System;
using System.IO;
using System.Linq;
using ZaloManagerPoc;

public static class AvatarRuntime
{
    [STAThread]
    public static int Main()
    {
        try
        {
            var profile = Store.Load().First(item => Directory.Exists(item.DataPath) && Directory.GetFiles(item.DataPath, "ftsm_*.db").Length == 1);
            var identity = IdentityReader.Read(profile);
            string avatar = IdentityReader.DownloadAvatar(profile, identity);
            if (new FileInfo(avatar).Length == 0) throw new IOException("Empty avatar");
            Console.WriteLine("PASS avatar HTTPS download under the same .NET Framework runtime as the app");
            // Apply the requested current account identity to the existing profile.
            profile.AvatarPath = avatar;
            profile.Name = identity.Name;
            ProfileActions.CreateShortcut(profile);
            ProfileActions.ApplyAvatar(profile);
            Store.Save(Store.Load().Select(item => item.Id == profile.Id ? profile : item));
            Console.WriteLine("PASS current account identity saved and applied to desktop shortcut and running clone");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }
}
