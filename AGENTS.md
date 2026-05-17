# Repository Guidelines

## Project Structure & Module Organization
`Flow.sln` is the solution entry point. The WPF app lives in `Flow/`. Keep domain models in `Flow/Models/`, application logic and persistence in `Flow/Services/`, observable state and commands in `Flow/ViewModels/`, and custom UI controls in `Flow/Views/Controls/`. `MainWindow.xaml` and `MainWindow.xaml.cs` remain the shell for app-wide styles, bindings, and window behavior. `bin/` and `obj/` are generated outputs and should not be edited or reviewed.

There is no test project or dedicated assets folder yet. If you add tests, place them in a sibling project such as `Flow.Tests/`.

## Build, Test, and Development Commands
Use the .NET CLI from the repository root:

- `dotnet restore Flow.sln` restores NuGet packages.
- `dotnet build Flow.sln` builds the .NET 9 WPF app.
- `dotnet run --project Flow/Flow.csproj` builds and launches in one step.
- `Start-Process .\Flow\bin\Debug\net9.0-windows\Flow.exe` relaunches the latest build without rebuilding.

Close any running `Flow.exe` before rebuilding; the output binary is locked while the app is open.

## Coding Style & Naming Conventions
Follow the existing MVVM structure and keep responsibilities separated: view logic in XAML/code-behind only when it is UI-specific, analysis and storage logic in services, and state mutation in view models. Use 4-space indentation in C# and preserve the current multiline alignment style in XAML.

Use file-scoped namespaces, `PascalCase` for public types and members, `_camelCase` for private fields, and suffixes such as `*ViewModel` and `*Service`. Prefer CommunityToolkit.Mvvm attributes like `[ObservableProperty]` and `[RelayCommand]` over manual boilerplate. No repo-level linter or `.editorconfig` is configured, so rely on IDE formatting and keep diffs tight.

## Testing Guidelines
Automated tests are not set up yet, so every change needs manual verification. At minimum, test item creation, drag/resize behavior, lane assignment, dependency arrows and validation errors, and save/load of `.flow` files.

When adding tests, use xUnit or the standard .NET test stack, name the project `Flow.Tests`, and use method names like `Analyze_WhenDependencyOverlaps_ReturnsError`.

## Commit & Pull Request Guidelines
Recent commits use short, imperative summaries focused on visible behavior, often in Japanese, for example `ActivityバーとサイドバーUIを追加`. Keep the same style: one scoped change per commit, with the affected area named in the subject when possible.

Pull requests should include a short description, linked issue or task, manual test notes, and screenshots or GIFs for UI changes. Call out any `.flow` format changes explicitly.
