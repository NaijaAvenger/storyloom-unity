// Storyloom Unity Kit — edit-mode tests for the runner and the simulator.
// Pure C# over hand-built fixture stories: traversal (choice / check / random / jump), conditions and effects, the
// item:<id> inventory convention, save/restore round-trips, and simulator ground truths (a story with a known soft-lock
// must report it; a clean one must not).
//
// To see these in a project's Test Runner, add the package to "testables" in Packages/manifest.json:
//   "testables": ["com.storyloom.unity"]
using System.Linq;
using NUnit.Framework;
using Storyloom;

namespace Storyloom.Tests
{
    public class StoryloomRunnerTests
    {
        // ---- fixture builders --------------------------------------------------------------------------------------
        static StoryNode N(string id, string type = "scene", string title = null) => new StoryNode { id = id, type = type, title = title ?? id };
        static Link L(string to, string port = "out", Condition[] conditions = null) => new Link { toNodeId = to, port = port, conditions = conditions, conditionMode = "all" };
        static Condition C(string variable, string op, string value) => new Condition { variable = variable, op = op, value = value };
        static Effect E(string variable, string op, string value) => new Effect { variable = variable, op = op, value = value };
        static StoryloomStory Story(string startId, StoryNode[] nodes, StoryVariable[] vars = null, Item[] items = null) =>
            new StoryloomStory { name = "fixture", startNodeId = startId, nodes = nodes, variables = vars ?? new StoryVariable[0], items = items ?? new Item[0], characters = new Character[0], locations = new Location[0] };

        // ---- runner: traversal -------------------------------------------------------------------------------------
        [Test]
        public void LinearFlow_ReachesEnding()
        {
            var a = N("a"); a.links = new[] { L("b") };
            var b = N("b"); b.links = new[] { L("end") };
            var end = N("end", "ending");
            var r = new StoryRunner(Story("a", new[] { a, b, end }));
            r.Start();
            Assert.AreEqual("a", r.Current.id);
            Assert.IsTrue(r.Continue()); Assert.AreEqual("b", r.Current.id);
            Assert.IsTrue(r.Continue()); Assert.AreEqual("end", r.Current.id);
            Assert.IsTrue(r.Current.IsEnding);
            Assert.AreEqual(0, r.GetOptions().Count, "an ending offers no options");
            CollectionAssert.AreEqual(new[] { "a", "b", "end" }, r.History);
        }

        [Test]
        public void Effects_ApplyOnEnter_AndConditionsLockLinks()
        {
            var a = N("a"); a.effects = new[] { E("gold", "add", "5") }; a.links = new[] { L("rich", "out", new[] { C("gold", ">=", "10") }), L("poor") };
            var rich = N("rich", "ending"); var poor = N("poor", "ending");
            var story = Story("a", new[] { a, rich, poor }, new[] { new StoryVariable { name = "gold", type = "number", defaultValue = "0" } });
            var r = new StoryRunner(story);
            r.Start();
            Assert.AreEqual(5, r.GetInt("gold"), "entering a node applies its effects");
            var opts = r.GetOptions();
            Assert.AreEqual(2, opts.Count);
            Assert.IsTrue(opts.First(o => o.target.id == "rich").locked, "gold >= 10 must lock at 5");
            Assert.IsFalse(opts.First(o => o.target.id == "poor").locked);
            r.Choose(opts.First(o => !o.locked));
            Assert.AreEqual("poor", r.Current.id);
        }

        [Test]
        public void Items_GiveTake_AndHasLacksConditions()
        {
            var story = Story("a", new[] { N("a") }, items: new[] { new Item { id = "key", name = "Key" } });
            var r = new StoryRunner(story);
            Assert.IsFalse(r.HasItem("key"));
            Assert.IsTrue(r.EvaluateOne(C("item:key", "lacks", "")));
            r.GiveItem("key");
            Assert.IsTrue(r.HasItem("key"));
            Assert.IsTrue(r.EvaluateOne(C("item:key", "has", "")));
            Assert.AreEqual("Key", r.Inventory().Single().name);
            r.TakeItem("key");
            Assert.IsFalse(r.HasItem("key"));
        }

        [Test]
        public void Choice_LockedOptionCarriesReason()
        {
            var c = N("c", "choice");
            c.options = new[] {
                new ChoiceOption { id = "yes", label = "Pay", conditions = new[] { C("gold", ">=", "10") }, conditionMode = "all" },
                new ChoiceOption { id = "no", label = "Refuse" } };
            c.links = new[] { L("paid", "yes"), L("kept", "no") };
            var story = Story("c", new[] { c, N("paid", "ending"), N("kept", "ending") }, new[] { new StoryVariable { name = "gold", type = "number", defaultValue = "3" } });
            var r = new StoryRunner(story);
            r.Start();
            var opts = r.GetOptions();
            Assert.AreEqual(2, opts.Count);
            var pay = opts.First(o => o.label == "Pay");
            Assert.IsTrue(pay.locked); Assert.IsNotEmpty(pay.lockReason);
            r.Choose(opts.First(o => !o.locked));
            Assert.AreEqual("kept", r.Current.id);
        }

