# Distribution

ArmAware ships as an ARM64 Microsoft Store package; see [MSIX packaging](msix-packaging.md). This page covers unpackaged, self-contained folders for development and testing.

```powershell
dotnet publish src/ArmAware.App -c Release -p:Platform=ARM64 -r win-arm64 --self-contained true -o artifacts/ArmAware-arm64
dotnet publish src/ArmAware.Cli -c Release -r win-arm64 --self-contained true -o artifacts/ArmAware-cli-arm64
```

Both the .NET runtime and the Windows App SDK runtime travel with the application, so no certificate or runtime installation is needed. Run `ArmAware.App.exe` from the complete folder; copying only the EXE does not work. These outputs are unsigned, so Windows SmartScreen may flag them when downloaded. Settings and cache live in `%LOCALAPPDATA%\ArmAware`, separate from the published folder.

Microsoft's [self-contained deployment guide](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps) documents the `WindowsAppSDKSelfContained` property, and the [deployment overview](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/deploy-overview) compares packaging choices.

## Continuous integration

The workflow in `.github/workflows/build.yml` runs on `windows-latest`, builds and tests the solution, checks formatting, cross-compiles the ARM64 app and CLI, and builds an unsigned ARM64 MSIX. Tests run on the runner's own architecture, so a cross-build does not replace testing on an ARM64 device. The workflow uploads build artifacts to the run only; it never publishes a release or submits to the Store.
