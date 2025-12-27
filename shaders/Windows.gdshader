shader_type spatial;
render_mode blend_add, cull_back, depth_prepass_alpha, vertex_lighting, specular_disabled, fog_disabled;

// --- Сетка и рамки ---
uniform vec2 window_tiling = vec2(10.0, 20.0);
uniform vec2 window_padding = vec2(0.1, 0.1);
uniform float edge_softness = 0.05;
uniform float parallax_depth = 0.05;

// --- Цвета (только свечение) ---
uniform vec4 emission_color : source_color = vec4(1.0, 0.75, 0.4, 1.0);
uniform float emission_energy = 12.0;

// --- Случайность/вариации ---
uniform float lit_probability : hint_range(0.0, 1.0) = 0.7;
uniform float min_dark_fraction : hint_range(0.0, 1.0) = 0.2;
uniform float brightness_variation : hint_range(0.0, 1.0) = 0.3;
uniform float color_variation : hint_range(0.0, 1.0) = 0.2;
uniform float size_variation : hint_range(0.0, 1.0) = 0.3;

// --- Панорамные блоки ---
uniform float panorama_probability : hint_range(0.0, 1.0) = 0.25;
uniform int panorama_max_width : hint_range(1,4) = 3;
uniform int panorama_max_height : hint_range(1,4) = 2;

// --- Супер панорамы (залы/лестницы) ---
uniform float mega_block_probability : hint_range(0.0,1.0) = 0.05;
uniform ivec2 mega_block_size = ivec2(4,4);

// --- Привязка к сетке ---
uniform bool snap_blocks_to_grid = true;

// --- Киберпанк-тусклые ---
uniform float dim_probability : hint_range(0.0, 1.0) = 0.2;
uniform float dim_min : hint_range(0.0, 1.0) = 0.3;
uniform float dim_flicker_speed : hint_range(0.0,10.0) = 1.5;

// --- Random seat от мира ---
uniform float world_random_scale = 37.0;

// --- Кластеризация по дистанции ---
uniform float cluster_distance_strength : hint_range(0.0, 1.0) = 0.5;

// --- Режим тайлинга ---
uniform int tiling_mode : hint_range(0,2) = 0;

// --- Пульсация ---
uniform float pulse_intensity : hint_range(0.0,1.0) = 0.3;
uniform float pulse_speed : hint_range(0.1,10.0) = 2.0;

// --- Вечернее включение ---
uniform float evening_progress : hint_range(0.0,1.0) = 1.0;

// --- Fade ---
uniform float fade_start = 500.0;
uniform float fade_end   = 1500.0;
uniform float fade_min   = 0.05;

// --- Шум ---
uniform sampler2D noise_tex : filter_linear, repeat_enable;

float rand_from_tex(vec2 id) {
    return texture(noise_tex, fract(id / 256.0)).r;
}

