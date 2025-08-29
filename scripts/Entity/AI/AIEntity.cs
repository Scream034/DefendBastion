using Godot;
using Game.Entity.AI.Behaviors;
using Game.Entity.AI.States;
using System.Threading.Tasks;
using Game.Interfaces;
using System.Collections.Generic;
using System.Linq;

namespace Game.Entity.AI
{
    /// <summary>
    /// Главная задача, которую выполняет ИИ, когда не находится в бою.
    /// </summary>
    public enum AIMainTask
    {
        /// <summary>Патрулирование в случайных точках в радиусе от спавна.</summary>
        FreePatrol,
        /// <summary>Патрулирование по заданному пути (Path3D).</summary>
        PathPatrol,
        /// <summary>Движение по пути к конечной цели.</summary>
        Assault
    }

    /// <summary>
    /// Режим поведения при выполнении задачи "Штурм".
    /// </summary>
    public enum AssaultMode
    {
        /// <summary>Атаковать всех врагов на пути к цели.</summary>
        Destroy,
        /// <summary>Игнорировать врагов и как можно быстрее добраться до цели.</summary>
        Rush
    }

    /// <summary>
    /// Базовый класс для всех сущностей, управляемых ИИ.
    /// Инкапсулирует логику навигации, машину состояний и базовое поведение.
    /// </summary>
    public abstract partial class AIEntity : MoveableEntity
    {
        [ExportGroup("AI Mission Settings")]
        [Export] public AIMainTask MainTask { get; private set; } = AIMainTask.FreePatrol;
        [Export] public AssaultMode AssaultBehavior { get; private set; } = AssaultMode.Destroy;
        [Export] public Path3D MissionPath { get; private set; }

        [ExportGroup("AI Movement Profiles")]
        [Export] public float SlowSpeed { get; private set; } = 3.0f;
        [Export] public float NormalSpeed { get; private set; } = 5.0f;
        [Export] public float FastSpeed { get; private set; } = 8.0f;

        [ExportGroup("AI Patrol Parameters")]
        [Export(PropertyHint.Range, "1, 20, 0.5")] public float BodyRotationSpeed { get; private set; } = 10f;
        [Export(PropertyHint.Range, "1, 20, 0.5")] public float HeadRotationSpeed { get; private set; } = 15f;
        [Export] public bool UseRandomPatrolRadius { get; private set; } = true;
        [Export] public float MinPatrolRadius { get; private set; } = 5f;
        [Export] public float MaxPatrolRadius { get; private set; } = 20f;
        [Export] public bool UseRandomWaitTime { get; private set; } = true;
        [Export(PropertyHint.Range, "0, 10, 0.5")] public float MinPatrolWaitTime { get; private set; } = 1.0f;
        [Export(PropertyHint.Range, "0, 10, 0.5")] public float MaxPatrolWaitTime { get; private set; } = 3.0f;

        [ExportGroup("AI Targetting System")]
        /// <summary>
        /// Как часто (в секундах) ИИ будет переоценивать цели в своем списке.
        /// Более низкие значения делают ИИ более реактивным, но увеличивают нагрузку.
        /// </summary>
        [Export(PropertyHint.Range, "0.1, 2.0, 0.1")]
        public float TargetEvaluationInterval = 0.5f;

        [ExportGroup("AI Combat Parameters")]
        /// <summary>
        /// Шаг в метрах для поиска новой огневой позиции. Меньшие значения - точнее, но больше проверок.
        /// </summary>
        [Export(PropertyHint.Range, "0.5, 5.0, 0.1")] public float RepositionSearchStep { get; private set; } = 1.5f;

        [ExportGroup("Dependencies")]
        [Export] private NavigationAgent3D _navigationAgent;
        [Export] private Area3D _targetDetectionArea;
        [Export] private Node _combatBehaviorNode;
        [Export] private Node3D _headPivot;
        [Export] private Marker3D _eyesPosition; // Точка для проверки линии видимости

        public NavigationAgent3D NavigationAgent => _navigationAgent;

        public PhysicsBody3D CurrentTarget { get; private set; }
        public ICombatBehavior CombatBehavior { get; private set; }
        public Vector3 SpawnPosition { get; private set; }
        public Vector3 LastKnownTargetPosition { get; set; }
        public Vector3 InvestigationPosition { get; set; }

