using System;
using System.Collections.Generic;

namespace KKVRHandHairCollider.Core
{
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
            "KKVRHandHairCollider_cf_s_hand_L",
            "KKVRHandHairCollider_cf_s_hand_R"
        };

        public static bool IsReusableArmColliderName(string colliderName)
        {
            return colliderName != null && ReusableArmColliderNames.Contains(colliderName);
        }
    }
}
