using Godot;
using System.Collections.Generic;

namespace Game.Projectiles
{
    /// <summary>
    /// Пул для переиспользования объектов снарядов (BaseProjectile).
    /// Реализован как Autoload (Singleton) для корректной интеграции с жизненным циклом Godot.
    /// </summary>
    public partial class ProjectilePool : Node
    {
        /// <summary>
        /// Глобальная точка доступа к экземпляру пула.
        /// </summary>
        public static ProjectilePool Instance { get; private set; }

        private readonly Dictionary<PackedScene, Queue<BaseProjectile>> _pools = [];

        public override void _EnterTree()
        {
            if (Instance != null)
            {
                GD.PushWarning($"Duplicate instance of {nameof(ProjectilePool)} detected. Overwriting...");
            }
            Instance = this;
        }

        public override void _ExitTree()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public BaseProjectile Get(PackedScene projectileScene)
        {
            if (!_pools.TryGetValue(projectileScene, out var queue))
            {
                queue = new Queue<BaseProjectile>();
                _pools[projectileScene] = queue;
            }

            BaseProjectile projectile;
            if (queue.Count > 0)
            {
                projectile = queue.Dequeue();
                // Отсоединяем от контейнера пула. Reparent(null) не сработает, если узел уже в сцене.
                projectile.GetParent()?.RemoveChild(projectile);
            }
            else
            {
                projectile = projectileScene.Instantiate<BaseProjectile>();
                projectile.SourceScene = projectileScene;
            }

            // ResetState вызывается здесь, когда узел еще не в сцене
            projectile.ResetState();
            return projectile;
        }

        public void Return(BaseProjectile projectile)
        {
            // Дополнительная проверка, чтобы убедиться, что _poolContainer еще валиден
            if (!IsInstanceValid(projectile) || projectile.SourceScene == null)
            {
                // Если пул уже уничтожается, просто удаляем снаряд
                if (IsInstanceValid(projectile)) projectile.QueueFree();
                return;
            }

            if (!_pools.TryGetValue(projectile.SourceScene, out var queue))
            {
                queue = new Queue<BaseProjectile>();
                _pools[projectile.SourceScene] = queue;
            }

            // Вместо GetParent().RemoveChild(this) используем Reparent, это безопаснее
            projectile.Reparent(this);
            projectile.Disable();
            queue.Enqueue(projectile);
        }
    }
}