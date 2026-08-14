# KKVR Hair and Clothing Interaction 0.6.4 validation

## Goal

Make the original Koikatu VR VRTK controller transforms interact with hair,
accessory, and skirt DynamicBones, plus garments that already use Unity Cloth.
Keep forces local and bounded, preserve existing garment physics data, reuse
installed body colliders, add a bounded controller-grip interaction, and recover
cleanly after contact. Breast and hip physics remain native and out of scope.

## Implementation evidence

- Clothing discovery is restricted to `ChaControl.objClothes` slots 0 and 1,
  matching the game's `Studio.AddObjectFemale.GetSkirtDynamic` boundary.
- Skirt chains are recognized through the observed `cf_j_sk_*`, `cf_d_sk_*`,
  `backsk`, and `spinesk` bone families. Bust, thigh, and accessory lookalikes
  are rejected by focused tests.
- Original-game bottom garments are also recognized through the
  `ct_clothesBot` DynamicBone component marker used by KK_Colliders, even when
  their root bones have custom names.
- Physical accessories are discovered across the union of KKAPI accessory
  objects and live `cusAcsCmp` objects, covering modern MoreAccessories slots
  and load-time array reconstruction.
- DynamicBone, DynamicBone_Ver01, DynamicBone_Ver02, and Unity Cloth components
  below accessory objects all receive the corresponding controller contact
  path. Disabled/no-shake components and rootless components are skipped.
- Accessory motion has an independent adaptive profile. Short chains use 65%
  of the configured strength/caps, scaling to full response at a `0.30 m` chain
  span. Defaults are strength `0.015`, cap `0.030`, stationary push `0.006`,
  and contact padding `0.012 m`.
- Physics roots rebound to character bones by clothes-to-accessory tools remain
  valid. Native breast, hip, groin, and anal body-physics chains are excluded
  by exact component comments and bone families.
- A stationary controller applies a local outward skirt-contact push of at
  most `0.006`, still subject to the existing per-chain `0.025` clothing cap.
- Clothing scans emit one summary per changed top/bottom object signature,
  including a bounded list of component/root names for unsupported modded
  garments.
- All three installed DynamicBone variants are supported.
- Controller contact uses at most 24 evenly distributed transforms per chain,
  preserving both endpoints and preventing unbounded per-frame work.
- Skirt force has independent conservative defaults: strength `0.012`, maximum
  force `0.025`, controller radius `0.035 m`, and contact shell `0.008 m`.
- Existing `KK_Colliders 1.3.1` thigh DynamicBoneColliders are reused. If they
  are absent, six compatible upper/lower-thigh capsules are created.
- Unity Cloth receives separate trigger spheres through appended
  `ClothSphereColliderPair` entries. Existing sphere and capsule arrays are
  preserved.
- Disabling the plugin or controller collision disables the created controller
  colliders and restores all transient force.
- Non-finite tracking positions or velocities are ignored for the affected
  frame instead of causing repeated Unity `Update` exceptions.
- Grip reads the existing public `VRTK_ControllerEvents.gripPressed` state. It
  latches the closest sampled point with its current relative offset, so the
  first frame adds no pull or snap.
- Grab pull uses a `0.005 m` dead zone, a `0.04` general cap, the lower `0.025`
  skirt cap, and automatic release beyond `0.22 m` anchor error.
- Existing `KK_Colliders` hand, forearm, and upper-arm colliders can be reused
  by the optional character-arm path. Breast, hip, and leg names are excluded.
- No `dictDynamicBoneBust` target is scanned and no breast/hip DynamicBone
  parameter is read or written.

## Research evidence

- GitHub source inspection confirmed Koikatu's two-slot skirt search and
  `cf_j_sk_*` chain layout.
- Local asset inspection confirmed standard `cf_j_sk_00_00` through
  `cf_j_sk_07_05`, deformation `cf_d_sk_*`, and back/spine skirt families.
- Local `KK_Colliders.dll` IL confirmed its six leg-collider dimensions and
  `ct_clothesBot` binding behavior.
- Local Unity 5.6 metadata confirmed `Cloth.sphereColliders`,
  `Cloth.capsuleColliders`, and both `ClothSphereColliderPair` constructors.
- Local `Assembly-CSharp.dll` metadata and the matching upstream source both
  confirmed `VRTK_ControllerEvents.gripPressed` as a public runtime field.
- VRChat PhysBones documentation informed explicit collider lists, grabbing,
  posing, maximum-stretch, and hand-collision semantics; no VRChat code was
  copied.
- Current upstream KKAPI source confirms `GetAccessoryObjects` uses the
  character accessory array. Current MoreAccessories source confirms it grows
  both `objAccessory` and `cusAcsCmp`, supporting the runtime union used here.

## Automated evidence

### 0.6.2 lifecycle regression cycle