        [Test]
        public void Check_TakesPassOrFailPort()
        {
            var chk = N("chk", "check"); chk.conditions = new[] { C("brave", "==", "true") }; chk.conditionMode = "all";
            chk.links = new[] { L("won", "pass"), L("lost", "fail") };
            var story = Story("chk", new[] { chk, N("won", "ending"), N("lost", "ending") }, new[] { new StoryVariable { name = "brave", type = "bool", defaultValue = "false" } });
            var r = new StoryRunner(story);
            r.Start();
            Assert.IsFalse(r.CurrentCheckPasses());
            Assert.AreEqual("lost", r.GetOptions().Single().target.id, "a failing check offers only the fail port");
            r.Set("brave", true);
            Assert.IsTrue(r.CurrentCheckPasses());
            Assert.AreEqual("won", r.GetOptions().Single().target.id);
        }

        [Test]
        public void Jump_PassesThroughAndAppliesEffects()
        {
            var a = N("a"); a.links = new[] { L("j") };
            var j = N("j", "jump"); j.jumpToNodeId = "end"; j.effects = new[] { E("gold", "add", "2") };
            var end = N("end", "ending");
            var r = new StoryRunner(Story("a", new[] { a, j, end }, new[] { new StoryVariable { name = "gold", type = "number", defaultValue = "0" } }));
            r.Start(); r.Continue();
            Assert.AreEqual("end", r.Current.id, "jump is pass-through");
            Assert.AreEqual(2, r.GetInt("gold"), "jump effects still apply");
        }

        [Test]
        public void Random_WithSeededRoll_IsDeterministic()
        {
            var rnd = N("rnd", "random");
            rnd.options = new[] { new ChoiceOption { id = "x", label = "X", weight = 1 }, new ChoiceOption { id = "y", label = "Y", weight = 1 } };
            rnd.links = new[] { L("nx", "x"), L("ny", "y") };
            var r = new StoryRunner(Story("rnd", new[] { rnd, N("nx", "ending"), N("ny", "ending") }));
            r.Random = () => 0.0;   // always the first unlocked outcome
            r.Start();
            var pick = r.PickRandom();
            Assert.IsNotNull(pick);
            Assert.AreEqual("nx", pick.target.id);
        }

        [Test]
        public void SnapshotRestore_RoundTripsStateExactly()
        {
            var a = N("a"); a.effects = new[] { E("gold", "add", "7"), E("item:key", "give", "") }; a.links = new[] { L("end") };
            var story = Story("a", new[] { a, N("end", "ending") },
                new[] { new StoryVariable { name = "gold", type = "number", defaultValue = "0" } },
                new[] { new Item { id = "key", name = "Key" } });
            var r = new StoryRunner(story);
            r.Start();
            var snap = r.SnapshotState();
            r.Continue(); r.Set("gold", 999); r.TakeItem("key");   // mutate everything
            r.RestoreState(snap);
            Assert.AreEqual("a", r.Current.id);
            Assert.AreEqual(7, r.GetInt("gold"));
            Assert.IsTrue(r.HasItem("key"));
            CollectionAssert.AreEqual(new[] { "a" }, r.History);
        }

        [Test]
        public void FromJson_ParsesMinimalStory()
        {
            var story = StoryloomStory.FromJson("{\"name\":\"T\",\"startNodeId\":\"a\",\"nodes\":[{\"id\":\"a\",\"title\":\"A\",\"type\":\"scene\"}]}");
            Assert.IsNotNull(story);
            Assert.AreEqual("a", story.StartNode.id);
            Assert.AreEqual("A", story.GetNode("a").title);
        }
    }

    public class StorySimulatorTests
    {
        static StoryNode N(string id, string type = "scene") => new StoryNode { id = id, type = type, title = id };
        static Link L(string to, string port = "out") => new Link { toNodeId = to, port = port, conditionMode = "all" };

        [Test]
        public void CleanLinearStory_CompletesWithNoSoftLocks()
        {
            var a = N("a"); a.links = new[] { L("b") };
            var b = N("b"); b.links = new[] { L("end") };
            var end = N("end", "ending");
            var story = new StoryloomStory { name = "clean", startNodeId = "a", nodes = new[] { a, b, end }, variables = new StoryVariable[0], items = new Item[0], characters = new Character[0] };
            var r = new StorySimulator(story).Run();
            Assert.IsFalse(r.truncated);
            Assert.AreEqual(0, r.softLocks.Count, "a linear story cannot soft-lock");
            CollectionAssert.Contains(r.endingsReached.ToList(), "end");
            Assert.IsEmpty(r.endingsMissed);
            Assert.IsEmpty(r.neverPlayed, "every node lies on the one path");
            Assert.Greater(r.completions, 0);
        }

