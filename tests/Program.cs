using System;
using System.Collections.Generic;
using System.Linq;
using KKVRHandHairCollider.Core;

internal static class Program
{
    private static int _failed;

    private static int Main()
    {
        Run("plans both hands for every dynamic bone", PlansBothHandsForEveryDynamicBone);
        Run("does not plan bindings that already exist", DoesNotPlanExistingBindings);
        Run("works when only one hand collider is available", WorksWithOneHand);
        Run("deduplicates repeated inputs", DeduplicatesRepeatedInputs);
        Run("returns no work for empty inputs", ReturnsNoWorkForEmptyInputs);
        Run("uses tracked controllers without character hands by default", UsesControllersByDefault);
        Run("can include character hand colliders", CanIncludeCharacterHands);
        Run("deduplicates collider sources", DeduplicatesColliderSources);
        Run("force falls off with controller distance", ForceFallsOffWithDistance);
        Run("force ignores slow controller drift", ForceIgnoresSlowDrift);
        Run("force is capped for fast swings", ForceIsCappedForFastSwings);
        Run("force stops outside interaction radius", ForceStopsOutsideRadius);
        Run("recognizes standard and modded skirt bone names", RecognizesSkirtBoneNames);
        Run("rejects non-skirt clothing bones", RejectsNonSkirtBoneNames);
        Run("force uses the nearest sampled chain point", ForceUsesNearestChainPoint);
        Run("force rejects invalid physics inputs", ForceRejectsInvalidInputs);
        Run("trajectory simulation stays bounded and local", TrajectorySimulationStaysBoundedAndLocal);
        Run("contact sampling preserves endpoints within budget", ContactSamplingPreservesEndpointsWithinBudget);
        Run("grab starts without an initial force jump", GrabStartsWithoutInitialForceJump);
        Run("grab pull is bounded and releases past maximum stretch", GrabPullIsBoundedAndReleasesPastMaximumStretch);
        Run("grab physics rejects invalid inputs", GrabPhysicsRejectsInvalidInputs);
        Run("recognizes reusable character arm colliders only", RecognizesReusableCharacterArmCollidersOnly);
        Run("recognizes real KK_Colliders names", RecognizesRealKkColliderNames);
        Run("skirt sources exclude character arm colliders", SkirtSourcesExcludeCharacterArmColliders);
        Run("target registry removes disabled and missing targets", TargetRegistryRemovesDisabledAndMissingTargets);
        Run("Unity reference fallback treats destroyed objects as missing", UnityReferenceFallbackTreatsDestroyedObjectsAsMissing);
        Run("cloth binding sync removes stale managed pairs", ClothBindingSyncRemovesStaleManagedPairs);
        Run("owned collider enablement follows both switches", OwnedColliderEnablementFollowsBothSwitches);

        Console.WriteLine(_failed == 0 ? "ALL TESTS PASSED" : $"{_failed} TEST(S) FAILED");
        return _failed == 0 ? 0 : 1;
    }

    private static void PlansBothHandsForEveryDynamicBone()
    {
        var result = BindingPlanner.Plan(
            new[] { "bangs", "side-hair" },
            new[] { "left-hand", "right-hand" },
            Array.Empty<BindingPair>());

        AssertPairs(result,
            "bangs:left-hand",
            "bangs:right-hand",
            "side-hair:left-hand",
            "side-hair:right-hand");
    }

    private static void DoesNotPlanExistingBindings()
    {
        var result = BindingPlanner.Plan(
            new[] { "bangs" },
            new[] { "left-hand", "right-hand" },
            new[] { new BindingPair("bangs", "left-hand") });

        AssertPairs(result, "bangs:right-hand");
    }

    private static void WorksWithOneHand()
    {
        var result = BindingPlanner.Plan(
            new[] { "bangs" },
            new[] { "right-hand" },
            Array.Empty<BindingPair>());

        AssertPairs(result, "bangs:right-hand");
    }

    private static void DeduplicatesRepeatedInputs()
    {
        var result = BindingPlanner.Plan(
            new[] { "bangs", "bangs" },
            new[] { "left-hand", "left-hand" },
            Array.Empty<BindingPair>());

        AssertPairs(result, "bangs:left-hand");
    }

