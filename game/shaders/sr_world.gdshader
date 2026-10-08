// Landscape material: a primary texture projected from three sides in world space, with a
// "topper" texture (grass, moss, sand) laid over surfaces that face upward.
// First approximation for milestone 1; colour ramps and detail textures come later.
shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

uniform sampler2D primary_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform vec4 primary_st = vec4(0.2, 0.2, 0.0, 0.0);
uniform sampler2D topper_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform vec4 topper_st = vec4(0.2, 0.2, 0.0, 0.0);
uniform bool has_topper = false;
uniform float topper_coverage = 0.5;
uniform bool use_mesh_uvs = false;
uniform vec4 tint : source_color = vec4(1.0);

varying vec3 world_pos;
varying vec3 world_normal;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

vec3 triplanar(sampler2D tex, vec4 st, vec3 p, vec3 n) {
	vec3 w = pow(abs(n), vec3(4.0));
	w /= max(w.x + w.y + w.z, 1e-4);
	vec3 x = texture(tex, p.zy * st.xy + st.zw).rgb;
	vec3 y = texture(tex, p.xz * st.xy + st.zw).rgb;
	vec3 z = texture(tex, p.xy * st.xy + st.zw).rgb;
	return x * w.x + y * w.y + z * w.z;
}

void fragment() {
	vec3 n = normalize(world_normal);
	vec3 base = use_mesh_uvs
		? texture(primary_tex, UV * primary_st.xy + primary_st.zw).rgb
		: triplanar(primary_tex, primary_st, world_pos, n);
	if (has_topper) {
		vec3 top = texture(topper_tex, world_pos.xz * topper_st.xy + topper_st.zw).rgb;
		float threshold = 1.0 - topper_coverage;
		float amount = smoothstep(threshold - 0.08, threshold + 0.08, n.y);
		base = mix(base, top, amount);
	}
	ALBEDO = base * tint.rgb;
	ROUGHNESS = 0.92;
	SPECULAR = 0.25;
}
