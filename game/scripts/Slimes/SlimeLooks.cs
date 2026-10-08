using Godot;
using OpenRanch.Formats.Unity;
using OpenRanch.Game.World;

namespace OpenRanch.Game.Slimes;

/// <summary>
/// Simple stand-ins for the slime and plort shaders (docs/formats/prefabs.md, "Slime appearances"):
/// the body takes the material's bottom, middle and top colours from the foot to the top of its
/// model, and the face draws the eyes or mouth shapes of the face texture over the body.
/// </summary>
public static class SlimeLooks
{
    private static readonly Shader BodyShader = new()
    {
        Code = """
            shader_type spatial;
            render_mode cull_back, diffuse_lambert;

            uniform vec4 top_color : source_color = vec4(1.0);
            uniform vec4 middle_color : source_color = vec4(1.0);
            uniform vec4 bottom_color : source_color = vec4(1.0);
            uniform float y_min = 0.0;
            uniform float y_max = 1.0;
            uniform bool has_stripes = false;
            uniform bool stripes_uv2 = false;
            uniform sampler2D stripes : source_color, hint_default_white, filter_linear_mipmap, repeat_enable;

            varying float height;

            void vertex() {
                height = clamp((VERTEX.y - y_min) / max(y_max - y_min, 0.0001), 0.0, 1.0);
            }

            void fragment() {
                vec3 c = height < 0.5
                    ? mix(bottom_color.rgb, middle_color.rgb, height * 2.0)
                    : mix(middle_color.rgb, top_color.rgb, height * 2.0 - 1.0);
                if (has_stripes)
                    c *= texture(stripes, stripes_uv2 ? UV2 : UV).rgb;
                ALBEDO = c;
            }
            """,
    };

    // The face texture is sampled with the body's UVs as they are: v = 0 is the bottom of the face (docs/formats/prefabs.md).
    // The layer sits a few millimetres out along the normal so it doesn't fight the body for depth.
    // It is drawn opaque with an alpha cut-off rather than blended: the full-screen fog pass repaints the
    // screen from a copy taken before transparent objects are drawn, which would erase a blended face.
    private static readonly Shader FaceShader = new()
    {
        Code = """
            shader_type spatial;
            render_mode cull_back, diffuse_lambert, specular_disabled;

            uniform sampler2D atlas : filter_linear, repeat_disable;
            uniform bool eyes = true;
            uniform vec4 color_a : source_color = vec4(0.0, 0.0, 0.0, 1.0);
            uniform vec4 color_b : source_color = vec4(0.0, 0.0, 0.0, 1.0);
            uniform vec4 color_c : source_color = vec4(1.0);
            uniform float threshold = 0.5;

            void vertex() {
                VERTEX += NORMAL * 0.004;
            }

            void fragment() {
                if (UV.x < 0.0 || UV.x > 1.0)
                    discard;
                vec4 t = texture(atlas, UV);
                float a;
                vec3 c;
                if (eyes) {
                    a = smoothstep(threshold - 0.05, threshold + 0.05, t.r);
                    c = mix(color_b.rgb, color_a.rgb, smoothstep(threshold, 1.0, t.r));
                    c = mix(c, color_c.rgb, t.b);
                } else {
                    a = 1.0 - smoothstep(threshold - 0.05, threshold + 0.05, t.a);
                    c = color_a.rgb;
                }
                ALBEDO = c;
                ALPHA = a;
                ALPHA_SCISSOR_THRESHOLD = 0.5;
            }
            """,
    };

    /// <summary>Whether a material colours its model top to bottom (slime bodies and plorts) and has no picture texture.</summary>
    public static bool IsGradient(MaterialData mat) =>
        mat.Colors.ContainsKey("_TopColor") && mat.Colors.ContainsKey("_MiddleColor") && mat.Colors.ContainsKey("_BottomColor")
        && mat.Texture("_Diffuse") is null && mat.Texture("_MainTex") is null;

    /// <summary>The gradient body material for a model whose height runs from <paramref name="yMin"/> to <paramref name="yMax"/>.</summary>
    public static ShaderMaterial Body(MaterialData mat, float yMin, float yMax, Texture2D? stripes)
    {
        var m = new ShaderMaterial { Shader = BodyShader };
        m.SetShaderParameter("top_color", UnityConvert.Color(mat.Color("_TopColor", System.Numerics.Vector4.One)));
        m.SetShaderParameter("middle_color", UnityConvert.Color(mat.Color("_MiddleColor", System.Numerics.Vector4.One)));
        m.SetShaderParameter("bottom_color", UnityConvert.Color(mat.Color("_BottomColor", System.Numerics.Vector4.One)));
        m.SetShaderParameter("y_min", yMin);
        m.SetShaderParameter("y_max", yMax);
        m.SetShaderParameter("has_stripes", stripes is not null);
        if (stripes is not null)
        {
            m.SetShaderParameter("stripes", stripes);
            m.SetShaderParameter("stripes_uv2", mat.Float("_StripeUV1", 0) > 0.5f);
        }
        return m;
    }

    /// <summary>An eyes or mouth layer from the face material, or null when it has no face texture.</summary>
    public static ShaderMaterial? Face(MaterialData mat, Texture2D? atlas)
    {
        if (atlas is null)
            return null;
        var eyes = mat.Colors.ContainsKey("_EyeRed");
        var m = new ShaderMaterial { Shader = FaceShader };
        m.SetShaderParameter("atlas", atlas);
        m.SetShaderParameter("eyes", eyes);
        System.Numerics.Vector4 C(string name) => mat.Color(name, new System.Numerics.Vector4(0, 0, 0, 1));
        if (eyes)
        {
            m.SetShaderParameter("color_a", UnityConvert.Color(C("_EyeRed")));
            m.SetShaderParameter("color_b", UnityConvert.Color(C("_EyeGreen")));
            m.SetShaderParameter("color_c", UnityConvert.Color(C("_EyeBlue")));
            m.SetShaderParameter("threshold", mat.Float("_EyeSmoothStepBase", 0.5f));
        }
        else
        {
            m.SetShaderParameter("color_a", UnityConvert.Color(C("_MouthMid")));
            m.SetShaderParameter("threshold", mat.Float("_MouthSmoothStepBase", 0.5f));
        }
        return m;
    }
}
