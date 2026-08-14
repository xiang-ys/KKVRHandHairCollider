# Changelog

All notable public changes are documented here.

## 1.0 - 2026-08-15

- Connects original-game and mod-provided DynamicBone physics under hair,
  accessories, and top/bottom clothing to tracked VR controller interaction.
- Supports `DynamicBone`, `DynamicBone_Ver01`, `DynamicBone_Ver02`, and existing
  Unity Cloth components.
- Discovers generic mod garment chains without requiring standard skirt bone
  names, while excluding native breast and hip physics.
- Adds continuous segment contact for sparse accessory and garment bone chains.
- Adds isolated `0.065 m` garment controller colliders without changing the
  established `0.035 m` hair and accessory interaction radius.
- Keeps whole-chain clothing force available as an optional fallback but
  disables it by default for more local movement.
- Supports modern MoreAccessories slots and clothing converted to accessories.
- Includes bounded controller-velocity response, stationary accessory contact,
  grip interaction, head collision, skirt/thigh collision, cleanup, and tuning
  migration.
- Verified with 39 focused tests, a warning-free `net35` Release build, assembly
  inspection, original-VR startup, and attended original/mod garment testing.

## 0.5.0 - 2026-08-12

- First public experimental release.
- Added original-VRTK controller collision for hair DynamicBone chains.
- Added bounded near-contact force, quiet particle reset, and head collision.
