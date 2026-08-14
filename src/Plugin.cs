using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using KKAPI;
using KKAPI.Maker;
using KKVRHandHairCollider.Core;
using UnityEngine;
using VRTK;

namespace KKVRHandHairCollider
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("KoikatuVR")]
    [BepInDependency(KoikatuAPI.GUID, KoikatuAPI.VersionConst)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "local.kkvr.handhaircollider";
        public const string Name = "KKVR Hair and Clothing Interaction";
        public const string Version = "1.0";

        private const int MaximumContactSamplesPerTarget = 24;

        private static readonly SkirtColliderSpec[] SkirtColliderSpecs =
        {
            new SkirtColliderSpec("cf_s_thigh01_L", "L_UpperOuter", 0.095f, 0.32f, new Vector3(0.05f, -0.10f, -0.015f)),
            new SkirtColliderSpec("cf_s_thigh01_L", "L_UpperInner", 0.095f, 0.32f, new Vector3(0.01f, -0.125f, -0.015f)),
            new SkirtColliderSpec("cf_s_thigh01_R", "R_UpperOuter", 0.095f, 0.32f, new Vector3(-0.05f, -0.10f, -0.015f)),
            new SkirtColliderSpec("cf_s_thigh01_R", "R_UpperInner", 0.095f, 0.32f, new Vector3(-0.01f, -0.125f, -0.015f)),
            new SkirtColliderSpec("cf_s_thigh02_L", "L_Lower", 0.083f, 0.35f, new Vector3(-0.0065f, 0f, -0.012f)),
            new SkirtColliderSpec("cf_s_thigh02_R", "R_Lower", 0.083f, 0.35f, new Vector3(0.0065f, 0f, -0.012f))
        };

        private ConfigEntry<bool> _enabled;
        private ConfigEntry<bool> _includeAccessories;
        private ConfigEntry<bool> _includeClothing;
        private ConfigEntry<bool> _controllerCollidersEnabled;
        private ConfigEntry<float> _controllerRadius;
        private ConfigEntry<bool> _controllerForceEnabled;
        private ConfigEntry<float> _forceContactPadding;
        private ConfigEntry<float> _forceStrength;
        private ConfigEntry<float> _maximumForce;
        private ConfigEntry<float> _minimumControllerSpeed;
        private ConfigEntry<float> _velocitySmoothing;
        private ConfigEntry<bool> _accessoryForceEnabled;
        private ConfigEntry<float> _accessoryForceStrength;
        private ConfigEntry<float> _accessoryMaximumForce;
        private ConfigEntry<float> _accessoryContactPushStrength;
        private ConfigEntry<float> _accessoryContactPadding;
        private ConfigEntry<bool> _clothingForceEnabled;
        private ConfigEntry<float> _clothingForceStrength;
        private ConfigEntry<float> _clothingMaximumForce;
        private ConfigEntry<float> _clothingContactPushStrength;
        private ConfigEntry<float> _clothingColliderRadius;
        private ConfigEntry<bool> _grabEnabled;
        private ConfigEntry<float> _grabStrength;
        private ConfigEntry<float> _grabMaximumForce;
        private ConfigEntry<float> _grabDeadZone;
        private ConfigEntry<float> _grabMaximumStretch;
        private ConfigEntry<bool> _includeCharacterHandColliders;
        private ConfigEntry<bool> _createFallbackColliders;
        private ConfigEntry<bool> _headColliderEnabled;
        private ConfigEntry<float> _headColliderRadius;
        private ConfigEntry<float> _headColliderHeight;
        private ConfigEntry<float> _headColliderCenterY;
        private ConfigEntry<bool> _skirtBodyCollidersEnabled;
        private ConfigEntry<bool> _unityClothEnabled;
        private ConfigEntry<float> _scanInterval;
        private ConfigEntry<int> _tuningVersion;
        private float _nextScan;
        private bool _initialized;
        private DynamicBoneCollider _leftControllerCollider;
        private DynamicBoneCollider _rightControllerCollider;
        private DynamicBoneCollider _leftGarmentControllerCollider;
        private DynamicBoneCollider _rightGarmentControllerCollider;
        private SphereCollider _leftControllerClothCollider;
        private SphereCollider _rightControllerClothCollider;
        private readonly ControllerMotionState _leftControllerMotion = new ControllerMotionState();
        private readonly ControllerMotionState _rightControllerMotion = new ControllerMotionState();
        private readonly GrabState _leftGrab = new GrabState();
        private readonly GrabState _rightGrab = new GrabState();
        private readonly Dictionary<string, DynamicBoneTarget> _forceTargets = new Dictionary<string, DynamicBoneTarget>(StringComparer.Ordinal);
        private readonly HashSet<string> _desiredForceTargetIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, OwnedDynamicBoneBinding> _ownedBindings = new Dictionary<string, OwnedDynamicBoneBinding>(StringComparer.Ordinal);
        private readonly HashSet<string> _desiredBindingIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<Cloth> _managedCloths = new HashSet<Cloth>();
        private readonly HashSet<Cloth> _desiredManagedCloths = new HashSet<Cloth>();
        private readonly HashSet<DynamicBoneCollider> _ownedHeadColliders = new HashSet<DynamicBoneCollider>();
        private readonly HashSet<DynamicBoneCollider> _ownedSkirtBodyColliders = new HashSet<DynamicBoneCollider>();
        private readonly HashSet<DynamicBoneCollider> _ownedFallbackHandColliders = new HashSet<DynamicBoneCollider>();
        private readonly Dictionary<int, string> _clothingScanSignatures = new Dictionary<int, string>();
        private readonly Dictionary<int, string> _accessoryScanSignatures = new Dictionary<int, string>();
        private bool _controllerLookupWarningLogged;
        private bool _grabInputWarningLogged;

        private void Awake() => Initialize();

        private void OnEnable() => Initialize();

        private void Start() => Initialize();

        private void Initialize()
        {
            if (_initialized)
                return;

            _enabled = Config.Bind("General", "Enabled", true, "Enable controller interaction with supported hair, accessories, and clothing.");
            _includeAccessories = Config.Bind("General", "Include accessory Dynamic Bones", true, "Also bind hand colliders to accessory Dynamic Bones.");
            _includeClothing = Config.Bind("General", "Include skirt Dynamic Bones", true, "Bind controllers to physical clothing chains found in the top and bottom clothing slots, including mod garments with generic bone names.");
            _controllerCollidersEnabled = Config.Bind("Controller collision", "Enabled", true, "Use the tracked VR controllers as Dynamic Bone colliders.");
            _controllerRadius = Config.Bind("Controller collision", "Radius meters", 0.035f, new ConfigDescription("Radius of each spherical controller collider.", new AcceptableValueRange<float>(0.015f, 0.12f)));
            _controllerForceEnabled = Config.Bind("Controller force", "Enabled", true, "Master switch for controller movement and stationary contact forces on supported Dynamic Bones.");
            _forceContactPadding = Config.Bind("Controller force", "Contact padding meters", 0.008f, new ConfigDescription("Soft force falloff outside the controller collider surface.", new AcceptableValueRange<float>(0.001f, 0.05f)));
            _forceStrength = Config.Bind("Controller force", "Strength", 0.018f, new ConfigDescription("Force generated per meter/second of controller speed.", new AcceptableValueRange<float>(0.005f, 0.20f)));
            _maximumForce = Config.Bind("Controller force", "Maximum force", 0.04f, new ConfigDescription("Safety cap for the force applied to one Dynamic Bone.", new AcceptableValueRange<float>(0.02f, 0.30f)));
            _minimumControllerSpeed = Config.Bind("Controller force", "Minimum speed meters per second", 0.15f, new ConfigDescription("Controller movement slower than this is treated as tracking drift.", new AcceptableValueRange<float>(0f, 0.50f)));
            _velocitySmoothing = Config.Bind("Controller force", "Velocity smoothing", 0.35f, new ConfigDescription("Higher values reduce tracking jitter but soften sudden motion.", new AcceptableValueRange<float>(0f, 0.95f)));
            _accessoryForceEnabled = Config.Bind("Accessory force", "Enabled", true, "Apply adaptive controller force and stationary contact push to physical accessories.");
            _accessoryForceStrength = Config.Bind("Accessory force", "Strength", 0.015f, new ConfigDescription("Base force generated per meter/second for long accessory chains; shorter chains scale down automatically.", new AcceptableValueRange<float>(0.002f, 0.10f)));
            _accessoryMaximumForce = Config.Bind("Accessory force", "Maximum force", 0.030f, new ConfigDescription("Base safety cap for long accessory chains; shorter chains use a lower cap.", new AcceptableValueRange<float>(0.005f, 0.15f)));
            _accessoryContactPushStrength = Config.Bind("Accessory force", "Stationary contact push", 0.006f, new ConfigDescription("Base outward push while a controller rests against a physical accessory.", new AcceptableValueRange<float>(0f, 0.05f)));
            _accessoryContactPadding = Config.Bind("Accessory force", "Contact padding meters", 0.012f, new ConfigDescription("Soft contact shell around the controller for accessory chains.", new AcceptableValueRange<float>(0.001f, 0.05f)));
            _clothingForceEnabled = Config.Bind("Clothing force", "Enabled", false, "Optional whole-chain force fallback for garments that do not respond to local controller colliders.");
            _clothingForceStrength = Config.Bind("Clothing force", "Strength", 0.012f, new ConfigDescription("Force generated per meter/second for physical clothing chains.", new AcceptableValueRange<float>(0.002f, 0.10f)));
            _clothingMaximumForce = Config.Bind("Clothing force", "Maximum force", 0.025f, new ConfigDescription("Safety cap for force applied to one physical clothing Dynamic Bone.", new AcceptableValueRange<float>(0.005f, 0.15f)));
            _clothingContactPushStrength = Config.Bind("Clothing force", "Stationary contact push", 0.006f, new ConfigDescription("Bounded outward push while a controller rests against a physical clothing chain.", new AcceptableValueRange<float>(0f, 0.05f)));
            _clothingColliderRadius = Config.Bind("Clothing collision", "Radius meters", 0.065f, new ConfigDescription("Radius of the dedicated local controller collider used only by physical garments.", new AcceptableValueRange<float>(0.04f, 0.12f)));
            _grabEnabled = Config.Bind("Grab interaction", "Enabled", true, "Hold the controller grip near hair, accessories, or skirt chains to pull them without replacing their physics.");
            _grabStrength = Config.Bind("Grab interaction", "Strength", 0.20f, new ConfigDescription("Bounded pull force per meter moved after a chain is grabbed.", new AcceptableValueRange<float>(0.02f, 1.0f)));
            _grabMaximumForce = Config.Bind("Grab interaction", "Maximum force", 0.04f, new ConfigDescription("Safety cap for grab force; skirt chains retain their lower clothing force cap.", new AcceptableValueRange<float>(0.01f, 0.15f)));
            _grabDeadZone = Config.Bind("Grab interaction", "Dead zone meters", 0.005f, new ConfigDescription("Controller movement ignored after latching to prevent tracking jitter.", new AcceptableValueRange<float>(0f, 0.03f)));
            _grabMaximumStretch = Config.Bind("Grab interaction", "Maximum stretch meters", 0.22f, new ConfigDescription("Release a grabbed chain when its anchor error exceeds this distance.", new AcceptableValueRange<float>(0.08f, 0.50f)));
            _includeCharacterHandColliders = Config.Bind("Character hands", "Include character hand colliders", false, "Also bind colliders attached to the character's hand bones.");
            _createFallbackColliders = Config.Bind("Character hands", "Create fallback hand colliders", true, "Create character hand colliders when KK_Colliders has not created them.");
            _headColliderEnabled = Config.Bind("Head collision", "Enabled", true, "Prevent hair Dynamic Bones from passing through the character's head.");
            _headColliderRadius = Config.Bind("Head collision", "Radius meters", 0.075f, new ConfigDescription("Radius of the head capsule collider.", new AcceptableValueRange<float>(0.05f, 0.16f)));
            _headColliderHeight = Config.Bind("Head collision", "Height meters", 0.10f, new ConfigDescription("Height of the head capsule collider along its local Y axis.", new AcceptableValueRange<float>(0f, 0.25f)));
            _headColliderCenterY = Config.Bind("Head collision", "Center Y meters", 0.015f, new ConfigDescription("Vertical offset of the head collider on the head bone.", new AcceptableValueRange<float>(-0.10f, 0.10f)));
            _skirtBodyCollidersEnabled = Config.Bind("Skirt body collision", "Enabled", true, "Reuse or create thigh colliders so skirt chains stay outside the body.");
            _unityClothEnabled = Config.Bind("Unity Cloth", "Enabled", true, "Append controller spheres to clothing that uses Unity Cloth instead of Dynamic Bones.");
            _scanInterval = Config.Bind("General", "Scan interval seconds", 1.0f, new ConfigDescription("How often loaded characters are checked.", new AcceptableValueRange<float>(0.25f, 10f)));
            _tuningVersion = Config.Bind("General", "Tuning version", 0, "Internal parameter migration version.");
            WatchForRescan(_includeAccessories);
            WatchForRescan(_includeClothing);
            WatchForRescan(_controllerCollidersEnabled);
            WatchForRescan(_includeCharacterHandColliders);
            WatchForRescan(_createFallbackColliders);
            WatchForRescan(_headColliderEnabled);
            WatchForRescan(_skirtBodyCollidersEnabled);
            WatchForRescan(_unityClothEnabled);
            WatchForRescan(_clothingColliderRadius);
            MigrateTuning();
            Config.Save();
            _initialized = true;
            Logger.LogMessage("Controller hair/accessory/clothing collision, force, and grip interaction loaded; waiting for VR controllers and characters.");
        }

        private void WatchForRescan<T>(ConfigEntry<T> entry)
        {
            entry.SettingChanged += (sender, args) => _nextScan = 0f;
        }

        private void MigrateTuning()
        {
            if (_tuningVersion.Value < 2)
            {
                _controllerRadius.Value = 0.035f;
                _forceContactPadding.Value = 0.008f;
                _forceStrength.Value = 0.018f;
                _maximumForce.Value = 0.04f;
                _minimumControllerSpeed.Value = 0.15f;
                _headColliderRadius.Value = 0.075f;
                _headColliderHeight.Value = 0.10f;
                _headColliderCenterY.Value = 0.015f;
                _tuningVersion.Value = 2;
                Logger.LogMessage("Applied conservative contact tuning: smaller controller sphere, shorter force shell, and smaller head capsule.");
            }

            if (_tuningVersion.Value < 3)
            {
                _clothingForceStrength.Value = 0.012f;
                _clothingMaximumForce.Value = 0.025f;
                _tuningVersion.Value = 3;
                Logger.LogMessage("Applied conservative skirt tuning with a lower force cap than hair.");
            }

            if (_tuningVersion.Value < 4)
            {
                _grabStrength.Value = 0.20f;
                _grabMaximumForce.Value = 0.04f;
                _grabDeadZone.Value = 0.005f;
                _grabMaximumStretch.Value = 0.22f;
                _tuningVersion.Value = 4;
                Logger.LogMessage("Applied bounded grip tuning with no-snap anchoring and automatic stretch release.");
            }

            if (_tuningVersion.Value < 5)
            {
                _clothingContactPushStrength.Value = 0.006f;
                _tuningVersion.Value = 5;
                Logger.LogMessage("Applied bounded stationary-contact tuning for skirt interaction.");
            }

            if (_tuningVersion.Value < 6)
            {
                _accessoryForceStrength.Value = 0.015f;
                _accessoryMaximumForce.Value = 0.030f;
                _accessoryContactPushStrength.Value = 0.006f;
                _accessoryContactPadding.Value = 0.012f;
                _tuningVersion.Value = 6;
                Logger.LogMessage("Applied adaptive contact tuning for physical accessories.");
            }

            if (_tuningVersion.Value < 7)
            {
                _clothingForceEnabled.Value = false;
                _clothingColliderRadius.Value = 0.065f;
                _tuningVersion.Value = 7;
                Logger.LogMessage("Applied local garment-collision tuning without global clothing force.");
            }
        }

        private void Update()
        {
            Initialize();
            if (!_enabled.Value)
            {
                SetControllerColliderState(false, false);
                DisableOwnedCharacterColliders();
                ClearOwnedBindings();
                ClearForceTargets();
                ClearManagedClothBindings();
                return;
            }

            SetControllerColliderState(_controllerCollidersEnabled.Value, _unityClothEnabled.Value);
            SetOwnedCharacterColliderState();
            if (Time.unscaledTime < _nextScan)
            {
                ApplyControllerForces();
                return;
            }

            _nextScan = Time.unscaledTime + _scanInterval.Value;
            _desiredBindingIds.Clear();
            _desiredForceTargetIds.Clear();
            _desiredManagedCloths.Clear();
            var controllerColliders = _controllerCollidersEnabled.Value || _controllerForceEnabled.Value || _grabEnabled.Value || _unityClothEnabled.Value
                ? EnsureControllerColliders(_controllerCollidersEnabled.Value)
                : new List<DynamicBoneCollider>();

            foreach (var character in FindObjectsOfType<ChaControl>())
            {
                if (character == null || !character.loadEnd || !character.gameObject.activeInHierarchy)
                    continue;

                try
                {
                    BindCharacter(character, controllerColliders);
                }
                catch (Exception exception)
                {
                    Logger.LogError($"Failed to bind character {character.chaID}: {exception}");
                }
            }

            ReconcileOwnedBindings();
            ReconcileForceTargets();
            ReconcileManagedCloths();
            SetOwnedCharacterColliderState();

            ApplyControllerForces();
        }

        private void BindCharacter(ChaControl character, IList<DynamicBoneCollider> controllerColliders)
        {
            var characterHandColliders = new List<DynamicBoneCollider>();
            if (_includeCharacterHandColliders.Value)
            {
                characterHandColliders = FindHandColliders(character);
                if (characterHandColliders.Count == 0 && _createFallbackColliders.Value)
                    characterHandColliders.AddRange(CreateFallbackHandColliders(character));
            }

            var hairColliders = ColliderSourceSelector.Select(
                    AddHeadCollider(controllerColliders, _headColliderEnabled.Value ? EnsureHeadCollider(character) : null),
                    characterHandColliders,
                    _includeCharacterHandColliders.Value)
                .Where(collider => collider != null)
                .ToList();

            var hairTargets = FindHairTargets(character);
            RegisterForceTargets(hairTargets.Values, "hair");
            BindTargets(character, hairTargets, hairColliders, "controller/head/hand-to-hair");

            if (_includeAccessories.Value)
            {
                var accessoryTargets = FindAccessoryTargets(character);
                RegisterForceTargets(accessoryTargets.Values, "accessory");
                BindTargets(character, accessoryTargets, hairColliders, "controller/head/hand-to-accessory");
            }

            SyncUnityCloth(character, _unityClothEnabled.Value
                ? AvailableControllerClothColliders()
                : new SphereCollider[0]);

            if (!_includeClothing.Value)
                return;

            var clothingTargets = FindClothingTargets(character);
            LogClothingScan(character, clothingTargets.Count);
            if (clothingTargets.Count == 0)
                return;

            var skirtBodyColliders = _skirtBodyCollidersEnabled.Value
                ? EnsureSkirtBodyColliders(character)
                : new List<DynamicBoneCollider>();
            var garmentControllerColliders = TargetControllerColliderSelector.Select(
                InteractionTargetKind.Skirt,
                controllerColliders,
                AvailableGarmentControllerColliders());
            var skirtColliders = ColliderSourceSelector.SelectForSkirt(
                    garmentControllerColliders,
                    skirtBodyColliders)
                .Where(collider => collider != null)
                .ToList();

            RegisterForceTargets(clothingTargets.Values, "physical garment");
            BindTargets(character, clothingTargets, skirtColliders, "controller/body-to-physical-garment");
        }

        private void BindTargets(
            ChaControl character,
            IDictionary<string, DynamicBoneTarget> targets,
            IList<DynamicBoneCollider> colliders,
            string bindingLabel)
        {
            if (targets.Count == 0 || colliders.Count == 0)
                return;

            var existing = new List<BindingPair>();
            foreach (var target in targets.Values)
            {
                foreach (var collider in colliders)
                {
                    if (target.Contains(collider))
                        existing.Add(new BindingPair(target.Id, ColliderId(collider)));
                }
            }

            var colliderById = colliders.ToDictionary(ColliderId, collider => collider);
            var planned = BindingPlanner.Plan(targets.Keys, colliderById.Keys, existing);
            foreach (var pair in planned)
            {
                var target = targets[pair.DynamicBoneId];
                var collider = colliderById[pair.ColliderId];
                target.Add(collider);
                var bindingId = BindingId(target.Id, collider);
                _ownedBindings[bindingId] = new OwnedDynamicBoneBinding(target, collider);
            }

            foreach (var target in targets.Values)
            {
                foreach (var collider in colliders)
                {
                    if (target.Contains(collider))
                        _desiredBindingIds.Add(BindingId(target.Id, collider));
                }
            }

            if (planned.Count > 0)
                Logger.LogInfo($"Character {character.chaID}: added {planned.Count} {bindingLabel} bindings across {targets.Count} Dynamic Bones.");
        }

        private void SyncUnityCloth(ChaControl character, IList<SphereCollider> desiredColliders)
        {
            var clothes = character.objClothes ?? new GameObject[0];
            var accessories = GetAccessoryRoots(character);
            var interactionRoots = InteractionRootSelector.Select(
                clothes,
                accessories,
                _includeAccessories.Value);

            var added = 0;
            var clothCount = 0;
            foreach (var root in interactionRoots)
            {
                if (root == null)
                    continue;

                foreach (var cloth in root.GetComponentsInChildren<Cloth>(true))
                {
                    if (cloth == null)
                        continue;

                    clothCount++;
                    _managedCloths.Add(cloth);
                    _desiredManagedCloths.Add(cloth);
                    added += SyncClothSpheres(cloth, desiredColliders);
                }
            }

            if (added > 0)
                Logger.LogInfo($"Character {character.chaID}: added {added} controller sphere bindings across {clothCount} clothing/accessory Unity Cloth components.");
        }

        private static int SyncClothSpheres(Cloth cloth, IList<SphereCollider> desiredColliders)
        {
            if (cloth == null)
                return 0;

            var existing = cloth.sphereColliders ?? new ClothSphereColliderPair[0];
            var plan = ClothBindingPlanner.Plan(
                existing,
                desiredColliders,
                pair => pair.first,
                pair => pair.second,
                collider => collider != null,
                collider => ReferenceEquals(collider, null),
                IsManagedClothCollider);
            var updated = new List<ClothSphereColliderPair>(plan.RetainedPairs);
            updated.AddRange(plan.CollidersToAdd.Select(collider => new ClothSphereColliderPair(collider)));
            if (!existing.SequenceEqual(updated))
                cloth.sphereColliders = updated.ToArray();
            return plan.CollidersToAdd.Count;
        }

        private IList<SphereCollider> AvailableControllerClothColliders()
        {
            var result = new List<SphereCollider>(2);
            if (_leftControllerClothCollider != null)
                result.Add(_leftControllerClothCollider);
            if (_rightControllerClothCollider != null)
                result.Add(_rightControllerClothCollider);
            return result;
        }

        private static bool IsManagedClothCollider(SphereCollider collider)
        {
            return collider != null &&
                   collider.gameObject.name.StartsWith("KKVRHandHairCollider_UnityCloth_", StringComparison.Ordinal);
        }

        private static IList<DynamicBoneCollider> AddHeadCollider(IList<DynamicBoneCollider> controllerColliders, DynamicBoneCollider headCollider)
        {
            if (headCollider == null)
                return controllerColliders;

            var result = new List<DynamicBoneCollider>(controllerColliders);
            result.Add(headCollider);
            return result;
        }

        private List<DynamicBoneCollider> EnsureControllerColliders(bool createColliders)
        {
            var result = new List<DynamicBoneCollider>(2);
            GameObject leftController = null;
            GameObject rightController = null;

            try
            {
                leftController = UnityReferenceSelector.FirstAvailable(
                    VRTK_DeviceFinder.GetControllerLeftHand(false),
                    () => VRTK_DeviceFinder.GetControllerLeftHand(true),
                    controller => controller == null);
                rightController = UnityReferenceSelector.FirstAvailable(
                    VRTK_DeviceFinder.GetControllerRightHand(false),
                    () => VRTK_DeviceFinder.GetControllerRightHand(true),
                    controller => controller == null);

                if (leftController == null || rightController == null)
                {
                    var manager = UnityEngine.Object.FindObjectOfType<VRViveControllerManager>();
                    if (manager != null)
                    {
                        if (leftController == null)
                            leftController = TransformGameObject(manager, 0);
                        if (rightController == null)
                            rightController = TransformGameObject(manager, 1);
                    }
                }

                _controllerLookupWarningLogged = false;
            }
            catch (Exception exception)
            {
                if (!_controllerLookupWarningLogged)
                {
                    Logger.LogWarning($"Waiting for original Koikatu VR controllers: {exception.Message}");
                    _controllerLookupWarningLogged = true;
                }
            }

            _leftControllerMotion.SetController(leftController);
            _rightControllerMotion.SetController(rightController);
            if (_grabEnabled.Value &&
                (_leftControllerMotion.IsAvailable || _rightControllerMotion.IsAvailable) &&
                !_leftControllerMotion.HasGripInput && !_rightControllerMotion.HasGripInput)
            {
                if (!_grabInputWarningLogged)
                {
                    Logger.LogWarning("VR controllers were found without VRTK_ControllerEvents; collision and velocity force remain active, but grip interaction is unavailable.");
                    _grabInputWarningLogged = true;
                }
            }
            else if (_leftControllerMotion.HasGripInput || _rightControllerMotion.HasGripInput)
            {
                _grabInputWarningLogged = false;
            }

            EnsureControllerClothCollider(leftController, "L", ref _leftControllerClothCollider);
            EnsureControllerClothCollider(rightController, "R", ref _rightControllerClothCollider);

            if (createColliders)
            {
                AddControllerCollider(leftController, "L", ref _leftControllerCollider, result);
                AddControllerCollider(rightController, "R", ref _rightControllerCollider, result);
                if (_includeClothing.Value)
                {
                    EnsureGarmentControllerCollider(leftController, "L", ref _leftGarmentControllerCollider);
                    EnsureGarmentControllerCollider(rightController, "R", ref _rightGarmentControllerCollider);
                }
            }
            return result;
        }

        private static GameObject TransformGameObject(VRViveControllerManager manager, int index)
        {
            try
            {
                var controllerTransform = manager.GetTransform(index);
                return controllerTransform == null ? null : controllerTransform.gameObject;
            }
            catch
            {
                return null;
            }
        }

        private void AddControllerCollider(
            GameObject controller,
            string side,
            ref DynamicBoneCollider collider,
            ICollection<DynamicBoneCollider> result)
        {
            if (controller == null || controller.transform == null)
                return;

            var expectedName = $"KKVRHandHairCollider_Controller_{side}";
            if (collider == null || collider.transform.parent != controller.transform)
            {
                collider = controller.GetComponentsInChildren<DynamicBoneCollider>(true)
                    .FirstOrDefault(item => item.gameObject.name == expectedName);

                if (collider == null)
                {
                    var colliderObject = new GameObject(expectedName);
                    colliderObject.transform.SetParent(controller.transform, false);
                    collider = colliderObject.AddComponent<DynamicBoneCollider>();
                    Logger.LogMessage($"Created original-VR Dynamic Bone collider for controller {side} on {controller.name}.");
                }
            }

            collider.m_Center = Vector3.zero;
            collider.m_Radius = _controllerRadius.Value;
            collider.m_Height = 0f;
            collider.enabled = true;
            result.Add(collider);
        }

        private void EnsureControllerClothCollider(
            GameObject controller,
            string side,
            ref SphereCollider collider)
        {
            if (!_unityClothEnabled.Value || controller == null || controller.transform == null)
                return;

            var expectedName = $"KKVRHandHairCollider_UnityCloth_{side}";
            if (collider == null || collider.transform.parent != controller.transform)
            {
                collider = controller.GetComponentsInChildren<SphereCollider>(true)
                    .FirstOrDefault(item => item.gameObject.name == expectedName);
                if (collider == null)
                {
                    var colliderObject = new GameObject(expectedName);
                    colliderObject.transform.SetParent(controller.transform, false);
                    collider = colliderObject.AddComponent<SphereCollider>();
                    Logger.LogMessage($"Created Unity Cloth interaction sphere for controller {side} on {controller.name}.");
                }
            }

            collider.center = Vector3.zero;
            collider.radius = _controllerRadius.Value;
            collider.isTrigger = true;
            collider.enabled = true;
        }

        private void EnsureGarmentControllerCollider(
            GameObject controller,
            string side,
            ref DynamicBoneCollider collider)
        {
            if (controller == null || controller.transform == null)
                return;

            var expectedName = $"KKVRHandHairCollider_GarmentController_{side}";
            if (collider == null || collider.transform.parent != controller.transform)
            {
                collider = controller.GetComponentsInChildren<DynamicBoneCollider>(true)
                    .FirstOrDefault(item => item.gameObject.name == expectedName);
                if (collider == null)
                {
                    var colliderObject = new GameObject(expectedName);
                    colliderObject.transform.SetParent(controller.transform, false);
                    collider = colliderObject.AddComponent<DynamicBoneCollider>();
                    Logger.LogMessage($"Created dedicated garment Dynamic Bone collider for controller {side} on {controller.name}.");
                }
            }

            collider.m_Center = Vector3.zero;
            collider.m_Radius = _clothingColliderRadius.Value;
            collider.m_Height = 0f;
            collider.m_Bound = DynamicBoneCollider.Bound.Outside;
            collider.enabled = true;
        }

        private IList<DynamicBoneCollider> AvailableGarmentControllerColliders()
        {
            var result = new List<DynamicBoneCollider>(2);
            if (_leftGarmentControllerCollider != null && _leftGarmentControllerCollider.enabled)
                result.Add(_leftGarmentControllerCollider);
            if (_rightGarmentControllerCollider != null && _rightGarmentControllerCollider.enabled)
                result.Add(_rightGarmentControllerCollider);
            return result;
        }

        private void SetControllerColliderState(bool dynamicBoneEnabled, bool clothEnabled)
        {
            if (_leftControllerCollider != null)
                _leftControllerCollider.enabled = dynamicBoneEnabled;
            if (_rightControllerCollider != null)
                _rightControllerCollider.enabled = dynamicBoneEnabled;
            var garmentEnabled = dynamicBoneEnabled && _includeClothing.Value;
            if (_leftGarmentControllerCollider != null)
                _leftGarmentControllerCollider.enabled = garmentEnabled;
            if (_rightGarmentControllerCollider != null)
                _rightGarmentControllerCollider.enabled = garmentEnabled;
            if (_leftControllerClothCollider != null)
                _leftControllerClothCollider.enabled = clothEnabled;
            if (_rightControllerClothCollider != null)
                _rightControllerClothCollider.enabled = clothEnabled;
        }

        private DynamicBoneCollider EnsureHeadCollider(ChaControl character)
        {
            var headRoot = character.objHeadBone == null ? character.transform : character.objHeadBone.transform;
            var headTransform = FindFirstNamedTransform(headRoot, "cf_j_head", "cf_J_Head", "cf_s_head");
            if (headTransform == null)
                headTransform = headRoot;

            if (headTransform == null)
                return null;

            const string colliderName = "KKVRHandHairCollider_Head";
            var collider = headTransform.GetComponentsInChildren<DynamicBoneCollider>(true)
                .FirstOrDefault(item => item.gameObject.name == colliderName);
            if (collider == null)
            {
                var colliderObject = new GameObject(colliderName);
                colliderObject.transform.SetParent(headTransform, false);
                collider = colliderObject.AddComponent<DynamicBoneCollider>();
                Logger.LogMessage($"Created head Dynamic Bone collider for character {character.chaID}.");
            }

            _ownedHeadColliders.Add(collider);

            collider.m_Center = new Vector3(0f, _headColliderCenterY.Value, 0f);
            collider.m_Radius = _headColliderRadius.Value;
            collider.m_Height = _headColliderHeight.Value;
            collider.m_Direction = DynamicBoneCollider.Direction.Y;
            collider.m_Bound = DynamicBoneCollider.Bound.Outside;
            return collider;
        }

        private List<DynamicBoneCollider> EnsureSkirtBodyColliders(ChaControl character)
        {
            var result = new List<DynamicBoneCollider>();
            foreach (var group in SkirtColliderSpecs.GroupBy(spec => spec.BoneName))
            {
                var bone = FindTransform(character.transform, group.Key);
                if (bone == null)
                    continue;

                var existing = bone.GetComponentsInChildren<DynamicBoneCollider>(true)
                    .Where(collider => collider != null && collider.transform.parent == bone)
                    .ToList();
                if (existing.Count > 0)
                {
                    result.AddRange(existing);
                    continue;
                }

                foreach (var spec in group)
                {
                    var colliderObject = new GameObject($"KKVRHandHairCollider_SkirtBody_{spec.NameSuffix}");
                    colliderObject.transform.SetParent(bone, false);
                    var collider = colliderObject.AddComponent<DynamicBoneCollider>();
                    collider.m_Radius = spec.Radius;
                    collider.m_Height = spec.Height;
                    collider.m_Center = spec.Center;
                    collider.m_Direction = DynamicBoneCollider.Direction.Y;
                    collider.m_Bound = DynamicBoneCollider.Bound.Outside;
                    result.Add(collider);
                    _ownedSkirtBodyColliders.Add(collider);
                }

                Logger.LogMessage($"Created {group.Count()} fallback skirt body colliders on {group.Key} for character {character.chaID}.");
            }

            return result.GroupBy(ColliderId).Select(group => group.First()).ToList();
        }

        private static Transform FindFirstNamedTransform(Transform root, params string[] names)
        {
            foreach (var name in names)
            {
                var found = FindTransform(root, name);
                if (found != null)
                    return found;
            }

            return null;
        }

        private void RegisterForceTargets(IEnumerable<DynamicBoneTarget> targets, string targetLabel)
        {
            var added = 0;
            foreach (var target in targets)
            {
                _desiredForceTargetIds.Add(target.Id);
                if (!_forceTargets.ContainsKey(target.Id))
                {
                    _forceTargets.Add(target.Id, target);
                    added++;
                }
            }

            if (added > 0)
                Logger.LogInfo($"Registered {added} {targetLabel} Dynamic Bones for controller contact interaction.");
        }

        private void ApplyControllerForces()
        {
            if ((!_controllerForceEnabled.Value && !_grabEnabled.Value) || _forceTargets.Count == 0)
            {
                ResetAllForces();
                return;
            }

            var deltaTime = Time.unscaledDeltaTime;
            _leftControllerMotion.Sample(deltaTime, _velocitySmoothing.Value);
            _rightControllerMotion.Sample(deltaTime, _velocitySmoothing.Value);
            UpdateGrab(_leftControllerMotion, _leftGrab);
            UpdateGrab(_rightControllerMotion, _rightGrab);

            List<string> staleTargetIds = null;
            foreach (var entry in _forceTargets)
            {
                var target = entry.Value;
                if (!target.IsAlive)
                {
                    target.ResetForce();
                    if (staleTargetIds == null)
                        staleTargetIds = new List<string>();
                    staleTargetIds.Add(entry.Key);
                    continue;
                }

                var profile = GetInteractionProfile(target);
                var maximumForce = profile.MaximumForce;
                var velocityForceEnabled = _controllerForceEnabled.Value && IsTargetForceEnabled(target.Kind);
                var force = velocityForceEnabled
                    ? CalculateControllerForce(_leftControllerMotion, target, profile) +
                      CalculateControllerForce(_rightControllerMotion, target, profile)
                    : Vector3.zero;
                if (_controllerForceEnabled.Value &&
                    profile.ContactPushStrength > 0f &&
                    IsTargetForceEnabled(target.Kind))
                {
                    force += CalculateContactPush(_leftControllerMotion, target, profile);
                    force += CalculateContactPush(_rightControllerMotion, target, profile);
                }
                var grabMaximumForce = Math.Min(maximumForce, _grabMaximumForce.Value);
                force += CalculateGrabForce(_leftControllerMotion, _leftGrab, target, grabMaximumForce);
                force += CalculateGrabForce(_rightControllerMotion, _rightGrab, target, grabMaximumForce);
                if (force.sqrMagnitude > maximumForce * maximumForce)
                    force = force.normalized * maximumForce;

                if (force.sqrMagnitude > 0f)
                    target.ApplyForce(force);
                else
                    target.ReleaseForce(deltaTime);
            }

            if (staleTargetIds != null)
            {
                foreach (var targetId in staleTargetIds)
                    _forceTargets.Remove(targetId);
            }
        }

        private void UpdateGrab(ControllerMotionState controller, GrabState grab)
        {
            if (!_grabEnabled.Value || !controller.IsAvailable || !controller.GripPressed)
            {
                grab.Release();
                return;
            }

            if (grab.IsActive)
            {
                if (!grab.Target.IsAlive || grab.Sample == null)
                {
                    grab.Release();
                }
                else
                {
                    var displacement = (controller.Position + grab.ControllerToSampleOffset - grab.Sample.position).magnitude;
                    if (IsFinite(displacement) &&
                        !GrabInteractionMath.ExceedsMaximumStretch(displacement, _grabMaximumStretch.Value))
                        return;
                    grab.Release();
                }
            }

            DynamicBoneTarget nearestTarget = null;
            Transform nearestSample = null;
            var nearestDistance = float.MaxValue;
            foreach (var target in _forceTargets.Values)
            {
                if (!target.IsAlive)
                    continue;

                Transform sample;
                float distance;
                if (!target.TryGetClosestSample(controller.Position, out sample, out distance) ||
                    distance >= nearestDistance)
                    continue;

                nearestTarget = target;
                nearestSample = sample;
                nearestDistance = distance;
            }

            var latchDistance = nearestTarget == null
                ? _controllerRadius.Value + _forceContactPadding.Value
                : _controllerRadius.Value + GetInteractionProfile(nearestTarget).ContactPadding;
            if (nearestTarget != null &&
                GrabInteractionMath.CanLatch(nearestDistance, latchDistance))
                grab.Latch(nearestTarget, nearestSample, controller.Position);
        }

        private Vector3 CalculateGrabForce(
            ControllerMotionState controller,
            GrabState grab,
            DynamicBoneTarget target,
            float maximumForce)
        {
            if (!_grabEnabled.Value || !grab.IsActive || grab.Target != target || grab.Sample == null)
                return Vector3.zero;

            var displacement = controller.Position + grab.ControllerToSampleOffset - grab.Sample.position;
            var distance = displacement.magnitude;
            if (!IsFinite(distance) || distance <= 0f)
                return Vector3.zero;

            var magnitude = GrabInteractionMath.ComputePullMagnitude(
                distance,
                _grabStrength.Value,
                maximumForce,
                _grabDeadZone.Value);
            return magnitude <= 0f ? Vector3.zero : displacement / distance * magnitude;
        }

        private Vector3 CalculateControllerForce(
            ControllerMotionState controller,
            DynamicBoneTarget target,
            InteractionTuning profile)
        {
            if (!controller.IsAvailable)
                return Vector3.zero;

            var speed = controller.Velocity.magnitude;
            if (!IsFinite(speed))
                return Vector3.zero;

            float distance;
            if (!target.TryGetMinimumDistance(controller.Position, out distance))
                return Vector3.zero;

            var magnitude = ForceFieldMath.ComputeMagnitude(
                speed,
                profile.VelocityStrength,
                profile.MaximumForce,
                distance,
                _controllerRadius.Value,
                profile.ContactPadding,
                _minimumControllerSpeed.Value);
            return speed <= 0f || magnitude <= 0f
                ? Vector3.zero
                : controller.Velocity / speed * magnitude;
        }

        private Vector3 CalculateContactPush(
            ControllerMotionState controller,
            DynamicBoneTarget target,
            InteractionTuning profile)
        {
            if (!controller.IsAvailable)
                return Vector3.zero;

            Vector3 closestPosition;
            float distance;
            if (!target.TryGetClosestContact(controller.Position, out closestPosition, out distance))
                return Vector3.zero;

            var magnitude = ContactPushMath.ComputeMagnitude(
                distance,
                _controllerRadius.Value,
                profile.ContactPadding,
                profile.ContactPushStrength,
                profile.MaximumForce);
            if (magnitude <= 0f || distance <= 0f)
                return Vector3.zero;

            return (closestPosition - controller.Position) / distance * magnitude;
        }

        private InteractionTuning GetInteractionProfile(DynamicBoneTarget target)
        {
            return InteractionProfilePlanner.Plan(
                target.Kind,
                target.ChainSpan,
                new InteractionTuning(
                    _forceStrength.Value,
                    _maximumForce.Value,
                    0f,
                    _forceContactPadding.Value),
                new InteractionTuning(
                    _accessoryForceStrength.Value,
                    _accessoryMaximumForce.Value,
                    _accessoryContactPushStrength.Value,
                    _accessoryContactPadding.Value),
                new InteractionTuning(
                    _clothingForceStrength.Value,
                    _clothingMaximumForce.Value,
                    _clothingContactPushStrength.Value,
                    _forceContactPadding.Value));
        }

        private bool IsTargetForceEnabled(InteractionTargetKind kind)
        {
            switch (kind)
            {
                case InteractionTargetKind.Hair:
                    return true;
                case InteractionTargetKind.Accessory:
                    return _accessoryForceEnabled.Value;
                case InteractionTargetKind.Skirt:
                    return _clothingForceEnabled.Value;
                default:
                    return false;
            }
        }

        private void ResetAllForces()
        {
            _leftGrab.Release();
            _rightGrab.Release();
            foreach (var target in _forceTargets.Values)
                target.ResetForce();
        }

        private void ReconcileForceTargets()
        {
            foreach (var targetId in TargetRegistryPlanner.PlanRemovals(
                         _forceTargets.Keys,
                         _desiredForceTargetIds))
            {
                _forceTargets[targetId].ResetForce();
                _forceTargets.Remove(targetId);
            }
        }

        private void ClearForceTargets()
        {
            ResetAllForces();
            _forceTargets.Clear();
            _desiredForceTargetIds.Clear();
        }

        private void ReconcileOwnedBindings()
        {
            var removalIds = _ownedBindings.Keys
                .Where(bindingId => !_desiredBindingIds.Contains(bindingId))
                .ToList();
            foreach (var bindingId in removalIds)
            {
                _ownedBindings[bindingId].Remove();
                _ownedBindings.Remove(bindingId);
            }
        }

        private void ClearOwnedBindings()
        {
            foreach (var binding in _ownedBindings.Values)
                binding.Remove();
            _ownedBindings.Clear();
            _desiredBindingIds.Clear();
        }

        private void ReconcileManagedCloths()
        {
            var stale = _managedCloths
                .Where(cloth => cloth == null || !_desiredManagedCloths.Contains(cloth))
                .ToList();
            foreach (var cloth in stale)
            {
                if (cloth != null)
                    SyncClothSpheres(cloth, new SphereCollider[0]);
                _managedCloths.Remove(cloth);
            }
        }

        private void ClearManagedClothBindings()
        {
            foreach (var cloth in _managedCloths.ToList())
            {
                if (cloth != null)
                    SyncClothSpheres(cloth, new SphereCollider[0]);
            }
            _managedCloths.Clear();
            _desiredManagedCloths.Clear();
        }

        private void SetOwnedCharacterColliderState()
        {
            SetColliderCollectionState(
                _ownedHeadColliders,
                OwnedColliderState.ShouldEnable(_enabled.Value, _headColliderEnabled.Value));
            SetColliderCollectionState(
                _ownedSkirtBodyColliders,
                OwnedColliderState.ShouldEnable(
                    _enabled.Value,
                    _includeClothing.Value && _skirtBodyCollidersEnabled.Value));
            SetColliderCollectionState(
                _ownedFallbackHandColliders,
                OwnedColliderState.ShouldEnable(
                    _enabled.Value,
                    _includeCharacterHandColliders.Value && _createFallbackColliders.Value));
        }

        private void DisableOwnedCharacterColliders()
        {
            SetColliderCollectionState(_ownedHeadColliders, false);
            SetColliderCollectionState(_ownedSkirtBodyColliders, false);
            SetColliderCollectionState(_ownedFallbackHandColliders, false);
        }

        private static void SetColliderCollectionState(
            ICollection<DynamicBoneCollider> colliders,
            bool enabled)
        {
            var destroyed = colliders.Where(collider => collider == null).ToList();
            foreach (var collider in destroyed)
                colliders.Remove(collider);
            foreach (var collider in colliders)
                collider.enabled = enabled;
        }

        private void OnDisable()
        {
            SetControllerColliderState(false, false);
            DisableOwnedCharacterColliders();
            ClearOwnedBindings();
            ClearForceTargets();
            ClearManagedClothBindings();
        }

        private void OnDestroy()
        {
            SetControllerColliderState(false, false);
            DisableOwnedCharacterColliders();
            ClearOwnedBindings();
            ClearForceTargets();
            ClearManagedClothBindings();
        }

        private void OnLevelWasLoaded(int level)
        {
            ClearOwnedBindings();
            ClearForceTargets();
            ClearManagedClothBindings();
            _ownedHeadColliders.Clear();
            _ownedSkirtBodyColliders.Clear();
            _ownedFallbackHandColliders.Clear();
        }

        private List<DynamicBoneCollider> FindHandColliders(ChaControl character)
        {
            return character.GetComponentsInChildren<DynamicBoneCollider>(true)
                .Where(collider =>
                    CharacterColliderClassifier.IsReusableArmColliderName(collider.gameObject.name) &&
                    (_createFallbackColliders.Value ||
                     !CharacterColliderClassifier.IsPluginFallbackHandColliderName(collider.gameObject.name)))
                .GroupBy(ColliderId)
                .Select(group => group.First())
                .ToList();
        }

        private IList<DynamicBoneCollider> CreateFallbackHandColliders(ChaControl character)
        {
            var result = new List<DynamicBoneCollider>();
            var left = FindTransform(character.transform, "cf_s_hand_L");
            var right = FindTransform(character.transform, "cf_s_hand_R");

            if (left != null)
                result.Add(EnsureFallbackHandCollider(left, "L", new Vector3(-0.03f, -0.005f, 0f)));
            if (right != null)
                result.Add(EnsureFallbackHandCollider(right, "R", new Vector3(0.03f, -0.005f, 0f)));
            return result;
        }

        private DynamicBoneCollider EnsureFallbackHandCollider(Transform parent, string side, Vector3 center)
        {
            var colliderName = $"KKVRHandHairCollider_cf_s_hand_{side}";
            var collider = parent.GetComponentsInChildren<DynamicBoneCollider>(true)
                .FirstOrDefault(item => item.transform.parent == parent && item.gameObject.name == colliderName);
            if (collider == null)
            {
                var colliderObject = new GameObject(colliderName);
                colliderObject.transform.SetParent(parent, false);
                collider = colliderObject.AddComponent<DynamicBoneCollider>();
            }
            collider.m_Radius = 0.020f;
            collider.m_Height = 0.075f;
            collider.m_Center = center;
            collider.enabled = true;
            _ownedFallbackHandColliders.Add(collider);
            return collider;
        }

        private Dictionary<string, DynamicBoneTarget> FindHairTargets(ChaControl character)
        {
            var result = new Dictionary<string, DynamicBoneTarget>(StringComparer.Ordinal);
            AddTargets(character.objHair, result, InteractionTargetKind.Hair);
            return result;
        }

        private Dictionary<string, DynamicBoneTarget> FindAccessoryTargets(ChaControl character)
        {
            var result = new Dictionary<string, DynamicBoneTarget>(StringComparer.Ordinal);
            var roots = GetAccessoryRoots(character);
            var totalCount = 0;
            var inactiveCount = 0;
            var bodyPhysicsCount = 0;
            var descriptions = new HashSet<string>(StringComparer.Ordinal);
            var signatureParts = new List<string>();

            for (var slot = 0; slot < roots.Length; slot++)
            {
                var root = roots[slot];
                signatureParts.Add(root == null ? "null" : root.GetInstanceID().ToString());
                if (root == null)
                    continue;

                foreach (var bone in root.GetComponentsInChildren<DynamicBone>(true))
                {
                    totalCount++;
                    var rootNames = TransformNames(bone.m_Root).ToArray();
                    AddAccessoryDescription(descriptions, slot, nameof(DynamicBone), bone.name, rootNames.FirstOrDefault());
                    var bodyPhysics = AccessoryTargetClassifier.IsNativeBodyPhysics(bone.name, null, rootNames);
                    if (!AccessoryTargetClassifier.ShouldInclude(bone.enabled, bone.m_Root != null, bodyPhysics))
                    {
                        if (bodyPhysics)
                            bodyPhysicsCount++;
                        else
                            inactiveCount++;
                        continue;
                    }
                    AddTarget(result, DynamicBoneTarget.For(bone, InteractionTargetKind.Accessory));
                }

                foreach (var bone in root.GetComponentsInChildren<DynamicBone_Ver01>(true))
                {
                    totalCount++;
                    var rootNames = TransformNames(bone.m_Root).ToArray();
                    AddAccessoryDescription(descriptions, slot, nameof(DynamicBone_Ver01), bone.name, rootNames.FirstOrDefault());
                    var bodyPhysics = AccessoryTargetClassifier.IsNativeBodyPhysics(bone.name, null, rootNames);
                    if (!AccessoryTargetClassifier.ShouldInclude(bone.enabled, bone.m_Root != null, bodyPhysics))
                    {
                        if (bodyPhysics)
                            bodyPhysicsCount++;
                        else
                            inactiveCount++;
                        continue;
                    }
                    AddTarget(result, DynamicBoneTarget.For(bone, InteractionTargetKind.Accessory));
                }

                foreach (var bone in root.GetComponentsInChildren<DynamicBone_Ver02>(true))
                {
                    totalCount++;
                    var boneNames = bone.Bones == null
                        ? new string[0]
                        : bone.Bones.Where(item => item != null).Select(item => item.name).ToArray();
                    AddAccessoryDescription(descriptions, slot, nameof(DynamicBone_Ver02), bone.name, boneNames.FirstOrDefault());
                    var bodyPhysics = AccessoryTargetClassifier.IsNativeBodyPhysics(bone.name, bone.Comment, boneNames);
                    if (!AccessoryTargetClassifier.ShouldInclude(bone.enabled, boneNames.Length > 0, bodyPhysics))
                    {
                        if (bodyPhysics)
                            bodyPhysicsCount++;
                        else
                            inactiveCount++;
                        continue;
                    }
                    AddTarget(result, DynamicBoneTarget.For(bone, InteractionTargetKind.Accessory));
                }
            }

            LogAccessoryScan(
                character,
                signatureParts,
                descriptions,
                totalCount,
                result.Count,
                inactiveCount,
                bodyPhysicsCount);
            return result;
        }

        private static GameObject[] GetAccessoryRoots(ChaControl character)
        {
            var apiRoots = AccessoriesApi.GetAccessoryObjects(character) ?? new GameObject[0];
            var componentRoots = character.cusAcsCmp == null
                ? new GameObject[0]
                : character.cusAcsCmp
                    .Where(component => component != null)
                    .Select(component => component.gameObject)
                    .ToArray();
            return InteractionRootSelector.Select(apiRoots, componentRoots, true).ToArray();
        }

        private void LogAccessoryScan(
            ChaControl character,
            IList<string> signatureParts,
            HashSet<string> descriptions,
            int totalCount,
            int targetCount,
            int inactiveCount,
            int bodyPhysicsCount)
        {
            var sortedDescriptions = descriptions.OrderBy(item => item, StringComparer.Ordinal).ToArray();
            var signature = string.Join(",", signatureParts.ToArray()) + ":" + totalCount + ":" +
                            targetCount + ":" + inactiveCount + ":" + bodyPhysicsCount + ":" +
                            string.Join("|", sortedDescriptions);
            var characterId = character.GetInstanceID();
            string previousSignature;
            if (_accessoryScanSignatures.TryGetValue(characterId, out previousSignature) && previousSignature == signature)
                return;

            _accessoryScanSignatures[characterId] = signature;
            var examples = sortedDescriptions.Take(12).ToArray();
            var suffix = examples.Length == 0 ? string.Empty : $" Components: {string.Join(", ", examples)}.";
            Logger.LogInfo(
                $"Character {character.chaID}: accessory scan found {totalCount} Dynamic Bone components across {signatureParts.Count} slots, registered {targetCount}, skipped {inactiveCount} disabled/rootless, and excluded {bodyPhysicsCount} native body-physics references.{suffix}");
        }

        private static void AddAccessoryDescription(
            HashSet<string> descriptions,
            int slot,
            string typeName,
            string componentName,
            string rootName)
        {
            if (descriptions.Count >= 12)
                return;
            descriptions.Add($"slot{slot} {typeName} {componentName ?? "<unnamed>"}/{rootName ?? "<no-root>"}");
        }

        private static IEnumerable<string> TransformNames(Transform root)
        {
            if (root == null)
                yield break;

            yield return root.name;
            for (var index = 0; index < root.childCount; index++)
            {
                foreach (var childName in TransformNames(root.GetChild(index)))
                    yield return childName;
            }
        }

        private static Dictionary<string, DynamicBoneTarget> FindClothingTargets(ChaControl character)
        {
            var result = new Dictionary<string, DynamicBoneTarget>(StringComparer.Ordinal);
            var clothes = character.objClothes;
            if (clothes == null)
                return result;

            var slotCount = Math.Min(2, clothes.Length);
            for (var slot = 0; slot < slotCount; slot++)
            {
                var root = clothes[slot];
                if (root == null)
                    continue;

                foreach (var bone in root.GetComponentsInChildren<DynamicBone>(true))
                {
                    var boneNames = TransformNames(bone.m_Root).ToArray();
                    if (ClothingTargetClassifier.ShouldInclude(
                            bone.enabled,
                            bone.m_Root != null,
                            bone.name,
                            null,
                            boneNames))
                        AddTarget(result, DynamicBoneTarget.For(bone, InteractionTargetKind.Skirt));
                }

                foreach (var bone in root.GetComponentsInChildren<DynamicBone_Ver01>(true))
                {
                    var boneNames = TransformNames(bone.m_Root).ToArray();
                    if (ClothingTargetClassifier.ShouldInclude(
                            bone.enabled,
                            bone.m_Root != null,
                            bone.name,
                            bone.comment,
                            boneNames))
                        AddTarget(result, DynamicBoneTarget.For(bone, InteractionTargetKind.Skirt));
                }

                foreach (var bone in root.GetComponentsInChildren<DynamicBone_Ver02>(true))
                {
                    var boneNames = bone.Bones == null
                        ? new string[0]
                        : bone.Bones.Where(item => item != null).Select(item => item.name).ToArray();
                    if (ClothingTargetClassifier.ShouldInclude(
                            bone.enabled,
                            boneNames.Length > 0,
                            bone.name,
                            bone.Comment,
                            boneNames))
                        AddTarget(result, DynamicBoneTarget.For(bone, InteractionTargetKind.Skirt));
                }
            }

            return result;
        }

        private void LogClothingScan(ChaControl character, int skirtTargetCount)
        {
            var clothes = character.objClothes;
            if (clothes == null)
                return;

            var totalCount = 0;
            var descriptions = new HashSet<string>(StringComparer.Ordinal);
            var signatureParts = new List<string>();
            var slotCount = Math.Min(2, clothes.Length);
            for (var slot = 0; slot < slotCount; slot++)
            {
                var root = clothes[slot];
                signatureParts.Add(root == null ? "null" : root.GetInstanceID().ToString());
                if (root == null)
                    continue;

                foreach (var bone in root.GetComponentsInChildren<DynamicBone>(true))
                {
                    totalCount++;
                    AddClothingDescription(descriptions, slot, bone.name, bone.m_Root == null ? null : bone.m_Root.name);
                }

                foreach (var bone in root.GetComponentsInChildren<DynamicBone_Ver01>(true))
                {
                    totalCount++;
                    AddClothingDescription(descriptions, slot, bone.name, bone.m_Root == null ? null : bone.m_Root.name);
                }

                foreach (var bone in root.GetComponentsInChildren<DynamicBone_Ver02>(true))
                {
                    totalCount++;
                    var firstBoneName = bone.Bones == null
                        ? null
                        : bone.Bones.Where(item => item != null).Select(item => item.name).FirstOrDefault();
                    AddClothingDescription(descriptions, slot, bone.name, firstBoneName);
                }
            }

            var sortedDescriptions = descriptions.OrderBy(item => item, StringComparer.Ordinal).ToArray();
            var signature = string.Join(",", signatureParts.ToArray()) + ":" + totalCount + ":" +
                            skirtTargetCount + ":" + string.Join("|", sortedDescriptions);
            var characterId = character.GetInstanceID();
            string previousSignature;
            if (_clothingScanSignatures.TryGetValue(characterId, out previousSignature) && previousSignature == signature)
                return;

            _clothingScanSignatures[characterId] = signature;
            var examples = sortedDescriptions.Take(12).ToArray();
            var suffix = examples.Length == 0 ? string.Empty : $" Components: {string.Join(", ", examples)}.";
            Logger.LogInfo($"Character {character.chaID}: clothing scan found {totalCount} Dynamic Bone components in top/bottom slots and registered {skirtTargetCount} physical garment targets.{suffix}");
        }

        private static void AddClothingDescription(HashSet<string> descriptions, int slot, string componentName, string rootName)
        {
            if (descriptions.Count >= 12)
                return;
            descriptions.Add($"slot{slot} {componentName ?? "<unnamed>"}/{rootName ?? "<no-root>"}");
        }

        private static void AddTargets(
            IEnumerable<GameObject> roots,
            IDictionary<string, DynamicBoneTarget> result,
            InteractionTargetKind kind)
        {
            if (roots == null)
                return;

            foreach (var root in roots)
            {
                if (root == null)
                    continue;

                foreach (var bone in root.GetComponentsInChildren<DynamicBone>(true))
                {
                    if (bone.enabled && bone.m_Root != null)
                        AddTarget(result, DynamicBoneTarget.For(bone, kind));
                }
                foreach (var bone in root.GetComponentsInChildren<DynamicBone_Ver01>(true))
                {
                    if (bone.enabled && bone.m_Root != null)
                        AddTarget(result, DynamicBoneTarget.For(bone, kind));
                }
                foreach (var bone in root.GetComponentsInChildren<DynamicBone_Ver02>(true))
                {
                    if (bone.enabled && bone.Bones != null && bone.Bones.Any(item => item != null))
                        AddTarget(result, DynamicBoneTarget.For(bone, kind));
                }
            }
        }

        private static void AddTarget(IDictionary<string, DynamicBoneTarget> result, DynamicBoneTarget target)
        {
            result[target.Id] = target;
        }

        private static string ColliderId(DynamicBoneCollider collider) => collider.GetInstanceID().ToString();

        private static string BindingId(string targetId, DynamicBoneCollider collider)
        {
            return targetId + ":" + ColliderId(collider);
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static Transform FindTransform(Transform root, string name)
        {
            if (root.name == name)
                return root;

            for (var index = 0; index < root.childCount; index++)
            {
                var found = FindTransform(root.GetChild(index), name);
                if (found != null)
                    return found;
            }

            return null;
        }

        private static Transform[] GetContactSamples(Transform root)
        {
            if (root == null)
                return new Transform[0];

            var transforms = new List<Transform>();
            AddContactSamples(root, transforms);
            return LimitContactSamples(transforms);
        }

        private static ContactSegment[] GetContactSegments(Transform root)
        {
            if (root == null)
                return new ContactSegment[0];

            var segments = new List<ContactSegment>();
            AddContactSegments(root, segments);
            return LimitContactSegments(segments);
        }

        private static ContactSegment[] GetContactSegments(IList<Transform> transforms)
        {
            var bones = transforms == null
                ? new List<Transform>()
                : transforms.Where(item => item != null).Distinct().ToList();
            var knownBones = new HashSet<Transform>(bones);
            var segments = bones
                .Where(item => item.parent != null && knownBones.Contains(item.parent))
                .Select(item => new ContactSegment(item.parent, item))
                .ToList();

            if (segments.Count == 0)
            {
                for (var index = 1; index < bones.Count; index++)
                    segments.Add(new ContactSegment(bones[index - 1], bones[index]));
            }
            return LimitContactSegments(segments);
        }

        private static void AddContactSamples(Transform current, ICollection<Transform> samples)
        {
            samples.Add(current);
            for (var index = 0; index < current.childCount; index++)
                AddContactSamples(current.GetChild(index), samples);
        }

        private static void AddContactSegments(Transform current, ICollection<ContactSegment> segments)
        {
            for (var index = 0; index < current.childCount; index++)
            {
                var child = current.GetChild(index);
                segments.Add(new ContactSegment(current, child));
                AddContactSegments(child, segments);
            }
        }

        private static Transform[] LimitContactSamples(IList<Transform> samples)
        {
            var result = new List<Transform>(MaximumContactSamplesPerTarget);
            var uniqueSamples = samples.Where(item => item != null).Distinct().ToList();
            foreach (var sourceIndex in ContactSamplePlanner.PlanIndices(
                         uniqueSamples.Count,
                         MaximumContactSamplesPerTarget))
            {
                result.Add(uniqueSamples[sourceIndex]);
            }

            return result.ToArray();
        }

        private static ContactSegment[] LimitContactSegments(IList<ContactSegment> segments)
        {
            var result = new List<ContactSegment>(MaximumContactSamplesPerTarget);
            foreach (var sourceIndex in ContactSamplePlanner.PlanIndices(
                         segments.Count,
                         MaximumContactSamplesPerTarget))
            {
                result.Add(segments[sourceIndex]);
            }
            return result.ToArray();
        }

        private sealed class ContactSegment
        {
            public ContactSegment(Transform start, Transform end)
            {
                Start = start;
                End = end;
            }

            public Transform Start { get; }
            public Transform End { get; }
        }

        private sealed class SkirtColliderSpec
        {
            public SkirtColliderSpec(string boneName, string nameSuffix, float radius, float height, Vector3 center)
            {
                BoneName = boneName;
                NameSuffix = nameSuffix;
                Radius = radius;
                Height = height;
                Center = center;
            }

            public string BoneName { get; }
            public string NameSuffix { get; }
            public float Radius { get; }
            public float Height { get; }
            public Vector3 Center { get; }
        }

        private sealed class OwnedDynamicBoneBinding
        {
            private readonly DynamicBoneTarget _target;
            private readonly DynamicBoneCollider _collider;

            public OwnedDynamicBoneBinding(DynamicBoneTarget target, DynamicBoneCollider collider)
            {
                _target = target ?? throw new ArgumentNullException(nameof(target));
                _collider = collider ?? throw new ArgumentNullException(nameof(collider));
            }

            public void Remove()
            {
                if (_target.IsAlive)
                    _target.Remove(_collider);
            }
        }

        private sealed class DynamicBoneTarget
        {
            private readonly MonoBehaviour _bone;
            private readonly Func<DynamicBoneCollider, bool> _contains;
            private readonly Action<DynamicBoneCollider> _add;
            private readonly Action<DynamicBoneCollider> _remove;
            private readonly Transform[] _contactSamples;
            private readonly ContactSegment[] _contactSegments;
            private readonly Func<Vector3> _getForce;
            private readonly Action<Vector3> _setForce;
            private readonly Action _resetParticles;
            private Vector3? _originalForce;
            private float _quietTime;

            private DynamicBoneTarget(
                string id,
                MonoBehaviour bone,
                Func<DynamicBoneCollider, bool> contains,
                Action<DynamicBoneCollider> add,
                Action<DynamicBoneCollider> remove,
                Transform[] contactSamples,
                ContactSegment[] contactSegments,
                Func<Vector3> getForce,
                Action<Vector3> setForce,
                Action resetParticles,
                InteractionTargetKind kind)
            {
                Id = id;
                _bone = bone;
                _contains = contains;
                _add = add;
                _remove = remove;
                _contactSamples = contactSamples;
                _contactSegments = contactSegments;
                _getForce = getForce;
                _setForce = setForce;
                _resetParticles = resetParticles;
                Kind = kind;
                ChainSpan = CalculateChainSpan(contactSamples);
            }

            public string Id { get; }
            public bool IsAlive => _bone != null;
            public InteractionTargetKind Kind { get; }
            public float ChainSpan { get; }
            public bool Contains(DynamicBoneCollider collider) => _contains(collider);
            public void Add(DynamicBoneCollider collider) => _add(collider);
            public void Remove(DynamicBoneCollider collider) => _remove(collider);

            public bool TryGetMinimumDistance(Vector3 point, out float distance)
            {
                Vector3 closestPosition;
                return TryGetClosestContact(point, out closestPosition, out distance);
            }

            public bool TryGetClosestContact(Vector3 point, out Vector3 closestPosition, out float distance)
            {
                Transform sample;
                var found = TryGetClosestSample(point, out sample, out distance);
                var minimumSquaredDistance = found ? distance * distance : float.MaxValue;
                closestPosition = found ? sample.position : Vector3.zero;
                if (Kind == InteractionTargetKind.Hair || _contactSegments.Length == 0)
                    return found;

                var contactPoint = ToContactVector(point);
                foreach (var segment in _contactSegments)
                {
                    if (segment.Start == null || segment.End == null)
                        continue;

                    SegmentProjection projection;
                    if (!ContactSegmentMath.TryProject(
                            contactPoint,
                            ToContactVector(segment.Start.position),
                            ToContactVector(segment.End.position),
                            out projection) ||
                        projection.SquaredDistance >= minimumSquaredDistance)
                        continue;

                    minimumSquaredDistance = projection.SquaredDistance;
                    closestPosition = new Vector3(
                        projection.Point.X,
                        projection.Point.Y,
                        projection.Point.Z);
                    found = true;
                }

                distance = found ? (float)Math.Sqrt(minimumSquaredDistance) : 0f;
                return found;
            }

            public bool TryGetClosestSample(Vector3 point, out Transform closestSample, out float distance)
            {
                var minimumSquaredDistance = float.MaxValue;
                closestSample = null;
                foreach (var sample in _contactSamples)
                {
                    if (sample == null)
                        continue;

                    var squaredDistance = (sample.position - point).sqrMagnitude;
                    if (!IsFinite(squaredDistance))
                        continue;

                    if (squaredDistance < minimumSquaredDistance)
                    {
                        minimumSquaredDistance = squaredDistance;
                        closestSample = sample;
                    }
                }

                distance = closestSample == null ? 0f : (float)Math.Sqrt(minimumSquaredDistance);
                return closestSample != null;
            }

            public void ApplyForce(Vector3 force)
            {
                if (!IsAlive)
                    return;
                if (!_originalForce.HasValue)
                    _originalForce = _getForce();
                _setForce(_originalForce.Value + force);
                _quietTime = 0f;
            }

            public void ReleaseForce(float deltaTime)
            {
                if (!_originalForce.HasValue)
                    return;

                _setForce(_originalForce.Value);
                _quietTime += Math.Max(0f, deltaTime);
                if (_quietTime < 1f)
                    return;

                _resetParticles?.Invoke();
                _originalForce = null;
                _quietTime = 0f;
            }

            public void ResetForce()
            {
                if (!_originalForce.HasValue)
                    return;
                if (IsAlive)
                    _setForce(_originalForce.Value);
                _originalForce = null;
                _quietTime = 0f;
            }

            public static DynamicBoneTarget For(DynamicBone bone, InteractionTargetKind kind)
            {
                if (bone.m_Colliders == null)
                    bone.m_Colliders = new List<DynamicBoneCollider>();
                return Create(
                    bone,
                    bone.m_Colliders,
                    GetContactSamples(bone.m_Root),
                    GetContactSegments(bone.m_Root),
                    () => bone.m_Force,
                    force => bone.m_Force = force,
                    bone.ResetParticlesPosition,
                    kind);
            }

            public static DynamicBoneTarget For(DynamicBone_Ver01 bone, InteractionTargetKind kind)
            {
                if (bone.m_Colliders == null)
                    bone.m_Colliders = new List<DynamicBoneCollider>();
                return Create(
                    bone,
                    bone.m_Colliders,
                    GetContactSamples(bone.m_Root),
                    GetContactSegments(bone.m_Root),
                    () => bone.m_Force,
                    force => bone.m_Force = force,
                    bone.ResetParticlesPosition,
                    kind);
            }

            public static DynamicBoneTarget For(DynamicBone_Ver02 bone, InteractionTargetKind kind)
            {
                if (bone.Colliders == null)
                    bone.Colliders = new List<DynamicBoneCollider>();
                var contactBones = bone.Bones == null
                    ? new List<Transform>()
                    : bone.Bones.Where(item => item != null).Distinct().ToList();
                return Create(
                    bone,
                    bone.Colliders,
                    LimitContactSamples(contactBones),
                    GetContactSegments(contactBones),
                    () => bone.Force,
                    force => bone.Force = force,
                    bone.ResetParticlesPosition,
                    kind);
            }

            private static DynamicBoneTarget Create(
                MonoBehaviour bone,
                IList<DynamicBoneCollider> colliders,
                Transform[] contactSamples,
                ContactSegment[] contactSegments,
                Func<Vector3> getForce,
                Action<Vector3> setForce,
                Action resetParticles,
                InteractionTargetKind kind)
            {
                var id = $"{bone.GetType().Name}:{bone.GetInstanceID()}";
                return new DynamicBoneTarget(
                    id,
                    bone,
                    colliders.Contains,
                    colliders.Add,
                    collider => colliders.Remove(collider),
                    contactSamples,
                    contactSegments,
                    getForce,
                    setForce,
                    resetParticles,
                    kind);
            }

            private static ContactVector3 ToContactVector(Vector3 value)
            {
                return new ContactVector3(value.x, value.y, value.z);
            }

            private static float CalculateChainSpan(IList<Transform> samples)
            {
                var maximumSquaredDistance = 0f;
                for (var firstIndex = 0; firstIndex < samples.Count; firstIndex++)
                {
                    var first = samples[firstIndex];
                    if (first == null)
                        continue;

                    for (var secondIndex = firstIndex + 1; secondIndex < samples.Count; secondIndex++)
                    {
                        var second = samples[secondIndex];
                        if (second == null)
                            continue;

                        var squaredDistance = (second.position - first.position).sqrMagnitude;
                        if (IsFinite(squaredDistance) && squaredDistance > maximumSquaredDistance)
                            maximumSquaredDistance = squaredDistance;
                    }
                }

                return (float)Math.Sqrt(maximumSquaredDistance);
            }
        }

        private sealed class GrabState
        {
            public bool IsActive => Target != null;
            public DynamicBoneTarget Target { get; private set; }
            public Transform Sample { get; private set; }
            public Vector3 ControllerToSampleOffset { get; private set; }

            public void Latch(DynamicBoneTarget target, Transform sample, Vector3 controllerPosition)
            {
                if (target == null) throw new ArgumentNullException(nameof(target));
                if (sample == null) throw new ArgumentNullException(nameof(sample));

                Target = target;
                Sample = sample;
                ControllerToSampleOffset = sample.position - controllerPosition;
            }

            public void Release()
            {
                Target = null;
                Sample = null;
                ControllerToSampleOffset = Vector3.zero;
            }
        }

        private sealed class ControllerMotionState
        {
            private Transform _transform;
            private VRTK_ControllerEvents _controllerEvents;
            private Vector3 _previousPosition;
            private bool _hasPreviousPosition;

            public bool IsAvailable { get; private set; }
            public bool HasGripInput => _controllerEvents != null;
            public bool GripPressed => IsAvailable && _controllerEvents != null && _controllerEvents.gripPressed;
            public Vector3 Position { get; private set; }
            public Vector3 Velocity { get; private set; }

            public void SetController(GameObject controller)
            {
                var controllerTransform = controller == null ? null : controller.transform;
                if (_transform == controllerTransform)
                {
                    if (_controllerEvents == null && controller != null)
                        _controllerEvents = FindControllerEvents(controller);
                    return;
                }

                _transform = controllerTransform;
                _controllerEvents = controller == null ? null : FindControllerEvents(controller);
                _hasPreviousPosition = false;
                IsAvailable = false;
                Velocity = Vector3.zero;
            }

            private static VRTK_ControllerEvents FindControllerEvents(GameObject controller)
            {
                var events = controller.GetComponent<VRTK_ControllerEvents>();
                if (events == null)
                    events = controller.GetComponentInChildren<VRTK_ControllerEvents>(true);
                if (events == null)
                    events = controller.GetComponentInParent<VRTK_ControllerEvents>();
                return events;
            }

            public void Sample(float deltaTime, float smoothing)
            {
                if (_transform == null || !_transform.gameObject.activeInHierarchy)
                {
                    IsAvailable = false;
                    Velocity = Vector3.zero;
                    _hasPreviousPosition = false;
                    return;
                }

                Position = _transform.position;
                IsAvailable = true;
                if (!_hasPreviousPosition || deltaTime <= 0f || deltaTime > 0.25f)
                {
                    _previousPosition = Position;
                    Velocity = Vector3.zero;
                    _hasPreviousPosition = true;
                    return;
                }

                var rawVelocity = (Position - _previousPosition) / deltaTime;
                Velocity = IsFinite(rawVelocity.sqrMagnitude)
                    ? Vector3.Lerp(rawVelocity, Velocity, smoothing)
                    : Vector3.zero;
                _previousPosition = Position;
            }
        }
    }
}
