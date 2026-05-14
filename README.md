# Shutdown Scheduler

Windows-only WPF utility for scheduling one shutdown-related action at a time.

## Build

```powershell
dotnet build
```

## Self-contained executable

```powershell
dotnet publish ".\Shutdown Scheduler.csproj" /p:PublishProfile=win-x64-self-contained
```

The publish profile creates a self-contained `Shutdown Scheduler.exe` under:

```text
bin\Release\net10.0-windows\publish\win-x64\
```

An installer wrapper can be added with Inno Setup or WiX around that published output.
