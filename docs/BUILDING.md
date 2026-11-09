# Building

## Prerequisites

- The .NET 11 SDK. `global.json` asks for 11.0.100 with roll-forward to the latest major, prereleases allowed.
- Windows 10 build 22621 or later for the app project. Core and the tests build and run on any OS with the SDK.
- For Native AOT publishes on Windows: the standard AOT prerequisites for your architecture, including Visual Studio's C++ workload on x64. The GitHub runners already have them.

## Commands

Restore and build:

```
dotnet restore ReactorWinFile.slnx
dotnet build src/ReactorWinFile/ReactorWinFile.csproj -c Release -p:Platform=x64
dotnet build src/WinFile.Core/WinFile.Core.csproj -c Release
```

The app project requires an explicit platform, x64 or ARM64. That matches Reactor's own guidance. The Core and test projects ignore the platform.

Run the tests:

```
dotnet test tests/WinFile.UnitTests -c Release
dotnet test tests/WinFile.FunctionalTests -c Release
dotnet test tests/WinFile.IntegrationTests -c Release
dotnet test tests/WinFile.E2ETests -c Release
```

Run the app from source:

```
dotnet run --project src/ReactorWinFile -p:Platform=x64
dotnet run --project src/ReactorWinFile -p:Platform=x64 -- search C:\work "*.md"
```

The second form reaches console mode through the same binary entry point.

## Publish

One architecture per command:

```
dotnet publish src/ReactorWinFile/ReactorWinFile.csproj -c Release -r win-x64 -p:Platform=x64 -p:PublishAot=true --self-contained true -o out/win-x64
dotnet publish src/ReactorWinFile/ReactorWinFile.csproj -c Release -r win-arm64 -p:Platform=ARM64 -p:PublishAot=true --self-contained true -o out/win-arm64
```

Build ARM64 on an ARM64 machine or runner. Cross-architecture AOT is not the supported path.

## Verify a publish

```
out\win-x64\winfile.exe --self-test result.txt
type result.txt
out\win-x64\winfile.exe version
out\win-x64\winfile.exe ls C:\work
```

`--self-test` copies, searches, and renames inside a temp tree and writes PASS or FAIL to the result file. CI runs it on every publish.

## CI

`.github/workflows/build.yml` has two jobs. The test job builds the solution on windows-latest and runs all four test projects. The publish job runs per architecture on a matching runner, publishes with Native AOT, smoke tests the shipped binary, and uploads it as `winfile-win-x64` or `winfile-win-arm64`.
