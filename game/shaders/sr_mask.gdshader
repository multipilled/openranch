// Painted-object material (ranch tech, the ranch house, corrals and similar). See
// docs/behavior/painted-objects.md. The colour mask's red, green and blue channels mark eight
// regions (red, green, blue, black, magenta, yellow, cyan, white); each region has a dark and a
// light colour, blended by a paint-stroke texture. Ambient occlusion and the override decals can come
// from the mesh's second UV set.
shader_type spatial;
render_mode cull_back, diffuse_lambert, specular_schlick_ggx;

uniform sampler2D color_mask : hint_default_black, filter_linear_mipmap, repeat_enable;
uniform sampler2D ambient_occlusion : hint_default_white, filter_linear_mipmap, repeat_enable;
uniform bool ao_uv2 = false;
uniform sampler2D paint_strokes : hint_default_white, filter_linear_mipmap, repeat_enable;
uniform bool has_strokes = false;
uniform bool strokes_triplanar = false;
uniform vec4 strokes_st = vec4(1.0, 1.0, 0.0, 0.0);
uniform sampler2D override_tex : source_color, hint_default_transparent, filter_linear_mipmap, repeat_enable;
uniform bool has_override = false;
uniform bool override_uv2 = false;
uniform sampler2D glow_ramp : source_color, hint_default_black, filter_linear, repeat_enable;
uniform float glow_multiplier = 1.0;
// Dark and light colour per region (already linear), in this order:
// red, green, blue, black, magenta, yellow, cyan, white.
uniform vec4 dark_colors[8];
uniform vec4 light_colors[8];

varying vec3 object_pos;
varying vec3 object_normal;

void vertex() {
	object_pos = VERTEX;
	object_normal = NORMAL;
}

float strokes(vec2 uv) {
	if (!has_strokes) {
		return 0.5;
	}
	if (!strokes_triplanar) {
		return texture(paint_strokes, uv * strokes_st.xy + strokes_st.zw).r;
	}
	vec3 w = pow(abs(normalize(object_normal)), vec3(4.0));
	w /= max(w.x + w.y + w.z, 1e-4);
	return texture(paint_strokes, object_pos.zy * strokes_st.xy).r * w.x
		+ texture(paint_strokes, object_pos.xz * strokes_st.xy).r * w.y
		+ texture(paint_strokes, object_pos.xy * strokes_st.xy).r * w.z;
}

void fragment() {
	vec4 mask = texture(color_mask, UV);
	float s = strokes(UV);
	float r = mask.r, g = mask.g, b = mask.b;
	// Weight of each region: how close the mask is to that corner of the colour cube.
	float wt[8] = float[8](
		r * (1.0 - g) * (1.0 - b),
		(1.0 - r) * g * (1.0 - b),
		(1.0 - r) * (1.0 - g) * b,
		(1.0 - r) * (1.0 - g) * (1.0 - b),
		r * (1.0 - g) * b,
		r * g * (1.0 - b),
		(1.0 - r) * g * b,
		r * g * b);
	vec3 col = vec3(0.0);
	for (int i = 0; i < 8; i++) {
		col += wt[i] * mix(dark_colors[i].rgb, light_colors[i].rgb, s);
	}
	if (has_override) {
		vec4 o = texture(override_tex, override_uv2 ? UV2 : UV);
		col = mix(col, o.rgb, o.a);
	}
	col *= texture(ambient_occlusion, ao_uv2 ? UV2 : UV).r;
	ALBEDO = col;
	// Glowing parts are where the mask's alpha drops below one.
	EMISSION = (1.0 - mask.a) * texture(glow_ramp, vec2(0.5, 0.5)).rgb * glow_multiplier;
	ROUGHNESS = 0.8;
	SPECULAR = 0.3;
}
