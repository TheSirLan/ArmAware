# Contributing

Use Windows 11 and the .NET 10 SDK selected by `global.json`. Restore with `dotnet restore ArmAware.slnx`, build with `dotnet build ArmAware.slnx`, and run `dotnet test tests/ArmAware.Tests`. Desktop builds use Windows App SDK restored from NuGet. Visual Studio with WinUI support is useful but not required.

Keep domain and classification logic in Core, platform discovery in Windows, package metadata in Winget, and presentation logic in App/ViewModels. The CLI and app must continue sharing the scanner. Tests live in one project.

Use nullable reference types, descriptive names, cancellation and async APIs. Treat registry fields, PE files, package manifests and URLs as untrusted. Never load inspected assemblies or execute package-supplied commands. Keep incomplete evidence Unknown. Add focused tests for detection/classification changes, including a case that prevents false recommendations. Generated fixtures must not contain executable instructions or copyrighted vendor binaries.

Run `dotnet format whitespace ArmAware.slnx` and verify with `dotnet format whitespace ArmAware.slnx --verify-no-changes`. Build the desktop app with `-p:Platform=ARM64`. The app and CLI support ARM64 builds only. Test on a real ARM64 Windows device; retain x86/x64 PE inspection tests for emulated applications.

Pull requests should explain the concrete behaviour change, evidence, validation and limitations. Include screenshots for UI changes after manual inspection. Keep unrelated changes out of the patch. No telemetry, automatic installation, startup/background services or cloud backend are in scope. Do not publish packages/releases as part of routine contributions. Never attach an unredacted local inventory to an issue.

