using UnityEngine;

namespace RLGames
{
    public class AiBrain : BrainBase
    {
        private float nextLookChange;

        protected override void Think()
        {
            command.Move = Vector2.up;

            if (Time.time > nextLookChange)
            {
                command.Look = new Vector2(
                    Random.Range(-1f, 1f),
                    Random.Range(-0.3f, 0.3f)
                );

                nextLookChange = Time.time + Random.Range(1f, 3f);
            }
        }
    }
}