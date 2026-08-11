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
}
