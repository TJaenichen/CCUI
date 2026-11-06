using CCUI.Core.Folders;

namespace CCUI.Core.Tests;

public sealed class FolderTreeTests
{
    [Fact]
    public void ASessionLivesInAtMostOneFolder()
    {
        var tree = new FolderTree();
        var work = tree.Add("Work");
        var home = tree.Add("Home");

        Assert.True(tree.Place("s1", work.Id));
        Assert.True(tree.Place("s1", home.Id));

        Assert.Empty(work.Sessions);
        Assert.Equal(["s1"], home.Sessions);
        Assert.Same(home, tree.FolderOf("S1"));

        Assert.True(tree.Place("s1", null));
        Assert.Null(tree.FolderOf("s1"));
        Assert.False(tree.Place("s2", "no-such-folder"));
    }

    [Fact]
    public void DeletingAFolderLiftsItsContentsToTheParent()
    {
        var tree = new FolderTree();
        var clients = tree.Add("Clients");
        var acme = tree.Add("Acme", clients.Id);
        var acmeApi = tree.Add("API", acme.Id);
        tree.Place("s1", acme.Id);
        tree.Place("s2", clients.Id);

        Assert.True(tree.Delete(acme.Id));

        Assert.Equal([acmeApi], clients.Children);
        Assert.Equal(["s2", "s1"], clients.Sessions);
        Assert.Null(tree.Find(acme.Id));
    }

    [Fact]
    public void DeletingATopLevelFolderUnplacesItsSessions()
    {
        var tree = new FolderTree();
        var work = tree.Add("Work");
        var sub = tree.Add("Sub", work.Id);
        tree.Place("s1", work.Id);

        Assert.True(tree.Delete(work.Id));

        Assert.Equal([sub], tree.Folders);
        Assert.Null(tree.FolderOf("s1"));
    }

    [Fact]
    public void AFolderCannotMoveIntoItsOwnSubtree()
    {
        var tree = new FolderTree();
        var a = tree.Add("A");
        var b = tree.Add("B", a.Id);
        var c = tree.Add("C", b.Id);
        var d = tree.Add("D");

        Assert.False(tree.Move(a.Id, a.Id));
        Assert.False(tree.Move(a.Id, c.Id));
        Assert.True(tree.Move(c.Id, d.Id));
        Assert.Equal([c], d.Children);
        Assert.Empty(b.Children);

        // Reordering within the same parent accounts for the gap the move leaves.
        Assert.True(tree.Move(d.Id, null, 0));
        Assert.Equal([d, a], tree.Folders);
        Assert.True(tree.Move(d.Id, null, 2));
        Assert.Equal([a, d], tree.Folders);
    }

    [Fact]
    public void TheVaultFolderIsInheritedDownTheTreeUntilOverridden()
    {
        var tree = new FolderTree();
        var omni = tree.Add("Omni");
        omni.VaultFolder = @"D:\vault\Omni";
        var factory = tree.Add("Dark factory", omni.Id);
        factory.VaultFolder = @"D:\vault\Omni\domains\road-to-dark-factory";
        var ccui = tree.Add("CCUI", factory.Id);
        var misc = tree.Add("Misc", omni.Id);
        var loose = tree.Add("Loose");

        Assert.Equal(@"D:\vault\Omni\domains\road-to-dark-factory", tree.EffectiveVaultFolder(ccui.Id));
        Assert.Equal(@"D:\vault\Omni", tree.EffectiveVaultFolder(misc.Id));
        Assert.Null(tree.EffectiveVaultFolder(loose.Id));
        Assert.Null(tree.EffectiveVaultFolder("unknown"));
    }

    [Fact]
    public void AnUnplacedSessionDefaultsToTheDeepestFolderWhoseDirectoryContainsIt()
    {
        var tree = new FolderTree();
        var work = tree.Add("Work");
        work.WorkingDirectory = @"D:\work";
        var ccui = tree.Add("CCUI", work.Id);
        ccui.WorkingDirectory = @"D:/work/CCUI/";
        var other = tree.Add("Other");
        other.WorkingDirectory = @"D:\work\CCUI-old";

        Assert.Same(ccui, tree.DefaultFolderFor(@"D:\work\CCUI\src"));
        Assert.Same(ccui, tree.DefaultFolderFor(@"d:\WORK\ccui"));
        Assert.Same(work, tree.DefaultFolderFor(@"D:\work\Ooze"));
        Assert.Same(other, tree.DefaultFolderFor(@"D:\work\CCUI-old\x"));
        Assert.Null(tree.DefaultFolderFor(@"C:\elsewhere"));
        Assert.Null(tree.DefaultFolderFor(null));
    }

    [Fact]
    public void PinsAndForgetKeepTheListsConsistent()
    {
        var tree = new FolderTree();
        var work = tree.Add("Work");
        tree.Place("s1", work.Id);
        tree.Pin("s1", true);
        tree.Pin("s1", true);

        Assert.True(tree.IsPinned("S1"));
        Assert.Single(tree.Pinned);

        Assert.True(tree.Forget("s1"));
        Assert.False(tree.IsPinned("s1"));
        Assert.Empty(work.Sessions);
        Assert.False(tree.Forget("s1"));
    }

    [Fact]
    public void TheStoreRoundTripsAndSurvivesABrokenFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "ccui-folders-" + Guid.NewGuid().ToString("N"), "folders.json");
        try
        {
            var store = new FolderStore(path);
            Assert.Empty(store.Load().Folders);

            var tree = new FolderTree();
            var work = tree.Add("Work");
            work.VaultFolder = @"D:\vault";
            work.IsExpanded = false;
            tree.Add("Sub", work.Id).WorkingDirectory = @"D:\work\sub";
            tree.Place("s1", work.Id);
            tree.Pin("s1", true);
            store.Save(tree);

            var loaded = store.Load();
            var loadedWork = Assert.Single(loaded.Folders);
            Assert.Equal(work.Id, loadedWork.Id);
            Assert.Equal(@"D:\vault", loadedWork.VaultFolder);
            Assert.False(loadedWork.IsExpanded);
            Assert.Equal(@"D:\work\sub", Assert.Single(loadedWork.Children).WorkingDirectory);
            Assert.Equal(["s1"], loadedWork.Sessions);
            Assert.True(loaded.IsPinned("s1"));

            File.WriteAllText(path, "{ not json");
            Assert.Empty(store.Load().Folders);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }
}
