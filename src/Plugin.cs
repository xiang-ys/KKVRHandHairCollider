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
        public const string Name = "KKVR Hand Hair Collider Prototype";
        public const string Version = "0.5.0";

        private static readonly string[] ExistingColliderNames =
        {
            "Colliders_cf_s_hand_L",
            "Colliders_cf_s_hand_R",
            "KKVRHandHairCollider_cf_s_hand_L",
            "KKVRHandHairCollider_cf_s_hand_R"
        };

        private ConfigEntry<bool> _enabled;
        private ConfigEntry<bool> _includeAccessories;
        private ConfigEntry<bool> _controllerCollidersEnabled;
        private ConfigEntry<float> _controllerRadius;
        private ConfigEntry<bool> _controllerForceEnabled;
        private ConfigEntry<float> _forceContactPadding;
        private ConfigEntry<float> _forceStrength;
        private ConfigEntry<float> _maximumForce;
        private ConfigEntry<float> _minimumControllerSpeed;
        private ConfigEntry<float> _velocitySmoothing;
        private ConfigEntry<bool> _includeCharacterHandColliders;
        private ConfigEntry<bool> _createFallbackColliders;
        private ConfigEntry<bool> _headColliderEnabled;
        private ConfigEntry<float> _headColliderRadius;
        private ConfigEntry<float> _headColliderHeight;
        private ConfigEntry<float> _headColliderCenterY;
        private ConfigEntry<float> _scanInterval;
        private ConfigEntry<int> _tuningVersion;
        private float _nextScan;
        private bool _initialized;
        private DynamicBoneCollider _leftControllerCollider;
        private DynamicBoneCollider _rightControllerCollider;
        private readonly ControllerMotionState _leftControllerMotion = new ControllerMotionState();
        private readonly ControllerMotionState _rightControllerMotion = new ControllerMotionState();
        private readonly Dictionary<string, DynamicBoneTarget> _forceTargets = new Dictionary<string, DynamicBoneTarget>(StringComparer.Ordinal);
        private bool _controllerLookupWarningLogged;

        private void Awake() => Initialize();

        private void OnEnable() => Initialize();

        private void Start() => Initialize();

        private void Initialize()
        {
            if (_initialized)
                return;

            _enabled = Config.Bind("General", "Enabled", true, "Attach character hand colliders to hair Dynamic Bones.");
            _includeAccessories = Config.Bind("General", "Include accessory Dynamic Bones", true, "Also bind hand colliders to accessory Dynamic Bones.");
            _controllerCollidersEnabled = Config.Bind("Controller collision", "Enabled", true, "Use the tracked VR controllers as Dynamic Bone colliders.");
            _controllerRadius = Config.Bind("Controller collision", "Radius meters", 0.035f, new ConfigDescription("Radius of each spherical controller collider.", new AcceptableValueRange<float>(0.015f, 0.12f)));
            _controllerForceEnabled = Config.Bind("Controller force", "Enabled", true, "Apply controller-velocity force to nearby hair Dynamic Bones.");
            _forceContactPadding = Config.Bind("Controller force", "Contact padding meters", 0.008f, new ConfigDescription("Soft force falloff outside the controller collider surface.", new AcceptableValueRange<float>(0.001f, 0.05f)));
            _forceStrength = Config.Bind("Controller force", "Strength", 0.018f, new ConfigDescription("Force generated per meter/second of controller speed.", new AcceptableValueRange<float>(0.005f, 0.20f)));
            _maximumForce = Config.Bind("Controller force", "Maximum force", 0.04f, new ConfigDescription("Safety cap for the force applied to one Dynamic Bone.", new AcceptableValueRange<float>(0.02f, 0.30f)));
            _minimumControllerSpeed = Config.Bind("Controller force", "Minimum speed meters per second", 0.15f, new ConfigDescription("Controller movement slower than this is treated as tracking drift.", new AcceptableValueRange<float>(0f, 0.50f)));
            _velocitySmoothing = Config.Bind("Controller force", "Velocity smoothing", 0.35f, new ConfigDescription("Higher values reduce tracking jitter but soften sudden motion.", new AcceptableValueRange<float>(0f, 0.95f)));
            _includeCharacterHandColliders = Config.Bind("Character hands", "Include character hand colliders", false, "Also bind colliders attached to the character's hand bones.");
            _createFallbackColliders = Config.Bind("Character hands", "Create fallback hand colliders", true, "Create character hand colliders when KK_Colliders has not created them.");
            _headColliderEnabled = Config.Bind("Head collision", "Enabled", true, "Prevent hair Dynamic Bones from passing through the character's head.");
            _headColliderRadius = Config.Bind("Head collision", "Radius meters", 0.075f, new ConfigDescription("Radius of the head capsule collider.", new AcceptableValueRange<float>(0.05f, 0.16f)));
            _headColliderHeight = Config.Bind("Head collision", "Height meters", 0.10f, new ConfigDescription("Height of the head capsule collider along its local Y axis.", new AcceptableValueRange<float>(0f, 0.25f)));
            _headColliderCenterY = Config.Bind("Head collision", "Center Y meters", 0.015f, new ConfigDescription("Vertical offset of the head collider on the head bone.", new AcceptableValueRange<float>(-0.10f, 0.10f)));
            _scanInterval = Config.Bind("General", "Scan interval seconds", 1.0f, new ConfigDescription("How often loaded characters are checked.", new AcceptableValueRange<float>(0.25f, 10f)));
            _tuningVersion = Config.Bind("General", "Tuning version", 0, "Internal parameter migration version.");
            MigrateTuning();
            Config.Save();
            _initialized = true;
            Logger.LogMessage("Direct-controller prototype loaded; waiting for VR controllers and characters.");
        }

        private void MigrateTuning()
        {
            if (_tuningVersion.Value >= 2)
                return;

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

        private void Update()
        {
            Initialize();
            if (!_enabled.Value || Time.unscaledTime < _nextScan)
            {
                if (!_enabled.Value)
                    ResetAllForces();
                else
                    ApplyControllerForces();
                return;
            }

            _nextScan = Time.unscaledTime + _scanInterval.Value;
            var controllerColliders = _controllerCollidersEnabled.Value || _controllerForceEnabled.Value
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

            var colliders = ColliderSourceSelector.Select(
                    AddHeadCollider(controllerColliders, _headColliderEnabled.Value ? EnsureHeadCollider(character) : null),
                    characterHandColliders,
                    _includeCharacterHandColliders.Value)
                .Where(collider => collider != null)
                .ToList();

            var targets = FindHairTargets(character);
            if (targets.Count == 0)
                return;

            RegisterForceTargets(targets.Values);

            if (colliders.Count == 0)
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
                targets[pair.DynamicBoneId].Add(colliderById[pair.ColliderId]);

            if (planned.Count > 0)
                Logger.LogInfo($"Character {character.chaID}: added {planned.Count} controller/head/hand-to-hair bindings across {targets.Count} Dynamic Bones.");
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
                leftController = VRTK_DeviceFinder.GetControllerLeftHand(false) ?? VRTK_DeviceFinder.GetControllerLeftHand(true);
                rightController = VRTK_DeviceFinder.GetControllerRightHand(false) ?? VRTK_DeviceFinder.GetControllerRightHand(true);

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

            _leftControllerMotion.SetTransform(leftController == null ? null : leftController.transform);
            _rightControllerMotion.SetTransform(rightController == null ? null : rightController.transform);

            if (!createColliders)
                return result;

            AddControllerCollider(leftController, "L", ref _leftControllerCollider, result);
            AddControllerCollider(rightController, "R", ref _rightControllerCollider, result);
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
            result.Add(collider);
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

            collider.m_Center = new Vector3(0f, _headColliderCenterY.Value, 0f);
            collider.m_Radius = _headColliderRadius.Value;
            collider.m_Height = _headColliderHeight.Value;
            collider.m_Direction = DynamicBoneCollider.Direction.Y;
            collider.m_Bound = DynamicBoneCollider.Bound.Outside;
            return collider;
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

        private void RegisterForceTargets(IEnumerable<DynamicBoneTarget> targets)
        {
            var added = 0;
            foreach (var target in targets)
            {
                if (!_forceTargets.ContainsKey(target.Id))
                {
                    _forceTargets.Add(target.Id, target);
                    added++;
                }
            }

            if (added > 0)
                Logger.LogInfo($"Registered {added} hair Dynamic Bones for controller velocity force.");
        }

        private void ApplyControllerForces()
        {
            if (!_controllerForceEnabled.Value || _forceTargets.Count == 0)
            {
                ResetAllForces();
                return;
            }

            var deltaTime = Time.unscaledDeltaTime;
            _leftControllerMotion.Sample(deltaTime, _velocitySmoothing.Value);
            _rightControllerMotion.Sample(deltaTime, _velocitySmoothing.Value);

            var staleTargetIds = new List<string>();
            foreach (var entry in _forceTargets)
            {
                var target = entry.Value;
                Vector3 targetPosition;
                if (!target.TryGetPosition(out targetPosition))
                {
                    target.ResetForce();
                    if (!target.IsAlive)
                        staleTargetIds.Add(entry.Key);
                    continue;
                }

                var force = CalculateControllerForce(_leftControllerMotion, targetPosition) +
                            CalculateControllerForce(_rightControllerMotion, targetPosition);
                if (force.sqrMagnitude > _maximumForce.Value * _maximumForce.Value)
                    force = force.normalized * _maximumForce.Value;

                if (force.sqrMagnitude > 0f)
                    target.ApplyForce(force);
                else
                    target.ReleaseForce(deltaTime);
            }

            foreach (var targetId in staleTargetIds)
                _forceTargets.Remove(targetId);
        }

        private Vector3 CalculateControllerForce(ControllerMotionState controller, Vector3 targetPosition)
        {
            if (!controller.IsAvailable)
                return Vector3.zero;

            var speed = controller.Velocity.magnitude;
            var distance = Vector3.Distance(controller.Position, targetPosition);
            var magnitude = ForceFieldMath.ComputeMagnitude(
                speed,
                _forceStrength.Value,
                _maximumForce.Value,
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
            foreach (var target in _forceTargets.Values)
                target.ResetForce();
        }

        private void OnDisable() => ResetAllForces();

        private void OnDestroy() => ResetAllForces();

        private void OnLevelWasLoaded(int level) => ResetAllForces();

        private List<DynamicBoneCollider> FindHandColliders(ChaControl character)
        {
            return character.GetComponentsInChildren<DynamicBoneCollider>(true)
                .Where(collider => ExistingColliderNames.Contains(collider.gameObject.name, StringComparer.Ordinal))
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

        private static Transform FindDeepestTransform(Transform root)
        {
            if (root == null)
                return null;

            var deepest = root;
            var deepestDepth = 0;
            FindDeepestTransform(root, 0, ref deepest, ref deepestDepth);
            return deepest;
        }

        private static void FindDeepestTransform(Transform current, int depth, ref Transform deepest, ref int deepestDepth)
        {
            if (depth > deepestDepth)
            {
                deepest = current;
                deepestDepth = depth;
            }

            for (var index = 0; index < current.childCount; index++)
                FindDeepestTransform(current.GetChild(index), depth + 1, ref deepest, ref deepestDepth);
        }

        private sealed class DynamicBoneTarget
        {
            private readonly MonoBehaviour _bone;
            private readonly Func<DynamicBoneCollider, bool> _contains;
            private readonly Action<DynamicBoneCollider> _add;
            private readonly Func<Transform> _getTip;
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
                Func<Transform> getTip,
                Func<Vector3> getForce,
                Action<Vector3> setForce,
                Action resetParticles)
            {
                Id = id;
                _bone = bone;
                _contains = contains;
                _add = add;
                _getTip = getTip;
                _getForce = getForce;
                _setForce = setForce;
                _resetParticles = resetParticles;
            }

            public string Id { get; }
            public bool IsAlive => _bone != null;
            public bool Contains(DynamicBoneCollider collider) => _contains(collider);
            public void Add(DynamicBoneCollider collider) => _add(collider);

            public bool TryGetPosition(out Vector3 position)
            {
                var tip = IsAlive ? _getTip() : null;
                if (tip == null)
                {
                    position = Vector3.zero;
                    return false;
                }

                position = tip.position;
                return true;
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

            public static DynamicBoneTarget For(DynamicBone bone)
            {
                if (bone.m_Colliders == null)
                    bone.m_Colliders = new List<DynamicBoneCollider>();
                return Create(
                    bone,
                    bone.m_Colliders,
                    () => FindDeepestTransform(bone.m_Root),
                    () => bone.m_Force,
                    force => bone.m_Force = force,
                    bone.ResetParticlesPosition);
            }

            public static DynamicBoneTarget For(DynamicBone_Ver01 bone)
            {
                if (bone.m_Colliders == null)
                    bone.m_Colliders = new List<DynamicBoneCollider>();
                return Create(
                    bone,
                    bone.m_Colliders,
                    () => FindDeepestTransform(bone.m_Root),
                    () => bone.m_Force,
                    force => bone.m_Force = force,
                    bone.ResetParticlesPosition);
            }

            public static DynamicBoneTarget For(DynamicBone_Ver02 bone)
            {
                if (bone.Colliders == null)
                    bone.Colliders = new List<DynamicBoneCollider>();
                return Create(
                    bone,
                    bone.Colliders,
                    () => bone.Bones == null ? null : bone.Bones.LastOrDefault(item => item != null),
                    () => bone.Force,
                    force => bone.Force = force,
                    bone.ResetParticlesPosition);
            }

            private static DynamicBoneTarget Create(
                MonoBehaviour bone,
                IList<DynamicBoneCollider> colliders,
                Func<Transform> getTip,
                Func<Vector3> getForce,
                Action<Vector3> setForce,
                Action resetParticles)
            {
                var id = $"{bone.GetType().Name}:{bone.GetInstanceID()}";
                return new DynamicBoneTarget(id, bone, colliders.Contains, colliders.Add, getTip, getForce, setForce, resetParticles);
            }
        }

        private sealed class ControllerMotionState
        {
            private Transform _transform;
            private Vector3 _previousPosition;
            private bool _hasPreviousPosition;

            public bool IsAvailable { get; private set; }
            public Vector3 Position { get; private set; }
            public Vector3 Velocity { get; private set; }

            public void SetTransform(Transform controllerTransform)
            {
                if (_transform == controllerTransform)
                    return;

                _transform = controllerTransform;
                _hasPreviousPosition = false;
                IsAvailable = false;
                Velocity = Vector3.zero;
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
                Velocity = Vector3.Lerp(rawVelocity, Velocity, smoothing);
                _previousPosition = Position;
            }
        }
    }
}
