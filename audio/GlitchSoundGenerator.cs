using Godot;

namespace Game.Audio;

/// <summary>
/// Генерирует процедурный звук "хендшейка" (подключения).
/// Смешивает статический шум и цифровой писк (Square Wave).
/// </summary>
public partial class GlitchSoundGenerator : Node
{
    [Export] public float VolumeDb { get; set; } = -10.0f;

    // Ссылки на плеер и генератор
    private AudioStreamPlayer _player;
    private AudioStreamGenerator _generator;
    private AudioStreamGeneratorPlayback _playback;

    // Параметры генерации
    private float _sampleRate = 44100.0f;
    private double _time = 0.0;
    
    // Состояние текущего звука
    private bool _isPlaying = false;
    private float _durationRemaining = 0.0f;
    
    // Характеристики текущего глитча (рандомятся каждый раз)
    private float _carrierFreq = 0.0f; // Основная частота писка
    private float _noiseMix = 0.0f;    // Сколько шума добавлять (0.0 - 1.0)
    private float _chopRate = 0.0f;    // Частота прерываний

    public override void _Ready()
    {
        _player = new AudioStreamPlayer();
        AddChild(_player);

        // Настраиваем генератор
        _generator = new()
        {
            MixRate = _sampleRate,
            BufferLength = 0.1f // Короткий буфер для минимальной задержки
        };

        _player.Stream = _generator;
        _player.VolumeDb = VolumeDb;
        
        // Важно: Направь это на отдельную шину с эффектами, если хочешь (об этом ниже)
        // _player.Bus = "UI_Glitch"; 
    }

    /// <summary>
    /// Запускает генерацию уникального звука подключения.
    /// </summary>
    public void PlayConnectSound()
    {
        if (!_player.Playing) _player.Play();
        _playback = (AudioStreamGeneratorPlayback)_player.GetStreamPlayback();

        // Рандомизация параметров для уникальности
        var rng = new RandomNumberGenerator();
        rng.Randomize();

        // Длительность звука: от 0.3 до 0.6 секунды
        _durationRemaining = rng.RandfRange(0.3f, 0.6f);
        
        // Частота "модема": от 800 Гц до 3000 Гц (пронзительный цифровой звук)
        _carrierFreq = rng.RandfRange(800.0f, 3000.0f);
        
        // Количество шума: иногда чистый писк, иногда сплошной мусор
        _noiseMix = rng.RandfRange(0.2f, 0.8f);
        
        // Частота прерываний сигнала (Chopping): от 10 Гц до 50 Гц
        _chopRate = rng.RandfRange(10.0f, 50.0f);

        _isPlaying = true;
        _time = 0.0;
    }

    public override void _Process(double delta)
    {
        if (!_isPlaying || _playback == null) return;

        // Заполняем буфер
        FillBuffer();

        _durationRemaining -= (float)delta;
        if (_durationRemaining <= 0)
        {
            _isPlaying = false;
            // Заполняем остаток тишиной, чтобы не щелкало
            ClearBuffer(); 
            _player.Stop();
        }
    }

    private void FillBuffer()
    {
        int framesAvailable = _playback.GetFramesAvailable();
        if (framesAvailable < 1) return;

        var buffer = new Vector2[framesAvailable];
        float increment = 1.0f / _sampleRate;

        for (int i = 0; i < framesAvailable; i++)
        {
            // 1. Генерация несущей частоты (Квадратная волна для жесткости)
            // Square Wave: возвращает 1.0 или -1.0
            float signal = Mathf.Sin((float)_time * _carrierFreq * Mathf.Tau) > 0 ? 0.5f : -0.5f;

            // Иногда меняем частоту прямо посреди звука (Frequency Shift Glitch)
            if (_time % 0.1 < 0.01) 
            {
                 signal *= -1.0f; // Инверсия фазы
            }

            // 2. Генерация белого шума
            float noise = (GD.Randf() * 2.0f) - 1.0f;

            // 3. Эффект "Chopper" (прерывание сигнала)
            // Синус низкой частоты используется как множитель громкости (AM-синтез)
            float chopper = Mathf.Sin((float)_time * _chopRate * Mathf.Tau) > 0 ? 1.0f : 0.0f;

            // 4. Смешивание
            float finalSample = Mathf.Lerp(signal, noise, _noiseMix);
            finalSample *= chopper; // Применяем прерывания
            
            // Fade Out в конце (чтобы не было щелчка при остановке)
            if (_durationRemaining < 0.05f)
            {
                finalSample *= _durationRemaining / 0.05f;
            }

            // Стерео (одинаково в обоих ушах, но можно сдвинуть фазу)
            buffer[i] = new Vector2(finalSample, finalSample);

            _time += increment;
        }

        _playback.PushBuffer(buffer);
    }
    
    private void ClearBuffer()
    {
         // Очистка остатков (необязательно, но полезно для гигиены аудио)
    }
}