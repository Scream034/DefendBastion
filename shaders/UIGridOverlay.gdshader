shader_type canvas_item;

// Настройки сетки
uniform float grid_size_large : hint_range(20.0, 200.0) = 40.0;
uniform float grid_size_small : hint_range(5.0, 50.0) = 10.0;
uniform float line_width : hint_range(0.5, 3.0) = 1.06;

// Цвета
uniform vec4 grid_color_large : source_color = vec4(0.153, 0.6, 0.812, 0.251);
uniform vec4 grid_color_small : source_color = vec4(0.098, 0.4, 0.569, 0.15);
uniform vec4 vignette_color : source_color = vec4(0.0, 0.0, 0.0, 0.4);

// Эффекты
uniform float vignette_strength : hint_range(0.0, 1.0) = 0.3;
uniform float scanline_intensity : hint_range(0.0, 0.2) = 0.05;
uniform float noise_intensity : hint_range(0.0, 0.1) = 0.02;

// Интенсивность (управляется из кода)
uniform float intensity : hint_range(0.0, 1.0) = 1.0;

// Анимация
uniform float time_scale : hint_range(0.0, 4.0) = 3.0;

// Простой псевдо-шум
float hash(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float grid_line(float coord, float size, float width) {
    float half_width = width * 0.5;
    float mod_coord = mod(coord, size);
    
    float line = smoothstep(half_width, 0.0, mod_coord) + 
                 smoothstep(size - half_width, size, mod_coord);
    
    return clamp(line, 0.0, 1.0);
}

void fragment() {
    vec2 screen_size = 1.0 / SCREEN_PIXEL_SIZE;
    vec2 pixel_coord = FRAGCOORD.xy;
    vec2 uv = UV;
    
    // === СЕТКА ===
    float large_grid_x = grid_line(pixel_coord.x, grid_size_large, line_width);
    float large_grid_y = grid_line(pixel_coord.y, grid_size_large, line_width);
    float large_grid = max(large_grid_x, large_grid_y);
    
    float small_grid_x = grid_line(pixel_coord.x, grid_size_small, line_width * 0.5);
    float small_grid_y = grid_line(pixel_coord.y, grid_size_small, line_width * 0.5);
    float small_grid = max(small_grid_x, small_grid_y);
    
    // Пересечения (ярче)
    float large_cross = large_grid_x * large_grid_y;
    
    // Комбинируем сетки
    vec4 grid = grid_color_large * large_grid + grid_color_small * small_grid * (1.0 - large_grid);
    grid += grid_color_large * large_cross * 0.5; // Яркие точки на пересечениях
    
    // === ВИНЬЕТКА ===
    vec2 center_uv = uv - 0.5;
    float vignette = dot(center_uv, center_uv);
    vignette = smoothstep(0.2, 0.8, vignette) * vignette_strength;
    
    // === СКАНЛАЙНЫ ===
    float scanline = sin(pixel_coord.y * 1.5 + TIME * time_scale * 10.0) * 0.5 + 0.5;
    scanline = scanline * scanline_intensity;
    
    // === ШУМ ===
    float noise = (hash(pixel_coord * 0.1 + TIME * time_scale) - 0.5) * noise_intensity;
    
    // === ФИНАЛЬНЫЙ ЦВЕТ ===
    vec4 final_color = grid;
    
    // Добавляем виньетку
    final_color.rgb = mix(final_color.rgb, vignette_color.rgb, vignette);
    final_color.a += vignette * vignette_color.a * 0.5;
    
    // Сканлайны и шум
    final_color.a += scanline + noise;
    
    // Свечение в центре (crosshair area) - меньше сетки в центре
    float center_glow = 1.0 - smoothstep(0.0, 0.12, length(center_uv));
    final_color.a *= (1.0 - center_glow * 0.6);
    
    // === ПРИМЕНЯЕМ ИНТЕНСИВНОСТЬ ===
    // Нелинейная интерполяция для более плавного появления
    float adjusted_intensity = intensity * intensity; // квадратичная кривая
    final_color.a *= adjusted_intensity;
    
    // Цвет тоже немного меняется с интенсивностью
    final_color.rgb = mix(final_color.rgb * 0.7, final_color.rgb, intensity);
    
    COLOR = final_color;
}