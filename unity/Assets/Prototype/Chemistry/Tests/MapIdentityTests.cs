// A map's id and hash (docs/design/maps-as-data.md, section 2): the same folder hashes alike anywhere, any change to it
// shows, and the host says by name why a peer can't join.
using System;
using System.IO;
using NUnit.Framework;

namespace Chem.Tests
{
    public class MapIdentityTests
    {
        string repo;
        GameFolders folders;

        [SetUp]
        public void MakeAMap()
        {
            repo = Path.Combine(Path.GetTempPath(), "map-id-" + Guid.NewGuid().ToString("N"));
            folders = new GameFolders(Path.Combine(repo, "rules"));
            Directory.CreateDirectory(Path.Combine(folders.World, "themes"));
            Write("map.json", "{}");
            Write("themes/a.json", "a");
        }

        [TearDown]
        public void RemoveIt() => Directory.Delete(repo, true);

        void Write(string name, string text) => File.WriteAllText(Path.Combine(folders.World, name), text);

        [Test]
        public void TheSameFilesHashAlikeWhereverTheyAre()
        {
            string other = Path.Combine(repo, "copy");
            Directory.CreateDirectory(Path.Combine(other, "themes"));
            File.WriteAllText(Path.Combine(other, "themes", "a.json"), "a");
            File.WriteAllText(Path.Combine(other, "map.json"), "{}");
            Assert.That(MapIdentity.Hash(other), Is.EqualTo(MapIdentity.Hash(folders.World)));
        }

        [Test]
        public void AChangeAnywhereInTheFolderShows()
        {
            string was = MapIdentity.Hash(folders.World);
            Write("themes/a.json", "b");
            Assert.That(MapIdentity.Hash(folders.World), Is.Not.EqualTo(was), "a file under a folder");
            Write("themes/a.json", "a");
            Assert.That(MapIdentity.Hash(folders.World), Is.EqualTo(was));
            File.Move(Path.Combine(folders.World, "themes", "a.json"), Path.Combine(folders.World, "themes", "b.json"));
            Assert.That(MapIdentity.Hash(folders.World), Is.Not.EqualTo(was), "a file's name");
        }

        [Test]
        public void WhatAMachineAddsBesideTheFilesDoesnt()
        {
            string was = MapIdentity.Hash(folders.World);
            Write("themes.meta", "guid");
            Write(".DS_Store", "x");
            Assert.That(MapIdentity.Hash(folders.World), Is.EqualTo(was));
        }

        [Test]
        public void ACopyOffersEveryMapItHas()
        {
            Assert.That(MapIdentity.Offered(folders), Is.EqualTo($"valley:{MapIdentity.Hash(folders.World)}"));
            var isle = new GameFolders(folders.Rules, null, "isle");
            Directory.CreateDirectory(isle.World);
            File.WriteAllText(Path.Combine(isle.World, "map.json"), "{}");
            Directory.CreateDirectory(Path.Combine(folders.Maps, "not-a-map"));
            Assert.That(MapIdentity.Offered(folders), Is.EqualTo($"isle:{MapIdentity.Hash(isle.World)},valley:{MapIdentity.Hash(folders.World)}"));
        }

        [Test]
        public void ThePeerWithTheHostsMapIsLetIn() =>
            Assert.That(MapIdentity.Refusal("isle", "The Isle", "abc", "valley:123,isle:abc"), Is.Null);

        [Test]
        public void APeerWithoutTheHostsMapIsToldWhich()
        {
            Assert.That(MapIdentity.Refusal("isle", "The Isle", "abc", "valley:123"), Is.EqualTo("The host is on The Isle, and this copy doesn't have it."));
            Assert.That(MapIdentity.Refusal("isle", "The Isle", "abc", ""), Does.Contain("doesn't have it"));
            Assert.That(MapIdentity.Refusal("isle", "The Isle", "abc", null), Does.Contain("doesn't have it"));
        }

        [Test]
        public void APeerWithAnotherFolderOfTheSameNameIsToldSo() =>
            Assert.That(MapIdentity.Refusal("isle", "The Isle", "abc", "isle:def"), Is.EqualTo("The host is on The Isle, and this copy has a different The Isle."));

        [Test]
        public void TheCommandLineNamesTheMap()
        {
            Assert.That(MapIdentity.Named(new[] { "game", "-map", "isle" }), Is.EqualTo("isle"));
            Assert.That(MapIdentity.Named(new[] { "game" }), Is.EqualTo(GameFolders.DefaultMap));
            Assert.That(MapIdentity.Named(new[] { "game", "-map" }), Is.EqualTo(GameFolders.DefaultMap));
        }

        [Test]
        public void AnotherMapKeepsThePlayersFolders()
        {
            var player = new GameFolders(folders.Rules, Path.Combine(repo, "data"));
            player.Claude.Test = "probe";
            var isle = player.WithMap("isle");
            Assert.That(isle.World, Does.EndWith("isle"));
            Assert.That(isle.Map, Is.EqualTo("isle"));
            Assert.That(isle.Claude, Is.SameAs(player.Claude));
            Assert.That(isle.PlayerData, Is.EqualTo(player.PlayerData));
        }

        [Test]
        public void LandHashIsEmptyUntilLandIsStamped()
        {
            var w = new World(ChemistryTests.R, 64, 64, 1);
            w.SetLand(new int[64 * 64]);
            Assert.That(w.LandHash(), Is.EqualTo(""));
        }
    }
}
