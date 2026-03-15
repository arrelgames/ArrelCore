using UnityEngine;

namespace RLGames
{
    public class BehaviorTree
    {
        private ITask rootTask;

        public BehaviorTree(ITask rootTask)
        {
            this.rootTask = rootTask;
        }

        public void Update()
        {
            rootTask.Execute();
        }
    }
}