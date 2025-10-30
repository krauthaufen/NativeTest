# NativeTest

A .NET tool for inspecting and extracting native dependencies from .NET assemblies and NuGet packages.

## Installation

Install as a global .NET tool:

```bash
dotnet tool install -g NativeTest
```

## Usage

### Info Command

Display information about native dependencies in .dll or .nupkg files:

```bash
native-test info <file1.dll> <file2.nupkg> ...
```

Example:
```bash
native-test info MyAssembly.dll SomePackage.nupkg
```

### Unpack Command

Unpack native dependencies for a specific architecture and OS:

```bash
native-test unpack <directory> <architecture> <os> [--remove-zip]
```

**Arguments:**
- `directory` - Directory containing DLL files to process
- `architecture` - Target architecture: `x86`, `x64`, or `arm64`
- `os` - Target operating system: `macos`, `linux`, or `windows`
- `--remove-zip` or `-r` - (Optional) Remove the native.zip from DLLs after extracting

**Examples:**

Extract native dependencies without removing embedded zip:
```bash
native-test unpack ./bin/Release/net8.0 x64 linux
```

Extract and remove the embedded zip:
```bash
native-test unpack ./bin/Release/net8.0 x64 linux --remove-zip
```

## GitHub Actions Setup

To automatically publish to NuGet on release:

1. Add a `NUGET_KEY` secret to your GitHub repository with your NuGet API key
2. Update the `OWNER` in `.github/workflows/publish.yml` with your GitHub username/org
3. Update `PackageProjectUrl` and `RepositoryUrl` in the `.fsproj` file
4. Push changes to `RELEASE_NOTES.md` to trigger a build and publish