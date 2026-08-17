# Flow

Flow is a WPF timeline editor and a reusable `.flow` project library.

## NuGet

The package is intended for Windows WPF applications targeting `net9.0-windows`.
It exposes `Flow.Services.FlowProjectService` for reading and writing `.flow`
files and `Flow.Views.Controls.FlowWorkspaceControl` for embedding the timeline
and inspector without Flow's window title bar.

```xml
<PackageReference Include="Flow.Editor" Version="0.1.5" />
```

```xml
<flow:FlowWorkspaceControl FlowFilePath="C:\Projects\sample.flow" />
```

```csharp
var workspace = new FlowWorkspaceControl(filePath);
hostGrid.Children.Add(workspace);
```

The package contains the current WPF editor assembly, so hosts must run on
Windows with WPF enabled. The standalone Flow application remains available
for users who want the full title bar and file dialogs.
