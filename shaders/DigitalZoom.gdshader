shader_type canvas_item;

// === ПИКСЕЛИЗАЦИЯ ===
uniform float pixelation_intensity : hint_range(0.0, 1.0) = 0.0;
uniform float min_pixel_size : hint_range(1.0, 4.0) = 1.0;
uniform float max_pixel_size : hint_range(4.0, 32.0) = 16.0;

// === ЭФФЕКТЫ ЗУМА ===
uniform float zoom_level : hint_range(1.0, 10.0) = 1.0;
uniform float scanline_intensity : hint_range(0.0, 1.0) = 0.3;
uniform float vignette_intensity : hint_range(0.0, 1.0) = 0.4;
uniform float chromatic_aberration : hint_range(0.0, 5.0) = 1.0;
uniform float noise_intensity : hint_range(0.0, 0.3) = 0.05;

// === ЦВЕТА ===
uniform vec4 tint_color : source_color = vec4(0.2, 0.9, 0.8, 1.0);
uniform float tint_strength : hint_range(0.0, 0.5) = 0.1;

// === SAMPLER ===
uniform sampler2D screen_texture : hint_screen_texture, filter_linear_mipmap;

float hash(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

void fragment() {
    vec2 screen_size = 1.0 / SCREEN_PIXEL_SIZE;
    vec2 uv = SCREEN_UV;
    vec2 pixel_coord = FRAGCOORD.xy;
    
    // === ПИКСЕЛИЗАЦИЯ ===
    float pixel_size = mix(min_pixel_size, max_pixel_size, pixelation_intensity);
    
    vec2 pixelated_uv = uv;
    if (pixelation_intensity > 0.01) {
        // Округляем координаты до размера пикселя
        vec2 pixel_count = screen_size / pixel_size;
        pixelated_uv = floor(uv * pixel_count) / pixel_count;
        pixelated_uv += 0.5 / pixel_count; // Центрирование
    }
    
    // === ХРОМАТИЧЕСКАЯ АБЕРРАЦИЯ ПРИ ЗУМЕ ===
    vec3 color;
    if (chromatic_aberration > 0.01 && pixelation_intensity > 0.1) {
        float aberration = chromatic_aberration * pixelation_intensity * 0.002;
        vec2 dir = pixelated_uv - vec2(0.5);
        float dist = length(dir);
        vec2 offset = normalize(dir) * dist * aberration;
        
        color.r = texture(screen_texture, pixelated_uv + offset).r;
        color.g = texture(screen_texture, pixelated_uv).g;
        color.b = texture(screen_texture, pixelated_uv - offset).b;
    } else {
        color = texture(screen_texture, pixelated_uv).rgb;
    }
    
    // === СКАН-ЛИНИИ (усиливаются при зуме) ===
    if (scanline_intensity > 0.01 && pixelation_intensity > 0.05) {
        float scanline_period = max(2.0, pixel_size * 0.5);
        float scanline = sin(pixel_coord.y / scanline_period * 3.14159) * 0.5 + 0.5;
        scanline = pow(scanline, 0.5);
        float scanline_alpha = scanline_intensity * pixelation_intensity;
        color *= 1.0 - (1.0 - scanline) * scanline_alpha * 0.3;
    }
    
    // === ШУМ СЕНСОРА ===
    if (noise_intensity > 0.001 && pixelation_intensity > 0.1) {
        float time_seed = TIME * 10.0;
        float noise = hash(pixelated_uv * 1000.0 + time_seed) - 0.5;
        color += noise * noise_intensity * pixelation_intensity;
    }
    
    // === ВИНЬЕТКА (усиливается при зуме) ===
    if (vignette_intensity > 0.01) {
        vec2 vignette_uv = uv * 2.0 - 1.0;
        float vignette = 1.0 - dot(vignette_uv, vignette_uv) * 0.5;
        vignette = pow(vignette, 1.5);
        
        float vig_strength = vignette_intensity * (0.3 + pixelation_intensity * 0.7);
        color *= mix(1.0, vignette, vig_strength);
    }
    
    // === ЦВЕТОВОЙ ОТТЕНОК (циановый, как у прицела) ===
    if (tint_strength > 0.001 && pixelation_intensity > 0.05) {
        vec3 tinted = mix(color, color * tint_color.rgb * 1.5, tint_strength * pixelation_intensity);
        color = tinted;
    }
    
    // === ГРАНИЦЫ ПИКСЕЛЕЙ (сетка) ===
    if (pixelation_intensity > 0.3) {
        vec2 grid = fract(pixel_coord / pixel_size);
        float grid_line = 1.0;
        
        float line_width = 0.08;
        if (grid.x < line_width || grid.x > 1.0 - line_width ||
            grid.y < line_width || grid.y > 1.0 - line_width) {
            grid_line = 0.92;
        }
        
        float grid_alpha = (pixelation_intensity - 0.3) / 0.7 * 0.5;
        color *= mix(1.0, grid_line, grid_alpha);
    }
    
    // === ИНДИКАТОР УВЕЛИЧЕНИЯ (угловые метки) ===
    float corner_size = 60.0;
    float corner_thickness = 2.0;
    float corner_margin = 80.0;
    
    if (zoom_level > 1.1) {
        float zoom_indicator_alpha = clamp((zoom_level - 1.0) / 3.0, 0.0, 0.8);
        
        // Четыре угла
        bool in_corner = false;
        
        // Левый верхний
        if (pixel_coord.x < corner_margin + corner_size && pixel_coord.y < corner_margin + corner_size) {
            if ((pixel_coord.x > corner_margin && pixel_coord.x < corner_margin + corner_thickness && pixel_coord.y > corner_margin && pixel_coord.y < corner_margin + corner_size) ||
                (pixel_coord.y > corner_margin && pixel_coord.y < corner_margin + corner_thickness && pixel_coord.x > corner_margin && pixel_coord.x < corner_margin + corner_size)) {
                in_corner = true;
            }
        }
        // Правый верхний
        if (pixel_coord.x > screen_size.x - corner_margin - corner_size && pixel_coord.y < corner_margin + corner_size) {
            if ((pixel_coord.x < screen_size.x - corner_margin && pixel_coord.x > screen_size.x - corner_margin - corner_thickness && pixel_coord.y > corner_margin && pixel_coord.y < corner_margin + corner_size) ||
                (pixel_coord.y > corner_margin && pixel_coord.y < corner_margin + corner_thickness && pixel_coord.x < screen_size.x - corner_margin && pixel_coord.x > screen_size.x - corner_margin - corner_size)) {
                in_corner = true;
            }
        }
        // Левый нижний
        if (pixel_coord.x < corner_margin + corner_size && pixel_coord.y > screen_size.y - corner_margin - corner_size) {
            if ((pixel_coord.x > corner_margin && pixel_coord.x < corner_margin + corner_thickness && pixel_coord.y < screen_size.y - corner_margin && pixel_coord.y > screen_size.y - corner_margin - corner_size) ||
                (pixel_coord.y < screen_size.y - corner_margin && pixel_coord.y > screen_size.y - corner_margin - corner_thickness && pixel_coord.x > corner_margin && pixel_coord.x < corner_margin + corner_size)) {
                in_corner = true;
            }
        }
        // Правый нижний
        if (pixel_coord.x > screen_size.x - corner_margin - corner_size && pixel_coord.y > screen_size.y - corner_margin - corner_size) {
            if ((pixel_coord.x < screen_size.x - corner_margin && pixel_coord.x > screen_size.x - corner_margin - corner_thickness && pixel_coord.y < screen_size.y - corner_margin && pixel_coord.y > screen_size.y - corner_margin - corner_size) ||
                (pixel_coord.y < screen_size.y - corner_margin && pixel_coord.y > screen_size.y - corner_margin - corner_thickness && pixel_coord.x < screen_size.x - corner_margin && pixel_coord.x > screen_size.x - corner_margin - corner_size)) {
                in_corner = true;
            }
        }
        
        if (in_corner) {
            color = mix(color, tint_color.rgb, zoom_indicator_alpha);
        }
    }
    
    COLOR = vec4(color, 1.0);
}