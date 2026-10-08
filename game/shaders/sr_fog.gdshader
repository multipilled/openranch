// Distance fog as the original draws it (docs/behavior/day-and-night.md): exponential in the
// distance along the view direction, blended in sRGB values (the original renders in gamma space),
// and never on the sky. Drawn as a full-screen quad after the opaque scene.
shader_type spatial;
render_mode unshaded, cull_disabled, depth_test_disabled, depth_draw_never, shadows_disabled, fog_disabled;

uniform sampler2D screen_tex : hint_screen_texture, filter_nearest, repeat_disable;
uniform sampler2D depth_tex : hint_depth_texture, filter_nearest, repeat_disable;
uniform vec3 fog_color : source_color;
uniform float density = 0.005;

void vertex() {
	POSITION = vec4(VERTEX.xy * 2.0, 0.0, 1.0);
}

vec3 to_srgb(vec3 c) {
	return mix(12.92 * c, 1.055 * pow(c, vec3(1.0 / 2.4)) - 0.055, step(vec3(0.0031308), c));
}

vec3 to_linear(vec3 c) {
	return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
}

void fragment() {
	vec3 scene = texture(screen_tex, SCREEN_UV).rgb;
	float depth = texture(depth_tex, SCREEN_UV).r;
	if (depth <= 0.0) {
		// Reverse depth: 0 is the far plane, where only the sky is.
		ALBEDO = scene;
	} else {
		vec4 view = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, depth, 1.0);
		float distance = -view.z / view.w;
		float keep = exp(-density * max(distance, 0.0));
		ALBEDO = to_linear(mix(to_srgb(fog_color), to_srgb(scene), keep));
	}
	ALPHA = 1.0;
}
