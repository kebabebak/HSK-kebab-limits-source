# HSKKebabLimits — build kit

Files to compile `HSKKebabLimits.dll` for RimWorld HSK 1.5 / 1.6.

## Requirements

- [.NET SDK](https://dotnet.microsoft.com/download) (builds `net472` for 1.5, `net48` for 1.6)
- Harmony and RimWorld refs from NuGet (`Lib.Harmony`, `Krafs.Rimworld.Ref`)
- Place Multiplayer API assembly at `libs\0MultiplayerAPI.dll` (compile-time reference; soft at runtime)

## Build

```powershell
dotnet build HSKKebabLimits.csproj -c Release
```

For RimWorld 1.6:

```powershell
dotnet build HSKKebabLimits.csproj -c Release16
```

Output: `out\HSKKebabLimits.dll`
