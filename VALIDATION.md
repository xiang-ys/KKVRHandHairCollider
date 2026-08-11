# KKVR Hand Hair Collider 0.5.0 validation

## Goal

Make the original Koikatu VR VRTK left and right controller transforms act as
spherical DynamicBone colliders for character hair and accessory DynamicBones.
Controller velocity applies force only inside the controller sphere plus a
small contact-padding shell. A head capsule is also bound to the hair to stop
inward skull clipping. Character hand-bone colliders remain optional and are
disabled by default.

## Automated evidence

- RED 1: tests initially failed because `BindingPair` and `BindingPlanner` did
  not exist.
- GREEN 1: the binding cross-product, existing-binding skip, single-controller,
  duplicate-input, and empty-input cases passed.
- RED 2: controller-source tests initially failed because
  `ColliderSourceSelector` did not exist.
- GREEN 2: direct-controller priority, optional character hands, and source
  deduplication passed.
- RED 3: force-field tests failed because `ForceFieldMath` did not exist.
- GREEN 3: distance falloff, slow-drift rejection, maximum-force limiting, and
  outside-radius stopping passed.
- RED 4: contact-model tests failed because the old force API had no separate
  controller-radius and contact-padding inputs.
- GREEN 4: full force inside the controller sphere, short-shell falloff, and
  zero force outside the shell passed. Total: 12 passing tests.
- Tuning 5: existing configs are migrated once to a 0.035 m controller sphere,
  0.008 m contact shell, 0.15 m/s speed threshold, 0.018 strength, and 0.04
  maximum force.
- Recovery 5: transient DynamicBone force is restored immediately after
  contact; after one quiet second particle positions are reset to prevent a
  permanently raised hair state. Scene changes also clear transient force.
- Release build: `net35`, 0 warnings, 0 errors.
- Runtime: BepInEx loaded plugin version 0.5.0, plugin initialization completed,
  the config file was generated, and `KoikatuVR.exe` remained responsive.
- Original VR controller discovery: both VRTK controllers were found and each
  received a DynamicBone collider.
- Character scene: 20 hair DynamicBones were registered for velocity force and
  received 60 controller/head bindings. One additional dynamic accessory bone
  received three bindings.
- Head collision: a head capsule was created for character 0 and bound to all
  discovered hair DynamicBones.
- Stability: no plugin exceptions were observed while the character scene was
  running.
- Migration evidence: the live config reports tuning version 2 and the expected
  conservative values. The old `Influence radius meters` orphan is ignored.
- Regression: 0 plugin occurrences of the prior `IReadOnlyList`,
  `IteratorStateMachineAttribute`, `Array.Empty`, or missing-VRManager errors.

## Installed files

- Plugin: `Koikatu/BepInEx/plugins/KKVRHandHairCollider/KKVRHandHairCollider.dll`
- Config: `Koikatu/BepInEx/config/local.kkvr.handhaircollider.cfg`
- Source and tests: `_codex_updates/KKVRHandHairCollider`

## Visual-strength check

Quest/SteamVR controller discovery, hair registration, and binding are now
verified. The remaining check is subjective visible strength in the headset:

1. Start `KoikatuVR.exe` and enter a scene containing a character with movable
   bangs or side hair.
2. Pass either controller slowly through the movable hair. No grip action is
   required.
3. Compare a quick sweep with holding the controller visibly away from the
   hair. Movement should start only at near-contact distance.
4. If hair still enters the skull, increase `Head collision > Radius meters`
   from `0.095` to `0.11`; do not increase the controller force radius.

The old prototype binaries are retained beside the installed DLL with a `.bak`
suffix and are ignored by BepInEx.
