using System.Text;
using Anarres.Core.Mpq;

namespace Anarres.Core.Tests;

public static class MpqTests
{
    public static void WritesAndReadsBack()
    {
        using TempFolder temp = new TempFolder();
        byte[] big = new byte[50000];
        new Random(7).NextBytes(big.AsSpan(0, 1000));
        byte[] text = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("Spell.dbc ", 2000)));
        string path = temp.File("patch-A.MPQ");
        MpqWriter.Write(path, new List<(string, byte[])>
        {
            ("DBFilesClient\\Spell.dbc", big),
            ("DBFilesClient/SpellIcon.dbc", text),
            ("Empty.txt", Array.Empty<byte>())
        });

        using MpqArchive archive = MpqArchive.Open(path);
        Assert.True(archive.Read("dbfilesclient\\spell.dbc").AsSpan().SequenceEqual(big), "names ignore case");
        Assert.True(archive.Read("DBFilesClient\\SpellIcon.dbc").AsSpan().SequenceEqual(text), "slashes become \\");
        Assert.Equal(0, archive.Read("Empty.txt").Length, "empty file");
        Assert.True(!archive.Contains("DBFilesClient\\Item.dbc"), "missing file");
        List<string> list = archive.ListFiles();
        Assert.True(list.Contains("DBFilesClient\\Spell.dbc") && list.Count == 3, "listfile");
        Assert.True(new FileInfo(path).Length < big.Length + text.Length, "files are compressed");
    }

    // The example from the format's description: 8 bytes that explode to "AIAIAIAIAIAIA"
    public static void ExplodesPkwareData()
    {
        byte[] packed = { 0x00, 0x04, 0x82, 0x24, 0x25, 0x8F, 0x80, 0x7F };
        Assert.Equal("AIAIAIAIAIAIA", Encoding.ASCII.GetString(PkwareExplode.Decompress(packed, 13)), "explode");
    }

    public static void OrdersClientArchives()
    {
        int common = ClientArchives.Priority("common", null);
        int lichKing = ClientArchives.Priority("lichking", null);
        int locale = ClientArchives.Priority("locale-enUS", "enUS");
        int patch = ClientArchives.Priority("patch", null);
        int patch3 = ClientArchives.Priority("patch-3", null);
        int patchA = ClientArchives.Priority("patch-A", null);
        int localePatch = ClientArchives.Priority("patch-enUS-3", "enUS");
        Assert.True(common < lichKing && lichKing < locale && locale < patch && patch < patch3 &&
                    patch3 < patchA && patchA < localePatch, "load order");
        Assert.Equal(-1, ClientArchives.Priority("backup", null), "other archives are not read");
    }

    public static void ReadsNewestCopyFromClientFolder()
    {
        using TempFolder temp = new TempFolder();
        string data = temp.Path;
        Directory.CreateDirectory(Path.Combine(data, "enUS"));
        MpqWriter.Write(Path.Combine(data, "enUS", "locale-enUS.MPQ"), new List<(string, byte[])>
        {
            ("DBFilesClient\\Spell.dbc", Encoding.ASCII.GetBytes("old")),
            ("DBFilesClient\\Item.dbc", Encoding.ASCII.GetBytes("item"))
        });
        MpqWriter.Write(Path.Combine(data, "patch-A.MPQ"), new List<(string, byte[])>
        {
            ("DBFilesClient\\Spell.dbc", Encoding.ASCII.GetBytes("new"))
        });

        using ClientArchives archives = ClientArchives.Open(data, "enUS");
        Assert.Equal("new", Encoding.ASCII.GetString(archives.TryRead("DBFilesClient\\Spell.dbc")!), "patch wins");
        Assert.Equal("item", Encoding.ASCII.GetString(archives.TryRead("DBFilesClient\\Item.dbc")!), "locale file");
        Assert.Equal(2, archives.ListDbcFiles().Count, "DBC files");
    }
}
