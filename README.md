# KKVR Hand Hair Collider

This project is a local BepInEx plugin for the original `KoikatuVR.exe`.
It makes tracked Quest/SteamVR controllers interact with hair DynamicBone
chains in the original VR game. It does not require CharaStudio and does not
replace the game's DynamicBone implementation.

The current installed version is **0.5.0**. The project is experimental but
has passed focused automated tests and a runtime smoke test in the local game
installation.

This is an unofficial community project. It is not affiliated with or
endorsed by Illusion. The game, BepInEx, VRTK, and DynamicBone are not included.

## Scope And Definition Of Done

The current goal is:

- discover the original Koikatu VR VRTK controller transforms;
- create small spherical `DynamicBoneCollider` objects on the controllers;
- bind those colliders to character hair and optional accessory DynamicBones;
- apply a small velocity-based force only near the controller;
- keep hair from entering the character head with a head capsule collider;
- restore transient force and reset particles after contact ends.

The current goal is **not** a general cloth simulator, a mesh-level fabric
solver, or a grab/hold system.

## Installation

Download `KKVRHandHairCollider.dll` from the GitHub Releases page and place it
in the game directory as follows:

```text
Koikatu/
  KoikatuVR.exe
  BepInEx/
    plugins/
      KKVRHandHairCollider/
        KKVRHandHairCollider.dll
```

Start the original `KoikatuVR.exe`. On first load, BepInEx creates:

```text
BepInEx/config/local.kkvr.handhaircollider.cfg
```

The plugin is intended for the original VRTK-based Koikatu VR executable. It
has not been verified with CharaStudio, VRGIN builds, or other KKVR variants.

Old installed plugin binaries are kept beside the current DLL with a `.bak`
suffix. BepInEx does not load those backup files.

## Architecture

### Runtime integration

`src/Plugin.cs` is the BepInEx entry point. It is restricted to the process:

```text
[BepInProcess("KoikatuVR")]
```

Controller lookup uses the original game's APIs:

1. `VRTK_DeviceFinder.GetControllerLeftHand/RightHand`;
2. fallback to `VRViveControllerManager.GetTransform(0/1)`.

No VRGIN controller API is used. This matters because the installed game is
the original VRTK-based `KoikatuVR.exe`, not a VRGIN build.

For each discovered controller, the plugin creates or reuses a child
`DynamicBoneCollider` with a spherical radius. It scans loaded characters once
per configured scan interval and finds DynamicBone variants under:

- `ChaControl.objHair`;
- `ChaControl.objAccessory` when accessory binding is enabled.

The supported DynamicBone types are:

- `DynamicBone`;
- `DynamicBone_Ver01`;
- `DynamicBone_Ver02`.

Bindings are planned by `BindingPlanner` and added only when the pair does not
already exist. `ColliderSourceSelector` keeps tracked controller colliders as
the default source and can optionally include character hand colliders.

### Force and recovery

`ControllerMotionState` samples controller movement and smooths velocity.
`ForceFieldMath` computes a bounded force using these rules:

- below the minimum speed: no force;
- inside the controller sphere: full force;
- in the short contact-padding shell: linear falloff;
- outside the shell: zero force;
- fast movements: capped by `Maximum force`.

The force is applied to the DynamicBone's existing force field and is not
permanently written into the character. When contact ends, the original force
is restored. After one quiet second, `ResetParticlesPosition()` is called to
recover from a possible raised/stuck hair state. Scene changes and plugin
disable/destroy also clear transient force.

### Head collision

For each loaded character, a capsule-shaped `DynamicBoneCollider` is created
near the head bone (`cf_j_head`, `cf_J_Head`, or `cf_s_head`, with a fallback to
the head root). It is bound to discovered hair DynamicBones so hair is less
likely to pass through the skull.

## Current Configuration

The live configuration was migrated to tuning version `2` with these values:

```ini
[Controller collision]
Enabled = true
Radius meters = 0.035

[Controller force]
Enabled = true
Contact padding meters = 0.008
Strength = 0.018
Maximum force = 0.04
Minimum speed meters per second = 0.15
Velocity smoothing = 0.35

[General]
Enabled = true
Include accessory Dynamic Bones = true
Scan interval seconds = 1

[Head collision]
Enabled = true
Radius meters = 0.075
Height meters = 0.1
Center Y meters = 0.015
```

The old line `Influence radius meters = 0.2` may still be present in the
configuration from an earlier prototype. Version 0.5 no longer reads it; the
effective interaction distance is controller radius plus contact padding.

The character-hand options are disabled by default. They are fallback tools,
not the normal Quest controller path:

```ini
[Character hands]
Include character hand colliders = false
Create fallback hand colliders = true
```

## Verification Evidence

### Automated tests

Run from the repository root:

