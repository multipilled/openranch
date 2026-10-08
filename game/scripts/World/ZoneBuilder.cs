using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenRanch.Formats.Scene;
using OpenRanch.Formats.Unity;

namespace OpenRanch.Game.World;

/// <summary>
/// Builds Godot nodes for one area of the world: static-batch meshes as single instances, repeated
/// props as multimeshes split into spatial cells, and one static body holding the solid colliders.
/// </summary>
public static class ZoneBuilder
{
    private const float CellSize = 64f;

    public sealed record Result(Node3D Root, int MeshInstances, int MultiMeshes, int Instances, int Shapes);

    public static Result Build(ZoneExtract zone, WorldAssets assets, PhysicsLayers layers)
    {
        var root = new Node3D { Name = zone.Name };
        var visuals = new Node3D { Name = "Visuals" };
        root.AddChild(visuals);
        int meshInstances = 0, multiMeshes = 0, instances = 0;

        // Static batches: geometry is already in world space; each renderer names a run of sub-meshes.
        foreach (var batch in zone.Renderers.Where(r => r.StaticBatched).GroupBy(r => r.Mesh))
        {
            var parts = new List<(int, AssetRef?)>();
            foreach (var r in batch)
                for (var i = 0; i < r.SubMeshCount; i++)
                    parts.Add((r.FirstSubMesh + i, r.Materials.Count == 0 ? null : r.Materials[Math.Min(i, r.Materials.Count - 1)]));
            var mesh = assets.BuildMesh(batch.Key, parts, mirrored: false);
            visuals.AddChild(new MeshInstance3D
            {
                Mesh = mesh,
                CastShadow = batch.Any(r => r.CastShadows) ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
            });
            meshInstances++;
            instances += batch.Count();
        }

        // Everything else: group identical mesh + materials + mirroring, then by spatial cell.
        var meshCache = new Dictionary<(AssetRef, string, bool), ArrayMesh>();
        var groups = zone.Renderers.Where(r => !r.StaticBatched).GroupBy(r =>
        {
            var t = UnityConvert.Transform(r.World);
            var cell = (Mathf.FloorToInt(t.Origin.X / CellSize), Mathf.FloorToInt(t.Origin.Z / CellSize));
            var mats = string.Join(",", r.Materials.Select(m => m?.PathId.ToString() ?? "-"));
            return (r.Mesh, mats, UnityConvert.IsMirrored(r.World), r.CastShadows, cell);
        });
        foreach (var group in groups)
        {
            var (meshRef, mats, mirrored, castShadows, _) = group.Key;
            if (!meshCache.TryGetValue((meshRef, mats, mirrored), out var mesh))
            {
                var first = group.First();
                var subCount = assets.Mesh(meshRef).SubMeshes.Count;
                var parts = Enumerable.Range(0, subCount)
                    .Select(i => (i, first.Materials.Count == 0 ? (AssetRef?)null : first.Materials[Math.Min(i, first.Materials.Count - 1)]));
                meshCache[(meshRef, mats, mirrored)] = mesh = assets.BuildMesh(meshRef, parts, mirrored);
            }
            if (mesh.GetSurfaceCount() == 0)
                continue;
            var shadow = castShadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off;
            var items = group.ToList();
            if (items.Count == 1)
            {
                visuals.AddChild(new MeshInstance3D { Mesh = mesh, Transform = UnityConvert.Transform(items[0].World), CastShadow = shadow });
                meshInstances++;
            }
            else
            {
                var multi = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = items.Count };
                for (var i = 0; i < items.Count; i++)
                    multi.SetInstanceTransform(i, UnityConvert.Transform(items[i].World));
                visuals.AddChild(new MultiMeshInstance3D { Multimesh = multi, CastShadow = shadow });
                multiMeshes++;
            }
            instances += items.Count;
        }

        var body = new StaticBody3D { Name = "Collision" };
        root.AddChild(body);
        var shapes = 0;
        foreach (var c in zone.Colliders.Where(c => layers.Collide(PhysicsLayers.PlayerLayer, (int)c.Layer)))
        {
            var shape = MakeShape(c, assets);
            if (shape is null)
                continue;
            body.AddChild(shape);
            shapes++;
        }

        return new Result(root, meshInstances, multiMeshes, instances, shapes);
    }

    private static CollisionShape3D? MakeShape(ColliderItem item, WorldAssets assets)
    {
        var world = UnityConvert.Transform(item.World);
        var c = item.Collider;
        if (c.Shape == ColliderShape.Mesh)
        {
            var faces = assets.Faces(item.Mesh!.Value, world);
            if (faces.Length == 0)
                return null;
            var concave = new ConcavePolygonShape3D { BackfaceCollision = true };
            concave.SetFaces(faces);
            return new CollisionShape3D { Shape = concave };
        }

        // Primitive shapes: keep rotation and position, fold scale into the shape's size.
        var scale = world.Basis.Scale.Abs();
        var basis = world.Basis.Orthonormalized();
        if (basis.Determinant() < 0)
            basis.Column0 = -basis.Column0;
        var center = world * UnityConvert.Position(c.Center);

        switch (c.Shape)
        {
            case ColliderShape.Box:
                var size = new Vector3(c.Size.X * scale.X, c.Size.Y * scale.Y, c.Size.Z * scale.Z).Abs();
                return new CollisionShape3D { Shape = new BoxShape3D { Size = size }, Transform = new Transform3D(basis, center) };
            case ColliderShape.Sphere:
                return new CollisionShape3D
                {
                    Shape = new SphereShape3D { Radius = c.Radius * Math.Max(scale.X, Math.Max(scale.Y, scale.Z)) },
                    Transform = new Transform3D(basis, center),
                };
            case ColliderShape.Capsule:
                // Godot capsules run along local Y; Unity's direction is 0 = X, 1 = Y, 2 = Z.
                Basis b;
                float along, across;
                switch (c.Direction)
                {
                    case 0:
                        b = new Basis(-basis.Column1, basis.Column0, basis.Column2);
                        along = scale.X;
                        across = Math.Max(scale.Y, scale.Z);
                        break;
                    case 2:
                        b = new Basis(basis.Column0, basis.Column2, -basis.Column1);
                        along = scale.Z;
                        across = Math.Max(scale.X, scale.Y);
                        break;
                    default:
                        b = basis;
                        along = scale.Y;
                        across = Math.Max(scale.X, scale.Z);
                        break;
                }
                var radius = c.Radius * across;
                var height = Math.Max(c.Height * along, radius * 2);
                return new CollisionShape3D { Shape = new CapsuleShape3D { Radius = radius, Height = height }, Transform = new Transform3D(b, center) };
            default:
                return null;
        }
    }
}
