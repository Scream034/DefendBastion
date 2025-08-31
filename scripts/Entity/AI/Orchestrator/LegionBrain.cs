using Game.Entity.AI.Components;
using Game.Entity.AI.States.Squad;
using Game.Singletons;
using Godot;
using System.Collections.Generic;

namespace Game.Entity.AI.Orchestrator
{
    public partial class LegionBrain : Node
    {
        public static LegionBrain Instance { get; private set; }

        private readonly Dictionary<string, AISquad> _squads = [];

        private readonly Queue<CombatState> _tacticalQueue = new();
        /// <summary>
        /// Максимальное кол-во отрядов для обновления в одном кадре
        /// </summary>
        [Export] private int _maxTacticalUpdatesPerFrame = 2;

        public override void _EnterTree() => Instance = this;

        public override void _Ready()
        {
            Constants.Tree.CreateTimer(0.1).Timeout += FinalizeSquadInitialization;

            // Подписываемся на глобальные события
            AISignals.Instance.EnemySighted += OnEnemySighted;
        }

        public override void _PhysicsProcess(double delta)
        {
            int processedCount = 0;
            while (_tacticalQueue.Count > 0 && processedCount < _maxTacticalUpdatesPerFrame)
            {
                var combatState = _tacticalQueue.Dequeue();
                // Убедимся, что состояние все еще актуально
                if (combatState != null && combatState.IsActive)
                {
                    combatState.ExecuteTacticalEvaluation();
                    processedCount++;
                }
            }
        }

        /// <summary>
        /// Отряды больше не вычисляют тактику сами, а отправляют запрос сюда.
        /// </summary>
        public void RequestTacticalEvaluation(CombatState combatState)
        {
            if (!_tacticalQueue.Contains(combatState))
            {
                _tacticalQueue.Enqueue(combatState);
            }
        }

        // Обработчик события обнаружения врага
        private void OnEnemySighted(AIEntity reporter, LivingEntity target)
        {
            if (reporter.Squad == null) return;

            // Если отряд уже занят другой целью, можно добавить логику приоритетов.
            // Пока просто отдаем приказ атаковать новую цель.
            reporter.Squad.AssignCombatTarget(target);
        }

        public void RegisterSquad(AISquad squad)
        {
            if (!_squads.ContainsKey(squad.SquadName))
            {
                _squads[squad.SquadName] = squad;
                GD.Print($"Legion Brain: Squad '{squad.SquadName}' registered.");
            }
        }

        private void FinalizeSquadInitialization()
        {
            foreach (var squad in _squads.Values)
            {
                squad.InitializeMembersFromGroup();
            }
        }

        public void OrderSquadToMove(string squadName, Vector3 targetPosition)
        {
            if (_squads.TryGetValue(squadName, out var squad))
            {
                squad.AssignMoveTarget(targetPosition);
            }
            else
            {
                GD.PushWarning($"Legion Brain: Attempted to order non-existent squad '{squadName}'.");
            }
        }
    }
}