using Godot;
using System.Collections.Generic; // Может быть не нужен, если List не используется
using Game.Entity.AI.Components;

namespace Game.Entity.AI.Behaviors
{
    public partial class StationaryCombatBehavior : Node, ICombatBehavior
    {
        [Export] public float AttackRange { get; private set; } = 15f;
        [Export] public float AttackCooldown { get; private set; } = 2.0f;
        // [Export(PropertyHint.Range, "3, 20, 1")] private float _repositionSearchRadius = 10f; // Это поле теперь не используется
        [Export] private Node _attackActionNode;

        public IAttackAction Action { get; private set; } // <--- УБЕДИТЕСЬ, ЧТО ОНО ЗДЕСЬ

        private double _timeSinceLastAttack = 0;
        private double _effectiveAttackCooldown;
        private bool _isRepositioning = false; // Это поле теперь не используется, логика перенесена в AISquad

        // const и _allyRepositionCooldown больше не используются в этой версии
        // private const double ALLY_REPOSITION_COOLDOWN_TIME = 1.5;
        // private double _allyRepositionCooldown = 0;

        public override void _Ready()
        {
            if (_attackActionNode is IAttackAction action) Action = action;
            else { GD.PushError($"Для {GetPath()} не назначен узел с IAttackAction!"); SetProcess(false); return; }

            var variance = (float)GD.RandRange(-0.1, 0.1);
            _effectiveAttackCooldown = AttackCooldown * (1.0f + variance);

            _timeSinceLastAttack = _effectiveAttackCooldown; // Готов к атаке сразу
        }

        public void EnterCombat(AIEntity context)
        {
            _timeSinceLastAttack = _effectiveAttackCooldown;
            // _allyRepositionCooldown = 0; // Больше не используется
            // ResetRepositioningState(context); // Логика перенесена
        }

        public void ExitCombat(AIEntity context)
        {
            // ResetRepositioningState(context); // Логика перенесена
        }

        // Метод Process больше не нужен, его логика распределена между AIEntity и AISquad
        /*
        public bool Process(AIEntity context, double delta)
        {
            // ... (вся логика этого метода удалена)
            return true;
        }
        */

        // Эти вспомогательные методы больше не нужны, так как логика перенесена
        // private bool AttemptReposition(AIEntity context, LivingEntity target, bool preferSidestep) { return false; }
        // private void ResetRepositioningState(AIEntity context) { }
        // private bool CanSeeTarget(AIEntity context, LivingEntity target) { return false; }
    }
}