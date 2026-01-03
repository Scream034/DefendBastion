shader_type canvas_item;

// СОСТОЯНИЕ
uniform float spread : hint_range(-20.0, 100.0) = 0.0;
uniform float diamond_rotation : hint_range(-6.28, 6.28) = 0.0;
uniform float yaw_degrees : hint_range(-360.0, 360.0) = 0.0;
uniform float pitch_degrees : hint_range(-90.0, 90.0) = 0.0;
uniform float zoom_level : hint_range(1.0, 10.0) = 1.0;

uniform int turret_state : hint_range(0, 5) = 0;
uniform float state_time = 0.0;

// ЛИМИТЫ ПОВОРОТА СТВОЛА
uniform float turret_min_pitch : hint_range(-90.0, 90.0) = -0.5;
uniform float turret_max_pitch : hint_range(-90.0, 90.0) = 1.0;
uniform float turret_max_yaw : hint_range(-1.0, 180.0) = 90.0;
uniform bool show_turret_limits = true;

// === ЦЕЛЕВЫЕ УГЛЫ КАМЕРЫ ===
uniform float target_yaw_degrees : hint_range(-360.0, 360.0) = 0.0;
uniform float target_pitch_degrees : hint_range(-90.0, 90.0) = 0.0;
uniform bool show_target_marker = true;
uniform vec4 color_target : source_color = vec4(1.0, 0.9, 0.3, 0.8); // Жёлтый

// ЭФФЕКТЫ ВЫСТРЕЛА
uniform float convergence_intensity : hint_range(0.0, 1.0) = 0.0;
uniform float shot_flash : hint_range(0.0, 2.0) = 0.0;
uniform float impact_ring : hint_range(0.0, 1.0) = 0.0;
uniform float frost_level : hint_range(0.0, 1.0) = 0.0;

// РАЗМЕРЫ
uniform float reticle_gap : hint_range(20.0, 100.0) = 45.0;
uniform float diamond_base_size : hint_range(5.0, 30.0) = 12.0;
uniform float edge_margin : hint_range(20.0, 100.0) = 40.0;
uniform float pixels_per_degree : hint_range(5.0, 100.0) = 12.0;

uniform float tick_small : hint_range(2.0, 15.0) = 10.0;
uniform float tick_medium : hint_range(5.0, 25.0) = 25.0;
uniform float tick_large : hint_range(10.0, 40.0) = 40.0;
uniform float line_width : hint_range(1.0, 5.0) = 2.0;
uniform float center_dot_radius : hint_range(1.0, 5.0) = 1.0;

uniform int minor_interval : hint_range(1, 15) = 5;
uniform int medium_interval : hint_range(5, 30) = 15;
uniform int major_interval : hint_range(15, 90) = 45;

// ЦВЕТА
uniform vec4 color_main : source_color = vec4(0.2, 0.9, 0.8, 0.9);
uniform vec4 color_warning : source_color = vec4(1.0, 0.3, 0.2, 0.9);
uniform vec4 color_cooldown : source_color = vec4(0.4, 0.75, 1.0, 0.9);
uniform vec4 color_shooting : source_color = vec4(0.4, 1.0, 0.9, 1.0);
uniform vec4 color_flash : source_color = vec4(0.6, 1.0, 0.95, 1.0);
uniform vec4 color_frost : source_color = vec4(0.7, 0.9, 1.0, 0.9);
uniform vec4 color_minor : source_color = vec4(0.2, 0.9, 0.8, 0.25);
uniform vec4 color_medium : source_color = vec4(0.2, 0.9, 0.8, 0.5);
uniform vec4 color_limit : source_color = vec4(1.0, 0.5, 0.2, 0.8);

// ФУНКЦИИ

