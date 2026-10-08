// Painted-object material: a colour mask picks one of four colour pairs per area, and a
// paint-stroke texture blends between the two colours of each pair.
// First approximation for milestone 1.
shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

uniform sampler2D color_mask : hint_default_black, filter_linear_mipmap, repeat_enable;
uniform sampler2D ambient_occlusion : hint_default_white, filter_linear_mipmap, repeat_enable;
uniform sampler2D paint_strokes : hint_default_white, filter_linear_mipmap, repeat_enable;
uniform bool has_strokes = false;
uniform vec4 strokes_st = vec4(1.0, 1.0, 0.0, 0.0);
uniform vec4 color00 : source_color;
uniform vec4 color01 : source_color;
uniform vec4 color10 : source_color;
uniform vec4 color11 : source_color;
uniform vec4 color20 : source_color;
uniform vec4 color21 : source_color;
uniform vec4 color30 : source_color;
uniform vec4 color31 : source_color;

void fragment() {
	vec4 mask = texture(color_mask, UV);
	float s = has_strokes ? texture(paint_strokes, UV * strokes_st.xy + strokes_st.zw).r : 0.5;
	vec3 r = mix(color00.rgb, color01.rgb, s);
	vec3 g = mix(color10.rgb, color11.rgb, s);
	vec3 b = mix(color20.rgb, color21.rgb, s);
	vec3 rest = mix(color30.rgb, color31.rgb, s);
	float other = clamp(1.0 - mask.r - mask.g - mask.b, 0.0, 1.0);
	vec3 col = r * mask.r + g * mask.g + b * mask.b + rest * other;
	ALBEDO = col * texture(ambient_occlusion, UV).r;
	ROUGHNESS = 0.8;
	SPECULAR = 0.3;
}
