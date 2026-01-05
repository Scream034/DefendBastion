using Godot;
using Game.Projectiles; // Предполагаем, что ваши снаряды лежат здесь и наследуются от Node3D/Area3D

namespace Game.Entity.AI.Components
{
    [GlobalClass]
    public partial class AIThreatSensor : Node
    {
        [Export] private Area3D _sensorArea;

        private AIEntity _context;

        public void Initialize(AIEntity context)
        {
            _context = context;

            if (_sensorArea == null)
            {
                GD.PushWarning($"AIThreatSensor on {_context.Name} is missing detection Area3D!");
                return;
            }

            // Подписываемся на вход снарядов (обычно они Area3D)
            _sensorArea.AreaEntered += OnAreaThreatDetected;
        }

        private void OnAreaThreatDetected(Area3D area)
        {
            // Проверяем, что это снаряд
            if (area is BaseProjectile projectile)
            {
                ProcessProjectileThreat(projectile);
            }
        }

        private void ProcessProjectileThreat(BaseProjectile projectile)
        {
            // 1. Игнорируем свои снаряды и снаряды союзников (если есть ссылка на Owner)
            if (projectile.Initiator == _context) return;

            // 2. Вычисляем позицию угрозы. 
            // В идеале мы хотим посмотреть не на пулю, а ОТКУДА она прилетела.
            // Приближенно: берем позицию пули. Более умно: пуля + обратный вектор скорости.
            Vector3 threatPos = projectile.GlobalPosition;

            // 3. Сообщаем мозгу
            _context.ReportThreat(threatPos, isDirectHit: false);
        }
    }
}