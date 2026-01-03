shader_type canvas_item;

// === СОСТОЯНИЕ ===
uniform float spread : hint_range(-20.0, 100.0) = 0.0;  // Может быть отрицательным!
uniform float diamond_rotation : hint_range(-6.28, 6.28) = 0.0;
uniform float yaw_degrees : hint_range(-360.0, 360.0) = 0.0;
uniform float pitch_degrees : hint_range(-90.0, 90.0) = 0.0;

// Состояние: 0=idle, 1=cooldown, 2=reloading, 3=no_ammo, 4=broken, 5=shooting
uniform int turret_state : hint_range(0, 5) = 0;
uniform float state_time = 0.0;

// === РАЗМЕРЫ ===
uniform float reticle_gap : hint_range(20.0, 100.0) = 45.0;
uniform float diamond_base_size : hint_range(5.0, 30.0) = 12.0;
uniform float edge_margin : hint_range(20.0, 100.0) = 40.0;
uniform float pixels_per_degree : hint_range(5.0, 30.0) = 12.0;

uniform float tick_small : hint_range(2.0, 15.0) = 6.0;
uniform float tick_medium : hint_range(5.0, 25.0) = 12.0;
uniform float tick_large : hint_range(10.0, 40.0) = 20.0;
uniform float line_width : hint_range(1.0, 5.0) = 2.0;

uniform int minor_interval : hint_range(1, 15) = 5;
uniform int medium_interval : hint_range(5, 30) = 15;
uniform int major_interval : hint_range(15, 90) = 45;

// === ЦВЕТА ===
uniform vec4 color_main : source_color = vec4(0.2, 0.9, 0.8, 0.9);
uniform vec4 color_warning : source_color = vec4(1.0, 0.3, 0.2, 0.9);
uniform vec4 color_cooldown : source_color = vec4(1.0, 0.7, 0.2, 0.9);
uniform vec4 color_shooting : source_color = vec4(1.0, 0.95, 0.8, 1.0);  // Яркий при выстреле
uniform vec4 color_minor : source_color = vec4(0.2, 0.9, 0.8, 0.25);
uniform vec4 color_medium : source_color = vec4(0.2, 0.9, 0.8, 0.5);

// === ВСПОМОГАТЕЛЬНЫЕ ФУНКЦИИ ===

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

// Ромб с поддержкой сжатия (spread < 0 = треугольник)
float draw_diamond_squeeze(vec2 p, vec2 center, float base_size, float spread_val, float rotation, float width) {
    vec2 rp = p - center;
    float c = cos(-rotation);
    float s = sin(-rotation);
    rp = vec2(rp.x * c - rp.y * s, rp.x * s + rp.y * c);
    
    float size = base_size + max(spread_val, -base_size + 2.0); // Минимум 2 пикселя
    
    // Коэффициент сжатия: 1.0 = ромб, 0.0 = линия
    float squeeze_factor = clamp(1.0 + spread_val / base_size, 0.1, 1.0);
    
    // Модифицируем Y координату для сжатия
    rp.y *= squeeze_factor;
    
    // Манхэттенское расстояние для ромба
    float d = abs(rp.x) + abs(rp.y) - size * squeeze_factor;
    
    return 1.0 - smoothstep(-width * 0.5, width * 0.5, abs(d) - width * 0.5);
}

float draw_filled_diamond(vec2 p, vec2 center, float size) {
    vec2 rp = abs(p - center);
    float d = rp.x + rp.y - size;
    return 1.0 - smoothstep(-0.5, 0.5, d);
}

vec4 get_state_color() {
    if (turret_state == 5) return color_shooting;     // shooting - яркий
    if (turret_state == 4) return color_warning;      // broken
    if (turret_state == 3) return color_warning;      // no_ammo
    if (turret_state == 2) return color_cooldown;     // reloading
    if (turret_state == 1) return color_main;         // cooldown
    return color_main;                                 // idle
}

float mod_positive(float x, float y) {
    return x - y * floor(x / y);
}