        private State _currentState;
        private State _defaultState; // Состояние, к которому ИИ возвращается после боя/тревоги
        private bool _isAiActive = false;

        /// <summary>
        /// Список всех враждебных сущностей, которые в данный момент находятся в зоне обнаружения ИИ.
        /// </summary>
        private readonly List<PhysicsBody3D> _potentialTargets = [];

        private float _targetEvaluationTimer;

        protected AIEntity(IDs id) : base(id) { }
        public AIEntity() { }

        public override async void _Ready()
        {
            base._Ready();
            SpawnPosition = GlobalPosition;
            _targetEvaluationTimer = TargetEvaluationInterval; // Первый запуск оценки произойдет через interval

            if (!ValidateDependencies())
            {
                SetPhysicsProcess(false);
                return;
            }

            if (_targetDetectionArea != null)
            {
                _targetDetectionArea.BodyEntered += OnTargetDetected;
                _targetDetectionArea.BodyExited += OnTargetLost;
            }

            if (GameManager.Instance.IsNavigationReady)
            {
                InitializeAI();
            }
            else
            {
                GD.Print($"{Name} waiting for navigation map...");
                await ToSignal(GameManager.Instance, GameManager.SignalName.NavigationReady);
                InitializeAI();
            }
        }

        private void InitializeAI()
        {
            if (_isAiActive) return;

            // GlobalPosition = NavigationServer3D.MapGetClosestPoint(GetWorld3D().NavigationMap, GlobalPosition);

            GD.Print($"{Name} initializing AI, navigation map is ready.");
            _isAiActive = true;

            // Определяем "домашнее" состояние на основе настроек
            _defaultState = MainTask switch
            {
                AIMainTask.PathPatrol or AIMainTask.Assault => new PathFollowingState(this),
                _ => new PatrolState(this),
            };
            ChangeState(_defaultState);
        }

        private bool ValidateDependencies()
        {
            if (_navigationAgent == null) { GD.PushError($"NavigationAgent3D not assigned to {Name}!"); return false; }
            if (_combatBehaviorNode is ICombatBehavior behavior) { CombatBehavior = behavior; }
            else { GD.PushError($"CombatBehavior for {Name} is not assigned or invalid!"); return false; }

            if (_headPivot == null) { GD.PushWarning($"HeadPivot not assigned to {Name}. Head rotation will not work."); }
            if (_eyesPosition == null) { GD.PushWarning($"EyesPosition not assigned to {Name}. Line-of-sight checks will originate from the entity's center."); }

            if ((MainTask == AIMainTask.PathPatrol || MainTask == AIMainTask.Assault) && MissionPath == null)
            {
                GD.PushError($"AI {Name} is set to PathPatrol or Assault but has no MissionPath assigned!");
                return false;
            }

            return true;
        }

        public override void _PhysicsProcess(double delta)
        {
            base._PhysicsProcess(delta);

            _currentState?.Update((float)delta);

            // Периодически запускаем оценку целей
            _targetEvaluationTimer -= (float)delta;
            if (_targetEvaluationTimer <= 0f)
            {
                EvaluateTargets();
                _targetEvaluationTimer = TargetEvaluationInterval;
            }

            Vector3 targetVelocity = Vector3.Zero;

            if (_isAiActive && !_navigationAgent.IsNavigationFinished())
            {
                var nextPoint = _navigationAgent.GetNextPathPosition();
                var direction = GlobalPosition.DirectionTo(nextPoint);
                targetVelocity = direction * Speed;
            }

            Velocity = Velocity.Lerp(targetVelocity, Acceleration * (float)delta);

            var horizontalVelocity = Velocity with { Y = 0 };
            if (horizontalVelocity.LengthSquared() > 0.1f)
            {
                var targetRotation = Basis.LookingAt(horizontalVelocity.Normalized()).Orthonormalized();
                Basis = Basis.Orthonormalized().Slerp(targetRotation, BodyRotationSpeed * (float)delta);
            }

            MoveAndSlide();
        }

        public override async Task<bool> DamageAsync(float amount, LivingEntity source = null)
        {
            if (!await base.DamageAsync(amount, source)) return false;

            // РЕАКЦИЯ НА УРОН
            if (source != null && _currentState is not AttackState)
            {
                GD.Print($"{Name} took damage from {source.Name} direction!");
                InvestigationPosition = source.GlobalPosition;
                // Немедленно переходим в состояние расследования, если мы не в активном бою с другой целью
                if (CurrentTarget == null)
                {
                    ChangeState(new InvestigateState(this));
                }
            }
            return true;
        }