void fragment() {
    // --- Дистанция ---
    float dist = distance(CAMERA_POSITION_WORLD.xyz, NODE_POSITION_WORLD.xyz);
    float t = clamp((dist - fade_start) / (fade_end - fade_start), 0.0, 1.0);
    float core_fade = mix(1.0, fade_min, pow(t, 1.5));

    // --- Сетка ---
    vec2 scaled_uv = UV * window_tiling;
    vec2 cell_id   = floor(scaled_uv);
    vec2 block_size = vec2(1.0);

    // --- Mega-блоки ---
    if (rand_from_tex(cell_id*991.0) < mega_block_probability) {
        block_size = vec2(mega_block_size);
        cell_id = floor(cell_id / block_size);
    } else {
        // --- Панорамные блоки ---
        float pano_rand = rand_from_tex(cell_id * 77.1);
        if (pano_rand < panorama_probability) {
            float wx = floor(rand_from_tex(cell_id * 41.3) * float(panorama_max_width)) + 1.0;
            float wy = floor(rand_from_tex(cell_id * 83.7) * float(panorama_max_height)) + 1.0;
            block_size = vec2(wx, wy);
            if (snap_blocks_to_grid) {
                cell_id = floor(cell_id / block_size);
            }
        }
    }

    // UV внутри блока
    vec2 block_uv = fract(scaled_uv / block_size);

    // --- Вариация размеров ---
    float rand_x = rand_from_tex(cell_id * 13.1);
    float rand_y = rand_from_tex(cell_id * 29.7);
    vec2 scale_factor = vec2(
        mix(1.0, rand_x * 1.5, size_variation),
        mix(1.0, rand_y * 1.5, size_variation)
    );
    vec2 centered_uv = (block_uv - 0.5);
    vec2 modified_uv = centered_uv / scale_factor + 0.5;

    // Parallax
    vec2 parallax_offset = (modified_uv - 0.5) * VIEW.xy * parallax_depth;
    vec2 parallax_uv = modified_uv + parallax_offset;

    // Маска окна
    vec2 border = smoothstep(window_padding, window_padding + edge_softness, parallax_uv) *
                  smoothstep(window_padding, window_padding + edge_softness, 1.0 - parallax_uv);
    float window_mask = border.x * border.y;

    // --- Random Seed (world + cluster) ---
    vec2 world_seed = floor(NODE_POSITION_WORLD.xz / world_random_scale);
    float cluster_factor = mix(1.0, 3.0, t * cluster_distance_strength);
    vec2 clustered_id = floor(cell_id / cluster_factor);
    vec2 rnd_id = clustered_id + world_seed * 13.37;

    if (tiling_mode == 1) rnd_id = vec2(0.0, clustered_id.y + world_seed.y);
    else if (tiling_mode == 2) rnd_id = vec2(clustered_id.x + world_seed.x, 0.0);

    // --- ON/OFF окон ---
    float rnd = rand_from_tex(rnd_id * 17.123);
    float lit = step(min_dark_fraction, rnd) * step(rnd, lit_probability);

    // вечернее включение
    float switch_time = rand_from_tex(rnd_id * 33.77);
    float on_factor = smoothstep(switch_time, switch_time + 0.2, evening_progress);
    lit *= on_factor;

    // --- Оттенки+яркость ---
    float bright = 1.0 + (rnd - 0.5) * 2.0 * brightness_variation;
    vec3 tint = vec3(
        1.0 + (rand_from_tex(rnd_id + vec2(11.1,0.0)) - 0.5) * 2.0 * color_variation,
        1.0 + (rand_from_tex(rnd_id + vec2(57.3,0.0)) - 0.5) * 2.0 * color_variation,
        1.0 + (rand_from_tex(rnd_id + vec2(91.7,0.0)) - 0.5) * 2.0 * color_variation
    );

    // --- Тусклые киберпанк-окна ---
    float dim_rand = rand_from_tex(rnd_id * 123.45);
    float dim_factor = 1.0;
    if (dim_rand < dim_probability) {
        float local_dim = mix(dim_min, 1.0, rand_from_tex(rnd_id * 456.78));
        dim_factor = local_dim;
        if (dim_flicker_speed > 0.01) {
            float flicker = 0.9 + 0.1 * sin(TIME * dim_flicker_speed * (1.0 + rand_from_tex(rnd_id*777.0) * 2.0));
            dim_factor *= flicker;
        }
    }
    float bright_final = bright * dim_factor;

    // Пульсация
    float pulse_phase = rand_from_tex(rnd_id + vec2(33.3,0.0)) * 6.2831;
    float pulse = 1.0 + sin(TIME * pulse_speed + pulse_phase) * 0.5 * pulse_intensity;

    // --- Итоговый цвет ---
    vec3 window_col = emission_color.rgb * tint * bright_final * lit * pulse;
    vec3 fog_tint = vec3(0.6, 0.7, 1.0);
    vec3 volumetric_col = mix(window_col, fog_tint * window_col, t * 0.5);

    // Усиление на расстоянии
    float distance_boost = 1.0 + pow(t, 1.5) * 2.5;
    float tail = mix(1.0, fade_min, t) * 0.5;
    vec3 final_emission = volumetric_col * emission_energy * (distance_boost + tail);

    // --- Результат ---
    ALBEDO = vec3(0.0);
    EMISSION = window_mask * final_emission;
    ALPHA = window_mask * lit * core_fade;
}