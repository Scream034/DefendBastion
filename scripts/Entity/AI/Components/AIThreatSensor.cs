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
            if (projectile.Initiator == _context) return;

            Vector3 estimatedOrigin = projectile.Initiator.GlobalPosition + ((float)GD.RandRange(-1.0, 1.0) * Vector3.One); // Пример оценки позиции угрозы

            _context.ReportThreat(estimatedOrigin, isDirectHit: false);
        }
    }
}