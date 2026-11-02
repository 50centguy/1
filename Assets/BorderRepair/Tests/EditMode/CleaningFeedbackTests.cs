using System.Linq;
using BorderRepair.Cleaning;
using BorderRepair.Narrative;
using NUnit.Framework;
using UnityEngine;

namespace BorderRepair.Tests
{
    public sealed class CleaningFeedbackTests
    {
        [Test]
        public void WipeLoopJoinsWithoutAClick()
        {
            Assert.IsTrue(RepairSoundSynth.Loops(RepairSoundSynth.Cue.CleanWipe));
            Assert.IsFalse(RepairSoundSynth.Loops(RepairSoundSynth.Cue.Deny));
            var clip = RepairSoundSynth.Create(RepairSoundSynth.Cue.CleanWipe);
            var data = new float[clip.samples];
            clip.GetData(data, 0);
            float largestStep = 0;
            for (int i = 1; i < data.Length; i++) largestStep = Mathf.Max(largestStep, Mathf.Abs(data[i] - data[i - 1]));
            // The seam is no bigger a jump than the noise itself makes anywhere else.
            Assert.LessOrEqual(Mathf.Abs(data[0] - data[data.Length - 1]), largestStep);
            Object.DestroyImmediate(clip);
        }

        [Test]
        public void CleaningCuesAreDistinctClips()
        {
            var cues = new[] { RepairSoundSynth.Cue.CleanWipe, RepairSoundSynth.Cue.CleanScrape, RepairSoundSynth.Cue.CleanDust, RepairSoundSynth.Cue.CleanDone };
            var clips = cues.Select(RepairSoundSynth.Create).ToArray();
            for (int a = 0; a < clips.Length; a++)
                for (int b = a + 1; b < clips.Length; b++)
                    Assert.AreNotEqual(clips[a].samples, clips[b].samples, cues[a] + " / " + cues[b]);
            foreach (var clip in clips) Object.DestroyImmediate(clip);
        }

        [Test]
        public void RemainingDirtDropsWhereTheBrushPasses()
        {
            var data = new DirtMaskData(32, Enumerable.Repeat((byte)255, 32 * 32).ToArray());
            var centre = new Vector2(.5f, .5f);
            float before = data.RemainingAt(centre);
            Assert.Greater(before, 0);
            data.Paint(centre, .2f, 1);
            Assert.AreEqual(0, data.RemainingAt(centre));
            Assert.AreEqual(0, data.RemainingAt(new Vector2(float.NaN, .5f)));
        }
    }
}
