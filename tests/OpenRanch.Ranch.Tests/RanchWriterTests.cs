using System.Numerics;
using System.Text;

namespace OpenRanch.Ranch.Tests;

public class RanchWriterTests
{
    // The sample ranch with actor ids the original could hand out (the sample's largest id leaves no room for a new one).
    private static RanchState Loaded()
    {
        var ranch = RanchSaveTests.Sample();
        ranch.Actors[0].ActorId = 4321;
        ranch.Actors.Add(new Actor { ActorId = 500, TypeId = 12, Position = new Vec3(5, 6, 7), Emotions = { [0] = 0.2f, [1] = 0.3f, [2] = 0.4f } });
        return ranch;
    }

    // A live world that changed nothing: money and clock as loaded, no actors taken in.
    private static LiveRanch Unchanged(RanchState ranch) => new() { Money = ranch.Player.Money, WorldTime = ranch.WorldTime };

    private static string Json(RanchState ranch) => Encoding.UTF8.GetString(RanchSave.ToBytes(ranch));

    [Fact]
    public void A_world_that_changed_nothing_writes_the_ranch_as_loaded()
    {
        var loaded = Loaded();
        Assert.Equal(Json(loaded), Json(RanchWriter.Merge(loaded, Unchanged(loaded))));
    }

    [Fact]
    public void Taking_actors_in_and_writing_them_back_where_they_were_changes_nothing()
    {
        var loaded = Loaded();
        var live = new LiveRanch
        {
            Money = loaded.Player.Money,
            WorldTime = loaded.WorldTime,
            TrackedActorIds = loaded.Actors.Select(a => a.ActorId).ToHashSet(),
            Actors = loaded.Actors.Select(a => new LiveActor(a.ActorId, a.TypeId, a.Position, a.Rotation, a.Emotions)).Reverse().ToList(),
        };
        Assert.Equal(Json(loaded), Json(RanchWriter.Merge(loaded, live)));
    }

    [Fact]
    public void Clocks_of_produce_hens_and_crops_come_from_the_world()
    {
        var loaded = Loaded();
        var first = loaded.Actors[0];
        var live = new LiveRanch
        {
            Money = loaded.Player.Money,
            WorldTime = loaded.WorldTime,
            TrackedActorIds = loaded.Actors.Select(a => a.ActorId).ToHashSet(),
            Actors = loaded.Actors.Select(a => new LiveActor(a.ActorId, a.TypeId, a.Position, a.Rotation, a.Emotions,
                a == first ? new ActorTimers(CycleState: 1, CycleProgressTime: 5000, ReproduceTime: 6000) : null)).ToList(),
            ResourceSpawners = [new CropTimes(new Vec3(1, 2, 3), 7000, 1.5f)],
        };
        var written = RanchWriter.Merge(loaded, live);
        var actor = written.Actors.Single(a => a.ActorId == first.ActorId);
        Assert.Equal((1, 5000.0, 6000.0, first.TransformTime), (actor.CycleState, actor.CycleProgressTime, actor.ReproduceTime, actor.TransformTime));
        Assert.Equal([new CropTimes(new Vec3(1, 2, 3), 7000, 1.5f)], written.World.ResourceSpawners);
        // Without clocks from the world, the saved ones stay.
        var unchanged = RanchWriter.Merge(loaded, Unchanged(loaded));
        Assert.Equal(loaded.World.ResourceSpawners, unchanged.World.ResourceSpawners);
    }

    [Fact]
    public void Merge_leaves_the_loaded_ranch_alone()
    {
        var loaded = Loaded();
        var before = Json(loaded);
        RanchWriter.Merge(loaded, new LiveRanch { Money = 1, WorldTime = 2, TrackedActorIds = new HashSet<long> { 2 } });
        Assert.Equal(before, Json(loaded));
    }

    [Fact]
    public void Money_clock_progress_doors_and_plots_come_from_the_world()
    {
        var loaded = Loaded();
        var live = new LiveRanch
        {
            Money = 1234,
            MoneyEarned = 34,
            WorldTime = loaded.WorldTime + 600,
            Progress = new Dictionary<int, int> { [7] = 4, [8] = 1 },
            AccessDoors = new Dictionary<string, int> { ["door_grotto"] = 1 },
            Plots = new Dictionary<string, LivePlot> { ["plot3"] = new(2, [1, 4]) },
        };
        var ranch = RanchWriter.Merge(loaded, live);

        Assert.Equal(1234, ranch.Player.Money);
        Assert.Equal(loaded.Player.MoneyEverCollected + 34, ranch.Player.MoneyEverCollected);
        Assert.Equal(loaded.WorldTime + 600, ranch.WorldTime);
        Assert.Equal(4, ranch.Player.Progress[7]);
        Assert.Equal(1, ranch.Player.Progress[8]);
        Assert.Equal(1, ranch.AccessDoors["door_grotto"]);
        Assert.Equal(2, ranch.AccessDoors["door_lab"]);
        Assert.Equal(2, ranch.FindPlot("plot3")!.Type);
        Assert.Equal([1, 4], ranch.FindPlot("plot3")!.Upgrades);
        // What the world doesn't touch passes through: the garden's crop stays on the site.
        Assert.Equal(6, ranch.FindPlot("plot3")!.AttachedResource);
        Assert.Equal(loaded.Plots[0].Upgrades, ranch.Plots[0].Upgrades);
    }