        public void ChangeState(State newState)
        {
            _currentState?.Exit();
            _currentState = newState;
            GD.Print($"{Name} changing state to -> {newState.GetType().Name}");
            _currentState.Enter();
        }

        public void ReturnToDefaultState()
        {
            ClearTarget();
            ChangeState(_defaultState);
        }

        public void SetAttackTarget(PhysicsBody3D target)
        {
            if (target == this) return;

            // Если новая цель отличается от текущей, или у нас не было цели
            if (target != CurrentTarget)
            {
                // Проверяем, что цель вообще можно атаковать
                if (target is not ICharacter || target is not IFactionMember)
                {
                    GD.PrintErr($"Attempted to target {target.Name}, which is not a valid target.");
                    return;
                }

                CurrentTarget = target;
                GD.Print($"{Name} new best target is: {target.Name}");

                // Если мы не в состоянии атаки/преследования/расследования, переходим в атаку
                if (_currentState is not (AttackState or PursuitState or InvestigateState))
                {
                    ChangeState(new AttackState(this));
                }
            }
        }

        public void ClearTarget()
        {
            CurrentTarget = null;
        }

        /// <summary>
        /// Проверяет прямую видимость от "глаз" ИИ до центра цели.
        /// Это основной метод для обнаружения и преследования.
        /// </summary>
        public bool HasLineOfSightTo(PhysicsBody3D target)
        {
            if (target == null || !IsInstanceValid(target)) return false;

            var fromPosition = _eyesPosition?.GlobalPosition ?? GlobalPosition;
            return HasClearPath(fromPosition, target.GlobalPosition, [GetRid(), target.GetRid()]);
        }

        /// <summary>
        /// Универсальный метод проверки прямой видимости между двумя точками.
        /// </summary>
        /// <param name="from">Начальная точка луча.</param>
        /// <param name="to">Конечная точка луча.</param>
        /// <param name="exclude">Список объектов, которые луч должен игнорировать.</param>
        /// <param name="collisionMask">Маска физики для проверки.</param>
        /// <returns>True, если между точками нет препятствий.</returns>
        public bool HasClearPath(Vector3 from, Vector3 to, Godot.Collections.Array<Rid> exclude = null, uint collisionMask = 1)
        {
            var spaceState = GetWorld3D().DirectSpaceState;
            var query = PhysicsRayQueryParameters3D.Create(from, to, collisionMask, exclude);
            var result = spaceState.IntersectRay(query);
            return result.Count == 0;
        }

        /// <summary>
        /// [МЕТОД 1: СТАНДАРТНОЕ ЗОНДИРОВАНИЕ] Ищет позицию, "прощупывая" пространство вокруг себя 
        /// в приоритетных направлениях (фланги, отступление).
        /// </summary>
        public Vector3? FindOptimalFiringPosition_Probing(PhysicsBody3D target, Vector3 weaponLocalOffset, float searchRadius)
        {
            if (target == null || !IsInstanceValid(target)) return null;

            var targetPosition = target.GlobalPosition;
            var navMap = GetWorld3D().NavigationMap;
            var exclusionList = new Godot.Collections.Array<Rid> { GetRid(), target.GetRid() };

            var directionToTarget = GlobalPosition.DirectionTo(targetPosition).Normalized();
            var flankDirection = directionToTarget.Cross(Vector3.Up).Normalized();

            var searchVectors = new Vector3[]
            {
                flankDirection, -flankDirection, -directionToTarget, directionToTarget,
                (flankDirection - directionToTarget).Normalized(), (-flankDirection - directionToTarget).Normalized(),
            };

            var bestPosition = ProbeDirections(searchVectors, targetPosition, weaponLocalOffset, searchRadius, navMap, exclusionList);
            if (bestPosition.HasValue)
            {
                GD.Print($"{Name} found position via standard probing at {bestPosition.Value}");
                return bestPosition;
            }

            GD.PushWarning($"{Name} could not find any optimal firing position via standard probing.");
            return null;
        }

