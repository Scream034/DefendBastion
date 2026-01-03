shader_type canvas_item;

// === РАЗМЕР (передаётся из кода) ===
uniform vec2 rect_size = vec2(180.0, 120.0);

// === ПАРАМЕТРЫ ===
uniform float border_width : hint_range(1.0, 4.0) = 1.5;
uniform float dash_length : hint_range(4.0, 20.0) = 10.0;
uniform float gap_length : hint_range(2.0, 15.0) = 6.0;
uniform float animation_speed : hint_range(0.0, 80.0) = 25.0;

// === УГЛОВЫЕ СРЕЗЫ ===
uniform float corner_cut : hint_range(0.0, 30.0) = 12.0;  // Размер срезанного угла

// === ЦВЕТА (циановая палитра) ===
uniform vec4 border_color : source_color = vec4(0.2, 0.9, 0.8, 0.8);
uniform vec4 background_color : source_color = vec4(0.0, 0.08, 0.1, 0.25);
uniform vec4 corner_accent_color : source_color = vec4(0.3, 1.0, 0.9, 1.0);

// === ЭФФЕКТЫ ===
uniform float glow_intensity : hint_range(0.0, 1.0) = 0.3;
uniform float pulse_intensity : hint_range(0.0, 0.3) = 0.08;
uniform float pulse_speed : hint_range(0.0, 8.0) = 2.5;

// === СОСТОЯНИЕ ===
uniform float alert_level : hint_range(0.0, 1.0) = 0.0;
uniform vec4 alert_color : source_color = vec4(1.0, 0.3, 0.15, 0.9);

// SDF для прямоугольника со срезанными углами
float sdCutBox(vec2 p, vec2 size, float cut) {
    vec2 q = abs(p) - size;
    
    // Срезаем углы: проверяем диагональ в углах
    vec2 corner = abs(p) - (size - cut);
    if (corner.x > 0.0 && corner.y > 0.0) {
        // Мы в зоне угла - используем диагональную линию
        float diagonal = (corner.x + corner.y - cut) * 0.7071; // 1/sqrt(2)
        return max(max(q.x, q.y), diagonal);
    }
    
    return max(q.x, q.y);
}