    [Fact]
    public void A_plot_site_the_ranch_doesnt_have_is_refused()
    {
        var loaded = Loaded();
        var live = new LiveRanch { Plots = new Dictionary<string, LivePlot> { ["nowhere"] = new(1, []) } };
        Assert.Throws<ArgumentException>(() => RanchWriter.Merge(loaded, live));
    }

    [Fact]
    public void Actors_are_moved_dropped_kept_and_added()
    {
        var loaded = Loaded(); // actors 4321 (type 11), 2 (type 50), 500 (type 12)
        var live = new LiveRanch
        {
            Money = loaded.Player.Money,
            WorldTime = loaded.WorldTime,
            // 4321 and 2 were taken into the world; 500 wasn't (it stays as saved).
            TrackedActorIds = new HashSet<long> { 4321, 2 },
            Actors =
            [
                new LiveActor(4321, 11, new Vec3(10, 2, 30), new Vec3(0, 180, 0), new Dictionary<int, float> { [2] = 0.75f, [1] = 1.5f }),
                new LiveActor(null, 50, new Vec3(-1, 0, -2), new Vec3(0, 0, 0), new Dictionary<int, float>()),
                new LiveActor(null, 51, new Vec3(-3, 0, -4), new Vec3(0, 90, 0), new Dictionary<int, float>()),
            ],
        };
        var ranch = RanchWriter.Merge(loaded, live);

        // 2 is gone from the world (eaten, sold or sucked up).
        Assert.Equal([4321, 500, 4322, 4323], ranch.Actors.Select(a => a.ActorId));

        var moved = ranch.Actors[0];
        Assert.Equal(new Vec3(10, 2, 30), moved.Position);
        Assert.Equal(new Vec3(0, 180, 0), moved.Rotation);
        Assert.Equal(0.666f, moved.Emotions[0]); // not modelled: as saved
        Assert.Equal(1f, moved.Emotions[1]);     // clamped to the 0..1 range moods have
        Assert.Equal(0.75f, moved.Emotions[2]);
        Assert.Equal(77.25, moved.DisabledAtTime); // members the world doesn't model pass through
        Assert.Equal([500], moved.Fashions);

        Assert.Equal(new Vec3(5, 6, 7), ranch.Actors[1].Position);

        var added = ranch.Actors[2];
        Assert.Equal(50, added.TypeId);
        Assert.Equal(new Vec3(-1, 0, -2), added.Position);
        Assert.Equal(loaded.Player.RegionSetId, added.RegionSetId);
        Assert.Equal(0, added.DestroyTime);
        Assert.Empty(added.Emotions);
    }

    [Fact]
    public void New_actor_ids_start_where_the_originals_dynamic_ids_start()
    {
        var ranch = RanchWriter.Merge(new RanchState(), new LiveRanch
        {
            Actors = [new LiveActor(null, 1, default, default, new Dictionary<int, float>())],
        });
        Assert.Equal(RanchWriter.FirstDynamicActorId, ranch.Actors.Single().ActorId);
    }

    [Fact]
    public void Live_actors_must_be_ones_taken_in_from_the_save()
    {
        var loaded = Loaded();
        var stray = new LiveRanch { Actors = [new LiveActor(2, 50, default, default, new Dictionary<int, float>())] };
        Assert.Throws<ArgumentException>(() => RanchWriter.Merge(loaded, stray));
        var twice = new LiveRanch
        {
            TrackedActorIds = new HashSet<long> { 2 },
            Actors = [new LiveActor(2, 50, default, default, new Dictionary<int, float>()), new LiveActor(2, 50, default, default, new Dictionary<int, float>())],
        };
        Assert.Throws<ArgumentException>(() => RanchWriter.Merge(loaded, twice));
    }

