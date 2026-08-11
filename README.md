# KKVR Hand Hair Collider

This project is a local BepInEx plugin for the original `KoikatuVR.exe`.
It makes tracked Quest/SteamVR controllers interact with hair, accessory, and
skirt DynamicBone chains, plus clothing that already uses Unity Cloth. It does
not require CharaStudio and does not replace either physics implementation.

The current installed version is **0.6.1**. The project is experimental but
has passed focused automated tests and a runtime smoke test in the local game
installation.

This is an unofficial community project. It is not affiliated with or
endorsed by Illusion. The game, BepInEx, VRTK, and DynamicBone are not included.

## Scope And Definition Of Done

The current goal is:

- discover the original Koikatu VR VRTK controller transforms;
- create small spherical `DynamicBoneCollider` objects on the controllers;
- bind those colliders to character hair, accessories, and detected skirt chains;
- append separate controller spheres to existing Unity Cloth components;
- apply a small velocity-based force only near the controller;
- let a nearby controller grip latch and pull one sampled point without an
  initial snap, then release on button-up or excessive stretch;
- keep hair from entering the character head with a head capsule collider;
- keep skirts outside the legs by reusing or creating thigh capsules;
- restore transient force and reset particles after contact ends.

The current goal is **not** a new cloth solver, automatic physics for static
meshes, per-particle pinning, or replacement body physics. Breast and hip
physics remain owned by the game and installed physics plugins.

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
- `ChaControl.objAccessory` when accessory binding is enabled;
- clothing slots 0 and 1 in `ChaControl.objClothes` when a chain contains a
  recognized KK skirt bone.

The supported DynamicBone types are:

- `DynamicBone`;
- `DynamicBone_Ver01`;
- `DynamicBone_Ver02`.

Bindings are planned by `BindingPlanner` and added only when the pair does not
already exist. Existing `KK_Colliders` thigh colliders are reused. If none are
present, compatible upper/lower-thigh capsules are created as a fallback.
`ColliderSourceSelector` keeps tracked controller colliders as the default
source and can optionally include existing `KK_Colliders` hand, forearm, and
upper-arm colliders. Breast, hip, and leg collider names are not accepted by
that optional arm-collider path.

Unity Cloth uses a separate Unity `SphereCollider` on each controller. The
plugin appends these as `ClothSphereColliderPair` entries without replacing
the garment's existing sphere or capsule arrays.

### Force and recovery

`ControllerMotionState` samples controller movement and smooths velocity.
Contact distance is measured against up to 24 evenly distributed transforms
from each chain instead of only its tip. This catches skirt and long-hair
contact near the middle while bounding per-frame collision work.
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

Skirt chains use independent conservative defaults: strength `0.012` and
maximum force `0.025`, below the hair defaults of `0.018` and `0.04`.

### Grip interaction

The installed VRTK runtime exposes `VRTK_ControllerEvents.gripPressed`. When
grip is held inside the same `0.043 m` contact boundary, the nearest sampled
point is latched with its current controller-relative offset. Keeping that
offset prevents a snap on the first frame. Controller movement then adds a
bounded pull toward the moving anchor; a `0.005 m` dead zone filters tracking
jitter and a `0.22 m` anchor error releases the chain automatically.

The grab path only targets the already discovered hair, accessory, and skirt
DynamicBones. It does not scan `ChaInfo.dictDynamicBoneBust`, alter breast or
hip parameters, intercept VRTK events, or replace the original solver. A skirt
grab remains capped by the lower `0.025` clothing limit.

### Head collision

For each loaded character, a capsule-shaped `DynamicBoneCollider` is created
near the head bone (`cf_j_head`, `cf_J_Head`, or `cf_s_head`, with a fallback to
the head root). It is bound to discovered hair DynamicBones so hair is less
likely to pass through the skull.

## Current Configuration

The live configuration is migrated to tuning version `4` with these values:

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
Include skirt Dynamic Bones = true
Scan interval seconds = 1

[Clothing force]
Enabled = true
Strength = 0.012
Maximum force = 0.025

[Grab interaction]
Enabled = true
Strength = 0.2
Maximum force = 0.04
Dead zone meters = 0.005
Maximum stretch meters = 0.22

[Skirt body collision]
Enabled = true

[Unity Cloth]
Enabled = true