void fragment() {
    vec2 uv_pixel = UV * rect_size;
    vec2 center = rect_size * 0.5;
    vec2 p = uv_pixel - center;
    
    // Внутренний размер (с отступом для рамки)
    vec2 inner_size = center - vec2(border_width + 2.0);
    
    // SDF
    float box_dist = sdCutBox(p, inner_size, corner_cut);
    
    // Пульсация
    float pulse = 1.0 + sin(TIME * pulse_speed) * pulse_intensity;
    
    // Цвет с тревогой
    vec4 current_border = mix(border_color, alert_color, alert_level);
    vec4 current_accent = mix(corner_accent_color, alert_color, alert_level);
    
    vec4 final_color = vec4(0.0);
    
    // === ФОН ===
    float bg_mask = 1.0 - smoothstep(-1.0, 0.5, box_dist);
    final_color = background_color * bg_mask;
    
    // === СВЕЧЕНИЕ РАМКИ ===
    if (glow_intensity > 0.01) {
        float glow_dist = 4.0;
        float glow = 1.0 - smoothstep(0.0, glow_dist, abs(box_dist));
        glow *= glow_intensity * pulse;
        final_color += current_border * glow * 0.3;
    }
    
    // === ПУНКТИРНАЯ РАМКА ===
    float border_mask = 1.0 - smoothstep(0.0, border_width, abs(box_dist));
    
    if (border_mask > 0.01) {
        // Вычисляем позицию вдоль периметра
        float perimeter_pos = 0.0;
        
        // Определяем сторону и позицию на ней
        vec2 abs_p = abs(p);
        vec2 corner_zone = inner_size - corner_cut;
        
        // Проверяем срезанные углы
        bool in_corner = abs_p.x > corner_zone.x && abs_p.y > corner_zone.y;
        
        if (in_corner) {
            // На диагонали угла
            vec2 corner_vec = abs_p - corner_zone;
            perimeter_pos = corner_vec.x + corner_vec.y;
            
            // Добавляем смещение предыдущих сторон
            float side_length = inner_size.x + inner_size.y - corner_cut;
            if (p.x > 0.0 && p.y < 0.0) perimeter_pos += 0.0;
            else if (p.x > 0.0 && p.y > 0.0) perimeter_pos += side_length;
            else if (p.x < 0.0 && p.y > 0.0) perimeter_pos += side_length * 2.0;
            else perimeter_pos += side_length * 3.0;
        }
        else if (abs_p.x > abs_p.y * (inner_size.x / inner_size.y)) {
            // Вертикальные стороны
            if (p.x > 0.0) {
                perimeter_pos = inner_size.y - corner_cut + (p.y + corner_zone.y);
            } else {
                perimeter_pos = (inner_size.x + inner_size.y) * 2.0 - corner_cut * 2.0 + (corner_zone.y - p.y);
            }
        }
        else {
            // Горизонтальные стороны
            if (p.y < 0.0) {
                perimeter_pos = (p.x + corner_zone.x);
            } else {
                perimeter_pos = (inner_size.x + inner_size.y) - corner_cut + (corner_zone.x - p.x);
            }
        }
        
        // Анимация пунктира
        float animated = perimeter_pos + TIME * animation_speed;
        float cycle = dash_length + gap_length;
        float in_cycle = mod(animated, cycle);
        
        // Мягкие края пунктира
        float dash = smoothstep(0.0, 1.0, in_cycle) * 
                     smoothstep(dash_length, dash_length - 1.0, in_cycle);
        
        // Применяем
        vec4 dash_color = current_border * pulse;
        dash_color.a *= border_mask * dash;
        
        final_color = mix(final_color, dash_color, dash_color.a);
    }
    
    // === УГЛОВЫЕ АКЦЕНТЫ (яркие точки на срезах) ===
    float accent_size = 3.0;
    
    // 4 угла со срезами
    vec2 corners[4];
    corners[0] = vec2(inner_size.x - corner_cut * 0.5, -inner_size.y + corner_cut * 0.5);
    corners[1] = vec2(inner_size.x - corner_cut * 0.5, inner_size.y - corner_cut * 0.5);
    corners[2] = vec2(-inner_size.x + corner_cut * 0.5, inner_size.y - corner_cut * 0.5);
    corners[3] = vec2(-inner_size.x + corner_cut * 0.5, -inner_size.y + corner_cut * 0.5);
    
    for (int i = 0; i < 4; i++) {
        float d = length(p - corners[i]);
        if (d < accent_size) {
            float accent = (1.0 - d / accent_size);
            accent = accent * accent * pulse;
            final_color = mix(final_color, current_accent, accent * 0.8);
        }
    }
    
    // === ВНУТРЕННИЕ УГЛОВЫЕ ЛИНИИ (декор) ===
    float inner_line_len = 8.0;
    float inner_line_offset = 3.0;
    
    for (int i = 0; i < 4; i++) {
        vec2 corner_pos = corners[i];
        vec2 dir = -normalize(corner_pos);  // Направление к центру
        
        // Маленькая линия от угла к центру
        vec2 line_start = corner_pos + dir * inner_line_offset;
        vec2 line_end = line_start + dir * inner_line_len;
        
        // Проверяем расстояние до линии
        vec2 pa = p - line_start;
        vec2 ba = line_end - line_start;
        float h = clamp(dot(pa, ba) / dot(ba, ba), 0.0, 1.0);
        float d = length(pa - ba * h);
        
        if (d < 1.0) {
            float line_alpha = (1.0 - d) * 0.4 * pulse;
            final_color = mix(final_color, current_accent, line_alpha);
        }
    }
    
    COLOR = final_color;
}