using Game.Entity.AI.Components;

namespace Game.Entity.AI.States.Squad
{
    /// <summary>
    /// Интерфейс для состояний, которые могут обрабатывать запросы на смену позиции.
    /// </summary>
    public interface IRepositionHandler
    {
        void HandleRepositionRequest(AIEntity member);
    }

    public abstract class SquadStateBase(AISquad squad)
    {
        protected readonly AISquad Squad = squad;

        public virtual void Enter() { }
        public virtual void Process(double delta) { }
        public virtual void Exit() { }
    }
}