    [Fact]
    public void The_written_file_reads_back_as_the_merged_ranch()
    {
        var loaded = Loaded();
        var live = new LiveRanch
        {
            Money = 99,
            WorldTime = 123456.5,
            TrackedActorIds = new HashSet<long> { 500 },
            Actors =
            [
                new LiveActor(500, 12, new Vec3(1.25f, 2.5f, -3.75f), new Vec3(350, 10, 0), new Dictionary<int, float> { [0] = 0.9f }),
                new LiveActor(null, 60, new Vec3(0.1f, 0.2f, 0.3f), new Vec3(0, 270, 0), new Dictionary<int, float>()),
            ],
        };
        var merged = RanchWriter.Merge(loaded, live);
        var path = Path.Combine(Path.GetTempPath(), $"openranch-writer-{Guid.NewGuid():N}{RanchSave.FileExtension}");
        try
        {
            RanchSave.WriteFile(path, merged);
            var again = RanchFiles.Read(path);
            Assert.Equal(Json(merged), Json(again));
            Assert.Equal(99, again.Player.Money);
            Assert.Equal(123456.5, again.WorldTime);
            Assert.Equal(new Vec3(1.25f, 2.5f, -3.75f), again.Actors.Single(a => a.ActorId == 500).Position);
            Assert.Equal(60, again.Actors.Single(a => a.ActorId == 4322).TypeId);
            // Writing what was read changes nothing.
            Assert.Equal(Json(again), Json(RanchWriter.Merge(again, Unchanged(again))));
        }
        finally
        {
            File.Delete(path);
        }
    }

    // Unity applies Euler angles about z, then x, then y.
    private static Quaternion FromEuler(float x, float y, float z)
    {
        static Quaternion About(Vector3 axis, float degrees) => Quaternion.CreateFromAxisAngle(axis, degrees * MathF.PI / 180);
        return About(Vector3.UnitY, y) * About(Vector3.UnitX, x) * About(Vector3.UnitZ, z);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(0, 90, 0)]
    [InlineData(0, 270, 0)]
    [InlineData(10, 200, 30)]
    [InlineData(350, 45, 5)]
    [InlineData(80, 120, 300)]
    [InlineData(90, 30, 0)]
    [InlineData(270, 60, 0)]
    public void Euler_angles_are_the_originals_convention(float x, float y, float z)
    {
        var rotation = FromEuler(x, y, z);
        var euler = RanchWriter.EulerDegrees(rotation);
        foreach (var angle in new[] { euler.X, euler.Y, euler.Z })
            Assert.InRange(angle, 0f, 359.9999f);
        // The angles stand for the same rotation (the same angles, away from straight up or down).
        var again = FromEuler(euler.X, euler.Y, euler.Z);
        foreach (var v in new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ })
            Assert.True(Vector3.Distance(Vector3.Transform(v, rotation), Vector3.Transform(v, again)) < 1e-4f, $"{v}: {euler}");
        if (x is not (90 or 270))
        {
            Assert.Equal(x, euler.X, 0.01f);
            Assert.Equal(y, euler.Y, 0.01f);
            Assert.Equal(z, euler.Z, 0.01f);
        }
    }

    [Fact]
    public void Yaw_by_a_quarter_turn_faces_plus_x()
    {
        // In Unity's axes, a yaw of 90 degrees turns forward (+z) toward +x.
        var euler = RanchWriter.EulerDegrees(FromEuler(0, 90, 0));
        Assert.Equal(90f, euler.Y, 0.01f);
        Assert.True(Vector3.Distance(Vector3.UnitX, Vector3.Transform(Vector3.UnitZ, FromEuler(0, 90, 0))) < 1e-5f);
    }

    [Fact]
    public void The_markets_saturation_is_laid_over_the_saved_one()
    {
        var loaded = Loaded();
        loaded.World.MarketSaturation = new Dictionary<int, float> { [30] = 5.5f, [31] = 12f };
        var ranch = RanchWriter.Merge(loaded, Unchanged(loaded));
        Assert.Equal(loaded.World.MarketSaturation, ranch.World.MarketSaturation);
        var live = new LiveRanch { Money = loaded.Player.Money, WorldTime = loaded.WorldTime, MarketSaturation = new Dictionary<int, float> { [31] = 13f, [40] = 1f } };
        ranch = RanchWriter.Merge(loaded, live);
        Assert.Equal(5.5f, ranch.World.MarketSaturation[30]);
        Assert.Equal(13f, ranch.World.MarketSaturation[31]);
        Assert.Equal(1f, ranch.World.MarketSaturation[40]);
        Assert.Equal(loaded.World.EconomySeed, ranch.World.EconomySeed);
    }

    [Fact]
    public void The_player_is_written_where_the_world_has_them_or_left_where_saved()
    {
        var loaded = Loaded();
        loaded.Player.Position = new Vec3(1, 2, 3);
        loaded.Player.Rotation = new Vec3(10, 200, 0);
        Assert.Equal(new Vec3(1, 2, 3), RanchWriter.Merge(loaded, Unchanged(loaded)).Player.Position);
        var ranch = RanchWriter.Merge(loaded, new LiveRanch
        {
            Money = loaded.Player.Money,
            WorldTime = loaded.WorldTime,
            PlayerPosition = new Vec3(4, 5, 6),
            PlayerRotation = new Vec3(350, 90, 0),
        });
        Assert.Equal(new Vec3(4, 5, 6), ranch.Player.Position);
        Assert.Equal(new Vec3(350, 90, 0), ranch.Player.Rotation);
    }
}
