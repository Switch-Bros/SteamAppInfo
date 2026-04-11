using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using ValveKeyValue;

namespace SteamAppInfoParser;

class Program
{
    static int Main(string[] args)
    {
        Console.WriteLine("Usage: ./app [path to vdf]");
        Console.WriteLine("Do not specify path if you want to dump both files from your Steam client.");

        if (args.Length > 0)
        {
            using var stream = File.OpenRead(args[0]);

            // Use the second byte to check the type of file
            stream.ReadByte();
            var b = stream.ReadByte();
            stream.Position = 0;

            if (b == 0x44)
            {
                DumpAppInfo(stream);
            }
            else if (b == 0x55)
            {
                DumpPackageInfo(stream);
            }
            else
            {
                Console.WriteLine($"\"{stream.Name}\" has unknown magic.");
                return 1;
            }

            return 0;
        }

        var steamLocation = GetSteamPath();

        if (steamLocation == null)
        {
            Console.Error.WriteLine("Can not find Steam");
            return 1;
        }

        using (var stream = File.OpenRead(Path.Join(steamLocation, "appcache", "appinfo.vdf")))
        {
            DumpAppInfo(stream);
        }

        using (var stream = File.OpenRead(Path.Join(steamLocation, "appcache", "packageinfo.vdf")))
        {
            DumpPackageInfo(stream);
        }

        return 0;
    }

    private static void DumpAppInfo(FileStream inputStream)
    {
        Console.WriteLine($"Reading {inputStream.Name}");

        var appInfo = new AppInfo();
        appInfo.Read(inputStream);
        Console.WriteLine($"{appInfo.Apps.Count} apps");

        using var stream = File.OpenWrite("appinfo_text.vdf");

        var serializer = KVSerializer.Create(KVSerializationFormat.KeyValues1Text);

        foreach (var app in appInfo.Apps)
        {
            var root = app.Data.Root;
            root.Add("_token", new KVObject(app.Token));
            root.Add("_changenumber", new KVObject((long)app.ChangeNumber));
            root.Add("_updated", new KVObject(app.LastUpdated.ToString("s")));
            root.Add("_hash", new KVObject(Convert.ToHexString([.. app.Hash])));
            serializer.Serialize(stream, root, $"app_{app.AppID}");
        }

        Console.WriteLine($"Saved to {stream.Name}");
    }

    private static void DumpPackageInfo(FileStream inputStream)
    {
        Console.WriteLine($"Reading {inputStream.Name}");

        var packageInfo = new PackageInfo();
        packageInfo.Read(inputStream);
        Console.WriteLine($"{packageInfo.Packages.Count} packages");

        using var stream = File.OpenWrite("packageinfo_text.vdf");

        var serializer = KVSerializer.Create(KVSerializationFormat.KeyValues1Text);

        foreach (var app in packageInfo.Packages)
        {
            var root = app.Data.Root;
            root.Add("_token", new KVObject(app.Token));
            root.Add("_changenumber", new KVObject((long)app.ChangeNumber));
            root.Add("_hash", new KVObject(Convert.ToHexString([.. app.Hash])));
            serializer.Serialize(stream, root, $"package_{app.SubID}");
        }

        Console.WriteLine($"Saved to {stream.Name}");
    }

    private static string GetSteamPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Valve\\Steam") ??
                      RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                          .OpenSubKey("SOFTWARE\\Valve\\Steam");

            if (key != null && key.GetValue("SteamPath") is string steamPath)
            {
                return steamPath;
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var paths = new[] { ".steam", ".steam/steam", ".steam/root", ".local/share/Steam" };

            return paths
                .Select(path => Path.Join(home, path))
                .FirstOrDefault(steamPath => Directory.Exists(Path.Join(steamPath, "appcache")));
        }
        else if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Join(home, "Steam");
        }

        throw new PlatformNotSupportedException();
    }
}