        /// <summary>
        /// [МЕТОД 2: ГИБРИДНЫЙ АНАЛИЗ] Продвинутый алгоритм. Сначала определяет препятствие,
        /// затем вычисляет оптимальные векторы для его обхода (вдоль поверхности) и использует
        /// их для приоритетного зондирования. Если анализ не удался, откатывается к стандартному методу.
        /// </summary>
        public Vector3? FindOptimalFiringPosition_Hybrid(PhysicsBody3D target, Vector3 weaponLocalOffset, float searchRadius)
        {
            if (target == null || !IsInstanceValid(target)) return null;

            var targetPosition = target.GlobalPosition;
            var navMap = GetWorld3D().NavigationMap;
            var exclusionList = new Godot.Collections.Array<Rid> { GetRid(), target.GetRid() };

            // 1. Анализ препятствия
            var weaponPosition = GlobalPosition + (Basis * weaponLocalOffset);
            var spaceState = GetWorld3D().DirectSpaceState;
            var query = PhysicsRayQueryParameters3D.Create(weaponPosition, targetPosition, 1, [GetRid(), target.GetRid()]);
            var result = spaceState.IntersectRay(query);

            List<Vector3> searchVectors = new List<Vector3>();

            if (result.Count > 0)
            {
                var hitNormal = (Vector3)result["normal"];
                GD.Print($"{Name} obstacle detected. Surface normal: {hitNormal.Normalized()}");

                // 2. Вычисление векторов обхода (вдоль поверхности препятствия)
                var surfaceTangent = hitNormal.Cross(Vector3.Up).Normalized();

                // Если тангенс нулевой (например, смотрим ровно вверх или вниз на плоскую поверхность),
                // используем альтернативный расчет.
                if (surfaceTangent.IsZeroApprox())
                {
                    var directionToTarget = GlobalPosition.DirectionTo(targetPosition).Normalized();
                    surfaceTangent = directionToTarget.Cross(hitNormal).Normalized();
                }

                // 3. Формирование приоритетного списка векторов
                searchVectors.Add(surfaceTangent);      // Двигаться вдоль стены в одну сторону
                searchVectors.Add(-surfaceTangent);     // Двигаться вдоль стены в другую сторону
            }

            // 4. Добавляем стандартные векторы в качестве запасного варианта (fallback)
            var dirToTarget = GlobalPosition.DirectionTo(targetPosition).Normalized();
            var flankDir = dirToTarget.Cross(Vector3.Up).Normalized();
            searchVectors.Add(flankDir);
            searchVectors.Add(-flankDir);
            searchVectors.Add(-dirToTarget);

            // 5. Выполняем зондирование по вычисленным векторам
            var bestPosition = ProbeDirections(searchVectors.ToArray(), targetPosition, weaponLocalOffset, searchRadius, navMap, exclusionList);
            if (bestPosition.HasValue)
            {
                GD.Print($"{Name} found position via hybrid analysis at {bestPosition.Value}");
                return bestPosition;
            }

            GD.PushWarning($"{Name} could not find any optimal firing position via hybrid analysis.");
            return null;
        }

