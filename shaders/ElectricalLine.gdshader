shader_type spatial;
render_mode blend_add, depth_draw_never, cull_disabled, diffuse_lambert, specular_disabled, vertex_lighting, shadows_disabled, fog_disabled;

// Основные цвета электромагнитного снаряда
uniform vec4 core_color : source_color = vec4(0.4, 0.8, 1.0, 1.0); // Голубое ядро
uniform vec4 outer_color : source_color = vec4(0.2, 0.4, 1.0, 1.0); // Синяя оболочка
uniform float energy_intensity : hint_range(1.0, 20.0) = 10.0;

// Электрические импульсы
uniform float pulse_speed : hint_range(1.0, 20.0) = 15.0;
uniform float electric_noise_scale : hint_range(0.1, 5.0) = 2.0;

// Трассер
uniform float trail_length : hint_range(0.0, 2.0) = 1.0;
uniform float trail_intensity : hint_range(0.0, 5.0) = 2.0;

void vertex() {
	// Электрические искажения вершин
	float electric_pulse = sin(TIME * pulse_speed + VERTEX.z * 10.0) * 0.1;
	VERTEX.xy += NORMAL.xy * electric_pulse;
}

void fragment() {
	// Расстояние от центра для градиента
	vec2 center_uv = UV - vec2(0.5);
	float dist_from_center = length(center_uv);
	
	// Ядро снаряда - максимально яркое
	float core = 1.0 - smoothstep(0.0, 0.3, dist_from_center);
	core = pow(core, 0.5);
	
	// Внешнее свечение
	float outer_glow = 1.0 - smoothstep(0.2, 1.0, dist_from_center);
	
	// Электрические импульсы
	float time_offset = TIME * pulse_speed;
	float electric_pulse = sin(time_offset) * 0.3 + 0.7;
	electric_pulse += sin(time_offset * 3.7) * 0.2;
	electric_pulse += sin(time_offset * 7.3) * 0.1;
	
	// Электрический шум для реалистичности
	float noise = sin(UV.x * electric_noise_scale + time_offset) * 
	              sin(UV.y * electric_noise_scale - time_offset * 0.7);
	noise = noise * 0.5 + 0.5;
	
	// Комбинируем цвета
	vec3 final_color = core_color.rgb * core * energy_intensity;
	final_color += outer_color.rgb * outer_glow * energy_intensity * 0.5;
	final_color *= electric_pulse;
	final_color += outer_color.rgb * noise * outer_glow * 2.0;
	
	// Эффект трассера - усиливаем свечение сзади
	float trail_effect = UV.x * trail_length;
	final_color += outer_color.rgb * trail_effect * trail_intensity;
	
	// Rim эффект для видимости в тумане
	vec3 view_dir = normalize(CAMERA_POSITION_WORLD - VERTEX);
	float rim = 1.0 - dot(NORMAL, view_dir);
	rim = pow(rim, 1.5);
	final_color += core_color.rgb * rim * energy_intensity * 0.5;
	
	// Финальный вывод
	EMISSION = final_color;
	ALBEDO = vec3(0.0); // Только эмиссия, без альбедо
	ALPHA = clamp(core + outer_glow * 0.5, 0.0, 1.0);
}