[Head collision]
Enabled = true
Radius meters = 0.075
Height meters = 0.1
Center Y meters = 0.015
```

The old line `Influence radius meters = 0.2` may still be present in the
configuration from an earlier prototype. Version 0.5 and later do not read it; the
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

The current suite has **22 passing tests** covering:

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
- zero force outside the contact shell;
- standard/modded skirt-bone classification and non-skirt rejection;
- nearest-point force across a sampled chain;
- invalid physics-input rejection;
- contact sample endpoint preservation and the 24-point budget;
- no-snap grab latching, bounded pulling, and maximum-stretch release;
- exact reuse and exclusion rules for character arm colliders;
- 16 deterministic 90 Hz trajectories totaling 320,000 velocity-force steps,
  plus 100,000 grab-force boundary samples.

There is no configured coverage tool, so no coverage percentage has been
claimed.

### Release build

```powershell
dotnet build '.\src\KKVRHandHairCollider.csproj' -c Release -p:GameDir='D:\Games\Koikatu'
```

The 0.6.1 release build targeted `net35` and completed with 0 warnings and 0
errors. Assembly inspection confirmed CLR runtime `v2.0.50727` and only the
expected game, BepInEx, Unity, and framework references.

`GameDir` must point to a local Koikatu installation containing BepInEx and
`KoikatuVR_Data/Managed`. It can also be supplied through the
`KOIKATU_GAME_DIR` environment variable:

```powershell
$env:KOIKATU_GAME_DIR = 'D:\Games\Koikatu'
dotnet build '.\src\KKVRHandHairCollider.csproj' -c Release
```

### Runtime smoke test

The unattended 0.6.1 startup smoke tests showed:

- BepInEx loaded `KKVR Hair and Clothing Interaction 0.6.1` in both a 40-second
  batch/nographics run and a 45-second normal VR run;
- the live configuration migrated to tuning version 4 and persisted all five
  grab settings;
- no plugin error, `TypeLoadException`, `MissingMethodException`, or character
  binding failure occurred;
- each process stayed alive for its observation window and was stopped by its
  exact PID;
- the release and deployed DLL SHA-256 values both equal
  `FE5F6790B37150D01D21201E7928B048BBE1D250CA54DB9C40A4804D6D18914B`;
- the prior 0.6.0 DLL was preserved as a backup with SHA-256
  `27FDA8FE621F6F16CAB50E36F049D7F9B48E41A843D042CC7FE60D3487A59795`.

Those unattended starts did not enter a character scene or discover
controllers, so they did not produce 0.6.1 grab or skirt-binding counts. Earlier 0.5
scene evidence showed:

- original-VR left and right controllers discovered;
- controller DynamicBone colliders created;
- character 0 head collider created;
- one observed character registered 20 hair DynamicBones and received 60
  controller/head bindings;
- one additional accessory DynamicBone received 3 bindings;
- no plugin binding exceptions while the character scene was active;
- `KoikatuVR.exe` remained responsive.

Manual headset feedback for 0.5 confirmed visible hair movement. Earlier tuning
had an overly large activation region; version 0.5 reduced the controller
sphere and contact shell. A later stuck-raised-hair report led to the quiet
reset behavior described above.

The remaining runtime check is character/outfit-scene evidence: record detected
grab-capable and skirt/Cloth counts, then confirm that grip hold/release remains
local and returns cleanly. This is not claimed by the unattended startup test.

## Known Limitations

- Only clothing with existing DynamicBone or Unity Cloth physics can react;
  static skinned meshes are intentionally unchanged.
- The force field is per DynamicBone component, not a fully local per-particle
  hand solver. A single chain may therefore respond more broadly than the
  exact touched strands.
- Skirt identification is limited to clothing slots 0/1 and known KK skirt
  bone names; unusual mod naming may require another pattern.
- Grip interaction pulls an entire DynamicBone component through its existing
  force field; it is not VRChat's per-particle posing, friction, or cloth tear.
- No automated visual/headset test exists; runtime visual strength remains a
  subjective manual check.
- The plugin targets the original `KoikatuVR.exe` process and should not be
  assumed to work unchanged in CharaStudio, KKVR variants, or VRGIN builds.

## Clothing / Skirt Implementation Status

The installed `Assembly-CSharp.dll` was inspected without changing the game.
It provides:

- `ChaInfo.objClothes`, an array of clothing GameObjects;
- `Studio.AddObjectFemale.GetSkirtDynamic(...)`, which searches skirt objects
  for `DynamicBone` components;
- `UnityEngine.Cloth` in `UnityEngine.dll`, but no direct clothing `Cloth`
  fields or methods were found in `Assembly-CSharp.dll`.

Version 0.6 implements both observed paths:

1. standard and modded skirt DynamicBone discovery in clothing slots 0/1;
2. controller, optional character-hand, and thigh-collider binding;
3. reuse of `KK_Colliders` leg capsules with compatible fallback creation;
4. a lower capped skirt force using chain-wide contact samples;
5. non-destructive controller sphere appends for existing Unity Cloth.

Automated behavior and startup compatibility are verified. Outfit-by-outfit
runtime binding counts and headset visuals remain unobserved for 0.6.1 and must
not be reported as verified.

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
