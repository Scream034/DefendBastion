using Godot;
using Game.Entity.AI.Components;

namespace Game.Entity.AI.States.Squad
{
    public class SearchState : SquadStateBase
    {
        private double _timer;
        private double _scanTimer;
        private int _scanDirection = 1;

        public SearchState(AISquad squad) : base(squad) { }

        public override void Enter()
        {
            GD.Print($"Squad '{Squad.Name}' reached Last Known Position. Starting SEARCH.");
            _timer = Squad.SearchDuration;
            
            // Останавливаемся и переходим в режим внимательного осмотра
            foreach (var member in Squad.Members)
            {
                member.ClearOrders(); // Стоп
                member.SetMovementSpeed(AIEntity.MovementSpeedType.Slow); // Если вдруг надо будет шагнуть
                // Можно запустить анимацию "Idle_Alert" если есть
            }
        }

        public override void Process(double delta)
        {
            _timer -= delta;

            // 1. ПРОВЕРКА: Вдруг мы увидели врага?
            if (GodotObject.IsInstanceValid(Squad.CurrentTarget) && Squad.CurrentTarget.IsAlive)
            {
                foreach (var member in Squad.Members)
                {
                    if (member.GetVisibleTargetPoint(Squad.CurrentTarget).HasValue)
                    {
                        GD.Print($"Squad '{Squad.Name}' found target during search! Re-engaging.");
                        Squad.ChangeState(new CombatState(Squad, Squad.CurrentTarget));
                        return;
                    }
                }
            }
            // Также проверяем ThreatSensor или TargetingSystem на появление НОВЫХ врагов
            // (Это делает TargetingSystem сама, но если цель та же - мы перехватили выше)

            // 2. Имитация осмотра (сканирование)
            ScanArea(delta);

            // 3. Выход по таймеру
            if (_timer <= 0)
            {
                GD.Print($"Squad '{Squad.Name}' found nothing. Returning to patrol.");
                Squad.Disengage();
            }
        }

        private void ScanArea(double delta)
        {
            _scanTimer -= delta;
            if (_scanTimer <= 0)
            {
                _scanTimer = GD.RandRange(1.5, 3.0);
                _scanDirection *= -1; // Меняем направление

                foreach (var member in Squad.Members)
                {
                    // Заставляем смотреть в случайную сторону относительно текущего направления
                    float randomAngle = (float)GD.RandRange(45, 120) * _scanDirection;
                    Vector3 forward = -member.GlobalBasis.Z;
                    Vector3 lookDir = forward.Rotated(Vector3.Up, Mathf.DegToRad(randomAngle));
                    Vector3 lookPos = member.GlobalPosition + lookDir * 10f;
                    
                    // Используем LookController напрямую или через InterestPoint
                    member.LookController.SetInterestPoint(lookPos);
                    
                    // Опционально: можно заставить их сделать пару шагов (Roaming)
                    // member.ReceiveOrderMoveTo(member.GlobalPosition + lookDir * 2f);
                }
            }
        }
    }
}