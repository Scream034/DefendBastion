shader_type canvas_item;

// === СОСТОЯНИЕ ===
uniform float spread : hint_range(-20.0, 100.0) = 0.0;
uniform float diamond_rotation : hint_range(-6.28, 6.28) = 0.0;
uniform float yaw_degrees : hint_range(-360.0, 360.0) = 0.0;
uniform float pitch_degrees : hint_range(-90.0, 90.0) = 0.0;
uniform float zoom_level : hint_range(1.0, 10.0) = 1.0;

uniform int turret_state : hint_range(0, 5) = 0;
uniform float state_time = 0.0;

// === ЭФФЕКТЫ ВЫСТРЕЛА ===
uniform float convergence_intensity : hint_range(0.0, 1.0) = 0.0;
uniform float shot_flash : hint_range(0.0, 2.0) = 5.0;
uniform float impact_ring : hint_range(0.0, 1.0) = 6.0;
uniform float frost_level : hint_range(0.0, 1.0) = 0.0;

// === РАЗМЕРЫ ===
uniform float reticle_gap : hint_range(20.0, 100.0) = 45.0;
uniform float diamond_base_size : hint_range(5.0, 30.0) = 12.0;
uniform float edge_margin : hint_range(20.0, 100.0) = 40.0;
uniform float pixels_per_degree : hint_range(5.0, 30.0) = 5.0;

uniform float tick_small : hint_range(2.0, 15.0) = 10.0;
uniform float tick_medium : hint_range(5.0, 25.0) = 25.0;
uniform float tick_large : hint_range(10.0, 40.0) = 40.0;
uniform float line_width : hint_range(1.0, 5.0) = 2.0;
uniform float center_dot_radius : hint_range(1.0, 5.0) = 1.0;

uniform int minor_interval : hint_range(1, 15) = 5;
uniform int medium_interval : hint_range(5, 30) = 15;
uniform int major_interval : hint_range(15, 90) = 45;

// === ЦВЕТА ===
uniform vec4 color_main : source_color = vec4(0.2, 0.9, 0.8, 0.9);
uniform vec4 color_warning : source_color = vec4(1.0, 0.3, 0.2, 0.9);
uniform vec4 color_cooldown : source_color = vec4(0.4, 0.75, 1.0, 0.9);
uniform vec4 color_shooting : source_color = vec4(0.4, 1.0, 0.9, 1.0);
uniform vec4 color_flash : source_color = vec4(0.6, 1.0, 0.95, 1.0);
uniform vec4 color_frost : source_color = vec4(0.7, 0.9, 1.0, 0.9);
uniform vec4 color_minor : source_color = vec4(0.2, 0.9, 0.8, 0.25);
uniform vec4 color_medium : source_color = vec4(0.2, 0.9, 0.8, 0.5);

// === ФУНКЦИИ ===

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

// Ромб с эффектом сжатия
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

// Ледяной кристалл
float draw_ice_crystal(vec2 p, vec2 center, float size, float rotation) {
    vec2 rp = p - center;
    float c = cos(rotation);
    float s = sin(rotation);
    rp = vec2(rp.x * c - rp.y * s, rp.x * s + rp.y * c);
    float d = max(abs(rp.x) * 0.866 + abs(rp.y) * 0.5, abs(rp.y)) - size;
    return 1.0 - smoothstep(-0.5, 0.5, d);
}

