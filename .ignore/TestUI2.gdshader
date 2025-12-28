shader_type canvas_item;

// === ПРОГРЕСС ===
uniform float progress : hint_range(0.0, 1.0) = 1.0;
uniform float damage_ghost : hint_range(0.0, 1.0) = 1.0; // "призрак" предыдущего HP
uniform float dissolve_amount : hint_range(0.0, 1.0) = 0.0; // анимация распада урона

// === СЕГМЕНТЫ ===
uniform int segments : hint_range(1, 50) = 10;
uniform float segment_gap : hint_range(0.0, 0.3) = 0.08;
uniform float segment_bevel : hint_range(0.0, 0.5) = 0.15; // скос углов сегмента

// === ЦВЕТА ===
uniform vec4 color_full : source_color = vec4(0.2, 1.0, 0.8, 1.0);
uniform vec4 color_mid : source_color = vec4(1.0, 0.8, 0.2, 1.0);
uniform vec4 color_low : source_color = vec4(1.0, 0.2, 0.2, 1.0);
uniform vec4 color_glow : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform vec4 color_empty : source_color = vec4(0.05, 0.1, 0.15, 0.4);
uniform vec4 color_damage : source_color = vec4(1.0, 0.1, 0.0, 1.0);

// === ЭНЕРГИЯ ===
uniform float energy_speed : hint_range(0.0, 8.0) = 3.0;
uniform float energy_width : hint_range(0.05, 0.5) = 0.2;
uniform float pulse_speed : hint_range(0.0, 5.0) = 1.5;
uniform float pulse_intensity : hint_range(0.0, 0.5) = 0.2;

// === ГЛИТЧ ===
uniform float glitch_intensity : hint_range(0.0, 1.0) = 0.0;
uniform float scan_line_density : hint_range(0.0, 200.0) = 60.0;
uniform float flicker_speed : hint_range(0.0, 20.0) = 8.0;

// === ГРАНИЦА ===
uniform float border_thickness : hint_range(0.0, 0.1) = 0.02;
uniform vec4 border_color : source_color = vec4(0.3, 0.8, 1.0, 0.8);

// =====================
// ВСПОМОГАТЕЛЬНЫЕ
// =====================

float hash(float n) {
    return fract(sin(n) * 43758.5453);
}

