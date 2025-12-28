shader_type canvas_item;

// Настройки цвета (Tweak these in Inspector)
uniform vec3 tint_color : source_color = vec3(0.0, 0.48, 0.81); // Голубой
uniform float saturation : hint_range(0.0, 1.0) = 0.8;
uniform float brightness : hint_range(0.0, 2.0) = 1.0;
uniform float contrast : hint_range(0.0, 2.0) = 1.0;

// Настройки шума
uniform float noise_amount : hint_range(0.0, 1.0) = 0.03;
uniform float pixelation : hint_range(0.0, 10.0) = 0.8; // 0.0 = нет пикселизации

uniform sampler2D SCREEN_TEXTURE : hint_screen_texture, filter_linear_mipmap;

// Генератор случайных чисел
float random(vec2 uv) {
    return fract(sin(dot(uv, vec2(12.9898, 78.233))) * 43758.5453123);
}

void fragment() {
    // 1. Пикселизация (опционально при низком здоровье)
    vec2 grid_uv = round(SCREEN_UV * (1000.0 / pixelation)) / (1000.0 / pixelation);
    
    // 2. Получаем цвет мира
    vec4 screen_color = texture(SCREEN_TEXTURE, grid_uv);
    
    // 3. Обесцвечивание (Luma conversion)
    float gray = dot(screen_color.rgb, vec3(0.299, 0.587, 0.114));
    
    // 4. Применяем контраст и яркость к ч/б изображению
    gray = (gray - 0.5) * contrast + 0.5;
    gray *= brightness;
    
    // 5. Смешиваем ч/б базу с исходным цветом (оставляем чуть-чуть родных цветов)
    vec3 final_color = mix(vec3(gray), screen_color.rgb, saturation);
    
    // 6. Накладываем цифровой тинт (Multiply mode)
    // Умножаем светлые участки на наш tint_color
    final_color *= tint_color * 1.5; // чтобы не было слишком темно
    
    // 7. Цифровой шум (Static)
    // Шум меняется каждый кадр (TIME)
    float noise = random(grid_uv + vec2(TIME * 10.0, 0.0));
    final_color += (noise - 0.5) * noise_amount;

    COLOR = vec4(final_color, 1.0);
}