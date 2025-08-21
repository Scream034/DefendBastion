using System;
using System.Collections.Generic;

namespace Game.Entity;

public static class LivingEntityManager
{
    public static event Action<LivingEntity> OnAdded;
    public static event Action<LivingEntity> OnRemoved;

    private static readonly List<LivingEntity> _entities = [];

    public static void Add(LivingEntity entity)
    {
        _entities.Add(entity);
        OnAdded?.Invoke(entity);
    }

    public static void Remove(in LivingEntity entity)
    {
        _entities.Remove(entity);
        OnRemoved?.Invoke(entity);
    }
}