        /// <summary>
        /// Вспомогательный метод, который выполняет итеративное зондирование по заданному набору векторов.
        /// </summary>
        private Vector3? ProbeDirections(Vector3[] directions, Vector3 targetPosition, Vector3 weaponLocalOffset, float searchRadius, Rid navMap, Godot.Collections.Array<Rid> exclusionList)
        {
            foreach (var vector in directions.Where(v => !v.IsZeroApprox()))
            {
                for (float offset = RepositionSearchStep; offset <= searchRadius; offset += RepositionSearchStep)
                {
                    var candidatePoint = GlobalPosition + vector * offset;
                    var navMeshPoint = NavigationServer3D.MapGetClosestPoint(navMap, candidatePoint);

                    if (navMeshPoint.DistanceSquaredTo(candidatePoint) > RepositionSearchStep * RepositionSearchStep)
                    {
                        continue;
                    }

                    var weaponPositionAtCandidate = navMeshPoint + (Basis * weaponLocalOffset);

                    if (HasClearPath(weaponPositionAtCandidate, targetPosition, exclusionList))
                    {
                        return navMeshPoint;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Находит оптимальную позицию для стрельбы методом итеративного зондирования.
        /// Алгоритм проверяет приоритетные направления (фланги) с заданным шагом, привязывая каждую
        /// точку к навигационной сетке, и немедленно возвращает первую найденную валидную позицию.
        /// Это значительно производительнее "слепого" семплирования по кругу.
        /// </summary>
        /// <param name="target">Цель для атаки.</param>
        /// <param name="weaponLocalOffset">Локальное смещение оружия относительно центра AI.</param>
        /// <param name="searchRadius">Максимальный радиус поиска новой позиции.</param>
        /// <returns>Найденная позиция или null, если подходящей точки не найдено.</returns>
        public Vector3? FindOptimalFiringPosition(PhysicsBody3D target, Vector3 weaponLocalOffset, float searchRadius)
        {
            if (target == null || !IsInstanceValid(target)) return null;

            var targetPosition = target.GlobalPosition;
            var navMap = GetWorld3D().NavigationMap;
            var exclusionList = new Godot.Collections.Array<Rid> { GetRid(), target.GetRid() };

            // Определяем основные векторы для поиска
            var directionToTarget = GlobalPosition.DirectionTo(targetPosition).Normalized();
            // Вектор для стрейфа/фланга (перпендикулярный)
            var flankDirection = directionToTarget.Cross(Vector3.Up).Normalized();

            // Приоритетный список направлений для зондирования
            var searchVectors = new Vector3[]
            {
                flankDirection,          // Сначала пытаемся уйти на правый фланг
                -flankDirection,         // Затем на левый фланг
                -directionToTarget,      // Затем отступить назад
                directionToTarget,       // В крайнем случае - сократить дистанцию
                (flankDirection - directionToTarget).Normalized(), // Диагональ назад-вправо
                (-flankDirection - directionToTarget).Normalized(),// Диагональ назад-влево
            };

            // Итеративное зондирование по каждому вектору
            foreach (var vector in searchVectors)
            {
                // Начинаем с шага, постепенно увеличивая дистанцию
                for (float offset = RepositionSearchStep; offset <= searchRadius; offset += RepositionSearchStep)
                {
                    var candidatePoint = GlobalPosition + vector * offset;

                    // Привязываем точку к ближайшему месту на NavMesh
                    var navMeshPoint = NavigationServer3D.MapGetClosestPoint(navMap, candidatePoint);

                    // Проверяем, что точка на NavMesh не слишком далеко от нашей пробы.
                    // Если это так, значит, мы зондируем стену или пропасть. Пропускаем.
                    if (navMeshPoint.DistanceSquaredTo(candidatePoint) > RepositionSearchStep * RepositionSearchStep)
                    {
                        continue;
                    }

                    // Вычисляем, где будет дуло, если AI переместится в эту точку
                    var weaponPositionAtCandidate = navMeshPoint + (Basis * weaponLocalOffset);

                    if (HasClearPath(weaponPositionAtCandidate, targetPosition, exclusionList))
                    {
                        // НАЙДЕНО! Немедленно возвращаем первую подходящую позицию.
                        GD.Print($"{Name} found optimal position via probing at {navMeshPoint}");
                        return navMeshPoint;
                    }
                }
            }

            // Если после всех проверок ничего не найдено
            GD.PushWarning($"{Name} could not find any optimal firing position.");
            return null;
        }

        private void EvaluateTargets()
        {
            // 1. Очищаем список от уничтоженных или невалидных целей
            for (int i = _potentialTargets.Count - 1; i >= 0; i--)
            {
                var target = _potentialTargets[i];
                if (!IsInstanceValid(target) || (target is ICharacter character && character.Health <= 0))
                {
                    _potentialTargets.RemoveAt(i);
                    // ВАЖНО: Если уничтожена наша ТЕКУЩАЯ цель, обрабатываем это событие.
                    if (target == CurrentTarget)
                    {
                        OnCurrentTargetDestroyed(target);
                    }
                }
            }

            if (_potentialTargets.Count == 0 && CurrentTarget == null)
            {
                // Если в зоне видимости нет врагов, и текущей цели тоже нет, то ничего не делаем.
                // Переход в PursuitState обрабатывается в AttackState при потере LoS.
                return;
            }

            // 2. Получаем лучшую цель от нашего оценщика
            var bestTarget = AITargetEvaluator.GetBestTarget(this, _potentialTargets);

            // 3. Устанавливаем лучшую цель как текущую
            if (bestTarget != null)
            {
                SetAttackTarget(bestTarget);
            }
            // Если bestTarget == null (например, все цели за стеной), мы сохраняем текущую цель
            // и продолжим ее преследовать, пока не потеряем.
        }

        /// <summary>
        /// Вызывается, когда текущая цель AI была уничтожена.
        /// Проверяет, была ли цель контейнером, и переключается на её содержимое.
        /// </summary>
        private void OnCurrentTargetDestroyed(PhysicsBody3D destroyedTarget)
        {
            GD.Print($"{Name}: My target [{destroyedTarget.Name}] was destroyed.");

            // Очищаем текущую цель в любом случае.
            ClearTarget();

            // Проверяем, был ли уничтоженный объект контейнером
            if (destroyedTarget is IContainerEntity container)
            {
                var containedEntity = container.GetContainedEntity();
                if (containedEntity != null && IsInstanceValid(containedEntity) && IsHostile(containedEntity as IFactionMember))
                {
                    GD.Print($"{Name}: Target was a container. Now targeting its content: [{containedEntity.Name}].");

                    // Немедленно делаем "содержимое" новой целью
                    // Мы добавляем его в список потенциальных целей, чтобы оценщик мог его учесть,
                    // и сразу же устанавливаем как текущую цель для быстрой реакции.
                    if (!_potentialTargets.Contains(containedEntity))
                    {
                        _potentialTargets.Add(containedEntity);
                    }
                    SetAttackTarget(containedEntity);
                    return; // Новая цель найдена, выходим
                }
            }

            // Если это был не контейнер или он был пуст,
            // то мы просто остаемся без цели. Следующий вызов EvaluateTargets
            // найдет новую цель из оставшихся в _potentialTargets.
            // Если их нет, AI вернется в дефолтное состояние через логику состояний.
        }

        #region Movement API for States
        public void SetMovementSpeed(float speed)
        {
            Speed = speed;
        }

        public void MoveTo(Vector3 targetPosition)
        {
            if (_navigationAgent.TargetPosition == targetPosition) return;
            _navigationAgent.TargetPosition = targetPosition;
        }

        public void StopMovement()
        {
            _navigationAgent.TargetPosition = GlobalPosition;
        }
        #endregion

        #region Rotation API
        public void RotateBodyTowards(Vector3 targetPoint, float delta)
        {
            var direction = GlobalPosition.DirectionTo(targetPoint) with { Y = 0 };
            if (!direction.IsZeroApprox())
            {
                var targetRotation = Basis.LookingAt(direction.Normalized()).Orthonormalized();
                Basis = Basis.Orthonormalized().Slerp(targetRotation, BodyRotationSpeed * delta);
            }
        }

        public void RotateHeadTowards(Vector3 targetPoint, float delta)
        {
            if (_headPivot == null) return;
            var localTarget = _headPivot.ToLocal(targetPoint).Normalized();
            var targetRotation = Basis.LookingAt(localTarget).Orthonormalized();
            _headPivot.Basis = _headPivot.Basis.Orthonormalized().Slerp(targetRotation, HeadRotationSpeed * delta);
        }
        #endregion

        #region Signal Handlers
        private void OnTargetDetected(Node3D body)
        {
            if (body is PhysicsBody3D physicsBody && body is IFactionMember factionMember && body != this)
            {
                GD.Print($"{Name} finding {body.Name} ({factionMember.Faction}).");
                if (IsHostile(factionMember) && body is ICharacter)
                {
                    if (!_potentialTargets.Contains(physicsBody))
                    {
                        GD.Print($"{Name} added {body.Name} to potential targets.");
                        _potentialTargets.Add(physicsBody);
                        // Немедленно запускаем оценку, чтобы быстрее среагировать на нового врага
                        EvaluateTargets();
                    }
                }
            }
        }

        private void OnTargetLost(Node3D body)
        {
            if (body is PhysicsBody3D physicsBody)
            {
                if (_potentialTargets.Remove(physicsBody))
                {
                    GD.Print($"{Name} removed {body.Name} from potential targets.");
                }

                // Если мы потеряли из виду *текущую* цель
                if (body == CurrentTarget)
                {
                    // Не очищаем цель сразу, а переходим в преследование
                    if (_currentState is AttackState)
                    {
                        LastKnownTargetPosition = CurrentTarget.GlobalPosition;
                        ChangeState(new PursuitState(this));
                    }
                }
            }
        }
        #endregion
    }
}
