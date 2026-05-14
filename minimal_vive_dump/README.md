# Minimal Vive Pose Dump

This is a small .NET console app that uses the repository's existing OpenVR C# binding to connect to the SteamVR/OpenVR runtime and print tracked-device positions and quaternions with meaningful time fields.

## Status

This is an early development build.

It may be buggy, and it has not been revalidated on real HTC Vive hardware in this workspace. Treat the published Windows bundle as a practical MVP rather than a polished release.

## Prerequisites

- Windows x64
- .NET 5 SDK or newer
- SteamVR / OpenVR runtime installed
- A tracked device visible to SteamVR

The project already links the generated binding at `../headers/openvr_api.cs` and copies the native `openvr_api.dll` from `../bin/win64/openvr_api.dll` into the output folder.

## Build

```powershell
dotnet build .\minimal_vive_dump\minimal_vive_dump.csproj
```

## Publish Windows Bundle

Create a self-contained Windows x64 bundle plus a zip file under `releases/`:

```powershell
.\minimal_vive_dump\publish-windows.ps1
```

That script produces:

- `releases\vive-pose-dump-win-x64\`
- `releases\vive-pose-dump-win-x64.zip`

The published folder contains the executable, the native `openvr_api.dll`, this README, and the repository license.

## Run

Single snapshot:

```powershell
dotnet run --project .\minimal_vive_dump\minimal_vive_dump.csproj -- --once
```

Continuous stream at the default 200 ms interval (5 Hz):

```powershell
dotnet run --project .\minimal_vive_dump\minimal_vive_dump.csproj
```

Continuous stream with a custom interval:

```powershell
dotnet run --project .\minimal_vive_dump\minimal_vive_dump.csproj -- --interval-ms 20
```

Single snapshot plus CSV append:

```powershell
dotnet run --project .\minimal_vive_dump\minimal_vive_dump.csproj -- --once --csv .\vive-poses.csv
```

Published executable:

```powershell
.\releases\vive-pose-dump-win-x64\vive-pose-dump.exe --once --csv .\vive-poses.csv
```

Built-in help:

```powershell
.\releases\vive-pose-dump-win-x64\vive-pose-dump.exe --help
```

Example output:

```text
utc=2026-05-13T18:21:07.1234567+00:00    elapsed=1.400s    counter=1549283478211    freq=10000000    device=0    type=TrackedDeviceClass_HMD    serial=LHR-XXXXXXXX    pos=(0.0123, 1.4567, -0.0890)    quat=(0.99870, 0.00040, 0.05085, -0.00120)
```

CSV columns:

```text
timestamp_utc,elapsed_seconds,counter_ticks,counter_frequency_hz,device_index,device_class,serial_number,pos_x,pos_y,pos_z,quat_w,quat_x,quat_y,quat_z
```

The app now keeps both forms of time:

- `timestamp_utc` is human-readable wall-clock time.
- `elapsed_seconds` is time since this capture session started.
- `counter_ticks` and `counter_frequency_hz` are still included if you want the old raw performance-counter signal.

If SteamVR is not installed, startup will fail with an OpenVR initialization error similar to `Init_PathRegistryNotFound`.