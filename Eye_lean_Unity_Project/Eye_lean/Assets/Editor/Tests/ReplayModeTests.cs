using NUnit.Framework;
using EyeLean.Replay.SceneState;

namespace EyeLean.Tests.EditMode
{
    /// <summary>
    /// Pin the contract that researcher scripts rely on:
    /// <c>ReplayMode.IsActive</c> flips exactly once per Begin/End pair, and
    /// the <c>Changed</c> event fires only on real transitions (no double-
    /// fire from idempotent Begin or End calls).
    /// </summary>
    public class ReplayModeTests
    {
        [SetUp]
        public void SetUp()
        {
            ReplayMode.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            ReplayMode.ResetForTests();
        }

        [Test]
        public void Begin_SetsIsActiveTrue_FiresOnce()
        {
            int fired = 0;
            ReplayMode.Changed += active => { if (active) fired++; };
            ReplayMode.Begin();
            Assert.IsTrue(ReplayMode.IsActive);
            Assert.AreEqual(1, fired);
        }

        [Test]
        public void Begin_Idempotent_DoesNotDoubleFire()
        {
            int fired = 0;
            ReplayMode.Changed += active => { if (active) fired++; };
            ReplayMode.Begin();
            ReplayMode.Begin();
            ReplayMode.Begin();
            Assert.AreEqual(1, fired);
        }

        [Test]
        public void End_AfterBegin_ClearsAndFires()
        {
            int beginFires = 0, endFires = 0;
            ReplayMode.Changed += active => { if (active) beginFires++; else endFires++; };
            ReplayMode.Begin();
            ReplayMode.End();
            Assert.IsFalse(ReplayMode.IsActive);
            Assert.AreEqual(1, beginFires);
            Assert.AreEqual(1, endFires);
        }

        [Test]
        public void End_WithoutBegin_DoesNothing()
        {
            int fired = 0;
            ReplayMode.Changed += _ => fired++;
            ReplayMode.End();
            Assert.IsFalse(ReplayMode.IsActive);
            Assert.AreEqual(0, fired);
        }
    }
}