        [Test]
        public void BothChoiceBranches_AreExplored()
        {
            var c = N("c", "choice");
            c.options = new[] { new ChoiceOption { id = "l", label = "Left" }, new ChoiceOption { id = "r", label = "Right" } };
            c.links = new[] { L("endL", "l"), L("endR", "r") };
            var story = new StoryloomStory { name = "branchy", startNodeId = "c", nodes = new[] { c, N("endL", "ending"), N("endR", "ending") }, variables = new StoryVariable[0], items = new Item[0], characters = new Character[0] };
            var r = new StorySimulator(story).Run();
            CollectionAssert.Contains(r.endingsReached.ToList(), "endL");
            CollectionAssert.Contains(r.endingsReached.ToList(), "endR");
            Assert.IsEmpty(r.endingsMissed);
        }

        [Test]
        public void MissingKey_IsReportedAsSoftLock_AndKeyPathCompletes()
        {
            // choice: take the key or walk away (which loses it for good); the door needs the key; only the door leads to
            // the ending. Walking away must register as a soft-lock; taking it must complete. The "lost for good" flag is
            // what makes the branches genuinely exclusive — without it the simulator would rightly start getKey later.
            var c = N("c", "choice");
            c.options = new[] { new ChoiceOption { id = "a", label = "Take the key" }, new ChoiceOption { id = "b", label = "Leave it" } };
            c.links = new[] { L("getKey", "a"), L("noKey", "b") };
            var getKey = N("getKey"); getKey.effects = new[] { new Effect { variable = "item:key", op = "give" } }; getKey.links = new[] { L("door") };
            getKey.conditions = new[] { new Condition { variable = "keyLost", op = "==", value = "false" } }; getKey.conditionMode = "all";
            var noKey = N("noKey"); noKey.effects = new[] { new Effect { variable = "keyLost", op = "set", value = "true" } }; noKey.links = new[] { L("door") };
            var door = N("door"); door.conditions = new[] { new Condition { variable = "item:key", op = "has" } }; door.conditionMode = "all"; door.links = new[] { L("end") };
            var end = N("end", "ending");
            var story = new StoryloomStory
            {
                name = "keydoor", startNodeId = "c", nodes = new[] { c, getKey, noKey, door, end },
                variables = new[] { new StoryVariable { name = "keyLost", type = "bool", defaultValue = "false" } },
                items = new[] { new Item { id = "key", name = "Key" } }, characters = new Character[0]
            };
            var r = new StorySimulator(story).Run();
            Assert.Greater(r.softLocks.Count, 0, "the keyless branch stalls in front of the door and must be reported");
            CollectionAssert.Contains(r.endingsReached.ToList(), "end");
            Assert.Greater(r.completions, 0, "the key branch completes");
            var repro = r.softLocks[0].trail;
            CollectionAssert.Contains(repro, "noKey");
            CollectionAssert.DoesNotContain(repro, "getKey");
        }

        [Test]
        public void OrphanEnding_IsReportedMissed()
        {
            var a = N("a"); a.links = new[] { L("end") };
            var story = new StoryloomStory { name = "orphan", startNodeId = "a", nodes = new[] { a, N("end", "ending"), N("secret", "ending") }, variables = new StoryVariable[0], items = new Item[0], characters = new Character[0] };
            var r = new StorySimulator(story).Run();
            CollectionAssert.Contains(r.endingsMissed, "secret");
            CollectionAssert.Contains(r.neverPlayed, "secret");
            CollectionAssert.DoesNotContain(r.endingsMissed, "end");
        }

        [Test]
        public void StrictOrderOff_MakesEverythingStartable()
        {
            // an island scene with no inbound links: unreachable under strict order, startable without it
            var a = N("a"); a.links = new[] { L("end") };
            var island = N("island"); island.links = new[] { L("end") };
            var story = new StoryloomStory { name = "island", startNodeId = "a", nodes = new[] { a, island, N("end", "ending") }, variables = new StoryVariable[0], items = new Item[0], characters = new Character[0] };
            var strict = new StorySimulator(story, new StorySimulator.Options { strictOrder = true }).Run();
            CollectionAssert.Contains(strict.neverPlayed, "island");
            var loose = new StorySimulator(story, new StorySimulator.Options { strictOrder = false }).Run();
            CollectionAssert.DoesNotContain(loose.neverPlayed, "island");
        }
    }
}
