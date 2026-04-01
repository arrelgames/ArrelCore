using System.Collections.Generic;

namespace RLGames
{
    public static class GiSourceRegistry
    {
        private static readonly HashSet<GiSource> Registered = new();
        private static readonly List<GiSource> Snapshot = new();

        public static void Register(GiSource source)
        {
            if (source == null)
                return;
            Registered.Add(source);
        }

        public static void Unregister(GiSource source)
        {
            if (source == null)
                return;
            Registered.Remove(source);
        }

        public static IReadOnlyList<GiSource> GetSnapshot()
        {
            Snapshot.Clear();
            foreach (GiSource source in Registered)
            {
                if (source != null)
                    Snapshot.Add(source);
            }

            return Snapshot;
        }
    }
}
