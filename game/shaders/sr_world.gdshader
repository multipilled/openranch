// Landscape material (docs/behavior/landscape.md). Sides: a primary rock texture projected from three
// sides in world space, broken up by a detail texture through a large noise mask, with strata bands
// by height. Tops: a "topper" texture (dirt, sand, moss) on surfaces facing upward, broken up the
// same way. Tall areas fade toward the material's top colour, low areas toward its sea-level colour.
// An approximation built from the materials' own textures and values, checked against screenshots.
shader_type spatial;
render_mode cull_back, diffuse_lambert, specular_schlick_ggx;

uniform sampler2D primary_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform vec4 primary_st = vec4(0.2, 0.2, 0.0, 0.0);
uniform sampler2D detail_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform vec4 detail_st = vec4(0.2, 0.2, 0.0, 0.0);
uniform bool has_detail = false;
uniform sampler2D noise_mask : hint_default_white, filter_linear_mipmap, repeat_enable;
uniform vec4 noise_st = vec4(0.05, 0.05, 0.0, 0.0);
uniform sampler2D topper_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform vec4 topper_st = vec4(0.2, 0.2, 0.0, 0.0);
uniform sampler2D topper_detail_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform vec4 topper_detail_st = vec4(0.2, 0.2, 0.0, 0.0);
uniform bool has_topper = false;
uniform bool has_topper_detail = false;
uniform float topper_coverage = 0.5;
uniform sampler2D topper_depth : hint_default_white, filter_linear_mipmap, repeat_enable;
uniform vec4 topper_depth_st = vec4(0.2, 0.2, 0.0, 0.0);
uniform sampler2D vertical_ramp : hint_default_white, filter_linear_mipmap, repeat_enable;
uniform vec4 vertical_ramp_st = vec4(0.025, 0.0125, 0.0, 0.0);
uniform bool has_vertical_ramp = false;
uniform vec4 ramp_top : source_color = vec4(1.0);
uniform float top_ramp_offset = 35.0;
uniform float top_ramp_scale = 30.0;
uniform vec4 sea_ramp : source_color = vec4(1.0);
uniform float sea_ramp_offset = -3.0;
uniform float sea_ramp_scale = 1.0;
uniform bool use_mesh_uvs = false;
uniform vec4 tint : source_color = vec4(1.0);

varying vec3 world_pos;
varying vec3 world_normal;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

vec3 triplanar(sampler2D tex, vec4 st, vec3 p, vec3 w) {
	return texture(tex, p.zy * st.xy + st.zw).rgb * w.x
		+ texture(tex, p.xz * st.xy + st.zw).rgb * w.y
		+ texture(tex, p.xy * st.xy + st.zw).rgb * w.z;
}

// The same projection for the noise mask, which holds data rather than colour.
float triplanar_mask(vec4 st, vec3 p, vec3 w) {
	return texture(noise_mask, p.zy * st.xy + st.zw).r * w.x
		+ texture(noise_mask, p.xz * st.xy + st.zw).r * w.y
		+ texture(noise_mask, p.xy * st.xy + st.zw).r * w.z;
}

void fragment() {
	vec3 n = normalize(world_normal);
	vec3 w = pow(abs(n), vec3(4.0));
	w /= max(w.x + w.y + w.z, 1e-4);
	// Godot's Z points the other way from the original's; the patterns don't care which way.
	vec3 p = world_pos;
	float noise = triplanar_mask(noise_st, p, w);

	vec3 side = use_mesh_uvs
		? texture(primary_tex, UV * primary_st.xy + primary_st.zw).rgb
		: triplanar(primary_tex, primary_st, p, w);
	if (has_detail) {
		side = mix(side, triplanar(detail_tex, detail_st, p, w), smoothstep(0.35, 0.65, noise));
	}
	if (has_vertical_ramp) {
		// Strata: a grey band texture (0.5 is neutral) running around the rock by height.
		float band = texture(vertical_ramp, vec2((p.x + p.z) * vertical_ramp_st.x, p.y * vertical_ramp_st.y) + vertical_ramp_st.zw).r;
		side *= clamp(band * 2.0, 0.6, 1.4);
	}

	vec3 base = side;
	if (has_topper) {
		vec3 top = texture(topper_tex, p.xz * topper_st.xy + topper_st.zw).rgb;
		if (has_topper_detail) {
			top = mix(texture(topper_detail_tex, p.xz * topper_detail_st.xy + topper_detail_st.zw).rgb, top, smoothstep(0.4, 0.6, noise));
		}
		float bump = texture(topper_depth, p.xz * topper_depth_st.xy + topper_depth_st.zw).r - 0.5;
		float amount = smoothstep(0.42, 0.58, n.y * topper_coverage * 0.6 + bump * 0.3);
		base = mix(side, top, amount);
	}

	float high = clamp((p.y - top_ramp_offset) / max(top_ramp_scale, 0.001), 0.0, 1.0);
	base = mix(base, base * ramp_top.rgb * 1.6, high * 0.6);
	float low = clamp((sea_ramp_offset - p.y) / max(sea_ramp_scale * 8.0, 0.001), 0.0, 1.0);
	base = mix(base, base * sea_ramp.rgb * 2.0, low * 0.5);

	ALBEDO = base * tint.rgb;
	ROUGHNESS = 0.92;
	SPECULAR = 0.25;
}
