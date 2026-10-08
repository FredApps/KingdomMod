# ReskinPack regression checks

Run with a .NET 8 or later SDK:

```powershell
dotnet run --project tests/ReskinPack/ReskinPack.Tests.csproj -c Release
```

The runner compiles the actual `ReskinMod.cs` and `PackApi.cs` against a small
simulated Unity/MelonLoader boundary. It checks changing animation frames,
late-spawned/re-enabled renderers, scene transitions, original pivot/scale,
cached variants, asset lifetime flags, and match diagnostics. It needs no game
references and is not part of the deployed mod solution.

This does not validate Unity's real animation callback order, PNG decoding,
shaders, or the visible result in-game. For the live check, install user-created
replacement frames for the active monarch, enter an island, walk, and confirm
both the image and the `Matched sprite` entries in `MelonLoader/Latest.log`.
