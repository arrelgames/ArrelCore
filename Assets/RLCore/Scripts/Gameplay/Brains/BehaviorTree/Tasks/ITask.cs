using UnityEngine;

namespace RLGames
{
    public interface ITask
    {
        TaskStatus Execute();  // Execute the task, and return a status.
    }

    public enum TaskStatus
    {
        Success,
        Failure,
        Running
    }
}