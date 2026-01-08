using Godot;
using Game.Entity.AI.Components;

namespace Game.Entity.AI.States.Squad;

/// <summary>
/// Состояние преследования.
/// Реализует две стратегии:
/// 1. Перехват (Intercept): Если цель видна И находится в зоне сенсоров -> бежим на опережение.
/// 2. Расследование (Investigate): Если цель потеряна (за стеной или вышла из зоны) -> бежим к последней известной позиции.
/// Если цель не найдена в последней точке -> переход в SearchState.
/// </summary>
public class PursuitState : SquadStateBase
{
    private double _reevaluateTimer;
    private double _targetVelocityTimer;
    private double _pursuitTimer;
    private bool _isChasingVisibleTarget = true;

    public PursuitState(AISquad squad, LivingEntity target) : base(squad)
    {
        Squad.CurrentTarget = target;
    }

    public override void Enter()
    {
        if (!Squad.CanPursueTarget)
        {
            GD.Print($"Squad '{Squad.Name}' cannot pursue. Switching to Search immediately.");
            Squad.ChangeState(new SearchState(Squad));
            return;
        }

        GD.Print($"Squad '{Squad.Name}' PURSUIT started.");

        foreach (var member in Squad.Members)
        {
            member.SetMovementSpeed(AIEntity.MovementSpeedType.Fast);
        }

        _reevaluateTimer = 0.5f;
        _targetVelocityTimer = Squad.TargetVelocityTrackInterval;
        _pursuitTimer = 0;

        UpdateLogic();
    }

    public override void Process(double delta)
    {
        // 1. Валидация цели
        if (!GodotObject.IsInstanceValid(Squad.CurrentTarget) || !Squad.CurrentTarget.IsAlive)
        {
            Squad.ChangeState(new SearchState(Squad)); // Проверяем, может он умер там
            return;
        }

        _pursuitTimer += delta;

        // 2. Глобальный предохранитель (если цель улетела в космос)
        float distToTarget = Squad.GetSquadCenter().DistanceTo(Squad.CurrentTarget.GlobalPosition);
        if (distToTarget > Squad.MaxPursuitDistance)
        {
            GD.Print("Target beyond MaxPursuitDistance. Disengaging.");
            Squad.Disengage();
            return;
        }

        // 3. Тайм-аут погони
        if (_pursuitTimer > Squad.PursuitGiveUpTime)
        {
            GD.Print("Pursuit timed out. Switching to Search.");
            Squad.ChangeState(new SearchState(Squad));
            return;
        }

        // 4. Определение статуса видимости
        // Цель считается "видимой" ТОЛЬКО если:
        // А. RayCast проходит (нет стен)
        // Б. Цель находится внутри Area3D сенсора (не слишком далеко)
        bool anySeesTarget = false;
        foreach (var member in Squad.Members)
        {
            bool hasLineOfSight = member.GetVisibleTargetPoint(Squad.CurrentTarget).HasValue;
            bool inSensorArea = member.TargetingSystem.IsTargetInSensorRange(Squad.CurrentTarget);

            if (hasLineOfSight && inSensorArea)
            {
                anySeesTarget = true;
                Squad.LastKnownTargetPosition = Squad.CurrentTarget.GlobalPosition;
                break;
            }
        }

        _isChasingVisibleTarget = anySeesTarget;

        // 5. Возврат в бой
        // Если мы видим цель и подошли на дистанцию атаки - возвращаемся в CombatState
        if (_isChasingVisibleTarget && distToTarget < 20.0f)
        {
            Squad.ChangeState(new CombatState(Squad, Squad.CurrentTarget));
            return;
        }

        // 6. Обновление логики движения
        _reevaluateTimer -= delta;
        if (_reevaluateTimer <= 0)
        {
            UpdateLogic();
            _reevaluateTimer = 0.5f;
        }

        // Обновление скорости цели
        _targetVelocityTimer -= delta;
        if (_targetVelocityTimer <= 0)
        {
            UpdateTargetVelocity();
            _targetVelocityTimer = Squad.TargetVelocityTrackInterval;
        }
    }

    private void UpdateTargetVelocity()
    {
        if (!GodotObject.IsInstanceValid(Squad.CurrentTarget)) return;
        var displacement = Squad.CurrentTarget.GlobalPosition - Squad.TargetPreviousPosition;
        Squad.ObservedTargetVelocity = displacement / Squad.TargetVelocityTrackInterval;
        Squad.TargetPreviousPosition = Squad.CurrentTarget.GlobalPosition;
    }

    private void UpdateLogic()
    {
        // СЦЕНАРИЙ А: Активный перехват
        if (_isChasingVisibleTarget)
        {
            // Бежим на упреждение
            Vector3 pursuitPoint = Squad.LastKnownTargetPosition + (Squad.ObservedTargetVelocity * Squad.PursuitPredictionTime);
            OrderMove(pursuitPoint);
        }
        // СЦЕНАРИЙ Б: Цель потеряна (или вышла из зоны)
        else
        {
            // Бежим к ПОСЛЕДНЕЙ ИЗВЕСТНОЙ ТОЧКЕ
            float distToLastKnown = Squad.GetSquadCenter().DistanceTo(Squad.LastKnownTargetPosition);

            // Если мы прибыли в точку, где видели врага в последний раз (или на край зоны)
            if (distToLastKnown < 3.0f)
            {
                // Мы на месте, но врага не видно (anySeesTarget = false).
                // Значит, он спрятался или убежал дальше. Начинаем ПОИСК.
                Squad.ChangeState(new SearchState(Squad));
            }
            else
            {
                // Продолжаем бежать к точке потери
                OrderMove(Squad.LastKnownTargetPosition);
            }
        }
    }

    private void OrderMove(Vector3 destination)
    {
        foreach (var member in Squad.Members)
        {
            // Добавляем минимальное смещение, чтобы отряд не слипался в одну точку
            Vector3 offset = Vector3.Zero;
            if (Squad.Members.Count > 1)
            {
                int index = Squad.Members.IndexOf(member);
                float angle = (Mathf.Tau / Squad.Members.Count) * index;
                offset = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * 2.0f;
            }

            member.ReceiveOrderMoveTo(destination + offset);

            // Даже в погоне даем разрешение на огонь, если вдруг цель мелькнет
            member.ReceiveOrderAttackTarget(Squad.CurrentTarget);
        }
    }
}