    private static void ReturnsNoWorkForEmptyInputs()
    {
        AssertPairs(BindingPlanner.Plan(Array.Empty<string>(), new[] { "left-hand" }, Array.Empty<BindingPair>()));
        AssertPairs(BindingPlanner.Plan(new[] { "bangs" }, Array.Empty<string>(), Array.Empty<BindingPair>()));
    }

    private static void UsesControllersByDefault()
    {
        var result = ColliderSourceSelector.Select(
            new[] { "left-controller", "right-controller" },
            new[] { "left-character-hand", "right-character-hand" },
            false);

        AssertValues(result, "left-controller", "right-controller");
    }

    private static void CanIncludeCharacterHands()
    {
        var result = ColliderSourceSelector.Select(
            new[] { "left-controller", "right-controller" },
            new[] { "left-character-hand", "right-character-hand" },
            true);

        AssertValues(result,
            "left-controller",
            "right-controller",
            "left-character-hand",
            "right-character-hand");
    }

    private static void DeduplicatesColliderSources()
    {
        var result = ColliderSourceSelector.Select(
            new[] { "left-controller", "left-controller" },
            new[] { "left-controller" },
            true);

        AssertValues(result, "left-controller");
    }

    private static void ForceFallsOffWithDistance()
    {
        AssertNear(0.04f, ForceFieldMath.ComputeMagnitude(1f, 0.04f, 0.10f, 0.035f, 0.035f, 0.008f, 0.15f));
        AssertNear(0.02f, ForceFieldMath.ComputeMagnitude(1f, 0.04f, 0.10f, 0.039f, 0.035f, 0.008f, 0.15f));
    }

    private static void ForceIgnoresSlowDrift()
    {
        AssertNear(0f, ForceFieldMath.ComputeMagnitude(0.10f, 0.04f, 0.04f, 0.035f, 0.035f, 0.008f, 0.15f));
    }

    private static void ForceIsCappedForFastSwings()
    {
        AssertNear(0.04f, ForceFieldMath.ComputeMagnitude(8f, 0.04f, 0.04f, 0.035f, 0.035f, 0.008f, 0.15f));
    }

    private static void ForceStopsOutsideRadius()
    {
        AssertNear(0f, ForceFieldMath.ComputeMagnitude(1f, 0.04f, 0.04f, 0.043f, 0.035f, 0.008f, 0.15f));
        AssertNear(0f, ForceFieldMath.ComputeMagnitude(1f, 0.04f, 0.04f, 0.080f, 0.035f, 0.008f, 0.15f));
    }

    private static void RecognizesSkirtBoneNames()
    {
        AssertTrue(SkirtTargetClassifier.IsSkirtBoneName("cf_j_sk_00_00"));
        AssertTrue(SkirtTargetClassifier.IsSkirtBoneName("cf_J_sk_07_05"));
        AssertTrue(SkirtTargetClassifier.IsSkirtBoneName("cf_d_sk_03_00"));
        AssertTrue(SkirtTargetClassifier.IsSkirtBoneName("cf_j_backsk_L_01"));
        AssertTrue(SkirtTargetClassifier.IsSkirtBoneName("cf_j_spinesk_03"));
    }

    private static void RejectsNonSkirtBoneNames()
    {
        AssertFalse(SkirtTargetClassifier.IsSkirtBoneName(null));
        AssertFalse(SkirtTargetClassifier.IsSkirtBoneName(string.Empty));
        AssertFalse(SkirtTargetClassifier.IsSkirtBoneName("cf_j_bust01_L"));
        AssertFalse(SkirtTargetClassifier.IsSkirtBoneName("cf_s_thigh01_L"));
        AssertFalse(SkirtTargetClassifier.IsSkirtBoneName("acc_sk_ribbon"));
    }

    private static void ForceUsesNearestChainPoint()
    {
        var result = ForceFieldMath.ComputeMagnitudeForSamples(
            1f,
            0.02f,
            0.03f,
            new[] { 0.30f, 0.039f, 0.20f },
            0.035f,
            0.008f,
            0.15f);

        AssertNear(0.01f, result);
        AssertNear(0f, ForceFieldMath.ComputeMagnitudeForSamples(
            1f, 0.02f, 0.03f, Array.Empty<float>(), 0.035f, 0.008f, 0.15f));
    }