void fragment() {
    vec2 screen_size = 1.0 / SCREEN_PIXEL_SIZE;
    vec2 center = screen_size * 0.5;
    vec2 p = FRAGCOORD.xy;
    
    // Spread может быть отрицательным при сжатии
    float effective_spread = spread;
    float total_gap = reticle_gap + max(spread, 0.0); // Gap не уменьшается
    float diamond_size = diamond_base_size + spread;
    
    vec4 state_color = get_state_color();
    vec4 final_color = vec4(0.0);
    
    // === 1. ЦЕНТРАЛЬНЫЙ РОМБ (с эффектом сжатия) ===
    
    // При сжатии (spread < 0) ромб сжимается в вертикальную линию
    float diamond = draw_diamond_squeeze(p, center, diamond_base_size, spread, diamond_rotation, line_width);
    
    // При выстреле - ярче и с glow эффектом
    if (turret_state == 5) {
        float glow = draw_diamond_squeeze(p, center, diamond_base_size, spread, diamond_rotation, line_width * 4.0);
        vec4 glow_color = state_color;
        glow_color.a *= 0.3;
        final_color = mix(final_color, glow_color, glow * 0.5);
    }
    
    final_color = mix(final_color, state_color, diamond);
    
    // Центральная точка - только когда прицел сведён И не в режиме сжатия
    if (spread > -2.0 && spread < 3.0 && turret_state != 5) {
        float center_dot = draw_filled_circle(p, center, 2.0);
        final_color = mix(final_color, state_color, center_dot);
    }
    
    // При выстреле - яркая точка-вспышка в центре
    if (turret_state == 5) {
        float flash_size = 4.0 - spread * 0.3; // Чем больше сжатие, тем меньше вспышка
        float flash = draw_filled_circle(p, center, max(flash_size, 1.0));
        final_color = mix(final_color, color_shooting, flash * 0.8);
    }
    
    // === 2. ИНДИКАТОРЫ СОСТОЯНИЯ ===
    
    // Перезарядка - вращающиеся точки
    if (turret_state == 2) {
        float dot_radius = total_gap + 15.0;
        for (int i = 0; i < 4; i++) {
            float angle = state_time * 4.0 + float(i) * 1.5708;
            vec2 dot_pos = center + vec2(cos(angle), sin(angle)) * dot_radius;
            float pulse = 0.5 + sin(state_time * 10.0 + float(i)) * 0.5;
            float dot = draw_filled_circle(p, dot_pos, 3.0);
            vec4 dot_color = color_cooldown;
            dot_color.a *= pulse;
            final_color = mix(final_color, dot_color, dot);
        }
    }
    
    // Нет патронов - мигающие уголки
    if (turret_state == 3) {
        float blink = 0.3 + sin(state_time * 6.0) * 0.7;
        float indicator_dist = total_gap + 20.0;
        float indicator_size = 8.0;
        
        vec4 blink_color = color_warning;
        blink_color.a *= blink;
        
        for (int i = 0; i < 4; i++) {
            vec2 corner = vec2(
                (i == 0 || i == 2) ? -1.0 : 1.0,
                (i < 2) ? -1.0 : 1.0
            );
            vec2 pos = center + corner * indicator_dist;
            
            float line1 = draw_line(p, pos, pos + vec2(corner.x * indicator_size, 0.0), 2.0);
            float line2 = draw_line(p, pos, pos + vec2(0.0, corner.y * indicator_size), 2.0);
            
            final_color = mix(final_color, blink_color, max(line1, line2));
        }
    }
    
    // Cooldown - пунктирное кольцо
    if (turret_state == 1) {
        float ring_radius = total_gap + 5.0;
        vec2 rp = p - center;
        float angle = atan(rp.y, rp.x);
        float segment = mod_positive(angle, 0.785398);
        
        if (segment < 0.392699) {
            float ring = draw_circle(p, center, ring_radius, 1.5);
            vec4 ring_color = state_color;
            ring_color.a *= 0.5;
            final_color = mix(final_color, ring_color, ring);
        }
    }
    
    // === 3. ШКАЛЫ (только если не в режиме сжатия) ===
    
    if (spread > -5.0) {
        // Горизонтальная шкала YAW
        float y = center.y;
        float half_width = screen_size.x * 0.5;
        
        int visible_range = int(half_width / pixels_per_degree) + 10;
        int start_deg = int(floor(yaw_degrees)) - visible_range;
        start_deg -= int(mod_positive(float(start_deg), float(minor_interval)));
        
        for (int deg = start_deg; deg <= start_deg + visible_range * 2; deg += minor_interval) {
            float diff = float(deg) - yaw_degrees;
            float x_pos = center.x + diff * pixels_per_degree;
            
            if (x_pos < edge_margin || x_pos > screen_size.x - edge_margin) continue;
            
            float dist_from_center = abs(x_pos - center.x);
            if (dist_from_center < total_gap) continue;
            
            float max_dist = half_width - edge_margin;
            float fade_start = max_dist * 0.6;
            float alpha = 1.0;
            if (dist_from_center > fade_start) {
                alpha = 1.0 - (dist_from_center - fade_start) / (max_dist - fade_start);
            }
            if (alpha <= 0.01) continue;
            
            int abs_deg = abs(deg);
            bool is_major = (abs_deg % major_interval == 0) || (deg == 0 && major_interval > 0);
            bool is_medium = !is_major && (abs_deg % medium_interval == 0);
            
            float length_val = is_major ? tick_large : (is_medium ? tick_medium : tick_small);
            float width_val = is_major ? 2.5 : (is_medium ? 1.5 : 1.0);
            vec4 tick_color = is_major ? state_color : (is_medium ? color_medium : color_minor);
            tick_color.a *= alpha;
            
            float half_len = length_val * 0.5;
            float tick = draw_line(p, vec2(x_pos, y - half_len), vec2(x_pos, y + half_len), width_val);
            final_color = mix(final_color, tick_color, tick);
            
            if (is_major) {
                vec2 marker_pos = vec2(x_pos, y + half_len + 8.0);
                float marker = draw_filled_diamond(p, marker_pos, 4.0);
                final_color = mix(final_color, tick_color, marker);
            }
        }
        
        // Вертикальная шкала PITCH
        float x = center.x;
        float half_height = screen_size.y * 0.5;
        
        visible_range = int(half_height / pixels_per_degree) + 10;
        start_deg = int(floor(pitch_degrees)) - visible_range;
        start_deg -= int(mod_positive(float(start_deg), float(minor_interval)));
        
        for (int deg = start_deg; deg <= start_deg + visible_range * 2; deg += minor_interval) {
            float diff = float(deg) - pitch_degrees;
            float y_pos = center.y + diff * pixels_per_degree;
            
            if (y_pos < edge_margin || y_pos > screen_size.y - edge_margin) continue;
            
            float dist_from_center = abs(y_pos - center.y);
            if (dist_from_center < total_gap) continue;
            
            float max_dist = half_height - edge_margin;
            float fade_start = max_dist * 0.6;
            float alpha = 1.0;
            if (dist_from_center > fade_start) {
                alpha = 1.0 - (dist_from_center - fade_start) / (max_dist - fade_start);
            }
            if (alpha <= 0.01) continue;
            
            int abs_deg = abs(deg);
            bool is_major = (abs_deg % major_interval == 0) || (deg == 0);
            bool is_medium = !is_major && (abs_deg % medium_interval == 0);
            
            float length_val = is_major ? tick_large : (is_medium ? tick_medium : tick_small);
            float width_val = is_major ? 2.5 : (is_medium ? 1.5 : 1.0);
            vec4 tick_color = is_major ? state_color : (is_medium ? color_medium : color_minor);
            tick_color.a *= alpha;
            
            float half_len = length_val * 0.5;
            float tick = draw_line(p, vec2(x - half_len, y_pos), vec2(x + half_len, y_pos), width_val);
            final_color = mix(final_color, tick_color, tick);
            
            if (is_major) {
                float ladder_width = 30.0;
                float gap = half_len + 8.0;
                
                float left_line = draw_line(p, vec2(x - gap - ladder_width, y_pos), vec2(x - gap, y_pos), 2.0);
                float right_line = draw_line(p, vec2(x + gap, y_pos), vec2(x + gap + ladder_width, y_pos), 2.0);
                final_color = mix(final_color, tick_color, max(left_line, right_line));
                
                if (deg == 0) {
                    float left_circle = draw_filled_circle(p, vec2(x - gap - ladder_width - 6.0, y_pos), 3.0);
                    float right_circle = draw_filled_circle(p, vec2(x + gap + ladder_width + 6.0, y_pos), 3.0);
                    final_color = mix(final_color, tick_color, max(left_circle, right_circle));
                } else {
                    float tick_dir = deg < 0 ? 1.0 : -1.0;
                    float end_tick = 6.0;
                    float left_tick = draw_line(p, 
                        vec2(x - gap - ladder_width, y_pos), 
                        vec2(x - gap - ladder_width, y_pos + end_tick * tick_dir), 1.5);
                    float right_tick = draw_line(p, 
                        vec2(x + gap + ladder_width, y_pos), 
                        vec2(x + gap + ladder_width, y_pos + end_tick * tick_dir), 1.5);
                    final_color = mix(final_color, tick_color, max(left_tick, right_tick));
                }
            }
        }
    }
    
    // === 4. УГЛОВЫЕ СКОБКИ ===
    {
        float size = 25.0;
        float margin = 15.0;
        vec4 bracket_color = state_color * 0.6;
        
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
    
    COLOR = final_color;
}