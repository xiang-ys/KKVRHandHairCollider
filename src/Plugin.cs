using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using KKVRHandHairCollider.Core;
using UnityEngine;
using VRTK;

namespace KKVRHandHairCollider
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("KoikatuVR")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "local.kkvr.handhaircollider";
        public const string Name = "KKVR Hair and Clothing Interaction";
        public const string Version = "0.6.2";

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
        private ConfigEntry<bool> _clothingForceEnabled;
        private ConfigEntry<float> _clothingForceStrength;
        private ConfigEntry<float> _clothingMaximumForce;
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
            _includeClothing = Config.Bind("General", "Include skirt Dynamic Bones", true, "Bind controllers to skirt chains found in the top and bottom clothing slots.");
            _controllerCollidersEnabled = Config.Bind("Controller collision", "Enabled", true, "Use the tracked VR controllers as Dynamic Bone colliders.");
            _controllerRadius = Config.Bind("Controller collision", "Radius meters", 0.035f, new ConfigDescription("Radius of each spherical controller collider.", new AcceptableValueRange<float>(0.015f, 0.12f)));
            _controllerForceEnabled = Config.Bind("Controller force", "Enabled", true, "Apply controller-velocity force to nearby hair Dynamic Bones.");
            _forceContactPadding = Config.Bind("Controller force", "Contact padding meters", 0.008f, new ConfigDescription("Soft force falloff outside the controller collider surface.", new AcceptableValueRange<float>(0.001f, 0.05f)));
            _forceStrength = Config.Bind("Controller force", "Strength", 0.018f, new ConfigDescription("Force generated per meter/second of controller speed.", new AcceptableValueRange<float>(0.005f, 0.20f)));
            _maximumForce = Config.Bind("Controller force", "Maximum force", 0.04f, new ConfigDescription("Safety cap for the force applied to one Dynamic Bone.", new AcceptableValueRange<float>(0.02f, 0.30f)));
            _minimumControllerSpeed = Config.Bind("Controller force", "Minimum speed meters per second", 0.15f, new ConfigDescription("Controller movement slower than this is treated as tracking drift.", new AcceptableValueRange<float>(0f, 0.50f)));
            _velocitySmoothing = Config.Bind("Controller force", "Velocity smoothing", 0.35f, new ConfigDescription("Higher values reduce tracking jitter but soften sudden motion.", new AcceptableValueRange<float>(0f, 0.95f)));
            _clothingForceEnabled = Config.Bind("Clothing force", "Enabled", true, "Apply conservative controller-velocity force to nearby skirt chains.");
            _clothingForceStrength = Config.Bind("Clothing force", "Strength", 0.012f, new ConfigDescription("Force generated per meter/second for skirt chains.", new AcceptableValueRange<float>(0.002f, 0.10f)));
            _clothingMaximumForce = Config.Bind("Clothing force", "Maximum force", 0.025f, new ConfigDescription("Safety cap for force applied to one skirt Dynamic Bone.", new AcceptableValueRange<float>(0.005f, 0.15f)));
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
            MigrateTuning();
            Config.Save();
            _initialized = true;
            Logger.LogMessage("Controller hair/clothing collision, force, and grip interaction loaded; waiting for VR controllers and characters.");
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
            RegisterForceTargets(hairTargets.Values, "hair/accessory");
            BindTargets(character, hairTargets, hairColliders, "controller/head/hand-to-hair");

            SyncUnityCloth(character, _unityClothEnabled.Value
                ? AvailableControllerClothColliders()
                : new SphereCollider[0]);

            if (!_includeClothing.Value)
                return;

            var skirtTargets = FindSkirtTargets(character);
            if (skirtTargets.Count == 0)
                return;

            var skirtBodyColliders = _skirtBodyCollidersEnabled.Value
                ? EnsureSkirtBodyColliders(character)
                : new List<DynamicBoneCollider>();
            var skirtColliders = ColliderSourceSelector.SelectForSkirt(
                    controllerColliders,
                    skirtBodyColliders)
                .Where(collider => collider != null)
                .ToList();

            RegisterForceTargets(skirtTargets.Values, "skirt");
            BindTargets(character, skirtTargets, skirtColliders, "controller/body-to-skirt");
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
            var clothes = character.objClothes;
            if (clothes == null)
                return;

            var added = 0;
            var clothCount = 0;
            foreach (var root in clothes)
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
                Logger.LogInfo($"Character {character.chaID}: added {added} controller sphere bindings across {clothCount} Unity Cloth components.");
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
                leftController = VRTK_DeviceFinder.GetControllerLeftHand(false);
                if (leftController == null)
                    leftController = VRTK_DeviceFinder.GetControllerLeftHand(true);
                rightController = VRTK_DeviceFinder.GetControllerRightHand(false);
                if (rightController == null)
                    rightController = VRTK_DeviceFinder.GetControllerRightHand(true);

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

        private void SetControllerColliderState(bool dynamicBoneEnabled, bool clothEnabled)
        {
            if (_leftControllerCollider != null)
                _leftControllerCollider.enabled = dynamicBoneEnabled;
            if (_rightControllerCollider != null)
                _rightControllerCollider.enabled = dynamicBoneEnabled;
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
                Logger.LogInfo($"Registered {added} {targetLabel} Dynamic Bones for controller velocity force.");
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

                var strength = target.IsClothing ? _clothingForceStrength.Value : _forceStrength.Value;
                var maximumForce = target.IsClothing ? _clothingMaximumForce.Value : _maximumForce.Value;
                var velocityForceEnabled = _controllerForceEnabled.Value &&
                                           (!target.IsClothing || _clothingForceEnabled.Value);
                var force = velocityForceEnabled
                    ? CalculateControllerForce(_leftControllerMotion, target, strength, maximumForce) +
                      CalculateControllerForce(_rightControllerMotion, target, strength, maximumForce)
                    : Vector3.zero;
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

            var latchDistance = _controllerRadius.Value + _forceContactPadding.Value;
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
            float strength,
            float maximumForce)
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
                strength,
                maximumForce,
                distance,
                _controllerRadius.Value,
                _forceContactPadding.Value,
                _minimumControllerSpeed.Value);
            return speed <= 0f || magnitude <= 0f
                ? Vector3.zero
                : controller.Velocity / speed * magnitude;
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
        }

        private void DisableOwnedCharacterColliders()
        {
            SetColliderCollectionState(_ownedHeadColliders, false);
            SetColliderCollectionState(_ownedSkirtBodyColliders, false);
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
        }

        private List<DynamicBoneCollider> FindHandColliders(ChaControl character)
        {
            return character.GetComponentsInChildren<DynamicBoneCollider>(true)
                .Where(collider => CharacterColliderClassifier.IsReusableArmColliderName(collider.gameObject.name))
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
                result.Add(CreateCollider(left, "L", new Vector3(-0.03f, -0.005f, 0f)));
            if (right != null)
                result.Add(CreateCollider(right, "R", new Vector3(0.03f, -0.005f, 0f)));
            return result;
        }

        private static DynamicBoneCollider CreateCollider(Transform parent, string side, Vector3 center)
        {
            var colliderObject = new GameObject($"KKVRHandHairCollider_cf_s_hand_{side}");
            colliderObject.transform.SetParent(parent, false);
            var collider = colliderObject.AddComponent<DynamicBoneCollider>();
            collider.m_Radius = 0.020f;
            collider.m_Height = 0.075f;
            collider.m_Center = center;
            return collider;
        }

        private Dictionary<string, DynamicBoneTarget> FindHairTargets(ChaControl character)
        {
            var result = new Dictionary<string, DynamicBoneTarget>(StringComparer.Ordinal);
            AddTargets(character.objHair, result);
            if (_includeAccessories.Value)
                AddTargets(character.objAccessory, result);
            return result;
        }

        private static Dictionary<string, DynamicBoneTarget> FindSkirtTargets(ChaControl character)
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
                    if (ContainsSkirtBone(bone.m_Root))
                        AddTarget(result, DynamicBoneTarget.For(bone, true));
                }

                foreach (var bone in root.GetComponentsInChildren<DynamicBone_Ver01>(true))
                {
                    if (ContainsSkirtBone(bone.m_Root))
                        AddTarget(result, DynamicBoneTarget.For(bone, true));
                }

                foreach (var bone in root.GetComponentsInChildren<DynamicBone_Ver02>(true))
                {
                    if (bone.Bones != null && bone.Bones.Any(item => item != null && SkirtTargetClassifier.IsSkirtBoneName(item.name)))
                        AddTarget(result, DynamicBoneTarget.For(bone, true));
                }
            }

            return result;
        }

        private static bool ContainsSkirtBone(Transform root)
        {
            if (root == null)
                return false;
            if (SkirtTargetClassifier.IsSkirtBoneName(root.name))
                return true;

            for (var index = 0; index < root.childCount; index++)
            {
                if (ContainsSkirtBone(root.GetChild(index)))
                    return true;
            }

            return false;
        }

        private static void AddTargets(IEnumerable<GameObject> roots, IDictionary<string, DynamicBoneTarget> result)
        {
            if (roots == null)
                return;

            foreach (var root in roots)
            {
                if (root == null)
                    continue;

                foreach (var bone in root.GetComponentsInChildren<DynamicBone>(true))
                    AddTarget(result, DynamicBoneTarget.For(bone));
                foreach (var bone in root.GetComponentsInChildren<DynamicBone_Ver01>(true))
                    AddTarget(result, DynamicBoneTarget.For(bone));
                foreach (var bone in root.GetComponentsInChildren<DynamicBone_Ver02>(true))
                    AddTarget(result, DynamicBoneTarget.For(bone));
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

        private static void AddContactSamples(Transform current, ICollection<Transform> samples)
        {
            samples.Add(current);
            for (var index = 0; index < current.childCount; index++)
                AddContactSamples(current.GetChild(index), samples);
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
                Func<Vector3> getForce,
                Action<Vector3> setForce,
                Action resetParticles,
                bool isClothing)
            {
                Id = id;
                _bone = bone;
                _contains = contains;
                _add = add;
                _remove = remove;
                _contactSamples = contactSamples;
                _getForce = getForce;
                _setForce = setForce;
                _resetParticles = resetParticles;
                IsClothing = isClothing;
            }

            public string Id { get; }
            public bool IsAlive => _bone != null;
            public bool IsClothing { get; }
            public bool Contains(DynamicBoneCollider collider) => _contains(collider);
            public void Add(DynamicBoneCollider collider) => _add(collider);
            public void Remove(DynamicBoneCollider collider) => _remove(collider);

            public bool TryGetMinimumDistance(Vector3 point, out float distance)
            {
                Transform sample;
                return TryGetClosestSample(point, out sample, out distance);
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

            public static DynamicBoneTarget For(DynamicBone bone, bool isClothing = false)
            {
                if (bone.m_Colliders == null)
                    bone.m_Colliders = new List<DynamicBoneCollider>();
                return Create(
                    bone,
                    bone.m_Colliders,
                    GetContactSamples(bone.m_Root),
                    () => bone.m_Force,
                    force => bone.m_Force = force,
                    bone.ResetParticlesPosition,
                    isClothing);
            }

            public static DynamicBoneTarget For(DynamicBone_Ver01 bone, bool isClothing = false)
            {
                if (bone.m_Colliders == null)
                    bone.m_Colliders = new List<DynamicBoneCollider>();
                return Create(
                    bone,
                    bone.m_Colliders,
                    GetContactSamples(bone.m_Root),
                    () => bone.m_Force,
                    force => bone.m_Force = force,
                    bone.ResetParticlesPosition,
                    isClothing);
            }

            public static DynamicBoneTarget For(DynamicBone_Ver02 bone, bool isClothing = false)
            {
                if (bone.Colliders == null)
                    bone.Colliders = new List<DynamicBoneCollider>();
                return Create(
                    bone,
                    bone.Colliders,
                    LimitContactSamples(bone.Bones == null
                        ? new List<Transform>()
                        : bone.Bones.Where(item => item != null).Distinct().ToList()),
                    () => bone.Force,
                    force => bone.Force = force,
                    bone.ResetParticlesPosition,
                    isClothing);
            }

            private static DynamicBoneTarget Create(
                MonoBehaviour bone,
                IList<DynamicBoneCollider> colliders,
                Transform[] contactSamples,
                Func<Vector3> getForce,
                Action<Vector3> setForce,
                Action resetParticles,
                bool isClothing)
            {
                var id = $"{bone.GetType().Name}:{bone.GetInstanceID()}";
                return new DynamicBoneTarget(
                    id,
                    bone,
                    colliders.Contains,
                    colliders.Add,
                    collider => colliders.Remove(collider),
                    contactSamples,
                    getForce,
                    setForce,
                    resetParticles,
                    isClothing);
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