    private static void ForceRejectsInvalidInputs()
    {
        AssertThrows<ArgumentOutOfRangeException>(() =>
            ForceFieldMath.ComputeMagnitude(-0.1f, 0.02f, 0.03f, 0.02f, 0.035f, 0.008f, 0.15f));
        AssertThrows<ArgumentOutOfRangeException>(() =>
            ForceFieldMath.ComputeMagnitude(1f, 0.02f, 0.03f, float.NaN, 0.035f, 0.008f, 0.15f));
        AssertThrows<ArgumentOutOfRangeException>(() =>
            ForceFieldMath.ComputeMagnitudeForSamples(
                1f, 0.02f, 0.03f, new[] { 0.02f, float.PositiveInfinity }, 0.035f, 0.008f, 0.15f));
    }

    private static void TrajectorySimulationStaysBoundedAndLocal()
    {
        const float timeStep = 1f / 90f;
        const float radius = 0.035f;
        const float padding = 0.008f;
        const float maximumForce = 0.025f;
        var contacted = 0;
        var separated = 0;

        for (var scenario = 0; scenario < 16; scenario++)
        {
            var random = new Random(20260812 + scenario * 7919);
            for (var step = 0; step < 20000; step++)
            {
                var speed = (float)(random.NextDouble() * 6.0);
                var distance = (float)(random.NextDouble() * 0.25);
                var magnitude = ForceFieldMath.ComputeMagnitude(
                    speed, 0.012f, maximumForce, distance, radius, padding, 0.15f);

                AssertTrue(magnitude >= 0f && magnitude <= maximumForce);
                AssertTrue(magnitude * timeStep <= maximumForce * timeStep);
                if (distance >= radius + padding || speed < 0.15f)
                {
                    AssertNear(0f, magnitude);
                    separated++;
                }
                else if (magnitude > 0f)
                {
                    contacted++;
                }
            }
        }

        AssertTrue(contacted > 16000);
        AssertTrue(separated > 160000);
    }

    private static void ContactSamplingPreservesEndpointsWithinBudget()
    {
        AssertValues(ContactSamplePlanner.PlanIndices(0, 24).Select(index => index.ToString()));
        AssertValues(ContactSamplePlanner.PlanIndices(4, 24).Select(index => index.ToString()), "0", "1", "2", "3");
        AssertValues(ContactSamplePlanner.PlanIndices(100, 5).Select(index => index.ToString()), "0", "25", "50", "74", "99");

        var planned = ContactSamplePlanner.PlanIndices(1000, 24);
        AssertTrue(planned.Count == 24);
        AssertTrue(planned.First() == 0);
        AssertTrue(planned.Last() == 999);
        AssertTrue(planned.Distinct().Count() == planned.Count);
        AssertThrows<ArgumentOutOfRangeException>(() => ContactSamplePlanner.PlanIndices(-1, 24));
        AssertThrows<ArgumentOutOfRangeException>(() => ContactSamplePlanner.PlanIndices(10, 0));
    }

    private static void GrabStartsWithoutInitialForceJump()
    {
        AssertTrue(GrabInteractionMath.CanLatch(0.040f, 0.043f));
        AssertFalse(GrabInteractionMath.CanLatch(0.044f, 0.043f));
        AssertNear(0f, GrabInteractionMath.ComputePullMagnitude(0f, 0.20f, 0.04f, 0.005f));
        AssertNear(0f, GrabInteractionMath.ComputePullMagnitude(0.005f, 0.20f, 0.04f, 0.005f));
    }

