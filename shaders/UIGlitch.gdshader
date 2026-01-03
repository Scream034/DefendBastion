shader_type canvas_item;

uniform sampler2D SCREEN_TEXTURE : hint_screen_texture, filter_linear_mipmap;

uniform float glitch_intensity : hint_range(0.0, 1.0) = 0.0;
uniform float time = 0.0;
uniform vec4 color_tint : source_color = vec4(1.0);

// Эффекты
uniform float chromatic_offset : hint_range(0.0, 50.0) = 20.0;
uniform float scan_line_density : hint_range(0.5, 5.0) = 1.5;
uniform float block_glitch_size : hint_range(4.0, 64.0) = 24.0;
uniform float wave_distortion : hint_range(0.0, 0.1) = 0.02;

// Рандом
float hash(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float hash_time(vec2 p) {
    return hash(p + floor(time * 15.0));
}

// Блочный шум
float block_noise(vec2 uv) {
    vec2 block = floor(uv * block_glitch_size);
    return hash(block + floor(time * 20.0));
}

void fragment() {
    if (glitch_intensity < 0.001) {
        COLOR = vec4(0.0);
        discard;
    }
    
    vec2 uv = SCREEN_UV;
    float intensity = glitch_intensity;
    
    // 1. Волновое искажение
    float wave = sin(uv.y * 50.0 + time * 10.0) * wave_distortion * intensity;
    uv.x += wave;
    
    // 2. Горизонтальные блочные смещения
    float line_hash = hash_time(vec2(floor(uv.y * 30.0), 0.0));
    if (line_hash > (1.0 - intensity * 0.4)) {
        float offset = (hash_time(vec2(uv.y, 1.0)) - 0.5) * intensity * 0.15;
        uv.x += offset;
    }
    
    // 3. Вертикальные разрывы
    float block_hash = block_noise(uv);
    if (block_hash > (1.0 - intensity * 0.2)) {
        uv.y += (hash(uv + time) - 0.5) * intensity * 0.03;
    }
    
    // 4. Хроматическая аберрация
    float chroma = chromatic_offset * intensity / 1000.0;
    
    // Направление аберрации зависит от позиции
    vec2 center = vec2(0.5);
    vec2 dir = normalize(uv - center);
    
    vec4 color_r = texture(SCREEN_TEXTURE, uv + dir * chroma);
    vec4 color_g = texture(SCREEN_TEXTURE, uv);
    vec4 color_b = texture(SCREEN_TEXTURE, uv - dir * chroma);
    
    vec4 color = vec4(color_r.r, color_g.g, color_b.b, 1.0);
    
    // 5. Скан-линии
    float scan = pow(sin(FRAGCOORD.y * scan_line_density) * 0.5 + 0.5, 3.0);
    color.rgb -= scan * 0.15 * intensity;
    
    // 6. RGB шум в блоках
    if (block_hash > (1.0 - intensity * 0.15)) {
        vec3 noise_color = vec3(
            hash(uv * 100.0 + time),
            hash(uv * 100.0 + time + 1.0),
            hash(uv * 100.0 + time + 2.0)
        );
        color.rgb = mix(color.rgb, noise_color, intensity * 0.4);
    }
    
    // 7. Случайные белые/чёрные горизонтальные полосы
    float stripe_hash = hash_time(vec2(floor(uv.y * 80.0), 2.0));
    if (stripe_hash > (1.0 - intensity * 0.08)) {
        float stripe_val = step(0.5, hash(vec2(time, uv.y)));
        color.rgb = mix(color.rgb, vec3(stripe_val), intensity * 0.5);
    }
    
    // 8. Применяем цветовой оттенок
    color.rgb *= color_tint.rgb;
    
    // 9. Финальная альфа
    color.a = intensity * 0.8;
    
    // Добавляем мерцание
    color.a *= 0.8 + hash(vec2(time * 50.0, 0.0)) * 0.4;
    
    COLOR = color;
}