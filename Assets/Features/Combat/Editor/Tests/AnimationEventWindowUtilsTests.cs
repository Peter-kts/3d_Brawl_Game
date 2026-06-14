using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Edit Mode tests for AnimationEventWindowUtils.
/// Run via Window > General > Test Runner > EditMode.
/// </summary>
[TestFixture]
public class AnimationEventWindowUtilsTests
{
    // -----------------------------------------------------------------------
    // IsKnownIntParamFunction
    // -----------------------------------------------------------------------

    [TestCase("BeginHitbox")]
    [TestCase("EndHitbox")]
    [TestCase("OnBeginHitbox")]
    [TestCase("OnEndHitbox")]
    [TestCase("OnAttackSfxEvent")]
    [TestCase("OnAttackSFXEvent")]
    [TestCase("OnThrowSfxEvent")]
    [TestCase("SetGrip")]
    public void IsKnownIntParamFunction_KnownFunctions_ReturnsTrue(string functionName)
    {
        Assert.IsTrue(AnimationEventWindowUtils.IsKnownIntParamFunction(functionName));
    }

    [TestCase("SomeRandomMethod")]
    [TestCase("OnThrowRelease")]
    [TestCase("OnThrowDamage")]
    [TestCase("")]
    [TestCase(null)]
    public void IsKnownIntParamFunction_UnknownOrEmpty_ReturnsFalse(string functionName)
    {
        Assert.IsFalse(AnimationEventWindowUtils.IsKnownIntParamFunction(functionName));
    }

    // -----------------------------------------------------------------------
    // GuessParameterKind
    // -----------------------------------------------------------------------

    [Test]
    public void GuessParameterKind_NullEvent_ReturnsNone()
    {
        Assert.AreEqual(AnimationEventParameterKind.None,
            AnimationEventWindowUtils.GuessParameterKind(null));
    }

    [Test]
    public void GuessParameterKind_AllDefaults_ReturnsNone()
    {
        var ev = new AnimationEvent();
        Assert.AreEqual(AnimationEventParameterKind.None,
            AnimationEventWindowUtils.GuessParameterKind(ev));
    }

    [Test]
    public void GuessParameterKind_NonZeroInt_ReturnsInt()
    {
        var ev = new AnimationEvent { intParameter = 2 };
        Assert.AreEqual(AnimationEventParameterKind.Int,
            AnimationEventWindowUtils.GuessParameterKind(ev));
    }

    [Test]
    public void GuessParameterKind_ZeroInt_ReturnsNone_SoCallerCanApplyKnownFunctionFallback()
    {
        // int == 0 is indistinguishable from "no int set"; GuessParameterKind returns None
        // and the caller must use IsKnownIntParamFunction to pick Int for known combat functions.
        var ev = new AnimationEvent { intParameter = 0 };
        Assert.AreEqual(AnimationEventParameterKind.None,
            AnimationEventWindowUtils.GuessParameterKind(ev));
    }

    [Test]
    public void GuessParameterKind_NonZeroFloat_ReturnsFloat()
    {
        var ev = new AnimationEvent { floatParameter = 1.5f };
        Assert.AreEqual(AnimationEventParameterKind.Float,
            AnimationEventWindowUtils.GuessParameterKind(ev));
    }

    [Test]
    public void GuessParameterKind_NonEmptyString_ReturnsString()
    {
        var ev = new AnimationEvent { stringParameter = "hit" };
        Assert.AreEqual(AnimationEventParameterKind.String,
            AnimationEventWindowUtils.GuessParameterKind(ev));
    }

    // -----------------------------------------------------------------------
    // FindImportedClipIndex
    // -----------------------------------------------------------------------

    [Test]
    public void FindImportedClipIndex_NullArray_ReturnsNegativeOne()
    {
        Assert.AreEqual(-1, AnimationEventWindowUtils.FindImportedClipIndex(null, "Attack"));
    }

    [Test]
    public void FindImportedClipIndex_EmptyArray_ReturnsNegativeOne()
    {
        Assert.AreEqual(-1,
            AnimationEventWindowUtils.FindImportedClipIndex(
                new ModelImporterClipAnimation[0], "Attack"));
    }