    private static void GrabPullIsBoundedAndReleasesPastMaximumStretch()
    {
        AssertNear(0.019f, GrabInteractionMath.ComputePullMagnitude(0.10f, 0.20f, 0.04f, 0.005f));
        AssertNear(0.04f, GrabInteractionMath.ComputePullMagnitude(0.50f, 0.20f, 0.04f, 0.005f));
        AssertFalse(GrabInteractionMath.ExceedsMaximumStretch(0.22f, 0.22f));
        AssertTrue(GrabInteractionMath.ExceedsMaximumStretch(0.221f, 0.22f));

        var maximumObserved = 0f;
        for (var index = 0; index < 100000; index++)
        {
            var displacement = index / 100000f * 0.50f;
            var pull = GrabInteractionMath.ComputePullMagnitude(displacement, 0.20f, 0.04f, 0.005f);
            maximumObserved = Math.Max(maximumObserved, pull);
            AssertTrue(pull >= 0f && pull <= 0.04f);
        }

        AssertNear(0.04f, maximumObserved);
    }

    private static void GrabPhysicsRejectsInvalidInputs()
    {
        AssertThrows<ArgumentOutOfRangeException>(() => GrabInteractionMath.CanLatch(float.NaN, 0.043f));
        AssertThrows<ArgumentOutOfRangeException>(() => GrabInteractionMath.ComputePullMagnitude(-0.01f, 0.20f, 0.04f, 0.005f));
        AssertThrows<ArgumentOutOfRangeException>(() => GrabInteractionMath.ComputePullMagnitude(0.01f, 0.20f, 0.04f, -0.02f));
        AssertThrows<ArgumentOutOfRangeException>(() => GrabInteractionMath.ExceedsMaximumStretch(0.1f, 0f));
    }

    private static void RecognizesReusableCharacterArmCollidersOnly()
    {
        AssertTrue(CharacterColliderClassifier.IsReusableArmColliderName("Colliders_cf_s_hand_L"));
        AssertTrue(CharacterColliderClassifier.IsReusableArmColliderName("Colliders_cf_s_hand_R"));
        AssertTrue(CharacterColliderClassifier.IsReusableArmColliderName("Colliders_cf_s_forearm02_L"));
        AssertTrue(CharacterColliderClassifier.IsReusableArmColliderName("Colliders_cf_s_forearm02_R"));
        AssertTrue(CharacterColliderClassifier.IsReusableArmColliderName("Colliders_cf_s_arm02_L"));
        AssertTrue(CharacterColliderClassifier.IsReusableArmColliderName("Colliders_cf_s_arm02_R"));
        AssertTrue(CharacterColliderClassifier.IsReusableArmColliderName("KKVRHandHairCollider_cf_s_hand_L"));
        AssertFalse(CharacterColliderClassifier.IsReusableArmColliderName("Colliders_cf_j_bust01_L"));
        AssertFalse(CharacterColliderClassifier.IsReusableArmColliderName("Colliders_cf_s_thigh01_L"));
        AssertFalse(CharacterColliderClassifier.IsReusableArmColliderName(null));
    }

    private static void RecognizesRealKkColliderNames()
    {
        AssertTrue(CharacterColliderClassifier.IsReusableArmColliderName("KK_Colliders_cf_s_hand_L"));
        AssertTrue(CharacterColliderClassifier.IsReusableArmColliderName("KK_Colliders_cf_s_forearm02_R"));
        AssertTrue(CharacterColliderClassifier.IsReusableArmColliderName("KK_Colliders_cf_s_arm02_L"));
        AssertFalse(CharacterColliderClassifier.IsReusableArmColliderName("KK_Colliders_cf_s_thigh01_L"));
    }

    private static void SkirtSourcesExcludeCharacterArmColliders()
    {
        var result = ColliderSourceSelector.SelectForSkirt(
            new[] { "left-controller", "right-controller" },
            new[] { "left-thigh", "right-thigh" });

        AssertValues(result, "left-controller", "right-controller", "left-thigh", "right-thigh");
    }

    private static void TargetRegistryRemovesDisabledAndMissingTargets()
    {
        AssertValues(
            TargetRegistryPlanner.PlanRemovals(
                new[] { "hair", "accessory", "skirt", "destroyed" },
                new[] { "hair" }),
            "accessory",
            "destroyed",
            "skirt");
        AssertValues(TargetRegistryPlanner.PlanRemovals(Array.Empty<string>(), new[] { "hair" }));
    }

