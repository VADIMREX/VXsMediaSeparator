using Microsoft.WindowsAPICodePack.Shell;
using MediaInfoLib;
using System.Text.RegularExpressions;
using System.Xml;

Console.WriteLine("Media separator");
Console.WriteLine("This product uses MediaInfo library, Copyright (c) 2002-2023 MediaArea.net SARL. https://mediaarea.net/en/MediaInfo");
Console.WriteLine("Start separating");

var rootDir = args.Length > 0 ? args[1] : Directory.GetCurrentDirectory();

if (string.IsNullOrEmpty(rootDir))
{
    Console.WriteLine("can not acquire root directory");
    return;
}

var targetRootMask = new Regex(rootDir.Replace("\\", "\\\\") + "\\\\[0-9]{4}\\\\[0-9]{4}-[0-9]{2}");
var androidMask = new Regex("[0-9]{8}");
var IOSMask = new Regex("_[E]?[0-9]{4,5}");
var extMask = new Regex("jpg|jpeg|png|heic|mp4|mov|aae", RegexOptions.IgnoreCase);
var androidPrefixMask = new Regex("[0-9]+IMG_[0-9]+_BURST[0-9]+_COVER|IMG_|SAVE_|MVIMG_|PANO_|VID_");

string? parseAndroid(Match parsed, FileInfo fi)
{
    var check = androidPrefixMask.Match(fi.Name);
    if (!check.Success)
        return null;
    var year = parsed.Captures[0].Value.Substring(0, 4);
    var month = parsed.Captures[0].Value.Substring(4, 2);
    var day = parsed.Captures[0].Value.Substring(6, 2);
    return $"{rootDir}\\{year}\\{year}-{month}-{day}\\{fi.Name}";
}

string? parseIOS(Match parsed, FileInfo fi)
{
    var shellFile = ShellFile.FromFilePath(fi.FullName);
    switch (fi.Extension.ToUpper())
    {
        case ".HEIC":
        case ".JPG":
        case ".JPEG":
        case ".PNG":
            {
                var prop = shellFile.Properties.DefaultPropertyCollection.Where(x => x.CanonicalName == "System.Photo.DateTaken").FirstOrDefault();
                if (null == prop)
                    return null;
                var dt = (DateTime)prop.ValueAsObject;
                return $"{rootDir}\\{dt.Year}\\{dt.Year}-{dt.Month.ToString().PadLeft(2, '0')}-{dt.Day.ToString().PadLeft(2, '0')}\\{fi.Name}";
            }
        case ".AAE":
            {
                using StreamReader sr = new(fi.OpenRead());
                XmlDocument xml = new();
                xml.LoadXml(sr.ReadToEnd());
                var dict = xml.GetElementsByTagName("dict")[0];
                if (null == dict) return null;
                bool isKey = true;
                string key = "";
                foreach (XmlElement node in dict.ChildNodes)
                {
                    if (isKey)
                    {
                        if ("key" != node.Name) return null;
                        key = node.InnerXml;
                        isKey = false;
                    }
                    else
                    {
                        isKey = true;
                        if ("adjustmentTimestamp" != key) continue;
                        if (string.IsNullOrEmpty(node.InnerXml)) return null;
                        var year = node.InnerXml.Substring(0, 4);
                        var month = node.InnerXml.Substring(5, 2);
                        var day = node.InnerXml.Substring(8, 2);
                        return $"{rootDir}\\{year}\\{year}-{month}-{day}\\{fi.Name}";
                    }
                }
                return null;
            }
        case ".MOV":
            {
                var mi = new MediaInfo();
                mi.Open(fi.FullName);
                var date = mi.Get(StreamKind.General, 0, "com.apple.quicktime.creationdate");
                if (string.IsNullOrEmpty(date))
                {
                    date = mi.Get(StreamKind.General, 0, "Tagged date");
                    if (string.IsNullOrEmpty(date))
                        return null;
                }

                var year = date.Substring(0, 4);
                var month = date.Substring(5, 2);
                var day = date.Substring(8, 2);
                return $"{rootDir}\\{year}\\{year}-{month}-{day}\\{fi.Name}";
            }
        default:
            return null;
    }
}

async Task SeparatePhotos(string directory)
{
    Console.WriteLine($"Separating - {directory}");
    
    var checkPath = targetRootMask.Match(directory);
    if (checkPath.Success) return;
    var dir = new DirectoryInfo(directory);
    using StreamWriter logFile = new(Path.Combine(directory, "log.txt"));
    void log(string msg)
    {
        Console.WriteLine(msg);
        logFile.WriteLine(msg);
    }

    List<Task> subtasks = new();
    foreach (var subdir in dir.GetDirectories())
    {
        subtasks.Add(SeparatePhotos(subdir.FullName));
    }

    IEnumerable<(FileInfo, string)> GetFileList()
    {
        foreach (var fi in dir.GetFiles())
        {
            var checkExt = extMask.Match(fi.Name);
            if (!checkExt.Success)
            {
                log($"unknown file extension {fi.Extension}: {fi.FullName}");
                continue;
            }
            var isAndroid = androidMask.Match(fi.Name);
            var isIOS = IOSMask.Match(fi.Name);
            var targetPath = isAndroid.Success ? parseAndroid(isAndroid, fi) :
                             isIOS.Success ? parseIOS(isIOS, fi) :
                             null;
            if (null == targetPath)
            {
                log($"unknown file metadata {fi.FullName}");
                continue;
            }
            yield return (fi, targetPath);
        }
    }

    foreach (var (fi, newName) in GetFileList())
    {
        var newFi = new FileInfo(newName);
        int i = 0;

        while (newFi.Exists)
            newFi = new FileInfo($"{newFi.FullName[..^newFi.Extension.Length]}({++i}){newFi.Extension}");

        if (!newFi.Directory.Exists)
            newFi.Directory.Create();
        try
        {
            fi.MoveTo(newFi.FullName);
        }
        catch (IOException e)
        {
            log($"IO err on moving {fi.FullName} to {newName}: {e.Message}");
        }

        await Task.Delay(0);
    }
    Task.WaitAll(subtasks.ToArray());
    Console.WriteLine($"Separated - {directory}");
}

SeparatePhotos(rootDir).Wait();

Console.WriteLine("done");
