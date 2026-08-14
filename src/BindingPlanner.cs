using System;
using System.Collections.Generic;
using System.Linq;

namespace KKVRHandHairCollider.Core
{
    public enum InteractionTargetKind
    {
        Hair,
        Accessory,
        Skirt
    }

    public struct InteractionTuning
    {
        public InteractionTuning(
            float velocityStrength,
            float maximumForce,
            float contactPushStrength,
            float contactPadding)
        {
            ValidateNonNegativeFinite(velocityStrength, nameof(velocityStrength));
            ValidateNonNegativeFinite(maximumForce, nameof(maximumForce));
            ValidateNonNegativeFinite(contactPushStrength, nameof(contactPushStrength));
            if (float.IsNaN(contactPadding) || float.IsInfinity(contactPadding) || contactPadding <= 0f)
                throw new ArgumentOutOfRangeException(nameof(contactPadding));

            VelocityStrength = velocityStrength;
            MaximumForce = maximumForce;
            ContactPushStrength = contactPushStrength;
            ContactPadding = contactPadding;
        }

        public float VelocityStrength { get; }
        public float MaximumForce { get; }
        public float ContactPushStrength { get; }
        public float ContactPadding { get; }

        public InteractionTuning ScaleForAccessory(float scale)
        {
            if (float.IsNaN(scale) || float.IsInfinity(scale) || scale < 0f || scale > 1f)
                throw new ArgumentOutOfRangeException(nameof(scale));
            return new InteractionTuning(
                VelocityStrength * scale,
                MaximumForce * scale,
                ContactPushStrength * scale,
                ContactPadding);
        }