// Линия схождения к центру с "острым" концом
float draw_convergence_line(vec2 p, vec2 center, float angle, float inner_r, float outer_r, float width, float intensity) {
    vec2 dir = vec2(cos(angle), sin(angle));
    
    // Линия сужается к центру
    float taper_start = outer_r;
    float taper_end = mix(outer_r, inner_r, intensity);
    
    vec2 start_point = center + dir * taper_start;
    vec2 end_point = center + dir * taper_end;
    
    float d = line_dist(p, start_point, end_point);
    
    // Ширина уменьшается к центру
    float dist_along = dot(p - end_point, dir) / (taper_start - taper_end);
    float tapered_width = width * (0.3 + 0.7 * clamp(dist_along, 0.0, 1.0));
    
    float line_val = 1.0 - smoothstep(tapered_width * 0.5 - 0.5, tapered_width * 0.5 + 0.5, d);
    
    return line_val * intensity;
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
    
    // Модификация цвета при охлаждении
    if (frost_level > 0.01) {
        state_color = mix(state_color, color_frost, frost_level * 0.6);
    }
    
    vec4 final_color = vec4(0.0);
    
    // 1. ЛИНИИ СХОЖДЕНИЯ К ЦЕНТРУ
    if (convergence_intensity > 0.01) {
        float inner = reticle_gap * 0.3;
        float outer = 180.0;
        
        // 8 линий с разной длиной для динамики
        for (int i = 0; i < 8; i++) {
            float angle = float(i) * 0.785398 + 0.392699;
            
            // Каждая вторая линия короче
            float this_outer = (i % 2 == 0) ? outer : outer * 0.7;
            
            float line_val = draw_convergence_line(p, center, angle, inner, this_outer, 3.0, convergence_intensity);
            
            // Цвет линий - яркий циановый
            vec4 conv_color = color_shooting;
            conv_color.a *= line_val * 0.9;
            
            // Пульсация
            float pulse = 0.8 + sin(state_time * 15.0 + float(i)) * 0.2;
            conv_color.a *= pulse;
            
            final_color = mix(final_color, conv_color, line_val);
        }
        
        // Внешнее кольцо схождения
        float conv_ring = draw_circle(p, center, outer * convergence_intensity * 0.5 + reticle_gap, 1.5);
        vec4 ring_col = color_shooting;
        ring_col.a *= convergence_intensity * 0.3;
        final_color = mix(final_color, ring_col, conv_ring);
    }
    
    // 2. КОЛЬЦО УДАРА (расширяющееся при выстреле)
    if (impact_ring > 0.01) {
        float ring_radius = reticle_gap + (1.0 - impact_ring) * 60.0;
        float ring_width = 3.0 * impact_ring;
        
        float ring = draw_circle(p, center, ring_radius, ring_width);
        
        vec4 ring_color = color_flash;
        ring_color.a *= impact_ring * 0.8;
        
        final_color = mix(final_color, ring_color, ring);
    }
    
    // 3. ВСПЫШКА ПРИ ВЫСТРЕЛЕ
    if (shot_flash > 0.01) {
        float flash_radius = 50.0 + 30.0 * shot_flash;
        float dist = length(p - center);
        
        // Мягкое затухание от центра
        float flash = 1.0 - smoothstep(0.0, flash_radius, dist);
        flash *= shot_flash;
        flash = pow(flash, 0.7); // Более яркий центр
        
        vec4 flash_color = color_flash;
        flash_color.a = flash * 0.7;
        
        final_color = mix(final_color, flash_color, flash);
        
        // Яркая точка в центре
        float core = draw_filled_circle(p, center, 8.0 * shot_flash);
        vec4 core_color = vec4(1.0, 1.0, 1.0, shot_flash * 0.9);
        final_color = mix(final_color, core_color, core);
    }
    
    // 4. ЦЕНТРАЛЬНЫЙ РОМБ
    
    // Основной ромб
    float diamond = draw_diamond_squeeze(p, center, diamond_base_size, spread, diamond_rotation, line_width);
    
    // Свечение при выстреле
    if (turret_state == 5 || shot_flash > 0.05) {
        float glow_intensity = max(shot_flash * 0.5, 0.2);
        float glow = draw_diamond_squeeze(p, center, diamond_base_size, spread, diamond_rotation, line_width * 6.0);
        vec4 glow_color = color_shooting;
        glow_color.a *= glow_intensity;
        final_color = mix(final_color, glow_color, glow * 0.4);
    }
    
    final_color = mix(final_color, state_color, diamond);
    
    // Центральная точка (только в спокойном состоянии)
    if (spread > -2.0 && spread < 3.0 && shot_flash < 0.1) {
        float center_dot = draw_filled_circle(p, center, center_dot_radius);
        final_color = mix(final_color, state_color, center_dot);
    }
    
    // 5. ЭФФЕКТ ОХЛАЖДЕНИЯ (ЛЕДЯНЫЕ ЧАСТИЦЫ)
    if (frost_level > 0.05 && turret_state == 2) {
        float ice_radius = total_gap + 20.0 + sin(state_time * 2.0) * 5.0;
        int crystal_count = 6;
        
        for (int i = 0; i < crystal_count; i++) {
            float base_angle = float(i) * 6.28318 / float(crystal_count);
            float angle = base_angle + state_time * 1.5;
            
            float wobble = sin(state_time * 3.0 + float(i) * 1.5) * 8.0;
            vec2 crystal_pos = center + vec2(cos(angle), sin(angle)) * (ice_radius + wobble);
            
            float crystal_size = 4.0 + sin(state_time * 4.0 + float(i)) * 1.5;
            crystal_size *= frost_level;
            
            float crystal = draw_ice_crystal(p, crystal_pos, crystal_size, angle * 2.0);
            
            vec4 ice_color = color_frost;
            ice_color.a *= 0.7 + sin(state_time * 8.0 + float(i) * 2.0) * 0.3;
            ice_color.a *= frost_level;
            
            final_color = mix(final_color, ice_color, crystal);
        }
        
        // Холодное внутреннее кольцо
        float cold_ring = draw_circle(p, center, total_gap - 5.0, 1.5);
        vec4 ring_color = color_frost;
        ring_color.a *= 0.4 * frost_level * (0.7 + sin(state_time * 5.0) * 0.3);
        final_color = mix(final_color, ring_color, cold_ring);
        
        // Снежинки
        for (int i = 0; i < 12; i++) {
            float t = state_time * 0.5 + float(i) * 0.5;
            float spawn_angle = hash(vec2(float(i), 0.0)) * 6.28318;
            float dist = mod(total_gap + 50.0 - t * 30.0, 60.0) + total_gap - 10.0;
            float particle_angle = spawn_angle + t * 0.5;
            
            vec2 particle_pos = center + vec2(cos(particle_angle), sin(particle_angle)) * dist;
            float particle = draw_filled_circle(p, particle_pos, 1.5);
            vec4 particle_color = color_frost;
            particle_color.a *= 0.5 * frost_level;
            final_color = mix(final_color, particle_color, particle);
        }
    }
    
    // 6. ИНДИКАТОРЫ СОСТОЯНИЯ
    
    // Перезарядка
    if (turret_state == 2) {
        float dot_radius = total_gap + 15.0;
        for (int i = 0; i < 4; i++) {
            float angle = state_time * 4.0 + float(i) * 1.5708;
            vec2 dot_pos = center + vec2(cos(angle), sin(angle)) * dot_radius;
            float pulse = 0.5 + sin(state_time * 10.0 + float(i)) * 0.5;
            float dot_val = draw_filled_circle(p, dot_pos, 3.0);
            vec4 dot_color = mix(color_cooldown, color_frost, frost_level);
            dot_color.a *= pulse;
            final_color = mix(final_color, dot_color, dot_val);
        }
    }
    
    // Нет патронов
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
    
    // Cooldown
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
    
    // 7. ШКАЛЫ YAW/PITCH
    
    // Горизонтальная шкала
    {
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
            
            if (frost_level > 0.1) {
                tick_color = mix(tick_color, color_frost, frost_level * 0.3);
            }
            
            float half_len = length_val * 0.5;
            float tick = draw_line(p, vec2(x_pos, y - half_len), vec2(x_pos, y + half_len), width_val);
            final_color = mix(final_color, tick_color, tick);
            
            if (is_major) {
                vec2 marker_pos = vec2(x_pos, y + half_len + 8.0);
                float marker = draw_filled_diamond(p, marker_pos, 4.0);
                final_color = mix(final_color, tick_color, marker);
            }
        }
    }
    
    // Вертикальная шкала
    {
        float x = center.x;
        float half_height = screen_size.y * 0.5;
        
        int visible_range = int(half_height / pixels_per_degree) + 10;
        int start_deg = int(floor(pitch_degrees)) - visible_range;
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
            
            if (frost_level > 0.1) {
                tick_color = mix(tick_color, color_frost, frost_level * 0.3);
            }
            
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
    
    // 8. УГЛОВЫЕ СКОБКИ
    {
        float size = 25.0;
        float margin = 15.0;
        vec4 bracket_color = state_color * 0.6;
        
        if (frost_level > 0.1) {
            bracket_color = mix(bracket_color, color_frost * 0.6, frost_level);
        }
        
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

    // 9. ИНДИКАТОР ЗУМА (центральное кольцо)
    if (zoom_level > 1.1) {
        float zoom_ring_radius = reticle_gap - 10.0;
        float zoom_progress = (zoom_level - 1.0) / 5.0; // Для 1x-6x
        
        // Частичное кольцо, заполняющееся при зуме
        vec2 rp = p - center;
        float angle = atan(rp.y, rp.x);
        float normalized_angle = (angle + 3.14159) / 6.28318; // 0 to 1
        
        if (normalized_angle < zoom_progress) {
            float ring = draw_circle(p, center, zoom_ring_radius, 2.0);
            vec4 zoom_ring_color = state_color;
            zoom_ring_color.a *= 0.5;
            final_color = mix(final_color, zoom_ring_color, ring);
        }
        
        // Метки зума
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