float hash(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float line_dist(vec2 p, vec2 a, vec2 b) {
    vec2 pa = p - a;
    vec2 ba = b - a;
    float h = clamp(dot(pa, ba) / dot(ba, ba), 0.0, 1.0);
    return length(pa - ba * h);
}

float draw_line(vec2 p, vec2 a, vec2 b, float width) {
    float d = line_dist(p, a, b);
    return 1.0 - smoothstep(width * 0.5 - 0.5, width * 0.5 + 0.5, d);
}

float draw_filled_circle(vec2 p, vec2 center, float radius) {
    float d = length(p - center);
    return 1.0 - smoothstep(radius - 0.5, radius + 0.5, d);
}

float draw_circle(vec2 p, vec2 center, float radius, float width) {
    float d = abs(length(p - center) - radius);
    return 1.0 - smoothstep(width * 0.5 - 0.5, width * 0.5 + 0.5, d);
}

float draw_diamond_squeeze(vec2 p, vec2 center, float base_size, float spread_val, float rotation, float width) {
    vec2 rp = p - center;
    float c = cos(-rotation);
    float s = sin(-rotation);
    rp = vec2(rp.x * c - rp.y * s, rp.x * s + rp.y * c);
    
    float squeeze_factor = clamp(1.0 + spread_val / base_size, 0.05, 1.0);
    rp.y *= squeeze_factor;
    
    float size = base_size * squeeze_factor;
    float d = abs(rp.x) + abs(rp.y) - size;
    
    return 1.0 - smoothstep(-width * 0.5, width * 0.5, abs(d) - width * 0.5);
}

float draw_filled_diamond(vec2 p, vec2 center, float size) {
    vec2 rp = abs(p - center);
    float d = rp.x + rp.y - size;
    return 1.0 - smoothstep(-0.5, 0.5, d);
}

float draw_ice_crystal(vec2 p, vec2 center, float size, float rotation) {
    vec2 rp = p - center;
    float c = cos(rotation);
    float s = sin(rotation);
    rp = vec2(rp.x * c - rp.y * s, rp.x * s + rp.y * c);
    float d = max(abs(rp.x) * 0.866 + abs(rp.y) * 0.5, abs(rp.y)) - size;
    return 1.0 - smoothstep(-0.5, 0.5, d);
}

float draw_convergence_line(vec2 p, vec2 center, float angle, float inner_r, float outer_r, float width, float intensity) {
    vec2 dir = vec2(cos(angle), sin(angle));
    float taper_start = outer_r;
    float taper_end = mix(outer_r, inner_r, intensity);
    vec2 start_point = center + dir * taper_start;
    vec2 end_point = center + dir * taper_end;
    float d = line_dist(p, start_point, end_point);
    float dist_along = dot(p - end_point, dir) / (taper_start - taper_end);
    float tapered_width = width * (0.3 + 0.7 * clamp(dist_along, 0.0, 1.0));
    float line_val = 1.0 - smoothstep(tapered_width * 0.5 - 0.5, tapered_width * 0.5 + 0.5, d);
    return line_val * intensity;
}

// Маркер границы - горизонтальный (для YAW)
float draw_limit_bracket_h(vec2 p, float x_pos, float y_center, float size, bool points_right) {
    float dir = points_right ? 1.0 : -1.0;
    float bar = draw_line(p, vec2(x_pos, y_center - size), vec2(x_pos, y_center + size), 3.0);
    float tick_len = min(size * 0.5, 12.0);
    float tick1 = draw_line(p, vec2(x_pos, y_center - size * 0.6), vec2(x_pos + dir * tick_len, y_center - size * 0.6), 2.0);
    float tick2 = draw_line(p, vec2(x_pos, y_center + size * 0.6), vec2(x_pos + dir * tick_len, y_center + size * 0.6), 2.0);
    return max(bar, max(tick1, tick2));
}

// Маркер границы - вертикальный (для PITCH)
float draw_limit_bracket_v(vec2 p, float y_pos, float x_center, float size, bool points_down) {
    float dir = points_down ? 1.0 : -1.0;
    float bar = draw_line(p, vec2(x_center - size, y_pos), vec2(x_center + size, y_pos), 3.0);
    float tick_len = min(size * 0.5, 12.0);
    float tick1 = draw_line(p, vec2(x_center - size * 0.6, y_pos), vec2(x_center - size * 0.6, y_pos + dir * tick_len), 2.0);
    float tick2 = draw_line(p, vec2(x_center + size * 0.6, y_pos), vec2(x_center + size * 0.6, y_pos + dir * tick_len), 2.0);
    return max(bar, max(tick1, tick2));
}

// Треугольник-указатель
float draw_triangle(vec2 p, vec2 tip, float size, float rotation) {
    vec2 rp = p - tip;
    float c = cos(-rotation);
    float s = sin(-rotation);
    rp = vec2(rp.x * c - rp.y * s, rp.x * s + rp.y * c);
    
    // Треугольник с вершиной в (0,0), направлен вверх
    // Смещаем чтобы tip была вершиной
    rp.y += size * 0.5;
    
    // Проверка попадания в треугольник
    float h = size * 0.866; // высота равностороннего треугольника
    float half_base = size * 0.5;
    
    // Три условия для трёх сторон
    float d1 = rp.y; // верхняя граница
    float d2 = rp.y + (rp.x + half_base) * 1.732; // левая сторона  
    float d3 = rp.y + (-rp.x + half_base) * 1.732; // правая сторона
    
    float d = max(max(-d1, d1 - h), max(d2 - h * 2.0, d3 - h * 2.0));
    
    return 1.0 - smoothstep(-1.0, 1.0, d);
}

// Контур треугольника (только края)
float draw_triangle_outline(vec2 p, vec2 tip, float size, float rotation, float width) {
    vec2 rp = p - tip;
    float c = cos(-rotation);
    float s = sin(-rotation);
    rp = vec2(rp.x * c - rp.y * s, rp.x * s + rp.y * c);
    
    // Вершины треугольника относительно tip
    vec2 v1 = vec2(0.0, 0.0); // Вершина (tip)
    vec2 v2 = vec2(-size * 0.5, size * 0.866);
    vec2 v3 = vec2(size * 0.5, size * 0.866);
    
    // Три стороны
    float l1 = draw_line(rp, v1, v2, width);
    float l2 = draw_line(rp, v2, v3, width);
    float l3 = draw_line(rp, v3, v1, width);
    
    return max(max(l1, l2), l3);
}

vec4 get_state_color() {
    if (turret_state == 5) return color_shooting;
    if (turret_state == 4) return color_warning;
    if (turret_state == 3) return color_warning;
    if (turret_state == 2) return color_cooldown;
    if (turret_state == 1) return color_main;
    return color_main;
}

float mod_positive(float x, float y) {
    return x - y * floor(x / y);
}

void fragment() {
    vec2 screen_size = 1.0 / SCREEN_PIXEL_SIZE;
    vec2 center = screen_size * 0.5;
    vec2 p = FRAGCOORD.xy;
    
    float total_gap = reticle_gap + max(spread, 0.0);
    vec4 state_color = get_state_color();
    
    if (frost_level > 0.01) {
        state_color = mix(state_color, color_frost, frost_level * 0.6);
    }
    
    vec4 final_color = vec4(0.0);
    
    // 1. ЛИНИИ СХОЖДЕНИЯ
    if (convergence_intensity > 0.01) {
        float inner = reticle_gap * 0.3;
        float outer = 180.0;
        for (int i = 0; i < 8; i++) {
            float angle = float(i) * 0.785398 + 0.392699;
            float this_outer = (i % 2 == 0) ? outer : outer * 0.7;
            float line_val = draw_convergence_line(p, center, angle, inner, this_outer, 3.0, convergence_intensity);
            vec4 conv_color = color_shooting;
            conv_color.a *= line_val * 0.9 * (0.8 + sin(state_time * 15.0 + float(i)) * 0.2);
            final_color = mix(final_color, conv_color, line_val);
        }
        float conv_ring = draw_circle(p, center, outer * convergence_intensity * 0.5 + reticle_gap, 1.5);
        vec4 ring_col = color_shooting;
        ring_col.a *= convergence_intensity * 0.3;
        final_color = mix(final_color, ring_col, conv_ring);
    }
    
    // 2. КОЛЬЦО УДАРА
    if (impact_ring > 0.01) {
        float ring_radius = reticle_gap + (1.0 - impact_ring) * 60.0;
        float ring = draw_circle(p, center, ring_radius, 3.0 * impact_ring);
        vec4 ring_color = color_flash;
        ring_color.a *= impact_ring * 0.8;
        final_color = mix(final_color, ring_color, ring);
    }
    
    // 3. ВСПЫШКА ВЫСТРЕЛА
    if (shot_flash > 0.01) {
        float flash_radius = 50.0 + 30.0 * shot_flash;
        float dist = length(p - center);
        float flash = pow(1.0 - smoothstep(0.0, flash_radius, dist), 0.7) * shot_flash;
        vec4 flash_color = color_flash;
        flash_color.a = flash * 0.7;
        final_color = mix(final_color, flash_color, flash);
        float core = draw_filled_circle(p, center, 8.0 * shot_flash);
        final_color = mix(final_color, vec4(1.0, 1.0, 1.0, shot_flash * 0.9), core);
    }
    
    // 4. ЦЕНТРАЛЬНЫЙ РОМБ
    float diamond = draw_diamond_squeeze(p, center, diamond_base_size, spread, diamond_rotation, line_width);
    if (turret_state == 5 || shot_flash > 0.05) {
        float glow = draw_diamond_squeeze(p, center, diamond_base_size, spread, diamond_rotation, line_width * 6.0);
        vec4 glow_color = color_shooting;
        glow_color.a *= max(shot_flash * 0.5, 0.2);
        final_color = mix(final_color, glow_color, glow * 0.4);
    }
    final_color = mix(final_color, state_color, diamond);
    if (spread > -2.0 && spread < 3.0 && shot_flash < 0.1) {
        float center_dot = draw_filled_circle(p, center, center_dot_radius);
        final_color = mix(final_color, state_color, center_dot);
    }
    
    // 5. ЭФФЕКТ ОХЛАЖДЕНИЯ
    if (frost_level > 0.05 && turret_state == 2) {
        float ice_radius = total_gap + 20.0 + sin(state_time * 2.0) * 5.0;
        for (int i = 0; i < 6; i++) {
            float angle = float(i) * 1.0472 + state_time * 1.5;
            float wobble = sin(state_time * 3.0 + float(i) * 1.5) * 8.0;
            vec2 crystal_pos = center + vec2(cos(angle), sin(angle)) * (ice_radius + wobble);
            float crystal_size = (4.0 + sin(state_time * 4.0 + float(i)) * 1.5) * frost_level;
            float crystal = draw_ice_crystal(p, crystal_pos, crystal_size, angle * 2.0);
            vec4 ice_color = color_frost;
            ice_color.a *= (0.7 + sin(state_time * 8.0 + float(i) * 2.0) * 0.3) * frost_level;
            final_color = mix(final_color, ice_color, crystal);
        }
        float cold_ring = draw_circle(p, center, total_gap - 5.0, 1.5);
        vec4 ring_color = color_frost;
        ring_color.a *= 0.4 * frost_level * (0.7 + sin(state_time * 5.0) * 0.3);
        final_color = mix(final_color, ring_color, cold_ring);
    }
    
    // 6. ИНДИКАТОРЫ СОСТОЯНИЯ
    if (turret_state == 2) {
        float dot_radius = total_gap + 15.0;
        for (int i = 0; i < 4; i++) {
            float angle = state_time * 4.0 + float(i) * 1.5708;
            vec2 dot_pos = center + vec2(cos(angle), sin(angle)) * dot_radius;
            float dot_val = draw_filled_circle(p, dot_pos, 3.0);
            vec4 dot_color = mix(color_cooldown, color_frost, frost_level);
            dot_color.a *= 0.5 + sin(state_time * 10.0 + float(i)) * 0.5;
            final_color = mix(final_color, dot_color, dot_val);
        }
    }
    if (turret_state == 3) {
        float blink = 0.3 + sin(state_time * 6.0) * 0.7;
        float indicator_dist = total_gap + 20.0;
        vec4 blink_color = color_warning;
        blink_color.a *= blink;
        for (int i = 0; i < 4; i++) {
            vec2 corner = vec2((i == 0 || i == 2) ? -1.0 : 1.0, (i < 2) ? -1.0 : 1.0);
            vec2 pos = center + corner * indicator_dist;
            float line1 = draw_line(p, pos, pos + vec2(corner.x * 8.0, 0.0), 2.0);
            float line2 = draw_line(p, pos, pos + vec2(0.0, corner.y * 8.0), 2.0);
            final_color = mix(final_color, blink_color, max(line1, line2));
        }
    }
    if (turret_state == 1) {
        vec2 rp = p - center;
        float angle = atan(rp.y, rp.x);
        if (mod_positive(angle, 0.785398) < 0.392699) {
            float ring = draw_circle(p, center, total_gap + 5.0, 1.5);
            vec4 ring_color = state_color;
            ring_color.a *= 0.5;
            final_color = mix(final_color, ring_color, ring);
        }
    }
    
    // 7. ГОРИЗОНТАЛЬНАЯ ШКАЛА YAW + ГРАНИЦЫ
    {
        float y = center.y;
        float half_width = screen_size.x * 0.5;
        
        bool has_yaw_limits = show_turret_limits && turret_max_yaw >= 0.0;
        float yaw_limit_left = has_yaw_limits ? -turret_max_yaw : -9999.0;
        float yaw_limit_right = has_yaw_limits ? turret_max_yaw : 9999.0;
        
        int visible_range = int(half_width / pixels_per_degree) + 10;
        int start_deg = int(floor(yaw_degrees)) - visible_range;
        start_deg -= int(mod_positive(float(start_deg), float(minor_interval)));
        
        for (int deg = start_deg; deg <= start_deg + visible_range * 2; deg += minor_interval) {
            float deg_f = float(deg);
            if (has_yaw_limits && (deg_f < yaw_limit_left || deg_f > yaw_limit_right)) continue;
            
            float diff = -(deg_f - yaw_degrees);
            float x_pos = center.x + diff * pixels_per_degree;
            if (x_pos < edge_margin || x_pos > screen_size.x - edge_margin) continue;
            
            float dist_from_center = abs(x_pos - center.x);
            if (dist_from_center < total_gap) continue;
            
            float max_dist = half_width - edge_margin;
            float alpha = dist_from_center > max_dist * 0.6 
                ? 1.0 - (dist_from_center - max_dist * 0.6) / (max_dist * 0.4) 
                : 1.0;
            if (alpha <= 0.01) continue;
            
            int abs_deg = abs(deg);
            bool is_major = (abs_deg % major_interval == 0) || (deg == 0);
            bool is_medium = !is_major && (abs_deg % medium_interval == 0);
            
            float length_val = is_major ? tick_large : (is_medium ? tick_medium : tick_small);
            float width_val = is_major ? 2.5 : (is_medium ? 1.5 : 1.0);
            vec4 tick_color = is_major ? state_color : (is_medium ? color_medium : color_minor);
            tick_color.a *= alpha;
            if (frost_level > 0.1) tick_color = mix(tick_color, color_frost, frost_level * 0.3);
            
            float half_len = length_val * 0.5;
            float tick = draw_line(p, vec2(x_pos, y - half_len), vec2(x_pos, y + half_len), width_val);
            final_color = mix(final_color, tick_color, tick);
            
            if (is_major) {
                float marker = draw_filled_diamond(p, vec2(x_pos, y + half_len + 8.0), 4.0);
                final_color = mix(final_color, tick_color, marker);
            }
        }
        
        // Маркеры границ YAW
        if (has_yaw_limits) {
            float proximity = max(
                1.0 - abs(yaw_degrees - turret_max_yaw) / 15.0,
                1.0 - abs(yaw_degrees + turret_max_yaw) / 15.0
            );
            float pulse = 0.6 + clamp(proximity, 0.0, 1.0) * 0.4 * (0.5 + 0.5 * sin(state_time * 10.0));
            vec4 limit_color = color_limit;
            limit_color.a *= pulse;
            
            // Правая граница (+max_yaw) теперь СЛЕВА на экране
            float right_x = center.x - (turret_max_yaw - yaw_degrees) * pixels_per_degree;
            if (right_x > edge_margin && right_x < center.x - total_gap) {
                float bracket = draw_limit_bracket_h(p, right_x, y, tick_large, true); // points_right = true
                final_color = mix(final_color, limit_color, bracket);
            }
            
            // Левая граница (-max_yaw) теперь СПРАВА на экране
            float left_x = center.x - (-turret_max_yaw - yaw_degrees) * pixels_per_degree;
            if (left_x > center.x + total_gap && left_x < screen_size.x - edge_margin) {
                float bracket = draw_limit_bracket_h(p, left_x, y, tick_large, false); // points_right = false
                final_color = mix(final_color, limit_color, bracket);
            }
        }
    }
    
    // 8. ВЕРТИКАЛЬНАЯ ШКАЛА PITCH + ГРАНИЦЫ
    {
        float x = center.x;
        float half_height = screen_size.y * 0.5;
        bool has_pitch_limits = show_turret_limits;
        
        float pitch_range = turret_max_pitch - turret_min_pitch;
        
        float step_degrees;
        if (pitch_range <= 2.0) {
            step_degrees = 0.25;
        } else if (pitch_range <= 5.0) {
            step_degrees = 0.5;
        } else if (pitch_range <= 15.0) {
            step_degrees = 1.0;
        } else {
            step_degrees = float(minor_interval);
        }
        
        float visible_range_deg = half_height / pixels_per_degree + 5.0;
        float start_deg = floor((pitch_degrees - visible_range_deg) / step_degrees) * step_degrees;
        float end_deg = pitch_degrees + visible_range_deg;
        
        for (float deg_f = start_deg; deg_f <= end_deg; deg_f += step_degrees) {
            if (has_pitch_limits && (deg_f < turret_min_pitch - 0.01 || deg_f > turret_max_pitch + 0.01)) {
                continue;
            }
            
            // ★ ИНВЕРСИЯ: минус перед diff (вверх = меньше Y на экране)
            float diff = -(deg_f - pitch_degrees);
            float y_pos = center.y + diff * pixels_per_degree;
            
            if (y_pos < edge_margin || y_pos > screen_size.y - edge_margin) continue;
            
            float dist_from_center = abs(y_pos - center.y);
            if (dist_from_center < total_gap) continue;
            
            float max_dist = half_height - edge_margin;
            float alpha = dist_from_center > max_dist * 0.6 
                ? 1.0 - (dist_from_center - max_dist * 0.6) / (max_dist * 0.4) 
                : 1.0;
            if (alpha <= 0.01) continue;
            
            float abs_deg = abs(deg_f);
            bool is_zero = abs(deg_f) < 0.01;
            bool is_integer = abs(deg_f - round(deg_f)) < 0.01;
            bool is_half = abs(mod(abs_deg, 1.0) - 0.5) < 0.01;
            
            float length_val;
            float width_val;
            vec4 tick_color;
            
            if (is_zero) {
                length_val = tick_large;
                width_val = 2.5;
                tick_color = state_color;
            } else if (is_integer) {
                length_val = tick_medium;
                width_val = 1.8;
                tick_color = color_medium;
            } else if (is_half) {
                length_val = tick_small;
                width_val = 1.2;
                tick_color = color_minor;
                tick_color.a *= 0.7;
            } else {
                length_val = tick_small * 0.6;
                width_val = 1.0;
                tick_color = color_minor;
                tick_color.a *= 0.4;
            }
            
            tick_color.a *= alpha;
            if (frost_level > 0.1) tick_color = mix(tick_color, color_frost, frost_level * 0.3);
            
            float half_len = length_val * 0.5;
            float tick = draw_line(p, vec2(x - half_len, y_pos), vec2(x + half_len, y_pos), width_val);
            final_color = mix(final_color, tick_color, tick);
            
            if (is_zero || (is_integer && pitch_range > 3.0)) {
                float ladder_width = 30.0;
                float gap = half_len + 8.0;
                
                float left_line = draw_line(p, vec2(x - gap - ladder_width, y_pos), vec2(x - gap, y_pos), 2.0);
                float right_line = draw_line(p, vec2(x + gap, y_pos), vec2(x + gap + ladder_width, y_pos), 2.0);
                final_color = mix(final_color, tick_color, max(left_line, right_line));
                
                if (is_zero) {
                    float left_circle = draw_filled_circle(p, vec2(x - gap - ladder_width - 6.0, y_pos), 3.0);
                    float right_circle = draw_filled_circle(p, vec2(x + gap + ladder_width + 6.0, y_pos), 3.0);
                    final_color = mix(final_color, tick_color, max(left_circle, right_circle));
                }
            }
        }
        
        // ★ МАРКЕРЫ ГРАНИЦ PITCH - ИНВЕРТИРОВАНЫ
        if (has_pitch_limits) {
            float proximity = max(
                1.0 - abs(pitch_degrees - turret_max_pitch) / max(pitch_range * 0.5, 1.0),
                1.0 - abs(pitch_degrees - turret_min_pitch) / max(pitch_range * 0.5, 1.0)
            );
            float pulse = 0.7 + clamp(proximity, 0.0, 1.0) * 0.3 * (0.5 + 0.5 * sin(state_time * 10.0));
            
            vec4 limit_color = color_limit;
            limit_color.a *= pulse;
            
            float marker_size = max(tick_large * 0.8, 15.0);
            
            // ★ ИНВЕРСИЯ: max_pitch теперь ВВЕРХУ экрана (меньший Y)
            float top_diff = -(turret_max_pitch - pitch_degrees);
            float top_y = center.y + top_diff * pixels_per_degree;
            
            bool top_visible = top_y > edge_margin && top_y < screen_size.y - edge_margin;
            bool top_not_overlapping = abs(top_y - center.y) > total_gap * 0.5;
            
            if (top_visible && top_not_overlapping) {
                // points_down = true потому что стрелки указывают К центру (вниз)
                float bracket = draw_limit_bracket_v(p, top_y, x, marker_size, true);
                final_color = mix(final_color, limit_color, bracket);
                
                if (abs(pitch_degrees - turret_max_pitch) < 0.5) {
                    float glow = draw_line(p, vec2(x - marker_size * 1.5, top_y), vec2(x + marker_size * 1.5, top_y), 6.0);
                    vec4 glow_color = limit_color;
                    glow_color.a *= 0.3;
                    final_color = mix(final_color, glow_color, glow);
                }
            }
            
            // ★ ИНВЕРСИЯ: min_pitch теперь ВНИЗУ экрана (больший Y)
            float bottom_diff = -(turret_min_pitch - pitch_degrees);
            float bottom_y = center.y + bottom_diff * pixels_per_degree;
            
            bool bottom_visible = bottom_y > edge_margin && bottom_y < screen_size.y - edge_margin;
            bool bottom_not_overlapping = abs(bottom_y - center.y) > total_gap * 0.5;
            
            if (bottom_visible && bottom_not_overlapping) {
                // points_down = false потому что стрелки указывают К центру (вверх)
                float bracket = draw_limit_bracket_v(p, bottom_y, x, marker_size, false);
                final_color = mix(final_color, limit_color, bracket);
                
                if (abs(pitch_degrees - turret_min_pitch) < 0.5) {
                    float glow = draw_line(p, vec2(x - marker_size * 1.5, bottom_y), vec2(x + marker_size * 1.5, bottom_y), 6.0);
                    vec4 glow_color = limit_color;
                    glow_color.a *= 0.3;
                    final_color = mix(final_color, glow_color, glow);
                }
            }
            
            // Индикаторы за краем экрана (тоже инвертированы)
            if (!top_visible && turret_max_pitch > pitch_degrees) {
                vec2 arrow_pos = vec2(x, edge_margin + 15.0);
                float arrow = draw_filled_diamond(p, arrow_pos, 6.0);
                vec4 arrow_color = limit_color;
                arrow_color.a *= 0.6 + 0.4 * sin(state_time * 8.0);
                final_color = mix(final_color, arrow_color, arrow);
            }
            
            if (!bottom_visible && turret_min_pitch < pitch_degrees) {
                vec2 arrow_pos = vec2(x, screen_size.y - edge_margin - 15.0);
                float arrow = draw_filled_diamond(p, arrow_pos, 6.0);
                vec4 arrow_color = limit_color;
                arrow_color.a *= 0.6 + 0.4 * sin(state_time * 8.0);
                final_color = mix(final_color, arrow_color, arrow);
            }
        }
    }
    
    // ═══════════════════════════════════════════════════════════════════════
    // === 9. МАРКЕР ЦЕЛИ КАМЕРЫ (куда направляется ствол) ===
    // ═══════════════════════════════════════════════════════════════════════
    if (show_target_marker) {
        // Разница между целью камеры и текущим положением ствола
        float yaw_diff = target_yaw_degrees - yaw_degrees;
        float pitch_diff = target_pitch_degrees - pitch_degrees;
        
        // Если есть значительная разница - показываем маркер
        float total_diff = abs(yaw_diff) + abs(pitch_diff);
        
        if (total_diff > 0.1) {
            // Интенсивность маркера зависит от разницы (больше разница = ярче)
            float intensity = clamp(total_diff / 10.0, 0.3, 1.0);
            
            // Пульсация
            float pulse = 0.7 + 0.3 * sin(state_time * 12.0);
            
            vec4 target_color = color_target;
            target_color.a *= intensity * pulse;
            
            // === ГОРИЗОНТАЛЬНЫЙ МАРКЕР (YAW) ===
            if (abs(yaw_diff) > 0.05) {
                // Позиция на горизонтальной шкале (инвертировано как основная шкала)
                float target_x = center.x - yaw_diff * pixels_per_degree;
                
                // Проверяем границы
                bool in_limits = !show_turret_limits || turret_max_yaw < 0.0 || 
                    (target_yaw_degrees >= -turret_max_yaw && target_yaw_degrees <= turret_max_yaw);
                
                if (in_limits && target_x > edge_margin && target_x < screen_size.x - edge_margin) {
                    float dist_from_center = abs(target_x - center.x);
                    
                    if (dist_from_center > total_gap) {
                        // Треугольник указывает вниз (к шкале)
                        vec2 tri_pos = vec2(target_x, center.y - total_gap - 8.0);
                        float tri = draw_triangle_outline(p, tri_pos, 10.0, 3.14159, 1.5);
                        final_color = mix(final_color, target_color, tri);
                        
                        // Вертикальная линия-указатель к центру
                        float line_start = center.y - total_gap;
                        float line_end = center.y - total_gap - 5.0;
                        float guide = draw_line(p, vec2(target_x, line_start), vec2(target_x, line_end), 1.0);
                        vec4 guide_color = target_color;
                        guide_color.a *= 0.5;
                        final_color = mix(final_color, guide_color, guide);
                    }
                }
            }
            
            // === ВЕРТИКАЛЬНЫЙ МАРКЕР (PITCH) ===
            if (abs(pitch_diff) > 0.05) {
                // Позиция на вертикальной шкале (инвертировано)
                float target_y = center.y - pitch_diff * pixels_per_degree;
                
                // Проверяем границы pitch
                bool in_limits = !show_turret_limits || 
                    (target_pitch_degrees >= turret_min_pitch && target_pitch_degrees <= turret_max_pitch);
                
                if (in_limits && target_y > edge_margin && target_y < screen_size.y - edge_margin) {
                    float dist_from_center = abs(target_y - center.y);
                    
                    if (dist_from_center > total_gap) {
                        // Треугольник указывает к центру (влево если справа, вправо если слева)
                        // Размещаем справа от шкалы
                        vec2 tri_pos = vec2(center.x + total_gap + 8.0, target_y);
                        // Поворот: -90° (указывает влево, к центру)
                        float tri = draw_triangle_outline(p, tri_pos, 10.0, -1.5708, 1.5);
                        final_color = mix(final_color, target_color, tri);
                        
                        // Горизонтальная линия-указатель
                        float line_start = center.x + total_gap;
                        float line_end = center.x + total_gap + 5.0;
                        float guide = draw_line(p, vec2(line_start, target_y), vec2(line_end, target_y), 1.0);
                        vec4 guide_color = target_color;
                        guide_color.a *= 0.5;
                        final_color = mix(final_color, guide_color, guide);
                    }
                }
            }
            
            // === ЦЕНТРАЛЬНЫЙ ИНДИКАТОР (если цель близко к центру) ===
            float center_dist_yaw = abs(yaw_diff) * pixels_per_degree;
            float center_dist_pitch = abs(pitch_diff) * pixels_per_degree;
            
            if (center_dist_yaw < total_gap && center_dist_pitch < total_gap && total_diff > 0.2) {
                // Маленький ромб показывает направление внутри gap-зоны
                vec2 target_offset = vec2(-yaw_diff, -pitch_diff) * pixels_per_degree * 0.8;
                vec2 inner_pos = center + target_offset;
                
                // Ограничиваем чтобы не выходил за gap
                float max_offset = total_gap * 0.7;
                if (length(target_offset) > max_offset) {
                    inner_pos = center + normalize(target_offset) * max_offset;
                }
                
                float inner_marker = draw_filled_diamond(p, inner_pos, 4.0);
                final_color = mix(final_color, target_color, inner_marker * intensity);
                
                // Линия от центра к маркеру
                if (length(inner_pos - center) > 5.0) {
                    float connect = draw_line(p, center, inner_pos, 1.0);
                    vec4 connect_color = target_color;
                    connect_color.a *= 0.3;
                    final_color = mix(final_color, connect_color, connect);
                }
            }
        }
    }

    // 10. УГЛОВЫЕ СКОБКИ
    {
        float size = 25.0;
        float margin = 15.0;
        vec4 bracket_color = state_color * 0.6;
        if (frost_level > 0.1) bracket_color = mix(bracket_color, color_frost * 0.6, frost_level);
        
        vec2 corners[4];
        corners[0] = vec2(margin, margin);
        corners[1] = vec2(screen_size.x - margin, margin);
        corners[2] = vec2(margin, screen_size.y - margin);
        corners[3] = vec2(screen_size.x - margin, screen_size.y - margin);
        
        vec2 dirs[4];
        dirs[0] = vec2(1.0, 1.0);
        dirs[1] = vec2(-1.0, 1.0);
        dirs[2] = vec2(1.0, -1.0);
        dirs[3] = vec2(-1.0, -1.0);
        
        for (int i = 0; i < 4; i++) {
            float h_line = draw_line(p, corners[i], corners[i] + vec2(dirs[i].x * size, 0.0), 1.5);
            float v_line = draw_line(p, corners[i], corners[i] + vec2(0.0, dirs[i].y * size), 1.5);
            final_color = mix(final_color, bracket_color, max(h_line, v_line));
        }
    }

    // 11. ИНДИКАТОР ЗУМА
    if (zoom_level > 1.1) {
        float zoom_ring_radius = reticle_gap - 10.0;
        float zoom_progress = (zoom_level - 1.0) / 5.0;
        vec2 rp = p - center;
        float angle = atan(rp.y, rp.x);
        float normalized_angle = (angle + 3.14159) / 6.28318;
        
        if (normalized_angle < zoom_progress) {
            float ring = draw_circle(p, center, zoom_ring_radius, 2.0);
            vec4 zoom_ring_color = state_color;
            zoom_ring_color.a *= 0.5;
            final_color = mix(final_color, zoom_ring_color, ring);
        }
        
        for (int i = 1; i <= 5; i++) {
            float mark_angle = float(i) / 5.0 * 6.28318 - 3.14159;
            vec2 mark_pos = center + vec2(cos(mark_angle), sin(mark_angle)) * zoom_ring_radius;
            float mark = draw_filled_circle(p, mark_pos, 2.0);
            vec4 mark_color = (float(i) <= zoom_progress * 5.0) ? state_color : color_minor;
            final_color = mix(final_color, mark_color, mark);
        }
    }
    
    COLOR = final_color;
}