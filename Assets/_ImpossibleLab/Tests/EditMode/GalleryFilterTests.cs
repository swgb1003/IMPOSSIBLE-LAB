using System.Collections.Generic;
using System.IO;
using System.Linq;
using ImpossibleLab.Core;
using NUnit.Framework;
using UnityEngine;

namespace ImpossibleLab.Tests
{
    /// <summary>S06 ギャラリーのタグ絞り込み。</summary>
    public class GalleryFilterTests
    {
        static GalleryEntry E(string id, params string[] tags) => new GalleryEntry { Id = id, Name = id, Tags = tags.ToList() };

        static List<GalleryEntry> Sample() => new List<GalleryEntry>
        {
            E("a", "protection", "daily"), E("b", "lighting", "outdoor"), E("c", "protection", "outdoor"), E("d", "thermal"),
        };

        [Test] public void Apply_KeepsOrder_AndNullMeansAll()
        {
            var all = Sample();
            Assert.AreEqual(new[] { "a", "b", "c", "d" }, GalleryFilter.Apply(all, null).Select(e => e.Id).ToArray());
            Assert.AreEqual(new[] { "a", "c" }, GalleryFilter.Apply(all, "protection").Select(e => e.Id).ToArray());
            Assert.AreEqual(new[] { "b", "c" }, GalleryFilter.Apply(all, "outdoor").Select(e => e.Id).ToArray());
            Assert.AreEqual(0, GalleryFilter.Apply(all, "carrier").Count);
        }

        [Test] public void Counts_CatalogOrder_SkipsEmpty()
        {
            var c = GalleryFilter.Counts(Sample());
            Assert.AreEqual(new[] { "protection", "lighting", "thermal", "daily", "outdoor" }, c.Select(x => x.tag.Id).ToArray());
            Assert.AreEqual(2, c.First(x => x.tag.Id == "protection").count);
            Assert.AreEqual(1, c.First(x => x.tag.Id == "daily").count);
        }

        [Test] public void Repository_ListFillsTags()
        {
            string dir = Path.Combine(Path.GetTempPath(), "ilab-test-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                var r = DnaJson.Parse(Resources.Load<TextAsset>("ImpossibleLabSamples/back_heater").text);
                Assert.IsTrue(r.IsValid, r.Summary);
                var repo = new InventionRepository(dir);
                repo.Save(new SavedInvention { Current = r.Dna });
                var e = repo.List().Single();
                CollectionAssert.AreEqual(r.Dna.Tags, e.Tags);
                Assert.AreEqual(1, GalleryFilter.Apply(repo.List(), "thermal").Count);
                Assert.AreEqual(0, GalleryFilter.Apply(repo.List(), "carrier").Count);
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }
    }
}
