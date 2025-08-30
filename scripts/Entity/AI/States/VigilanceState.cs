using Godot;
using System;

namespace Game.Entity.AI.States
{
    /// <summary>
    /// Состояние бдительности после боя. ИИ осматривается на месте
    /// и/или медленно движется, чтобы проверить окружение перед возвращением к обычным задачам.
    /// </summary>
    public sealed class VigilanceState(AIEntity context) : State(context)
    {
        private enum SubState
        {
            Scanning,
            Strafing
        }

        private float _totalVigilanceTimer;
        private float _currentActionTimer;
        private Vector3 _strafeDirection;

        public override void Enter()
        {
            GD.Print($"{_context.Name} entering Vigilance state. Securing area around {_context.LastEngagementPosition}.");
            _context.MovementController.StopMovement();
            _context.SetMovementSpeed(_context.Profile.MovementProfile.SlowSpeed);
            _totalVigilanceTimer = _context.Profile.CombatProfile.VigilanceDuration;
            _context.LookController.SetInterestPoint(_context.LastEngagementPosition);
            ChooseNextAction();
        }

        public override void Exit()
        {
            _context.LookController.SetInterestPoint(null);
        }

        public override void Update(float delta)
        {
            // Главный приоритет: если появилась новая цель, немедленно атакуем.
            if (_context.TargetingSystem.CurrentTarget != null)
            {
                _context.ChangeState(new AttackState(_context));
                return;
            }

            _totalVigilanceTimer -= delta;
            _currentActionTimer -= delta;

            // Если общее время бдительности вышло, возвращаемся к стандартным задачам.
            if (_totalVigilanceTimer <= 0f)
            {
                GD.Print($"{_context.Name} vigilance complete. Returning to default state.");
                _context.ReturnToDefaultState();
                return;
            }

            // Если время на текущее действие вышло, выбираем новое.
            if (_currentActionTimer <= 0f)
            {
                ChooseNextAction();
            }
        }

        private void ChooseNextAction()
        {
            // Сбрасываем таймер для следующего действия (например, 2-3 секунды на одно действие).
            _currentActionTimer = (float)GD.RandRange(2.0, 3.5);
            bool canStrafe = _context.Profile.CombatProfile.AllowVigilanceStrafe;

            // Если стрейф запрещен или выпадает шанс, просто сканируем местность.
            if (!canStrafe || GD.Randf() > 0.6)
            {
                _context.MovementController.StopMovement();
                GD.Print($"{_context.Name} vigilance: Scanning.");
            }
            else // Иначе выбираем направление для стрейфа.
            {
                var directionToLastEnemy = _context.GlobalPosition.DirectionTo(_context.LastEngagementPosition);
                // Получаем вектор, перпендикулярный направлению на врага (для стрейфа влево/вправо).
                var perpendicular = directionToLastEnemy.Cross(Vector3.Up).Normalized();
                _strafeDirection = Random.Shared.Next(0, 2) == 0 ? perpendicular : -perpendicular;

                var targetPos = _context.GlobalPosition + _strafeDirection * 2f;
                _context.MovementController.MoveTo(NavigationServer3D.MapGetClosestPoint(_context.GetWorld3D().NavigationMap, targetPos));
                GD.Print($"{_context.Name} vigilance: Strafing towards {_strafeDirection}.");
            }
        }
    }
}