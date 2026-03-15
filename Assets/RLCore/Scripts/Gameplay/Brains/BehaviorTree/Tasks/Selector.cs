using System.Collections.Generic;
using UnityEngine;

namespace RLGames
{
    public class Selector : ITask
    {
        private List<ITask> children;

        public Selector(List<ITask> children)
        {
            this.children = children;
        }

        public TaskStatus Execute()
        {
            foreach (var child in children)
            {
                TaskStatus status = child.Execute();
                if (status == TaskStatus.Running || status == TaskStatus.Success)
                    return status;
            }
            return TaskStatus.Failure;
        }
    }
}