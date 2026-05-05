using NUnit.Framework;
using UnityEngine;
using EyeLean.SceneState;

namespace EyeLean.Tests.EditMode
{
    /// <summary>
    /// EditMode coverage for <see cref="Recordable"/> identity guarantees:
    /// auto-mint at OnValidate, regenerate-on-collision via the registry,
    /// and Disable removes from the registry.
    /// </summary>
    public class RecordableTests
    {
        private GameObject _go1;
        private GameObject _go2;

        [SetUp]
        public void SetUp()
        {
            RecordableRegistry.ResetForTests();
            _go1 = new GameObject("RecTest1");
            _go2 = new GameObject("RecTest2");
        }

        [TearDown]
        public void TearDown()
        {
            if (_go1 != null) Object.DestroyImmediate(_go1);
            if (_go2 != null) Object.DestroyImmediate(_go2);
            RecordableRegistry.ResetForTests();
        }

        [Test]
        public void Recordable_AddComponent_AutoMintsUniqueId()
        {
            var rec = _go1.AddComponent<Recordable>();
            Assert.IsFalse(string.IsNullOrEmpty(rec.UniqueId), "AddComponent should auto-mint a UniqueId on first OnEnable.");
            Assert.AreEqual(32, rec.UniqueId.Length, "GUID-N format is 32 hex characters.");
        }

        [Test]
        public void Recordable_RegenerateId_ProducesDifferentValue()
        {
            var rec = _go1.AddComponent<Recordable>();
            string before = rec.UniqueId;
            rec.RegenerateId();
            Assert.AreNotEqual(before, rec.UniqueId);
        }

        [Test]
        public void Registry_DuplicateId_RegeneratesNewcomer()
        {
            // EditMode caveat: MonoBehaviour OnEnable does NOT fire on
            // AddComponent in Edit mode (no [ExecuteAlways] on Recordable —
            // we don't want it polluting the registry during normal editor
            // workflow). Drive the registry manually to exercise the
            // production collision-handling path.
            var rec1 = _go1.AddComponent<Recordable>();
            string firstId = rec1.UniqueId;
            RecordableRegistry.Register(rec1);

            var rec2 = _go2.AddComponent<Recordable>();
            var idField = typeof(Recordable).GetField("uniqueId", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.NotNull(idField, "uniqueId field should exist on Recordable.");
            idField.SetValue(rec2, firstId);
            RecordableRegistry.Register(rec2);
            Assert.AreNotEqual(firstId, rec2.UniqueId, "Registry should have asked rec2 to regenerate on collision.");
            Assert.IsTrue(RecordableRegistry.TryGet(firstId, out var resolved1) && resolved1 == rec1, "Original holder of the id is preserved.");
            Assert.IsTrue(RecordableRegistry.TryGet(rec2.UniqueId, out var resolved2) && resolved2 == rec2, "Newcomer registered under fresh id.");
        }

        [Test]
        public void Registry_DisableRemovesFromRegistry()
        {
            var rec = _go1.AddComponent<Recordable>();
            string id = rec.UniqueId;
            RecordableRegistry.Register(rec);
            Assert.IsTrue(RecordableRegistry.TryGet(id, out _));
            RecordableRegistry.Unregister(rec);
            Assert.IsFalse(RecordableRegistry.TryGet(id, out _));
        }

        [Test]
        public void Registry_ChangedEvent_FiresOnEnableAndDisable()
        {
            int enabledCount = 0;
            int disabledCount = 0;
            RecordableRegistry.Changed += (ev, id, rec) =>
            {
                if (ev == RecordableRegistry.RegistryEvent.Enabled) enabledCount++;
                else if (ev == RecordableRegistry.RegistryEvent.Disabled) disabledCount++;
            };
            var rec = _go1.AddComponent<Recordable>();
            RecordableRegistry.Register(rec);
            RecordableRegistry.Unregister(rec);
            Assert.AreEqual(1, enabledCount);
            Assert.AreEqual(1, disabledCount);
        }
    }
}
