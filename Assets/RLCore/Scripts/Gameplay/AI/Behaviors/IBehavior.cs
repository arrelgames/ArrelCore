using System.Threading.Tasks;
using UnityEngine;

namespace RLGames
{
    public interface IBehavior
    {
        TaskStatus Execute();
    }
}