// Vertex-colour ramp material (vegetables and similar props). See docs/behavior/painted-objects.md.
// The mesh's vertex colours mark regions (red, green, blue, or none of them for "black"); each region
// takes its colour from a small ramp texture, read further along where the occlusion map is brighter.
shader_type spatial;
render_mode cull_back, diffuse_lambert, specular_schlick_ggx;

uniform sampler2D ramp_red : source_color, hint_default_black, filter_linear, repeat_disable;
uniform sampler2D ramp_green : source_color, hint_default_black, filter_linear, repeat_disable;
uniform sampler2D ramp_blue : source_color, hint_default_black, filter_linear, repeat_disable;
uniform sampler2D ramp_black : source_color, hint_default_black, filter_linear, repeat_disable;
uniform sampler2D ambient_occlusion : hint_default_white, filter_linear_mipmap, repeat_enable;

void fragment() {
	float ao = texture(ambient_occlusion, UV).r;
	vec2 at = vec2(clamp(0.15 + 0.7 * ao, 0.0, 1.0), 0.5);
	vec3 m = clamp(COLOR.rgb, 0.0, 1.0);
	float rest = clamp(1.0 - m.r - m.g - m.b, 0.0, 1.0);
	ALBEDO = texture(ramp_red, at).rgb * m.r + texture(ramp_green, at).rgb * m.g
		+ texture(ramp_blue, at).rgb * m.b + texture(ramp_black, at).rgb * rest;
	ROUGHNESS = 0.7;
	SPECULAR = 0.3;
}
