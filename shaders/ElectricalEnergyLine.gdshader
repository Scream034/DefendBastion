shader_type spatial;
render_mode blend_add, depth_draw_always, cull_disabled, shadows_disabled, fog_disabled;

// === Цвета и свечение ===
uniform vec3 emission_base_color : source_color = vec3(0.2, 0.8, 1.0);
uniform vec3 emission_glow_color : source_color = vec3(1.0, 1.0, 1.0);
uniform float emission_energy : hint_range(0.0, 30.0) = 12.0;

// === Сетка ===
uniform float grid_cols : hint_range(1, 200) = 40.0;
uniform float grid_rows : hint_range(1, 200) = 40.0;
uniform float line_thickness_x : hint_range(0.001, 0.2) = 0.02;
uniform float line_thickness_y : hint_range(0.001, 0.2) = 0.02;

uniform bool show_columns = true;
uniform bool show_rows = true;

// === Glow-анимация ===
uniform float glow_speed : hint_range(0.1, 10.0) = 2.0;
uniform float glow_width : hint_range(0.01, 1.0) = 0.15;
uniform float glow_intensity : hint_range(0.1, 5.0) = 1.5;

// === Живое поведение ===
uniform float variety : hint_range(0.0, 1.0) = 0.6;     // общий разброс
uniform float outage_chance : hint_range(0.0, 1.0) = 0.2; // вероятность перебоев

// === Доп. эффекты ===
uniform float breathe_speed : hint_range(0.01, 2.0) = 0.3;
uniform float noise_strength : hint_range(0.0, 1.0) = 0.15;
uniform float glow_distance_cutoff : hint_range(0.0, 2.0) = 0.7;
uniform float aa_sharpness : hint_range(0.5, 4.0) = 1.5;

// === Опции ===
uniform bool enable_distortion = false; // Живое колыхание UV
uniform bool use_albedo = false;        // Цвет плёнки, если true

// =============================
// Вспомогательные функции
// =============================
float hash(float n) {
    return fract(sin(n) * 43758.5453);
}

// Линия с fwidth-антиалиасингом
float lineAA(float coord, float thickness) {
    float fw = fwidth(coord) * aa_sharpness;
    return 1.0 - smoothstep(0.5 - thickness - fw, 0.5 + thickness + fw, coord);
}

// Сетка
float gridMask(vec2 uv, float cols, float rows, float tx, float ty) {
    vec2 g = fract(vec2(uv.x * cols, uv.y * rows));
    float lineX = show_columns ? lineAA(g.x, tx) : 0.0;
    float lineY = show_rows    ? lineAA(g.y, ty) : 0.0;
    return clamp(lineX + lineY, 0.0, 1.0);
}

// Бегущая энергия с вариативностью
float runningGlowVaried(vec2 uv, float cols, float rows, float base_width, float base_speed, float t) {
    float id = floor(uv.x * cols + uv.y * rows);

    // Разные скорости и яркости (рандом)
    float rnd_speed = mix(0.7, 1.3, hash(id) * variety);
    float rnd_amp   = mix(0.5, 1.5, hash(id + 17.1) * variety);
    float rnd_width = mix(0.7, 1.3, hash(id + 42.7) * variety);

    // Эффект перебоев (энергия локально тухнет)
    float outage_mask = 1.0;
    if (hash(id + 123.45) < outage_chance) {
        outage_mask = 0.4 + 0.6 * abs(sin(t * 0.7 + id)); // периодически тухнет
    }

    float posX = fract(uv.x * cols + t * base_speed * rnd_speed);
    float posY = fract(uv.y * rows + t * base_speed * rnd_speed * 0.7);

    float glowX = exp(-pow((posX - 0.5) / (base_width * rnd_width), 2.0) * 8.0);
    float glowY = exp(-pow((posY - 0.5) / (base_width * rnd_width), 2.0) * 8.0);

    return (glowX + glowY) * rnd_amp * outage_mask;
}

// Шум мягкий
float uvNoise(vec2 uv) {
    float n = sin(dot(uv, vec2(12.9898, 78.233))) * 43758.5453;
    return fract(n);
}

void fragment() {
    float t = TIME;
    vec2 uv_moved = UV;
    if (enable_distortion) {
        uv_moved += vec2(sin(t * 0.3), cos(t * 0.4)) * 0.02;
    }

    // Сетка
    float grid = gridMask(uv_moved, grid_cols, grid_rows, line_thickness_x, line_thickness_y);

    // Энергетические полосы (с вариацией и перебоями)
    float glow = 0.0;
    if (FRAGCOORD.z < glow_distance_cutoff) {
        glow = runningGlowVaried(uv_moved, grid_cols, grid_rows, glow_width, glow_speed, t) * glow_intensity * grid;
        float fade = smoothstep(glow_distance_cutoff * 0.8, glow_distance_cutoff, FRAGCOORD.z);
        glow *= (1.0 - fade);
    }

    // Дополнительная "дыхалка"
    float breathe = sin(t * breathe_speed) * 0.25 + 0.75;

    // Рябь через шум
    float noise_mod = mix(1.0 - noise_strength, 1.0 + noise_strength,
                          uvNoise(uv_moved + t * 0.05));

    float total_energy = (grid + glow) * breathe * noise_mod;
    total_energy = clamp(total_energy, 0.0, 1.0);

    // Цвет эмиссии
    vec3 animated_color = mix(emission_base_color, emission_glow_color, glow);
    EMISSION = animated_color * emission_energy * total_energy;

    // Опциональный ALBEDO — если хочешь видеть "плёнку", а не только glow
    if (use_albedo) {
        ALBEDO = animated_color * 0.1; // приглушённый базовый цвет
    } else {
        ALBEDO = vec3(0.0); // невидимое основание
    }

    // Прозрачность плёнки
    ALPHA = total_energy;
}