        private static void ValidateNonNegativeFinite(float value, string parameterName)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
                throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    public static class InteractionProfilePlanner
    {
        private const float MinimumAccessoryScale = 0.65f;
        private const float FullResponseSpan = 0.30f;

        public static InteractionTuning Plan(
            InteractionTargetKind kind,
            float chainSpan,
            InteractionTuning hair,
            InteractionTuning accessory,
            InteractionTuning skirt)
        {
            if (float.IsNaN(chainSpan) || float.IsInfinity(chainSpan) || chainSpan < 0f)
                throw new ArgumentOutOfRangeException(nameof(chainSpan));

            switch (kind)
            {
                case InteractionTargetKind.Hair:
                    return hair;
                case InteractionTargetKind.Accessory:
                    var normalizedSpan = Math.Min(chainSpan / FullResponseSpan, 1f);
                    return accessory.ScaleForAccessory(
                        MinimumAccessoryScale + (1f - MinimumAccessoryScale) * normalizedSpan);
                case InteractionTargetKind.Skirt:
                    return skirt;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }
    }

    public static class InteractionRootSelector
    {
        public static IList<T> Select<T>(
            IEnumerable<T> primaryRoots,
            IEnumerable<T> accessoryRoots,
            bool includeAccessories)
            where T : class
        {
            if (primaryRoots == null) throw new ArgumentNullException(nameof(primaryRoots));
            if (accessoryRoots == null) throw new ArgumentNullException(nameof(accessoryRoots));

            var result = new List<T>();
            var known = new HashSet<T>();
            AddUnique(primaryRoots, known, result);
            if (includeAccessories)
                AddUnique(accessoryRoots, known, result);
            return result;
        }

        private static void AddUnique<T>(IEnumerable<T> source, HashSet<T> known, ICollection<T> result)
            where T : class
        {
            foreach (var item in source)
            {
                if (item != null && known.Add(item))
                    result.Add(item);
            }
        }
    }

    public static class AccessoryTargetClassifier
    {
        private static readonly HashSet<string> NativeBodyPhysicsComments =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "右胸",
                "左胸",
                "right breast",
                "left breast",
                "right butt",
                "left butt"
            };

        public static bool IsNativeBodyPhysics(
            string componentName,
            string comment,
            IEnumerable<string> boneNames)
        {
            if (NativeBodyPhysicsComments.Contains(comment ?? string.Empty))
                return true;
            if (IsNativeBodyPhysicsBoneName(componentName))
                return true;
            if (boneNames == null)
                return false;
            return boneNames.Any(IsNativeBodyPhysicsBoneName);
        }

        public static bool ShouldInclude(
            bool componentEnabled,
            bool hasPhysicsRoot,
            bool isNativeBodyPhysics)
        {
            return componentEnabled && hasPhysicsRoot && !isNativeBodyPhysics;
        }

        public static bool IsNativeBodyPhysicsBoneName(string boneName)
        {
            if (string.IsNullOrEmpty(boneName))
                return false;

            return boneName.StartsWith("cf_j_bust", StringComparison.OrdinalIgnoreCase) ||
                   boneName.StartsWith("cf_d_bust", StringComparison.OrdinalIgnoreCase) ||
                   boneName.StartsWith("cf_s_bust", StringComparison.OrdinalIgnoreCase) ||
                   boneName.StartsWith("cf_j_siri", StringComparison.OrdinalIgnoreCase) ||
                   boneName.StartsWith("cf_d_siri", StringComparison.OrdinalIgnoreCase) ||
                   boneName.StartsWith("cf_s_siri", StringComparison.OrdinalIgnoreCase) ||
                   boneName.IndexOf("siridam", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   boneName.StartsWith("cf_j_hip", StringComparison.OrdinalIgnoreCase) ||
                   boneName.StartsWith("cf_d_hip", StringComparison.OrdinalIgnoreCase) ||
                   boneName.StartsWith("cf_j_kokan", StringComparison.OrdinalIgnoreCase) ||
                   boneName.StartsWith("cf_d_kokan", StringComparison.OrdinalIgnoreCase) ||
                   boneName.StartsWith("cf_j_ana", StringComparison.OrdinalIgnoreCase) ||
                   boneName.StartsWith("cf_d_ana", StringComparison.OrdinalIgnoreCase);
        }
    }

    public sealed class BindingPair : IEquatable<BindingPair>
    {
        public BindingPair(string dynamicBoneId, string colliderId)
        {
            DynamicBoneId = dynamicBoneId ?? throw new ArgumentNullException(nameof(dynamicBoneId));
            ColliderId = colliderId ?? throw new ArgumentNullException(nameof(colliderId));
        }

        public string DynamicBoneId { get; }
        public string ColliderId { get; }

        public bool Equals(BindingPair other)
        {
            return other != null &&
                   StringComparer.Ordinal.Equals(DynamicBoneId, other.DynamicBoneId) &&
                   StringComparer.Ordinal.Equals(ColliderId, other.ColliderId);
        }

        public override bool Equals(object obj) => Equals(obj as BindingPair);

        public override int GetHashCode()
        {
            unchecked
            {
                return (StringComparer.Ordinal.GetHashCode(DynamicBoneId) * 397) ^
                       StringComparer.Ordinal.GetHashCode(ColliderId);
            }
        }
    }

    public static class BindingPlanner
    {
        public static IList<BindingPair> Plan(
            IEnumerable<string> dynamicBoneIds,
            IEnumerable<string> colliderIds,
            IEnumerable<BindingPair> existingBindings)
        {
            if (dynamicBoneIds == null) throw new ArgumentNullException(nameof(dynamicBoneIds));
            if (colliderIds == null) throw new ArgumentNullException(nameof(colliderIds));
            if (existingBindings == null) throw new ArgumentNullException(nameof(existingBindings));

            var targets = new HashSet<string>(dynamicBoneIds, StringComparer.Ordinal);
            var colliders = new HashSet<string>(colliderIds, StringComparer.Ordinal);
            var known = new HashSet<BindingPair>(existingBindings);
            var result = new List<BindingPair>();

            foreach (var target in targets)
            {
                foreach (var collider in colliders)
                {
                    var pair = new BindingPair(target, collider);
                    if (known.Add(pair))
                        result.Add(pair);
                }
            }

            return result;
        }
    }

    public static class ColliderSourceSelector
    {
        public static IList<T> Select<T>(
            IEnumerable<T> controllerColliders,
            IEnumerable<T> characterHandColliders,
            bool includeCharacterHands)
        {
            if (controllerColliders == null) throw new ArgumentNullException(nameof(controllerColliders));
            if (characterHandColliders == null) throw new ArgumentNullException(nameof(characterHandColliders));

            var result = new List<T>();
            var known = new HashSet<T>();
            AddUnique(controllerColliders, known, result);
            if (includeCharacterHands)
                AddUnique(characterHandColliders, known, result);
            return result;
        }

        public static IList<T> SelectForSkirt<T>(
            IEnumerable<T> controllerColliders,
            IEnumerable<T> bodyColliders)
        {
            if (controllerColliders == null) throw new ArgumentNullException(nameof(controllerColliders));
            if (bodyColliders == null) throw new ArgumentNullException(nameof(bodyColliders));

            var result = new List<T>();
            var known = new HashSet<T>();
            AddUnique(controllerColliders, known, result);
            AddUnique(bodyColliders, known, result);
            return result;
        }

        private static void AddUnique<T>(IEnumerable<T> source, HashSet<T> known, ICollection<T> result)
        {
            foreach (var item in source)
            {
                if (known.Add(item))
                    result.Add(item);
            }
        }
    }

    public static class ForceFieldMath
    {
        public static float ComputeMagnitude(
            float speed,
            float strength,
            float maximumForce,
            float distance,
            float colliderRadius,
            float falloffPadding,
            float minimumSpeed)
        {
            ValidateNonNegativeFinite(speed, nameof(speed));
            ValidateNonNegativeFinite(strength, nameof(strength));
            ValidateNonNegativeFinite(maximumForce, nameof(maximumForce));
            ValidateNonNegativeFinite(distance, nameof(distance));
            ValidateNonNegativeFinite(colliderRadius, nameof(colliderRadius));
            ValidateNonNegativeFinite(minimumSpeed, nameof(minimumSpeed));
            if (float.IsNaN(falloffPadding) || float.IsInfinity(falloffPadding) || falloffPadding <= 0f)
                throw new ArgumentOutOfRangeException(nameof(falloffPadding));

            if (speed < minimumSpeed || distance >= colliderRadius + falloffPadding || maximumForce == 0f)
                return 0f;

            var outsideCollider = Math.Max(0f, distance - colliderRadius);
            var influence = 1f - outsideCollider / falloffPadding;
            return Math.Min(speed * strength * influence, maximumForce);
        }

        public static float ComputeMagnitudeForSamples(
            float speed,
            float strength,
            float maximumForce,
            IEnumerable<float> distances,
            float colliderRadius,
            float falloffPadding,
            float minimumSpeed)
        {
            if (distances == null) throw new ArgumentNullException(nameof(distances));

            var minimumDistance = float.MaxValue;
            var hasSamples = false;
            foreach (var distance in distances)
            {
                ValidateNonNegativeFinite(distance, nameof(distances));
                minimumDistance = Math.Min(minimumDistance, distance);
                hasSamples = true;
            }

            if (!hasSamples)
                return 0f;

            return ComputeMagnitude(
                speed,
                strength,
                maximumForce,
                minimumDistance,
                colliderRadius,
                falloffPadding,
                minimumSpeed);
        }

        private static void ValidateNonNegativeFinite(float value, string parameterName)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
                throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    public static class SkirtTargetClassifier
    {
        public static bool IsSkirtComponentName(string componentName)
        {
            return string.Equals(componentName, "ct_clothesBot", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsSkirtBoneName(string boneName)
        {
            if (string.IsNullOrEmpty(boneName))
                return false;

            return boneName.StartsWith("cf_j_sk_", StringComparison.OrdinalIgnoreCase) ||
                   boneName.StartsWith("cf_d_sk_", StringComparison.OrdinalIgnoreCase) ||
                   boneName.IndexOf("_backsk_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   boneName.IndexOf("_spinesk_", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    public static class ClothingTargetClassifier
    {
        public static bool ShouldInclude(
            bool componentEnabled,
            bool hasPhysicsRoot,
            string componentName,
            string comment,
            IEnumerable<string> boneNames)
        {
            return componentEnabled &&
                   hasPhysicsRoot &&
                   !AccessoryTargetClassifier.IsNativeBodyPhysics(componentName, comment, boneNames);
        }
    }

    public static class ContactPushMath
    {
        public static float ComputeMagnitude(
            float distance,
            float colliderRadius,
            float falloffPadding,
            float strength,
            float maximumForce)
        {
            ValidateNonNegativeFinite(distance, nameof(distance));
            ValidateNonNegativeFinite(colliderRadius, nameof(colliderRadius));
            ValidateNonNegativeFinite(strength, nameof(strength));
            ValidateNonNegativeFinite(maximumForce, nameof(maximumForce));
            if (float.IsNaN(falloffPadding) || float.IsInfinity(falloffPadding) || falloffPadding <= 0f)
                throw new ArgumentOutOfRangeException(nameof(falloffPadding));

            if (distance >= colliderRadius + falloffPadding || strength == 0f || maximumForce == 0f)
                return 0f;

            var outsideCollider = Math.Max(0f, distance - colliderRadius);
            var influence = 1f - outsideCollider / falloffPadding;
            return Math.Min(strength * influence, maximumForce);
        }

        private static void ValidateNonNegativeFinite(float value, string parameterName)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
                throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    public struct ContactVector3
    {
        public ContactVector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public float X { get; }
        public float Y { get; }
        public float Z { get; }
    }

    public struct SegmentProjection
    {
        public SegmentProjection(ContactVector3 point, float parameter, float squaredDistance)
        {
            Point = point;
            Parameter = parameter;
            SquaredDistance = squaredDistance;
        }

        public ContactVector3 Point { get; }
        public float Parameter { get; }
        public float SquaredDistance { get; }
    }

    public static class ContactSegmentMath
    {
        public static bool TryProject(
            ContactVector3 point,
            ContactVector3 start,
            ContactVector3 end,
            out SegmentProjection projection)
        {
            projection = default(SegmentProjection);
            if (!IsFinite(point) || !IsFinite(start) || !IsFinite(end))
                return false;

            var segmentX = end.X - start.X;
            var segmentY = end.Y - start.Y;
            var segmentZ = end.Z - start.Z;
            var segmentLengthSquared =
                segmentX * segmentX + segmentY * segmentY + segmentZ * segmentZ;
            var parameter = 0f;
            if (segmentLengthSquared > 0.000000000001f)
            {
                parameter = ((point.X - start.X) * segmentX +
                             (point.Y - start.Y) * segmentY +
                             (point.Z - start.Z) * segmentZ) /
                            segmentLengthSquared;
                parameter = Math.Max(0f, Math.Min(1f, parameter));
            }

            var closest = new ContactVector3(
                start.X + segmentX * parameter,
                start.Y + segmentY * parameter,
                start.Z + segmentZ * parameter);
            var offsetX = point.X - closest.X;
            var offsetY = point.Y - closest.Y;
            var offsetZ = point.Z - closest.Z;
            var squaredDistance = offsetX * offsetX + offsetY * offsetY + offsetZ * offsetZ;
            if (float.IsNaN(squaredDistance) || float.IsInfinity(squaredDistance))
                return false;

            projection = new SegmentProjection(closest, parameter, squaredDistance);
            return true;
        }

        private static bool IsFinite(ContactVector3 value)
        {
            return !float.IsNaN(value.X) && !float.IsInfinity(value.X) &&
                   !float.IsNaN(value.Y) && !float.IsInfinity(value.Y) &&
                   !float.IsNaN(value.Z) && !float.IsInfinity(value.Z);
        }
    }

    public static class ContactSamplePlanner
    {
        public static IList<int> PlanIndices(int sampleCount, int maximumSamples)
        {
            if (sampleCount < 0) throw new ArgumentOutOfRangeException(nameof(sampleCount));
            if (maximumSamples <= 0) throw new ArgumentOutOfRangeException(nameof(maximumSamples));

            var result = new List<int>(Math.Min(sampleCount, maximumSamples));
            if (sampleCount == 0)
                return result;
            if (sampleCount <= maximumSamples)
            {
                for (var index = 0; index < sampleCount; index++)
                    result.Add(index);
                return result;
            }
            if (maximumSamples == 1)
            {
                result.Add(sampleCount - 1);
                return result;
            }

            for (var index = 0; index < maximumSamples; index++)
            {
                var sourceIndex = (int)Math.Round(
                    index * (sampleCount - 1d) / (maximumSamples - 1d));
                if (result.Count == 0 || result[result.Count - 1] != sourceIndex)
                    result.Add(sourceIndex);
            }

            return result;
        }
    }

    public static class GrabInteractionMath
    {
        public static bool CanLatch(float distance, float maximumDistance)
        {
            ValidateNonNegativeFinite(distance, nameof(distance));
            ValidatePositiveFinite(maximumDistance, nameof(maximumDistance));
            return distance <= maximumDistance;
        }

        public static float ComputePullMagnitude(
            float displacement,
            float strength,
            float maximumForce,
            float deadZone)
        {
            ValidateNonNegativeFinite(displacement, nameof(displacement));
            ValidateNonNegativeFinite(strength, nameof(strength));
            ValidateNonNegativeFinite(maximumForce, nameof(maximumForce));
            ValidateNonNegativeFinite(deadZone, nameof(deadZone));

            if (displacement <= deadZone || strength == 0f || maximumForce == 0f)
                return 0f;

            return Math.Min((displacement - deadZone) * strength, maximumForce);
        }

        public static bool ExceedsMaximumStretch(float displacement, float maximumStretch)
        {
            ValidateNonNegativeFinite(displacement, nameof(displacement));
            ValidatePositiveFinite(maximumStretch, nameof(maximumStretch));
            return displacement > maximumStretch;
        }

        private static void ValidateNonNegativeFinite(float value, string parameterName)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
                throw new ArgumentOutOfRangeException(parameterName);
        }

        private static void ValidatePositiveFinite(float value, string parameterName)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f)
                throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    public static class CharacterColliderClassifier
    {
        private static readonly HashSet<string> ReusableArmColliderNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Colliders_cf_s_hand_L",
            "Colliders_cf_s_hand_R",
            "Colliders_cf_s_forearm02_L",
            "Colliders_cf_s_forearm02_R",
            "Colliders_cf_s_arm02_L",
            "Colliders_cf_s_arm02_R",
            "KK_Colliders_cf_s_hand_L",
            "KK_Colliders_cf_s_hand_R",
            "KK_Colliders_cf_s_forearm02_L",
            "KK_Colliders_cf_s_forearm02_R",
            "KK_Colliders_cf_s_arm02_L",
            "KK_Colliders_cf_s_arm02_R",
            "KKVRHandHairCollider_cf_s_hand_L",
            "KKVRHandHairCollider_cf_s_hand_R"
        };

        public static bool IsReusableArmColliderName(string colliderName)
        {
            return colliderName != null && ReusableArmColliderNames.Contains(colliderName);
        }

        public static bool IsPluginFallbackHandColliderName(string colliderName)
        {
            return StringComparer.Ordinal.Equals(colliderName, "KKVRHandHairCollider_cf_s_hand_L") ||
                   StringComparer.Ordinal.Equals(colliderName, "KKVRHandHairCollider_cf_s_hand_R");
        }
    }

    public static class TargetRegistryPlanner
    {
        public static IList<string> PlanRemovals(
            IEnumerable<string> registeredTargetIds,
            IEnumerable<string> desiredTargetIds)
        {
            if (registeredTargetIds == null) throw new ArgumentNullException(nameof(registeredTargetIds));
            if (desiredTargetIds == null) throw new ArgumentNullException(nameof(desiredTargetIds));

            var desired = new HashSet<string>(desiredTargetIds, StringComparer.Ordinal);
            return registeredTargetIds
                .Where(targetId => !desired.Contains(targetId))
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }
    }

    public static class UnityReferenceSelector
    {
        public static T FirstAvailable<T>(T primary, Func<T> getFallback, Func<T, bool> isMissing) where T : class
        {
            if (getFallback == null) throw new ArgumentNullException(nameof(getFallback));
            if (isMissing == null) throw new ArgumentNullException(nameof(isMissing));
            if (!isMissing(primary))
                return primary;

            var fallback = getFallback();
            return isMissing(fallback) ? null : fallback;
        }
    }

    public sealed class ClothBindingPlan<TPair, TCollider>
    {
        public ClothBindingPlan(IList<TPair> retainedPairs, IList<TCollider> collidersToAdd)
        {
            RetainedPairs = retainedPairs ?? throw new ArgumentNullException(nameof(retainedPairs));
            CollidersToAdd = collidersToAdd ?? throw new ArgumentNullException(nameof(collidersToAdd));
        }

        public IList<TPair> RetainedPairs { get; }
        public IList<TCollider> CollidersToAdd { get; }
    }

    public static class ClothBindingPlanner
    {
        public static ClothBindingPlan<TPair, TCollider> Plan<TPair, TCollider>(
            IEnumerable<TPair> existingPairs,
            IEnumerable<TCollider> desiredManagedColliders,
            Func<TPair, TCollider> getFirst,
            Func<TPair, TCollider> getSecond,
            Func<TCollider, bool> isAlive,
            Func<TCollider, bool> isEmpty,
            Func<TCollider, bool> isManaged)
            where TCollider : class
        {
            if (existingPairs == null) throw new ArgumentNullException(nameof(existingPairs));
            if (desiredManagedColliders == null) throw new ArgumentNullException(nameof(desiredManagedColliders));
            if (getFirst == null) throw new ArgumentNullException(nameof(getFirst));
            if (getSecond == null) throw new ArgumentNullException(nameof(getSecond));
            if (isAlive == null) throw new ArgumentNullException(nameof(isAlive));
            if (isEmpty == null) throw new ArgumentNullException(nameof(isEmpty));
            if (isManaged == null) throw new ArgumentNullException(nameof(isManaged));

            var desired = new HashSet<TCollider>(desiredManagedColliders.Where(isAlive));
            var present = new HashSet<TCollider>();
            var retained = new List<TPair>();

            foreach (var pair in existingPairs)
            {
                var first = getFirst(pair);
                var second = getSecond(pair);
                var firstAlive = isAlive(first);
                var secondAlive = isAlive(second);
                if (!firstAlive || (!isEmpty(second) && !secondAlive))
                    continue;

                var firstManaged = firstAlive && isManaged(first);
                var secondManaged = secondAlive && isManaged(second);
                if ((firstManaged && !desired.Contains(first)) ||
                    (secondManaged && !desired.Contains(second)))
                    continue;

                retained.Add(pair);
                if (firstManaged)
                    present.Add(first);
                if (secondManaged)
                    present.Add(second);
            }

            return new ClothBindingPlan<TPair, TCollider>(
                retained,
                desired.Where(collider => !present.Contains(collider)).ToList());
        }
    }

    public static class OwnedColliderState
    {
        public static bool ShouldEnable(bool pluginEnabled, bool featureEnabled)
        {
            return pluginEnabled && featureEnabled;
        }
    }
}
