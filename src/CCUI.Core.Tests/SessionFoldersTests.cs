using CCUI.Core.Claude;
using CCUI.Core.Folders;
using CCUI.Core.ViewModels;

namespace CCUI.Core.Tests;

public sealed class SessionFoldersTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly HashSet<string> None = [];

    private static SessionSummary S(string id, string cwd, int minutesAgo = 0) =>
        new() { SessionId = id, TranscriptPath = id, WorkingDirectory = cwd, LastActive = Now.AddMinutes(-minutesAgo) };

    [Fact]
    public void PlacedSessionsShowInTheirFolderRulesCatchTheRestAndOtherTakesTheLeftovers()
    {
        var store = new MemoryFolderStore();
        var work = store.Tree.Add("Work");
        store.Tree.Add("CCUI", work.Id).WorkingDirectory = @"D:\work\CCUI";
        store.Tree.Place("placed", work.Id);
        var list = new SessionListViewModel(store, new FakeDialogs(), null);

        list.Update([S("placed", @"C:\elsewhere", 1), S("ruled", @"D:\work\CCUI\src", 2), S("loose", @"C:\x", 3)], None, None, _ => null, Now);

        Assert.True(list.HasFolders);
        var workNode = Assert.IsType<FolderViewModel>(list.Roots[0]);
        var other = Assert.IsType<FolderViewModel>(list.Roots[1]);
        Assert.Equal(2, list.Roots.Count);
        Assert.Equal("Work", workNode.Name);
        var ccuiNode = Assert.IsType<FolderViewModel>(workNode.Children[0]);
        Assert.Equal("placed", Assert.IsType<SessionListItemViewModel>(workNode.Children[1]).SessionId);
        Assert.Equal("ruled", Assert.IsType<SessionListItemViewModel>(Assert.Single(ccuiNode.Children)).SessionId);
        Assert.True(other.IsOther);
        Assert.Equal("loose", Assert.IsType<SessionListItemViewModel>(Assert.Single(other.Children)).SessionId);
        Assert.Equal(2, workNode.SessionCount);
        Assert.Equal(1, ccuiNode.Depth);
        Assert.Same(workNode, list.FolderOf(list.Find("placed")!));
        Assert.Null(list.FolderOf(list.Find("ruled")!));

        // Filtering flattens the list; clearing the filter brings the tree back with the same nodes.
        list.FilterText = "loose";
        Assert.Equal(["loose"], list.Roots.Cast<SessionListItemViewModel>().Select(i => i.SessionId));
        list.FilterText = string.Empty;
        Assert.Same(workNode, list.Roots[0]);
    }

    [Fact]
    public void WithoutFoldersTheTreeIsTheFlatList()
    {
        var list = new SessionListViewModel();
        list.Update([S("a", @"C:\a"), S("b", @"C:\b", 1)], None, None, _ => null, Now);

        Assert.False(list.HasFolders);
        Assert.Equal(["a", "b"], list.Roots.Cast<SessionListItemViewModel>().Select(i => i.SessionId));
    }

    [Fact]
    public void FolderEditsMovesAndPinsPersistThroughTheStore()
    {
        var store = new MemoryFolderStore();
        var dialogs = new FakeDialogs { PromptResult = "Clients" };
        var list = new SessionListViewModel(store, dialogs, null);
        list.Update([S("a", @"C:\a"), S("b", @"C:\b", 1)], None, None, _ => null, Now);

        list.NewFolderCommand.Execute(null);
        var clients = Assert.Single(list.FolderList);
        Assert.Equal("Clients", clients.Name);
        Assert.Equal(1, store.Saves);
        Assert.Same(clients, list.SelectedItem);

        var a = list.Find("a")!;
        var b = list.Find("b")!;
        list.MoveSession(a, clients);
        list.MoveSession(b, clients);
        Assert.Equal(["a", "b"], clients.Children.Cast<SessionListItemViewModel>().Select(i => i.SessionId));
        Assert.Single(list.Roots); // nothing is left for "Other"

        list.TogglePinCommand.Execute(b);
        Assert.True(b.IsPinned);
        Assert.Equal(["b", "a"], clients.Children.Cast<SessionListItemViewModel>().Select(i => i.SessionId));

        dialogs.PromptResult = "Customers";
        list.RenameFolderCommand.Execute(clients);
        Assert.Equal("Customers", clients.Name);
        Assert.Equal("Customers", store.Tree.Folders[0].Name);

        clients.IsExpanded = false;
        Assert.False(store.Tree.Folders[0].IsExpanded);

        var savesBefore = store.Saves;
        list.DeleteFolderCommand.Execute(clients);
        Assert.Single(dialogs.Confirmations);
        Assert.Equal(savesBefore + 1, store.Saves);
        Assert.False(list.HasFolders);
        Assert.Equal(["a", "b"], list.Roots.Cast<SessionListItemViewModel>().Select(i => i.SessionId)); // the flat list is by recency
    }

    [Fact]
    public void AClearedPaneKeepsItsFolderAndPinUnderItsNewId()
    {
        var store = new MemoryFolderStore();
        var work = store.Tree.Add("Work");
        var list = new SessionListViewModel(store, new FakeDialogs(), null);
        list.Place("old", work.Id);
        store.Tree.Pin("old", true);

        list.SessionIdChanged("old", "new");

        Assert.Same(work, store.Tree.FolderOf("new"));
        Assert.Null(store.Tree.FolderOf("old"));
        Assert.True(store.Tree.IsPinned("new"));
        Assert.False(store.Tree.IsPinned("old"));
    }

    [Fact]
    public void NewSessionInAFolderIsRequestedFromTheShell()
    {
        var list = new SessionListViewModel(new MemoryFolderStore(), new FakeDialogs { PromptResult = "Work" }, null);
        list.NewFolderCommand.Execute(null);
        FolderViewModel? requested = null;
        list.NewSessionRequested += (_, folder) => requested = folder;

        list.NewSessionInCommand.Execute(list.FolderList[0]);

        Assert.Same(list.FolderList[0], requested);
    }

    private sealed class FakeDialogs : IDialogService
    {
        public string? PromptResult { get; set; }

        public string? FolderResult { get; set; }

        public List<string> Confirmations { get; } = [];

        public string? PickFolder(string title, string? initialDirectory) => FolderResult;

        public string? PickFile(string title, string filter, string? initialPath) => null;

        public string? Prompt(string title, string label, string? initial) => PromptResult;

        public bool Open(string target) => false;

        public bool Confirm(string title, string message)
        {
            Confirmations.Add(message);
            return true;
        }

        public void ShowError(string title, string message)
        {
        }
    }
}
