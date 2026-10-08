namespace OpenRanch.Formats.Unity;

/// <summary>Unity's numeric class ids for the object types openranch cares about.</summary>
public static class UnityClassId
{
    public const int GameObject = 1;
    public const int Transform = 4;
    public const int Material = 21;
    public const int MeshRenderer = 23;
    public const int Texture2D = 28;
    public const int MeshFilter = 33;
    public const int Mesh = 43;
    public const int Shader = 48;
    public const int TextAsset = 49;
    public const int Rigidbody = 54;
    public const int MeshCollider = 64;
    public const int BoxCollider = 65;
    public const int AnimationClip = 74;
    public const int AudioSource = 82;
    public const int AudioClip = 83;
    public const int Cubemap = 89;
    public const int Avatar = 90;
    public const int AnimatorController = 91;
    public const int Animator = 95;
    public const int Light = 108;
    public const int Animation = 111;
    public const int MonoBehaviour = 114;
    public const int MonoScript = 115;
    public const int Font = 128;
    public const int SphereCollider = 135;
    public const int CapsuleCollider = 136;
    public const int SkinnedMeshRenderer = 137;
    public const int ParticleSystem = 198;
    public const int ParticleSystemRenderer = 199;
    public const int LodGroup = 205;
    public const int Sprite = 213;
    public const int Terrain = 218;
    public const int Camera = 20;
    public const int AudioListener = 81;
    public const int TrailRenderer = 96;
    public const int LineRenderer = 120;
    public const int FixedJoint = 138;
    public const int CharacterJoint = 144;
    public const int SpringJoint = 145;
    public const int Canvas = 222;
    public const int CanvasRenderer = 223;
    public const int RectTransform = 224;
    public const int TerrainData = 156;

    private static readonly Dictionary<int, string> Names = new()
    {
        [GameObject] = "GameObject",
        [Transform] = "Transform",
        [Material] = "Material",
        [MeshRenderer] = "MeshRenderer",
        [Texture2D] = "Texture2D",
        [MeshFilter] = "MeshFilter",
        [Mesh] = "Mesh",
        [Shader] = "Shader",
        [TextAsset] = "TextAsset",
        [Rigidbody] = "Rigidbody",
        [MeshCollider] = "MeshCollider",
        [BoxCollider] = "BoxCollider",
        [AnimationClip] = "AnimationClip",
        [AudioSource] = "AudioSource",
        [AudioClip] = "AudioClip",
        [Cubemap] = "Cubemap",
        [Avatar] = "Avatar",
        [AnimatorController] = "AnimatorController",
        [Animator] = "Animator",
        [Light] = "Light",
        [Animation] = "Animation",
        [MonoBehaviour] = "MonoBehaviour",
        [MonoScript] = "MonoScript",
        [Font] = "Font",
        [SphereCollider] = "SphereCollider",
        [CapsuleCollider] = "CapsuleCollider",
        [SkinnedMeshRenderer] = "SkinnedMeshRenderer",
        [ParticleSystem] = "ParticleSystem",
        [ParticleSystemRenderer] = "ParticleSystemRenderer",
        [LodGroup] = "LODGroup",
        [Sprite] = "Sprite",
        [Terrain] = "Terrain",
        [TerrainData] = "TerrainData",
        [Camera] = "Camera",
        [AudioListener] = "AudioListener",
        [TrailRenderer] = "TrailRenderer",
        [LineRenderer] = "LineRenderer",
        [FixedJoint] = "FixedJoint",
        [CharacterJoint] = "CharacterJoint",
        [SpringJoint] = "SpringJoint",
        [Canvas] = "Canvas",
        [CanvasRenderer] = "CanvasRenderer",
        [RectTransform] = "RectTransform",
    };

    public static string NameOf(int classId) => Names.TryGetValue(classId, out var name) ? name : $"Class{classId}";

    /// <summary>Classes whose serialized data starts with <c>m_Name</c>.</summary>
    public static bool StoresNameFirst(int classId) => classId is Material or Texture2D or Mesh or Shader or TextAsset
        or AnimationClip or AudioClip or Cubemap or Avatar or AnimatorController or MonoScript or Font or Sprite
        or TerrainData;
}
