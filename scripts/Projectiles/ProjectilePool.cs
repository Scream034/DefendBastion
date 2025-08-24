using Game.Singletons;
using Godot;
using System.Collections.Generic;

namespace Game.Projectiles;

public static class ProjectilePool
{
    private static readonly Dictionary<PackedScene, Queue<BaseProjectile>> _pools = [];
    private static Node _poolContainer;

    public static BaseProjectile Get(PackedScene projectileScene)
    {
        if (!GodotObject.IsInstanceValid(_poolContainer))
        {
            _poolContainer = new Node { Name = "ProjectilePoolContainer" };
            Constants.Root.AddChild(_poolContainer);
        }

        if (!_pools.TryGetValue(projectileScene, out var queue))
        {
            queue = new Queue<BaseProjectile>();
            _pools[projectileScene] = queue;
        }

        BaseProjectile projectile;
        if (queue.Count > 0)
        {
            projectile = queue.Dequeue();
            // Отсоединяем от контейнера пула
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

    public static void Return(BaseProjectile projectile)
    {
        if (!GodotObject.IsInstanceValid(projectile) || projectile.SourceScene == null)
        {
            return;
        }

        if (!_pools.TryGetValue(projectile.SourceScene, out var queue))
        {
            queue = new Queue<BaseProjectile>();
            _pools[projectile.SourceScene] = queue;
        }

        projectile.Reparent(_poolContainer);

        projectile.Disable();
        
        queue.Enqueue(projectile);
    }
}