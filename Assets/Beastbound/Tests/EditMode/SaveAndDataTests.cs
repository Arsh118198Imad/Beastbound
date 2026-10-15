using System.IO;
using Beastbound.Creatures;
using Beastbound.Rules;
using Beastbound.Saving;
using NUnit.Framework;
using UnityEngine;

namespace Beastbound.Tests
{
    public class SaveSystemTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "BeastboundTests_" + System.Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        [Test]
        public void SaveThenLoad_RoundTripsJourney()
        {
            var saves = new SaveSystem(_dir);
            var data = new SaveData { playerX = 1f, playerY = 2f, playerZ = 3f, partnerUid = "abc", objectiveStep = 3, timeOfDay = 18.5f };
            var rec = CreatureRecord.Create(DefaultContent.PebbloomId, 4, Personality.Timid, 1.05f, "nest_lakeshore");
            data.collection.Add(rec);
            data.capturedNests.Add("nest_lakeshore");

            Assert.IsTrue(saves.Save(data, out var error), error);
            Assert.IsTrue(saves.HasSave);

            var loaded = saves.Load(out error);
            Assert.IsNotNull(loaded, error);
            Assert.AreEqual(3f, loaded.playerZ);
            Assert.AreEqual("abc", loaded.partnerUid);
            Assert.AreEqual(1, loaded.collection.Count);
            Assert.AreEqual(Personality.Timid, loaded.collection[0].personality);
            Assert.AreEqual(rec.uid, loaded.collection[0].uid);
            Assert.AreEqual(SaveData.CurrentVersion, loaded.version);
        }

        [Test]
        public void CorruptPrimarySave_FallsBackToBackup()
        {
            var saves = new SaveSystem(_dir);
            Assert.IsTrue(saves.Save(new SaveData { objectiveStep = 1 }, out _));
            Assert.IsTrue(saves.Save(new SaveData { objectiveStep = 2 }, out _)); // first save becomes .bak
            File.WriteAllText(saves.SavePath, "{ this is not json");

            var loaded = saves.Load(out _);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(1, loaded.objectiveStep);
        }

        [Test]
        public void MissingSave_ReturnsNull()
        {
            var saves = new SaveSystem(_dir);
            Assert.IsFalse(saves.HasSave);
            Assert.IsNull(saves.Load(out var error));
            Assert.IsNotNull(error);
        }

        [Test]
        public void Settings_AreSanitized()
        {
            var saves = new SaveSystem(_dir);
            var s = new SettingsData { mouseSensitivity = 999f, quality = 42, masterVolume = -3f, fieldOfView = float.NaN };
            Assert.IsTrue(saves.SaveSettings(s, out _));
            var loaded = saves.LoadSettings();
            Assert.LessOrEqual(loaded.mouseSensitivity, 8f);
            Assert.AreEqual(2, loaded.quality);
            Assert.AreEqual(0f, loaded.masterVolume);
            Assert.AreEqual(60f, loaded.fieldOfView);
        }
    }

    public class CreatureDataTests
    {
        [Test]
        public void DefaultPebbloom_IsValid()
        {
            var species = DefaultContent.CreatePebbloom();
            Assert.IsTrue(species.IsValid(out var problem), problem);
            Assert.AreEqual(DefaultContent.PebbloomId, species.speciesId);
            Assert.GreaterOrEqual(species.attacks.Length, 1);
            Object.DestroyImmediate(species);
        }

        [Test]
        public void Catalog_AlwaysContainsPebbloom()
        {
            var catalog = new CreatureCatalog(null);
            Assert.IsNotNull(catalog.Get(DefaultContent.PebbloomId));
            Assert.IsNull(catalog.Get("does_not_exist"));
        }

        [Test]
        public void DefaultThalorix_IsValid_AndInCatalog()
        {
            var species = DefaultContent.CreateThalorix();
            Assert.IsTrue(species.IsValid(out var problem), problem);
            Assert.AreEqual(CreatureModelKind.Thalorix, species.model);
            Assert.AreEqual(3, species.attacks.Length);
            Object.DestroyImmediate(species);
            Assert.IsNotNull(new CreatureCatalog(null).Get(DefaultContent.ThalorixId));
        }

        [Test]
        public void Record_ClampsInputs_AndClonesIndependently()
        {
            var r = CreatureRecord.Create("pebbloom", 999, Personality.Bold, 7f, "nest");
            Assert.AreEqual(CreatureStatRules.MaxLevel, r.level);
            Assert.AreEqual(CreatureStatRules.MaxVigor, r.vigor);
            var c = r.Clone();
            c.nickname = "Pip";
            Assert.AreNotEqual(r.nickname, c.nickname);
            Assert.AreEqual(r.uid, c.uid);
        }
    }
}