    private static void UnityReferenceFallbackTreatsDestroyedObjectsAsMissing()
    {
        var primary = new FakeUnityReference("destroyed", false);
        var fallback = new FakeUnityReference("live", true);
        var selected = UnityReferenceSelector.FirstAvailable(primary, fallback, item => item == null || !item.IsAlive);

        AssertTrue(ReferenceEquals(fallback, selected));
        AssertTrue(UnityReferenceSelector.FirstAvailable(fallback, primary, item => item == null || !item.IsAlive) == fallback);
        AssertTrue(UnityReferenceSelector.FirstAvailable(primary, null, item => item == null || !item.IsAlive) == null);
    }

    private static void ClothBindingSyncRemovesStaleManagedPairs()
    {
        var oldManaged = new FakeUnityReference("KKVRHandHairCollider_UnityCloth_L", false);
        var liveManaged = new FakeUnityReference("KKVRHandHairCollider_UnityCloth_R", true);
        var liveForeign = new FakeUnityReference("GarmentCollider", true);
        var newManaged = new FakeUnityReference("KKVRHandHairCollider_UnityCloth_L", true);
        var existing = new[]
        {
            new FakeClothPair(oldManaged),
            new FakeClothPair(liveManaged),
            new FakeClothPair(liveForeign),
            new FakeClothPair(null)
        };

        var plan = ClothBindingPlanner.Plan(
            existing,
            new[] { newManaged, liveManaged },
            pair => pair.First,
            pair => pair.Second,
            item => item != null && item.IsAlive,
            item => item != null && item.Name.StartsWith("KKVRHandHairCollider_UnityCloth_", StringComparison.Ordinal));

        AssertValues(plan.RetainedPairs.Select(pair => pair.First.Name), "GarmentCollider", "KKVRHandHairCollider_UnityCloth_R");
        AssertValues(plan.CollidersToAdd.Select(item => item.Name), "KKVRHandHairCollider_UnityCloth_L");
    }

    private static void OwnedColliderEnablementFollowsBothSwitches()
    {
        AssertTrue(OwnedColliderState.ShouldEnable(true, true));
        AssertFalse(OwnedColliderState.ShouldEnable(false, true));
        AssertFalse(OwnedColliderState.ShouldEnable(true, false));
    }

    private sealed class FakeUnityReference
    {
        public FakeUnityReference(string name, bool isAlive)
        {
            Name = name;
            IsAlive = isAlive;
        }

        public string Name { get; }
        public bool IsAlive { get; }
    }

    private sealed class FakeClothPair
    {
        public FakeClothPair(FakeUnityReference first, FakeUnityReference second = null)
        {
            First = first;
            Second = second;
        }

        public FakeUnityReference First { get; }
        public FakeUnityReference Second { get; }
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            Console.WriteLine($"PASS: {name}");
        }
        catch (Exception exception)
        {
            _failed++;
            Console.WriteLine($"FAIL: {name}: {exception.Message}");
        }
    }

    private static void AssertPairs(IEnumerable<BindingPair> actual, params string[] expected)
    {
        var actualValues = actual.Select(pair => $"{pair.DynamicBoneId}:{pair.ColliderId}").OrderBy(value => value).ToArray();
        var expectedValues = expected.OrderBy(value => value).ToArray();
        if (!actualValues.SequenceEqual(expectedValues))
            throw new InvalidOperationException($"Expected [{string.Join(", ", expectedValues)}], got [{string.Join(", ", actualValues)}]");
    }

    private static void AssertValues(IEnumerable<string> actual, params string[] expected)
    {
        var actualValues = actual.OrderBy(value => value).ToArray();
        var expectedValues = expected.OrderBy(value => value).ToArray();
        if (!actualValues.SequenceEqual(expectedValues))
            throw new InvalidOperationException($"Expected [{string.Join(", ", expectedValues)}], got [{string.Join(", ", actualValues)}]");
    }

    private static void AssertNear(float expected, float actual)
    {
        if (Math.Abs(expected - actual) > 0.0001f)
            throw new InvalidOperationException($"Expected {expected}, got {actual}");
    }

    private static void AssertTrue(bool value)
    {
        if (!value)
            throw new InvalidOperationException("Expected true, got false");
    }

    private static void AssertFalse(bool value)
    {
        if (value)
            throw new InvalidOperationException("Expected false, got true");
    }

    private static void AssertThrows<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}");
    }
}