- RED checkpoint `77583cf`: new tests failed at compile time because real
  KK_Colliders naming, skirt-source isolation, target reconciliation, Unity
  fake-null fallback, Cloth synchronization, and owned-collider switching did
  not exist.
- GREEN checkpoint `3eb85be`: the same suite passed after runtime ownership and
  reconciliation were implemented.
- Refactor checkpoint `7a4afcc`: review-added lazy fallback, stricter dead Cloth
  pair removal, immediate config rescan, and fallback-hand ownership remained
  green.
- The 0.6.2 lifecycle result was 29 of 29 focused tests passing.
- The 0.6.3 skirt-contact suite added two regressions.
- The 0.6.4 accessory-contact RED checkpoint is `8888577`; its tests reproduced
  the missing accessory profile, body boundary, root-union, and simulation
  behavior before implementation.
- GREEN checkpoint `087a076` implemented adaptive accessory contact. Review
  checkpoint `bd0c433` preserved clothes-to-accessory rebinding while retaining
  native body exclusions.
- Current result is 36 of 36 focused tests passing. Ten consecutive release
  test runs exercised 1,000,000 randomized accessory samples with no failure.
- There is no configured coverage collector for this net35 executable harness,
  so no percentage is claimed.

### Prior 0.6.1 feature evidence

- RED: the 0.6.1 suite initially failed because grab math and reusable-arm
  collider classification did not exist.
- GREEN: the prior 22 of 22 focused tests remain part of the 29-test suite.
- The suite covers binding/source deduplication, slow-drift rejection, force
  falloff and caps, skirt classification, invalid numeric input, nearest chain
  samples, and contact-sample budgeting.
- Deterministic physical-input simulation covers 16 seeds and 320,000 steps at
  90 Hz. Every generated force stayed non-negative, local to the 0.043 m
  interaction boundary, and at or below the `0.025` skirt cap.
- A further 100,000-point grab sweep verified zero force inside the dead zone,
  monotonic pull outside it, the `0.04` cap, and maximum-stretch release.
- Release build: `net35`, 0 warnings, 0 errors.
- Assembly inspection after deployment shows CLR `v2.0.50727`, plugin version
  `0.6.4`, and expected `mscorlib`, BepInEx, Assembly-CSharp, UnityEngine,
  System.Core, and KKAPI references.
- Diff check passed. The repository scan found no credential-like values.

## Prior 0.6.1 runtime smoke evidence

- The release DLL was deployed while `KoikatuVR.exe` was not running.
- Batch/nographics startup stayed alive for 40 seconds; normal VR startup stayed
  alive for 45 seconds. Both were stopped by the exact PID started by the test.
- BepInEx loaded `KKVR Hair and Clothing Interaction 0.6.1` in both runs.
- The live config migrated to tuning version 4 and persisted enabled grab,
  skirt, skirt-body, clothing-force, and Unity Cloth sections.
- Plugin error lines: 0. No plugin `TypeLoadException`, `MissingMethodException`,
  or character-binding failure was logged.
- The deployed DLL matches the build SHA-256:
  `FE5F6790B37150D01D21201E7928B048BBE1D250CA54DB9C40A4804D6D18914B`.
- The preserved 0.6.0 backup SHA-256 is
  `27FDA8FE621F6F16CAB50E36F049D7F9B48E41A843D042CC7FE60D3487A59795`.
- The recovered 0.5 backup matches its GitHub Release SHA-256:
  `3098DA8BE9E78C9DE32A13C4B9EF5B7EFF036D3FFD3A802A36E560BDFBC496A0`.
- A `MoreAccessories` type-load warning remains in the wider mod environment;
  it is not emitted by this plugin.

The unattended 0.6.1 starts did not enter a character scene and did not discover
the controllers before the observation windows ended. Therefore skirt target
counts, Unity Cloth binding counts, grip latching, and headset visuals are not
claimed as runtime-verified.

## 0.6.2 runtime smoke evidence

- The 0.6.1 installed DLL was backed up before deployment with SHA-256
  `FE5F6790B37150D01D21201E7928B048BBE1D250CA54DB9C40A4804D6D18914B`.
- The 0.6.2 Release build, installed DLL, and repository-root convenience copy
  all match SHA-256
  `24A4F7366C1F42543765D2C1E8EC9B494AA9767C14665D63573659CC4513EB32`.
- Assembly inspection reports plugin version `0.6.2`, CLR `v2.0.50727`, and the
  expected `mscorlib`, BepInEx, Assembly-CSharp, UnityEngine, and System.Core
  references only.
- Batch/nographics startup remained alive for 40 seconds. Normal VR startup
  remained alive for 45 seconds. Each exact test PID was stopped afterward.
- Each run loaded `KKVR Hair and Clothing Interaction 0.6.2` exactly once.
- The exact final-hash binary also passed a separate 30-second normal VR start,
  loaded once, and produced zero plugin errors.
- Plugin error lines: 0. No plugin `TypeLoadException`, `MissingMethodException`,
  or character-binding failure was logged.
