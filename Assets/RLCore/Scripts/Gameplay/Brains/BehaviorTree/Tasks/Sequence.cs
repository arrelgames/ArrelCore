using System.Collections.Generic;
using UnityEngine;

namespace RLGames
{
    public class Sequence : ITask
    {
        private List<ITask> children;

        public Sequence(List<ITask> children)
        {
            this.children = children;
        }

        public TaskStatus Execute()
        {
            foreach (var child in children)
            {
                TaskStatus status = child.Execute();
                if (status == TaskStatus.Failure)
                    return TaskStatus.Failure;
            }
            return TaskStatus.Success;
        }
    }
}