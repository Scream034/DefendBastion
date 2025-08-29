shader_type spatial;
render_mode blend_add, depth_draw_never, cull_disabled, unshaded, fog_disabled;

// Время жизни эффекта
uniform float lifetime : hint_range(0.0, 1.0) = 1.0; // Контролируется из кода

// Настройки вспышки (Railgun Impact)
uniform vec4 flash_color : source_color = vec4(1.0, 0.9, 0.7, 1.0); // Более белый/золотистый цвет для удара
uniform float flash_intensity : hint_range(1.0, 100.0) = 60.0; // Увеличиваем интенсивность для "удара"
uniform float flash_spokes : hint_range(4.0, 16.0) = 10.0; // Количество радиальных лучей
uniform float flash_focus : hint_range(1.0, 10.0) = 5.0; // Фокусировка центральной вспышки

// Настройки молний (3D Turbulence)
uniform vec4 lightning_color : source_color = vec4(0.4, 0.7, 1.0, 1.0);
uniform float lightning_intensity : hint_range(1.0, 30.0) = 20.0;
uniform int lightning_branches : hint_range(3, 8) = 6;
uniform float lightning_thickness : hint_range(0.01, 0.1) = 0.03;
uniform float lightning_chaos : hint_range(0.0, 1.0) = 0.8;
uniform float lightning_turbulence_scale : hint_range(1.0, 50.0) = 35.0; // Масштаб шума

// Генератор псевдослучайных чисел (для хаоса)
float hash12(vec2 p) {
    vec3 p3  = fract(vec3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return fract((p3.x + p3.y) * p3.z);
}

// Упрощенная функция шума (для турбулентности)
float noise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);

    float a = hash12(i);
    float b = hash12(i + vec2(1.0, 0.0));
    float c = hash12(i + vec2(0.0, 1.0));
    float d = hash12(i + vec2(1.0, 1.0));

    return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

// Генерация молнии с турбулентностью
float lightning_bolt(vec2 uv, float angle, float time, float seed) {
    // Поворачиваем UV для направления молнии
    float c = cos(angle);
    float s = sin(angle);
    uv = vec2(c * uv.x - s * uv.y, s * uv.x + c * uv.y);

    // Добавляем турбулентность, чтобы создать 3D эффект и движение
    float turb_x = noise(vec2(uv.x * lightning_turbulence_scale, time * 0.5 + seed));
    float turb_y = noise(vec2(uv.x * lightning_turbulence_scale * 0.5, time * 0.5 - seed));

    // Сдвигаем вертикальную позицию (y) в зависимости от турбулентности
    float chaos_offset = lightning_chaos * (turb_x * 2.0 - 1.0) * 0.1;

    // Добавляем быстрый "излом" для более резкого вида
    chaos_offset += sin(uv.x * 20.0 + time * 50.0 + seed * 100.0) * lightning_chaos * 0.05;

    float dist = abs(uv.y - chaos_offset);

    // Толщина молнии (основной ствол)
    float bolt = 1.0 - dist / lightning_thickness;
    bolt = clamp(bolt, 0.0, 1.0);
    bolt = pow(bolt, 3.0); // Увеличиваем степень для более четкого ядра

    // Затухание по длине (должна быть яркой только на конце)
    // Используем экспоненциальное затухание для более резкого конца
    float length_fade = 1.0 - pow(length(uv), 1.5);

    return bolt * length_fade;
}

void fragment() {
    vec2 uv = UV * 2.0 - 1.0; // Центрируем UV (-1 до 1)
    float dist_from_center = length(uv);

    // Фаза анимации: 0.0 = начало, 1.0 = конец
    float phase = 1.0 - lifetime;

    // === ВСПЫШКА РЕЛЬСОТРОНА (IMPACT FLASH) ===

    // Фаза вспышки: очень быстрая, пик в 0.1, затухание к 0.5
    float flash_phase = clamp(phase * 5.0, 0.0, 1.0);

    // 1. Центральное ядро
    // Используем высокую степень для фокусировки света
    float core_flash = pow(1.0 - dist_from_center, flash_focus * 2.0);

    // 2. Радиальные лучи (Spokes)
    float angle = atan(uv.y, uv.x);
    float spokes_mask = abs(sin(angle * flash_spokes));

    // Делаем лучи острыми
    spokes_mask = pow(spokes_mask, 5.0) * 0.5 + 0.5;

    // Применяем маску лучей и затухание по расстоянию
    float radial_flash = core_flash * spokes_mask;

    // Общая вспышка затухает очень быстро
    float final_flash = max(core_flash, radial_flash) * (1.0 - flash_phase);

    // === МОЛНИИ (EXPANDING LIGHTNING) ===

    // Молнии активны после пика вспышки
    float lightning_phase = clamp((phase - 0.05) * 1.5, 0.0, 1.0);
    float lightning = 0.0;

    // Расширение: молнии "выстреливают" из центра
    float expansion_factor = 1.0 / (lightning_phase * 1.5 + 0.05);
    vec2 lightning_uv = uv * expansion_factor;

    for (int i = 0; i < lightning_branches; i++) {
        float angle = (float(i) / float(lightning_branches)) * 6.28318;
        float seed = hash12(vec2(float(i), TIME));

        float bolt = lightning_bolt(lightning_uv, angle, TIME, seed);
        lightning = max(lightning, bolt);
    }

    // Молнии затухают по мере завершения фазы
    lightning *= pow(1.0 - lightning_phase, 2.0);

    // === ФИНАЛЬНЫЙ ЦВЕТ И СВЕЧЕНИЕ ===
    vec3 final_color = vec3(0.0);

    // 1. Вспышка (Impact Core)
    final_color += flash_color.rgb * final_flash * flash_intensity;

    // 2. Молнии и свечение
    // Используем гладкую ступеньку для лучшего свечения вокруг молний
    float glow_mask = smoothstep(0.0, 0.5, lightning);

    // Основной ствол
    final_color += lightning_color.rgb * lightning * lightning_intensity;

    // Волюметрическое свечение
    final_color += lightning_color.rgb * glow_mask * lightning_intensity * 0.4;


    EMISSION = final_color;
    // Альфа-канал определяет прозрачность
    ALPHA = clamp(final_flash + glow_mask, 0.0, 1.0);
}