float hash2(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

// Noise для dissolve
float noise(vec2 uv) {
    vec2 i = floor(uv);
    vec2 f = fract(uv);
    f = f * f * (3.0 - 2.0 * f);
    
    float a = hash2(i);
    float b = hash2(i + vec2(1.0, 0.0));
    float c = hash2(i + vec2(0.0, 1.0));
    float d = hash2(i + vec2(1.0, 1.0));
    
    return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

// Фрактальный шум для красивого распада
float fbm(vec2 uv) {
    float value = 0.0;
    float amplitude = 0.5;
    for (int i = 0; i < 4; i++) {
        value += amplitude * noise(uv);
        uv *= 2.0;
        amplitude *= 0.5;
    }
    return value;
}

void fragment() {
    float t = TIME;
    vec2 uv = UV;
    
    // === ГЛИТЧ СМЕЩЕНИЕ UV ===
    float glitch_offset = 0.0;
    if (glitch_intensity > 0.0) {
        float glitch_time = floor(t * flicker_speed);
        float glitch_random = hash(glitch_time + uv.y * 10.0);
        if (glitch_random > 1.0 - glitch_intensity * 0.3) {
            glitch_offset = (hash(glitch_time * 2.0) - 0.5) * glitch_intensity * 0.1;
        }
        uv.x += glitch_offset;
    }
    
    // === ОПРЕДЕЛЕНИЕ СЕГМЕНТА ===
    float seg_width = 1.0 / float(segments);
    float seg_index = floor(uv.x / seg_width);
    float seg_local_x = fract(uv.x / seg_width); // 0-1 внутри сегмента
    float seg_normalized = (seg_index + 0.5) / float(segments); // центр сегмента 0-1
    
    // Gap между сегментами
    float half_gap = segment_gap * 0.5;
    bool in_gap = seg_local_x < half_gap || seg_local_x > (1.0 - half_gap);
    
    // Скос углов (шестиугольная форма)
    float bevel_mask = 1.0;
    if (segment_bevel > 0.0) {
        float edge_dist_x = min(seg_local_x - half_gap, (1.0 - half_gap) - seg_local_x);
        float edge_dist_y = min(uv.y, 1.0 - uv.y);
        float corner = edge_dist_x + edge_dist_y * (1.0 / segment_bevel);
        bevel_mask = smoothstep(0.0, 0.02, corner - segment_bevel * 0.3);
    }
    
    // === СТАТУС СЕГМЕНТА ===
    bool is_filled = seg_normalized <= progress;
    bool is_damage_zone = seg_normalized > progress && seg_normalized <= damage_ghost;
    bool is_empty = seg_normalized > damage_ghost;
    
    // === ЦВЕТ ПО УРОВНЮ ПРОЧНОСТИ ===
    vec4 health_color;
    if (progress > 0.5) {
        health_color = mix(color_mid, color_full, (progress - 0.5) * 2.0);
    } else if (progress > 0.25) {
        health_color = mix(color_low, color_mid, (progress - 0.25) * 4.0);
    } else {
        health_color = color_low;
    }
    
    // === БЕГУЩАЯ ЭНЕРГИЯ ===
    float energy_pos = fract(t * energy_speed * 0.3);
    float energy_dist = abs(uv.x - energy_pos);
    energy_dist = min(energy_dist, 1.0 - energy_dist); // wrap
    float energy_wave = exp(-energy_dist * energy_dist / (energy_width * energy_width) * 10.0);
    
    // Вторичная волна (обратная)
    float energy_pos2 = fract(-t * energy_speed * 0.2 + 0.5);
    float energy_dist2 = abs(uv.x - energy_pos2);
    energy_dist2 = min(energy_dist2, 1.0 - energy_dist2);
    float energy_wave2 = exp(-energy_dist2 * energy_dist2 / (energy_width * energy_width) * 10.0) * 0.5;
    
    float total_energy = (energy_wave + energy_wave2);
    
    // Пульсация каждого сегмента (индивидуальная)
    float seg_pulse = sin(t * pulse_speed + seg_index * 0.7) * 0.5 + 0.5;
    seg_pulse = mix(1.0, 1.0 + pulse_intensity, seg_pulse);
    
    // === DISSOLVE ЭФФЕКТ ДЛЯ УРОНА ===
    float dissolve_noise = fbm(uv * 15.0 + t * 0.5);
    float dissolve_edge = dissolve_amount * 1.2;
    float dissolve_mask = smoothstep(dissolve_edge - 0.1, dissolve_edge, dissolve_noise);
    
    // Цифровые артефакты на границе распада
    float edge_glow = 0.0;
    if (is_damage_zone) {
        float edge_zone = abs(dissolve_noise - dissolve_edge);
        edge_glow = exp(-edge_zone * 30.0) * (1.0 - dissolve_mask);
    }
    
    // === SCANLINES ===
    float scanline = sin(uv.y * scan_line_density + t * 2.0) * 0.5 + 0.5;
    scanline = mix(0.9, 1.0, scanline);
    
    // === ФИНАЛЬНЫЙ ЦВЕТ ===
    vec4 final_color = vec4(0.0);
    
    if (in_gap || bevel_mask < 0.5) {
        // Промежуток между сегментами
        final_color = vec4(0.0);
    } 
    else if (is_filled) {
        // Заполненный сегмент
        vec4 base = health_color;
        base.rgb *= seg_pulse;
        base.rgb = mix(base.rgb, color_glow.rgb, total_energy * 0.4);
        base.rgb *= scanline;
        
        // Внутреннее свечение
        float inner_glow = 1.0 - abs(uv.y - 0.5) * 2.0;
        inner_glow = pow(inner_glow, 2.0) * 0.3;
        base.rgb += health_color.rgb * inner_glow;
        
        final_color = base;
    }
    else if (is_damage_zone) {
        // Зона урона — распадающиеся сегменты
        if (dissolve_mask > 0.5) {
            // Уже растворилось
            final_color = color_empty * 0.5;
        } else {
            // Ещё видно, но с glitch
            vec4 damage_base = color_damage;
            damage_base.rgb *= (0.7 + 0.3 * sin(t * 10.0 + seg_index));
            
            // Цифровой шум
            float digital_noise = step(0.7, hash(floor(uv.y * 20.0) + t * 5.0 + seg_index));
            damage_base.rgb = mix(damage_base.rgb, vec3(1.0), digital_noise * 0.5);
            
            // Edge glow
            damage_base.rgb += vec3(1.0, 0.5, 0.0) * edge_glow * 2.0;
            
            final_color = damage_base;
        }
    }
    else {
        // Пустой сегмент
        vec4 empty = color_empty;
        // empty.rgb *= scanline * 0.5;
        
        // Лёгкий контур
        // float outline = 1.0 - smoothstep(0.0, 0.1, min(
        //     min(seg_local_x - half_gap, (1.0 - half_gap) - seg_local_x),
        //     min(uv.y, 1.0 - uv.y)
        // ));
        // empty.rgb += border_color.rgb * outline * 0.3;
        
        final_color = empty;
    }
    
    // === ГРАНИЦА ВОКРУГ ВСЕГО БАРА ===
    // float border_mask = 0.0;
    // if (uv.x < border_thickness || uv.x > 1.0 - border_thickness ||
    //     uv.y < border_thickness || uv.y > 1.0 - border_thickness) {
    //     border_mask = 1.0;
    // }
    // final_color.rgb = mix(final_color.rgb, border_color.rgb, border_mask * border_color.a);
    
    // === ГЛИТЧ ЦВЕТОВОЕ РАЗДЕЛЕНИЕ ===
    if (glitch_intensity > 0.0 && abs(glitch_offset) > 0.001) {
        // RGB split
        final_color.r = mix(final_color.r, final_color.r * 1.5, glitch_intensity);
        final_color.b = mix(final_color.b, final_color.b * 0.5, glitch_intensity);
    }
    
    // === КРИТИЧЕСКИЙ РЕЖИМ (низкое HP) ===
    if (progress < 0.25) {
        float alarm = sin(t * 8.0) * 0.5 + 0.5;
        final_color.rgb = mix(final_color.rgb, color_low.rgb, alarm * 0.3);
    }
    
    COLOR = final_color;
}