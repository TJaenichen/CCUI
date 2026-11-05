# Plugins

A plugin is a .NET DLL that CCUI loads at start. It can put its own WPF panels above or below the session list and
follow what the sessions do: which ones are open, their state, and every prompt, response, thinking block, tool call,
tool result and system message as it lands in a transcript.

## Where plugins live

`%LOCALAPPDATA%\CCUI\plugins\<Name>\`: one folder per plugin, holding the plugin DLL (named like the folder by
preference), its `deps.json` and any dependencies. Extra folders can be listed under `Plugins:Directories`;
`Plugins:Enabled=false` turns loading off. The settings page has a Plugins section that shows the folder and which
plugins were found, loaded or failed.

Plugins are found by type: every class implementing `CCUI.Plugins.ICcuiPlugin` is created with its parameterless
constructor, on the UI thread, and `Initialize` is called with an `IPluginHost`. A plugin that throws is listed with
its error and does not affect the others. A plugin's handler that throws during an event is logged and skipped.

## The contract

`src/CCUI.Plugins` is the whole contract; a plugin references that project (or the `CCUI.Plugins.dll` next to the
installed app) and nothing else of CCUI.

```csharp
public interface ICcuiPlugin
{
    string Name { get; }
    void Initialize(IPluginHost host);
}
```

`IPluginHost` gives a plugin:

- `Logger`: an `ILogger` named `Plugin.<Name>`.
- `Settings`: the configuration section `PluginSettings:<Name>`, so a plugin's settings go in `appsettings.user.json`
  like everything else (and can be set with `--PluginSettings:<Name>:Key=value` or `CCUI_PluginSettings__<Name>__Key`).
- `Time`: a `TimeProvider`.
- `DataDirectory`: `%LOCALAPPDATA%\CCUI\plugin-data\<Name>`, created on first use.
- `Sessions`: the open session panes as `PluginSession` records (pane id, session id, title, working directory,
  activity, attention flag, exited).
- Events, all raised on the UI thread: `SessionOpened`, `SessionChanged` (title, session id, activity, attention or
  exit state changed), `SessionClosed`, and `ClaudeEvent` with a `ClaudeEventKind` of `Prompt`, `Response`,
  `Thinking`, `ToolCall` (tool name and input JSON), `ToolResult` (result text, error flag, duration) or `System`.
  Events from subagents carry an `AgentId`.
- `AddSidebarPanel(SidebarPanel)`: shows a WPF element above (`SidebarSlot.Top`) or below (`SidebarSlot.Bottom`) the
  session list, with an optional caption. The app's theme brushes are in `Application.Current.Resources`
  (`AccentBrush`, `AccentColor`, `TextPrimaryBrush`, `TextSecondaryBrush`, `TextMutedBrush`, `PanelBrush`, `MonoFont`,
  and `AnimationsEnabled`, a bool); `SetResourceReference` keeps an element following them when the settings change.

## Writing one

A plugin project is a WPF class library:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <EnableDynamicLoading>true</EnableDynamicLoading>
  </PropertyGroup>
  <ItemGroup>
    <!-- Private=false: the contract ships with CCUI; a copy in the plugin folder would be ignored anyway. -->
    <ProjectReference Include="..\..\CCUI\src\CCUI.Plugins\CCUI.Plugins.csproj" Private="false" />
  </ItemGroup>
</Project>
```

Build it and copy the output folder to `%LOCALAPPDATA%\CCUI\plugins\<Name>\`. CCUI loads each plugin in its own
`AssemblyLoadContext`: assemblies the app already has (the contract, WPF, `Microsoft.Extensions.*`) are shared so
types unify; everything else is resolved from the plugin folder through its `deps.json`.

Two things to know about that isolation:

- XAML compiled into a plugin cannot be loaded by its pack URI (`/Plugin;component/View.xaml`), because WPF looks
  the assembly up in the default context. Build views in code, or load XAML with `XamlReader` from a file or
  embedded resource. The same goes for images: load them from a file next to the DLL rather than by pack URI.
- A plugin that ships its own copy of a package the app also has gets the app's version.

The App test suite has an example (`PluginTests.cs`): a plugin that adds a panel and logs every event, loaded from
a temporary folder, with a second plugin that fails on purpose.