    [Test]
    public void FindImportedClipIndex_ExactMatch_ReturnsCorrectIndex()
    {
        var clips = new[]
        {
            new ModelImporterClipAnimation { name = "Idle" },
            new ModelImporterClipAnimation { name = "Attack" },
            new ModelImporterClipAnimation { name = "Walk" },
        };
        Assert.AreEqual(1, AnimationEventWindowUtils.FindImportedClipIndex(clips, "Attack"));
    }

    [Test]
    public void FindImportedClipIndex_CaseInsensitiveMatch_ReturnsIndex()
    {
        var clips = new[]
        {
            new ModelImporterClipAnimation { name = "Idle" },
            new ModelImporterClipAnimation { name = "ATTACK" },
        };
        Assert.AreEqual(1, AnimationEventWindowUtils.FindImportedClipIndex(clips, "attack"));
    }

    [Test]
    public void FindImportedClipIndex_ExactBeforeCaseInsensitive_PrefersExact()
    {
        var clips = new[]
        {
            new ModelImporterClipAnimation { name = "attack" },
            new ModelImporterClipAnimation { name = "Attack" },
        };
        // Exact match is "Attack" at index 1.
        Assert.AreEqual(1, AnimationEventWindowUtils.FindImportedClipIndex(clips, "Attack"));
    }

    [Test]
    public void FindImportedClipIndex_NoMatch_SingleClip_ReturnsFallbackZero()
    {
        var clips = new[]
        {
            new ModelImporterClipAnimation { name = "Take 001" },
        };
        // Single-clip fallback: the clip may have a different take name in the importer.
        Assert.AreEqual(0, AnimationEventWindowUtils.FindImportedClipIndex(clips, "Attack"));
    }

    [Test]
    public void FindImportedClipIndex_NoMatch_MultipleClips_ReturnsNegativeOne()
    {
        var clips = new[]
        {
            new ModelImporterClipAnimation { name = "Idle" },
            new ModelImporterClipAnimation { name = "Walk" },
        };
        Assert.AreEqual(-1, AnimationEventWindowUtils.FindImportedClipIndex(clips, "Attack"));
    }

    // -----------------------------------------------------------------------
    // NormalizedFromTimeline
    // -----------------------------------------------------------------------

    [Test]
    public void NormalizedFromTimeline_LeftEdge_ReturnsZero()
    {
        var rect = new Rect(10f, 0f, 200f, 40f);
        Assert.AreEqual(0f, AnimationEventWindowUtils.NormalizedFromTimeline(rect, 10f), 0.0001f);
    }

    [Test]
    public void NormalizedFromTimeline_RightEdge_ReturnsOne()
    {
        var rect = new Rect(10f, 0f, 200f, 40f);
        Assert.AreEqual(1f, AnimationEventWindowUtils.NormalizedFromTimeline(rect, 210f), 0.0001f);
    }

    [Test]
    public void NormalizedFromTimeline_Midpoint_ReturnsHalf()
    {
        var rect = new Rect(10f, 0f, 200f, 40f);
        Assert.AreEqual(0.5f, AnimationEventWindowUtils.NormalizedFromTimeline(rect, 110f), 0.0001f);
    }

    [Test]
    public void NormalizedFromTimeline_BeyondRight_ClampedToOne()
    {
        var rect = new Rect(10f, 0f, 200f, 40f);
        Assert.AreEqual(1f, AnimationEventWindowUtils.NormalizedFromTimeline(rect, 500f), 0.0001f);
    }

    [Test]
    public void NormalizedFromTimeline_BeforeLeft_ClampedToZero()
    {
        var rect = new Rect(10f, 0f, 200f, 40f);
        Assert.AreEqual(0f, AnimationEventWindowUtils.NormalizedFromTimeline(rect, -100f), 0.0001f);
    }

    [Test]
    public void NormalizedFromTimeline_ZeroWidthRect_DoesNotDivideByZero()
    {
        var rect = new Rect(10f, 0f, 0f, 40f);
        // Max(1f, width) prevents divide-by-zero; result should be clamped.
        float result = AnimationEventWindowUtils.NormalizedFromTimeline(rect, 10f);
        Assert.IsTrue(result >= 0f && result <= 1f);
    }
}