- The live configuration remains at tuning version 4; 0.6.2 changes lifecycle
  behavior and does not overwrite user physics tuning.

The unattended runs did not enter a character scene, so live config-toggle
reconciliation, outfit-change counts, controller grip behavior, and headset
visuals remain unobserved. These are not claimed as runtime-verified.

## 0.6.3 runtime smoke evidence

- The installed 0.6.2 DLL was preserved as
  `KKVRHandHairCollider.0.6.2.dll.bak` with SHA-256
  `24A4F7366C1F42543765D2C1E8EC9B494AA9767C14665D63573659CC4513EB32`.
- The 0.6.3 Release build, installed DLL, and repository-root copy all match
  SHA-256
  `72B9F8FAE24FA9BE42214A175435661372623242080FFC0C14CA3A398940772A`.
- A normal `KoikatuVR.exe` startup remained alive for the 45-second observation
  window and was stopped by the exact PID started for the smoke test.
- The exact final source/build artifact then passed an additional 30-second
  normal VR startup with the same zero-error result.
- BepInEx loaded `KKVR Hair and Clothing Interaction 0.6.3` once. The plugin
  emitted zero error, `TypeLoadException`, or `MissingMethodException` lines.
- The live config migrated to tuning version 5 and persisted
  `Stationary contact push = 0.006`.

The smoke run did not enter a character scene. The user's exact skirt visual
response is therefore not claimed as runtime-verified; the next clothing scan
will log recognized counts and component/root examples for direct diagnosis.

## 0.6.4 runtime smoke evidence

- The installed 0.6.3 DLL was preserved as
  `KKVRHandHairCollider.0.6.3.dll.bak` with SHA-256
  `72B9F8FAE24FA9BE42214A175435661372623242080FFC0C14CA3A398940772A`.
- The 0.6.4 Release build, installed DLL, and repository-root copy all match
  SHA-256
  `AD628E416EC8AF9D199D2C3EDD8D9B00A8273E31309D62CC98927DF0E9DDB3E3`.
- Assembly inspection reports version `0.6.4`, CLR `v2.0.50727`, and the
  expected KKAPI dependency in addition to the prior references.
- A normal `KoikatuVR.exe` startup remained alive for the 45-second observation
  window. The exact final-hash binary then remained alive for a separate
  30-second window and was stopped by the exact PID `37836` started for that
  test.
- BepInEx loaded `KKVR Hair and Clothing Interaction 0.6.4` exactly once in
  each run. Plugin error, `TypeLoadException`, and `MissingMethodException`
  counts were all zero.
- The live config migrated to tuning version 6 and persisted the complete
  `[Accessory force]` section.
- A pre-existing JetPack dependency error for the legacy MoreAccessories GUID
  remains in the wider mod environment; it is not emitted by this plugin.

The unattended smoke run did not enter a character scene. Therefore live
per-slot accessory counts and the user's exact headset-visible contact response
are not claimed as runtime-observed. The next loaded-character scan will report
registered, disabled/rootless, and native-body-excluded counts plus bounded
component/root examples.

## Prior 0.5 scene baseline

The previous version's interactive scene log remains useful as a regression
baseline only:

- both original-VR VRTK controllers were discovered;
- 20 hair DynamicBones and one accessory DynamicBone were observed;
- controller/head bindings were added without plugin exceptions;
- manual headset feedback confirmed visible near-contact hair movement.

This evidence predates clothing support and does not verify 0.6 skirt behavior.

## Installed files

- Plugin: `Koikatu/BepInEx/plugins/KKVRHandHairCollider/KKVRHandHairCollider.dll`
- Backup: `Koikatu/BepInEx/plugins/KKVRHandHairCollider/KKVRHandHairCollider.0.5.0.dll.bak`
- 0.6.0 backup: `Koikatu/BepInEx/plugins/KKVRHandHairCollider/KKVRHandHairCollider.0.6.0.dll.bak`
- 0.6.3 backup: `Koikatu/BepInEx/plugins/KKVRHandHairCollider/KKVRHandHairCollider.0.6.3.dll.bak`
- Config: `Koikatu/BepInEx/config/local.kkvr.handhaircollider.cfg`
- Source and tests: `_codex_updates/KKVRHandHairCollider`

## Remaining runtime observation

For a later attended or scripted character-scene run, record these objective
signals before making visual claims:

1. controller DynamicBone and Unity Cloth spheres are created;
2. detected skirt DynamicBone and Unity Cloth counts are nonzero for a known
   physics-enabled outfit;
3. controller/body/hand-to-skirt bindings are added once with no duplicates;
4. a near-contact sweep moves the touched chain and a distant sweep does not;
5. grip near a chain latches without a first-frame jump, follows within the
   configured cap, and releases on button-up or maximum stretch;
6. the chain returns after one quiet second without remaining raised;
7. no plugin exceptions occur during outfit changes or scene unload.