```powershell
dotnet run --project '.\tests\KKVRHandHairCollider.Tests.csproj' -p:GameDir='D:\Games\Koikatu'
```

The current suite has **12 passing tests** covering:

- both-controller binding cross-product;
- skipping existing bindings;
- one-controller operation;
- duplicate input removal;
- empty input handling;
- controller-first source selection;
- optional character-hand inclusion;
- collider-source deduplication;
- force falloff with distance;
- slow tracking-drift rejection;
- maximum-force limiting;
- zero force outside the contact shell.

There is no configured coverage tool, so no coverage percentage has been
claimed.

### Release build

```powershell
dotnet build '.\src\KKVRHandHairCollider.csproj' -c Release -p:GameDir='D:\Games\Koikatu'
```

The last verified release build targeted `net35` and completed with 0 warnings
and 0 errors.

`GameDir` must point to a local Koikatu installation containing BepInEx and
`KoikatuVR_Data/Managed`. It can also be supplied through the
`KOIKATU_GAME_DIR` environment variable:

```powershell
$env:KOIKATU_GAME_DIR = 'D:\Games\Koikatu'
dotnet build '.\src\KKVRHandHairCollider.csproj' -c Release
```

### Runtime smoke test

The local BepInEx log showed:

- plugin `0.5.0` loaded successfully;
- original-VR left and right controllers discovered;
- controller DynamicBone colliders created;
- character 0 head collider created;
- one observed character registered 20 hair DynamicBones and received 60
  controller/head bindings;
- one additional accessory DynamicBone received 3 bindings;
- no plugin binding exceptions while the character scene was active;
- `KoikatuVR.exe` remained responsive.

Manual headset feedback also confirmed visible hair movement. Earlier tuning
had an overly large activation region; version 0.5 reduced the controller
sphere and contact shell. A later stuck-raised-hair report led to the quiet
reset behavior described above.

The remaining manual check is visual tuning in the headset: move a controller
slowly through bangs or side hair and confirm that movement starts only near
contact, then confirm that hair returns after the controller leaves.

## Known Limitations

- This is a hair/accessory plugin. Clothing is not currently scanned or
  modified.
- The force field is per DynamicBone component, not a fully local per-particle
  hand solver. A single chain may therefore respond more broadly than the
  exact touched strands.
- Controller collisions depend on the outfit's existing DynamicBone setup.
  Static or ordinary skinned clothing cannot become cloth merely by adding a
  collider.
- No true grabbing, pinning, friction, or cloth-tearing behavior exists.
- No automated visual/headset test exists; runtime visual strength remains a
  subjective manual check.
- The plugin targets the original `KoikatuVR.exe` process and should not be
  assumed to work unchanged in CharaStudio, KKVR variants, or VRGIN builds.

## Clothing / Skirt Investigation Status

The installed `Assembly-CSharp.dll` was inspected without changing the game.
It provides:

- `ChaInfo.objClothes`, an array of clothing GameObjects;
- `Studio.AddObjectFemale.GetSkirtDynamic(...)`, which searches skirt objects
  for `DynamicBone` components;
- `UnityEngine.Cloth` in `UnityEngine.dll`, but no direct clothing `Cloth`
  fields or methods were found in `Assembly-CSharp.dll`.

This indicates that many skirts are likely DynamicBone chains rather than a
general Unity Cloth simulation. A practical next phase is therefore:

1. discovery-only logging of DynamicBones under `objClothes`;
2. optional binding of controller colliders to detected skirt chains;
3. body/hip/thigh collision protection;
4. conservative upward contact force and automatic recovery;
5. testing across several outfits before enabling clothing by default.

Do not describe this clothing work as implemented until the runtime log and
headset behavior have both been verified.

## Safe Handoff Procedure For A Later AI

1. Read this file and `VALIDATION.md` before editing.
2. Confirm the game path and inspect whether `KoikatuVR.exe` is running.
3. Do not copy a DLL over a running plugin; stop the game first or use a new
   backup filename.
4. Add or update focused tests before production code for behavior changes.
5. Build the `net35` release and run the test executable/project.
6. Preserve existing `.bak` files and unrelated user changes.
7. Launch the original executable only after the DLL copy succeeds, then check
   BepInEx logs for load errors and binding counts.
8. Record new runtime observations in `VALIDATION.md` and update this README's
   version and limitations.

## File Map

```text
KKVRHandHairCollider/
  README.md                         This handoff document
  VALIDATION.md                     Detailed v0.5 verification history
  src/
    Plugin.cs                       BepInEx runtime integration
    BindingPlanner.cs               Pure binding and force math helpers
    KKVRHandHairCollider.csproj     net35 release project
  tests/
    Program.cs                      Focused executable test suite
    KKVRHandHairCollider.Tests.csproj
```

## License

The plugin source is licensed under the MIT License. See `LICENSE`. No game or
third-party assemblies are redistributed by this repository.
