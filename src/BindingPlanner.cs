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
            if (colliderRadius < 0f) throw new ArgumentOutOfRangeException(nameof(colliderRadius));
            if (falloffPadding <= 0f) throw new ArgumentOutOfRangeException(nameof(falloffPadding));
            if (strength < 0f) throw new ArgumentOutOfRangeException(nameof(strength));
            if (maximumForce < 0f) throw new ArgumentOutOfRangeException(nameof(maximumForce));
            if (minimumSpeed < 0f) throw new ArgumentOutOfRangeException(nameof(minimumSpeed));

            if (speed < minimumSpeed || distance >= colliderRadius + falloffPadding || maximumForce == 0f)
                return 0f;

            var outsideCollider = Math.Max(0f, distance - colliderRadius);
            var influence = 1f - outsideCollider / falloffPadding;
            return Math.Min(speed * strength * influence, maximumForce);
        }
    }
}
